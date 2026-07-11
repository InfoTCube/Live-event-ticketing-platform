# T04 — Inventory: seed data (one venue, seat map, a few performances)

## Summary
Populate the Inventory database that T03 created with a small, realistic dataset:
one event, one venue, a two-section seat map (~100 seats), three performances, and
a `SeatInventory` row per seat per performance — all `Available`. This gives T05's
REST endpoints (list performances, seat availability, hold/sell) real data to serve
and lets anyone `docker compose up` and immediately see a fully-seated venue.

## Context
What already exists (from T03):
- Domain entities in vertical-slice folders: `Event`, `Venue`, `Section`, `Row`,
  `Seat`, `Performance`, `SeatInventory` + `SeatStatus { Available, Held, Sold }`.
- `InventoryDbContext` (`src/InventoryService/Data/`) with all `DbSet`s, FK
  relationships, `Seat (RowId, Number)` unique index, and
  `SeatInventory (PerformanceId, SeatId)` unique index; `Status` stored as text.
- `Program.cs` already opens a service scope after `Build()` and calls
  `Database.Migrate()` — the natural place to invoke a seeder.
- The service otherwise still serves only the scaffold Greeter (no REST yet — T05).

Decisions (interview):
- **Runtime idempotent seeder**, not EF `HasData` — a `Seed/InventorySeeder.cs`
  invoked right after `Migrate()`, guarded by "skip if any venue exists". Avoids
  hand-maintaining hundreds of deterministic GUIDs in a migration and composes
  cleanly with the existing auto-migrate startup block.
- **Small seat map** — 1 venue, 2 sections × 5 rows × 10 seats = 100 seats. Real
  enough for availability listings, small enough to eyeball in psql.
- **All seats `Available`** — clean baseline; T05's hold/sell flows and tests
  create `Held`/`Sold` themselves.

## Scope
### In scope
- `Seed/InventorySeeder.cs` — a static `Seed(InventoryDbContext db)` that, when the
  `Venues` table is empty, inserts: 1 `Event`, 1 `Venue`, 2 `Section`s, 10 `Row`s
  (5 per section, labels A–E), 100 `Seat`s (numbers 1–10 per row), 3 `Performance`s
  of the event at the venue at distinct future UTC times, and 300 `SeatInventory`
  rows (every seat × every performance, `Status = Available`), then `SaveChanges`.
  GUIDs generated at runtime; the empty-table guard makes re-runs no-ops.
- `Program.cs` — call `InventorySeeder.Seed(db)` inside the existing startup scope,
  immediately after `Database.Migrate()`.
- One seeder unit test proving row counts after a seed and idempotency (seeding
  twice does not duplicate).

### Out of scope
- **Any REST/gRPC endpoint** — T05. The seeder only writes rows; nothing serves them.
- **Pre-seeding `Held`/`Sold` seats** — interview decision: all `Available`.
- **Pricing / price tiers** — not in the model (T03 deferred it); seed no money.
- **Multiple venues or events** — the task says *one* venue; a second is YAGNI.
- **Redis / holds / idempotency keys / concurrency token** — T11–T13.
- **Environment gating** (seed only in Development) — the empty-table guard already
  makes the seeder safe to run everywhere; no env switch needed.
- **New migration** — seeding is runtime data, not schema; the T03 migration is
  untouched.

## Files affected
- `src/InventoryService/Seed/InventorySeeder.cs` *(new)* — static idempotent seeder.
- `src/InventoryService/Program.cs` *(modified)* — one line invoking the seeder after
  `Migrate()` in the existing scope.
- `tests/InventoryService.Tests/InventorySeederTests.cs` *(new)* — seed-count +
  idempotency test.
- `tests/InventoryService.Tests/InventoryService.Tests.csproj` *(modified)* — add
  `Microsoft.EntityFrameworkCore.InMemory` (test-only) so the seeder runs against an
  in-memory store without a live Postgres. If a provider is already referenced
  transitively, no change.

Nothing else is touched. If another file turns out to be needed, stop and revisit.

## Implementation steps
Single slice — the seeder plus its one-line wiring only become useful together.

1. **Seeder** — add `Seed/InventorySeeder.cs`: `public static class InventorySeeder`
   with `public static void Seed(InventoryDbContext db)`. First line:
   `if (db.Venues.Any()) return;`. Build the object graph (event, venue, 2 sections,
   5 rows each with labels A–E, 10 seats each numbered 1–10), 3 performances at
   distinct `DateTime.UtcNow`-relative times (use explicit UTC `DateTime`s), and a
   `SeatInventory { PerformanceId, SeatId, Status = Available }` for every
   (performance, seat) pair. `db.AddRange(...)` then `db.SaveChanges()`.
2. **Wire** — in `Program.cs`, inside the existing `using (var scope …)` block, after
   the `Migrate()` call, resolve the same `InventoryDbContext` and call
   `InventorySeeder.Seed(db)`.
3. **Test** — add `InventorySeederTests.cs`: build
   `DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(unique-name)`,
   run `Seed` once, assert counts (1 venue, 2 sections, 10 rows, 100 seats,
   3 performances, 300 seat-inventory rows, all `Available`); run `Seed` again on the
   same context and assert the counts are unchanged (idempotent guard).
4. **Verify** end to end (see Deliverables).

## Deliverables
- [x] `dotnet build` on `InventoryService` and `InventoryService.Tests` is green.
- [x] `InventorySeederTests` passes: after one seed exactly 1 venue / 2 sections /
      10 rows / 100 seats / 3 performances / 300 `SeatInventory` rows, all
      `Available`; a second `Seed` call leaves every count unchanged.
- [x] `docker compose up -d --build` brings `inventory` up; logs show migration
      applied then seed run once. (On this run the T03 volume was already migrated,
      so migration was a no-op; seed had run on first startup.)
- [x] `docker compose exec postgres-inventory psql -U postgres -d inventory -c
      "SELECT (SELECT count(*) FROM \"Venues\"), (SELECT count(*) FROM \"Seats\"),
      (SELECT count(*) FROM \"Performances\"), (SELECT count(*) FROM
      \"SeatInventories\");"` returns `1 | 100 | 3 | 300`.
- [x] Restarting the `inventory` container (re-running startup) does **not** add
      duplicate rows — counts stay `1 | 100 | 3 | 300`.

## Risks & open questions
- **In-memory provider vs. relational constraints:** `UseInMemoryDatabase` does not
  enforce the unique indexes, so the test validates counts and idempotency, not the
  DB constraints (those are T03's concern and already covered). Acceptable — the
  test's job here is the seed logic, not schema.
- **Seeder placement in the startup scope:** it reuses the same scope/`DbContext` as
  `Migrate()`; ensure the seeder runs after migration so tables exist. Trivial
  ordering, called out so it isn't reversed.
- **Performance times:** seeded as fixed UTC `DateTime`s relative to now; if a later
  task asserts on specific timestamps it should not depend on these seed values.
