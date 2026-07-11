# T03 — Inventory: domain model + EF Core + migrations

## Summary
Give the Inventory service its persistence foundation: the seat-map and
performance domain model (Event, Venue, Section, Row, Seat, Performance, and the
contended `SeatInventory` with `Available`/`Held`/`Sold` status), an EF Core
`DbContext` over Postgres via Npgsql, and an initial migration. The service
auto-applies the migration on startup so the `inventory` database has its schema
ready for T04 (seed) and T05 (REST API) to build on. No API, no seed rows, no
Redis, no concurrency token yet — those are later tasks.

## Context
What already exists:
- `src/InventoryService` is a bare .NET 10 gRPC scaffold (`Microsoft.NET.Sdk.Web`,
  Kestrel Http2-only, scaffold Greeter service). **No EF Core, no Npgsql, no
  DbContext, no connection string** today.
- `compose.yaml` (from T01/T02) defines `postgres-inventory` (image
  `postgres:17-alpine`, DB `inventory`, user/pass `postgres`, container address
  `postgres-inventory:5432`, host port `5433`) with a `pg_isready` healthcheck, and
  an `inventory` app service that already `depends_on` it `service_healthy`.
- T02's plan explicitly deferred **connection strings / EF Core / migrations** to
  this task — so wiring the connection string is T03's job.
- No `Directory.Build.props`, `global.json`, or central package management — package
  references go directly in `InventoryService.csproj`.

Decisions (interview):
- **Seat map = Section + Row + Seat (three entities)** — mirrors the README's
  "sections, rows, seats" vocabulary exactly; the most explicit model.
- **Status stored as a string column** (`SeatStatus` enum via
  `.HasConversion<string>()`) — debuggable in raw SQL, aligns with the project's
  explicit-string cross-wire ethos; ordinal-fragility avoided.
- **Auto-migrate on startup** (`db.Database.Migrate()` in `Program.cs`) — the
  container boots with schema ready so T04/T05 build straight on it. Dev-only
  convenience, acceptable for Phase 1.
- **No pricing** — T03 is inventory/seat-map + availability status only; price
  fields are added when a task actually needs money in Inventory.

## Scope
### In scope
- Domain entities: `Event`, `Venue`, `Section`, `Row`, `Seat`, `Performance`,
  `SeatInventory`, and the `SeatStatus` enum (`Available`, `Held`, `Sold`).
- `InventoryDbContext` with `DbSet`s and `OnModelCreating` config: relationships,
  the enum→string conversion, and the two integrity constraints that matter
  (unique seat identity within a row; one inventory row per seat per performance).
- Npgsql EF Core provider + Design package added to the csproj; `DbContext`
  registered in `Program.cs` with the `Inventory` connection string.
- Auto-migrate on startup.
- Initial EF migration (`Migrations/` folder + model snapshot).
- Connection string in `appsettings*.json` (localhost:5433 for host dev) plus a
  compose env override pointing at `postgres-inventory:5432`.
- One lightweight model test asserting the config is intact (string status column,
  unique `(PerformanceId, SeatId)` index).

### Out of scope
- **REST API / any endpoints** — T05. The service keeps serving only the scaffold
  Greeter; no new routes.
- **Seed data** (venue, seat map rows, performances) — T04. The migration creates
  empty tables only.
- **Redis holds / TTL / idempotency** — T11/T12. `SeatInventory.Status` is a plain
  column here; the `Held` value exists but nothing writes it yet.
- **Optimistic-locking / concurrency token** on seat rows — T13 owns it. No
  `xmin`/rowversion mapping in this migration.
- **Pricing / price tiers** — deferred (interview decision).
- **Health-probe endpoints** — T37. Startup migration is not a readiness probe.
- **gRPC contract / proto changes** — T15/T16. The Greeter scaffold is untouched.
- Booking's model (T06) and anything in other services.

## Files affected
- `src/InventoryService/InventoryService.csproj` *(modified)* — add
  `Npgsql.EntityFrameworkCore.PostgreSQL` and
  `Microsoft.EntityFrameworkCore.Design` package references.
- `src/InventoryService/Domain/Entities.cs` *(new)* — all six entity POCOs +
  `SeatStatus` enum in one file (trivial POCOs; split later if it grows).
- `src/InventoryService/Data/InventoryDbContext.cs` *(new)* — `DbContext`,
  `DbSet`s, `OnModelCreating` (enum→string, unique indexes, FK relationships).
- `src/InventoryService/Program.cs` *(modified)* — `AddDbContext<InventoryDbContext>`
  with `UseNpgsql(connectionString)`; scoped `db.Database.Migrate()` at startup.
  gRPC registration left as-is.
- `src/InventoryService/appsettings.json` *(modified)* — `ConnectionStrings:Inventory`
  default (`Host=localhost;Port=5433;Database=inventory;Username=postgres;Password=postgres`).
- `src/InventoryService/appsettings.Development.json` *(modified, if needed)* —
  inherits the base; only override if host-dev differs.
- `src/InventoryService/Migrations/*` *(new, generated)* — `InitialInventory`
  migration + `InventoryDbContextModelSnapshot`.
- `compose.yaml` *(modified)* — add `ConnectionStrings__Inventory` env to the
  `inventory` service (`Host=postgres-inventory;Port=5432;...`).
- `tests/InventoryService.Tests/InventoryModelTests.cs` *(new)* — model-validity
  test (no DB connection needed; asserts on `context.Model`).

Nothing else is touched. If another file turns out to be needed, stop and revisit.

## Implementation steps
Single feature slice, no phases needed — the change is small and only becomes
useful once all parts land together (context + migration + startup wiring).

1. **Entities** — add `Domain/Entities.cs`:
   - `Event { Guid Id; string Name }`
   - `Venue { Guid Id; string Name }`
   - `Section { Guid Id; Guid VenueId; string Name }`
   - `Row { Guid Id; Guid SectionId; string Label }`
   - `Seat { Guid Id; Guid RowId; int Number }`
   - `Performance { Guid Id; Guid EventId; Guid VenueId; DateTime StartsAtUtc }`
   - `SeatInventory { Guid Id; Guid PerformanceId; Guid SeatId; SeatStatus Status }`
   - `enum SeatStatus { Available, Held, Sold }`
2. **DbContext** — add `Data/InventoryDbContext.cs`: `DbSet`s for each entity;
   in `OnModelCreating` configure `SeatInventory.Status.HasConversion<string>()`,
   a unique index on `Seat (RowId, Number)`, a unique index on
   `SeatInventory (PerformanceId, SeatId)`, and the FK relationships. Map
   `StartsAtUtc` as `timestamptz` (Npgsql default for `DateTime`).
3. **Packages + wiring** — add the two package refs to the csproj; in `Program.cs`
   register `AddDbContext<InventoryDbContext>(o => o.UseNpgsql(builder.Configuration
   .GetConnectionString("Inventory")))`; after `Build()`, in a service scope call
   `db.Database.Migrate()`.
4. **Connection string** — set `ConnectionStrings:Inventory` in `appsettings.json`
   (localhost:5433); add `ConnectionStrings__Inventory` override to the `inventory`
   service in `compose.yaml` (`postgres-inventory:5432`).
5. **Migration** — `dotnet ef migrations add InitialInventory` (needs
   `Microsoft.EntityFrameworkCore.Design` + the `dotnet-ef` tool). Inspect the
   generated SQL: status column is `text`, both unique indexes present.
6. **Test** — add `InventoryModelTests.cs`: build
   `DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql("Host=localhost")
   .Options` (no connection made), instantiate the context, and assert the
   `SeatInventory.Status` provider type is `string` and the
   `(PerformanceId, SeatId)` unique index exists on `context.Model`.
7. **Verify** end to end (see Deliverables).

## Deliverables
- [x] `dotnet build` on `InventoryService` and `InventoryService.Tests` is green;
      the scaffold gRPC app still starts.
- [x] `dotnet ef migrations list` shows `InitialInventory`; the generated
      migration creates 7 tables (Events, Venues, Sections, Rows, Seats,
      Performances, SeatInventory).
- [x] Generated migration stores `SeatInventory.Status` as a **text** column
      (not integer).
- [x] Migration includes a unique index on `Seat (RowId, Number)` and a unique
      index on `SeatInventory (PerformanceId, SeatId)`.
- [x] `docker compose up -d --build` brings the `inventory` container up; its logs
      show the migration applied, and
      `docker compose exec postgres-inventory psql -U postgres -d inventory -c "\dt"`
      lists all 7 tables plus `__EFMigrationsHistory`.
- [x] `InventoryModelTests` passes (status mapped to string; unique inventory index
      present).

> **Deviation from plan:** at the user's direction the domain model was split
> into one-class-per-file vertical-slice folders (`Events/`, `Venues/`,
> `Sections/`, `Rows/`, `Seats/`, `Performances/`, `SeatInventories/`) instead of
> the planned single `Domain/Entities.cs`. Namespaces follow the folders.
> Provider pinned to `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (stable EF10).

## Risks & open questions
- **EF Core 10 / Npgsql provider version:** on `net10.0` the correct
  `Npgsql.EntityFrameworkCore.PostgreSQL` major version must match EF Core 10 —
  implementer pins the compatible version at package-add time. If a stable EF10
  Npgsql provider isn't available, fall back to the latest EF-compatible release
  and note it.
- **`dotnet-ef` tool availability:** generating the migration needs the `dotnet-ef`
  CLI (global or local tool) plus the `Design` package. Install if missing; this
  is a one-time dev-tooling step, not a code change.
- **Migrate-on-startup vs. the container race:** `depends_on: service_healthy`
  already gates the app on Postgres being ready, so `Migrate()` at boot should
  connect cleanly. If a transient failure appears, that's a resilience concern for
  a later task, not a reason to change the approach here.
- **`SeatInventory` has no concurrency token yet** — deliberate (T13). The initial
  migration will need a follow-up migration in T13 to add it; that's expected.
