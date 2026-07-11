# T05 — Inventory: REST API v1

## Summary
Give the Inventory service its first HTTP surface: five REST endpoints over the
existing EF Core model — list performances, get seat availability, hold seats,
release a hold, mark seats sold. Holds are **naive and DB-only** (flip
`SeatInventory.Status` in a transaction); there is no Redis, no hold-id, no
concurrency token yet. This unblocks T07 (Booking calls Inventory to hold
seats).

## Context
What already exists (Step 1):
- `src/InventoryService` is a .NET 10 **gRPC** app (`Microsoft.NET.Sdk.Web`) with
  the Greeter scaffold, EF Core over Postgres (`InventoryDbContext`), migrations,
  and an idempotent seeder run at startup (T03/T04).
- **Kestrel is pinned to `Http2` only** (`appsettings.json` → `Kestrel.EndpointDefaults.Protocols`).
  REST/JSON clients speak HTTP/1.1, so this must widen to `Http1AndHttp2` for the
  same cleartext port to serve both gRPC (h2c) and REST (h1). Kestrel distinguishes
  them via the HTTP/2 connection preface.
- Code follows a **vertical-slice, one-class-per-file** layout, folders by domain
  concept (`Performances/`, `SeatInventories/`, `Seats/`, …). Namespaces follow
  folders. (Established convention; see the T03 deviation note.)
- Entities have **no navigation properties**; FKs are configured with
  `HasOne<T>().WithMany().HasForeignKey(...)`. Reads that need the seat map join
  `SeatInventory → Seat → Row → Section` explicitly.
- `SeatInventory.Status` is a `SeatStatus` enum (`Available`/`Held`/`Sold`) stored
  as a **string** column. No concurrency token — T13 owns that.
- Tests use `Microsoft.EntityFrameworkCore.InMemory` against the real
  `InventoryDbContext` (see `InventorySeederTests`); no HTTP host.
- `compose.yaml` already exposes the inventory container on `5273:8080` and injects
  the connection string — the REST API rides the same port, so **no compose change**.

Decisions (interview):
- **Seat-ID list, no hold aggregate** — hold/release/sell each take a list of seat
  IDs and flip status. Seat IDs are the handle. Truly "naive DB-only"; real
  hold-ids + idempotency arrive in T11/T12, which will replace this.
- **Availability returns seat + labels + status** — flat list of `{ seatId,
  section, row, number, status }` from one join. Enough for a UI seat map and for
  Booking.
- **All-or-nothing holds** — if any requested seat isn't in the required state,
  reject the whole request (`409`), change nothing. One transaction. Same rule for
  release (must be `Held`) and sell (must be `Held`).
- **In-memory handler tests** — core state-transition logic lives in a testable
  static operations class; tests drive it against an in-memory `DbContext`, matching
  the existing pattern.

## Scope
### In scope
Five endpoints under `/api/v1`, served alongside the untouched gRPC Greeter:
- `GET  /api/v1/performances` — list performances (with event + venue names).
- `GET  /api/v1/performances/{performanceId}/seats` — seat availability.
- `POST /api/v1/performances/{performanceId}/hold` — hold seats `{ seatIds }`.
- `POST /api/v1/performances/{performanceId}/release` — release held seats `{ seatIds }`.
- `POST /api/v1/performances/{performanceId}/sell` — mark held seats sold `{ seatIds }`.

State transitions (all-or-nothing, in one transaction):
- hold: every seat must be `Available` → set `Held`; else `409`.
- release: every seat must be `Held` → set `Available`; else `409`.
- sell: every seat must be `Held` → set `Sold`; else `409`.
- unknown `performanceId` → `404`; unknown seat id in the list → treated as a
  conflict (`409`, nothing changes).

Widen Kestrel to `Http1AndHttp2`. Enum serialized as its string name on the wire.

### Out of scope
- **Redis holds, 10-min TTL, hold-ids** — T11. This flips DB status directly.
- **Idempotency keys** — T12. Repeated hold/sell calls are not deduplicated here.
- **Optimistic locking / concurrency token / retry-on-conflict** — T13. Naive holds
  have a known double-book race; that race is the *point* of T13/T14, not a bug to
  fix now.
- **gRPC/proto changes** — T15/T16. The Greeter scaffold stays as-is; REST is the
  Phase-1 transport.
- **OpenAPI document** — T08 owns the contract artifact.
- **Booking-side calls** — T07.
- **Health/readiness probes** — T37.
- **Pricing / money fields** — not needed for these endpoints.
- **Pagination / filtering** on the performance list — the seed set is tiny; add
  when a task needs it.
- **compose.yaml / Dockerfile / launchSettings** changes — the REST API rides the
  existing port and build.

## Files affected
- `src/InventoryService/Program.cs` *(modified)* — map the endpoints
  (`app.MapPerformanceEndpoints();`, `app.MapSeatInventoryEndpoints();`); keep gRPC
  + DbContext + seeder wiring intact.
- `src/InventoryService/appsettings.json` *(modified)* — `Kestrel …Protocols`
  `Http2` → `Http1AndHttp2`.
- `src/InventoryService/Performances/PerformanceEndpoints.cs` *(new)* —
  `MapPerformanceEndpoints` extension; `GET /api/v1/performances`.
- `src/InventoryService/Performances/PerformanceResponse.cs` *(new)* — response
  record `{ Id, EventId, EventName, VenueId, VenueName, StartsAtUtc }`.
- `src/InventoryService/SeatInventories/SeatInventoryEndpoints.cs` *(new)* —
  `MapSeatInventoryEndpoints` extension; availability GET + hold/release/sell POSTs,
  thin wrappers mapping outcomes to `Ok/NotFound/Conflict`.
- `src/InventoryService/SeatInventories/SeatInventoryOperations.cs` *(new)* — static
  methods `GetAvailability`, `Hold`, `Release`, `Sell` taking
  `(InventoryDbContext, Guid performanceId, IReadOnlyList<Guid> seatIds)`; the
  testable state-transition logic (mirrors the static `InventorySeeder` pattern).
- `src/InventoryService/SeatInventories/SeatAvailabilityResponse.cs` *(new)* — record
  `{ SeatId, Section, Row, Number, Status }` (Status as string).
- `src/InventoryService/SeatInventories/SeatActionRequest.cs` *(new)* — request
  record `{ IReadOnlyList<Guid> SeatIds }`.
- `src/InventoryService/SeatInventories/SeatActionOutcome.cs` *(new)* — enum
  `{ Ok, NotFound, Conflict }` returned by the operations, mapped to HTTP status.
- `tests/InventoryService.Tests/SeatInventoryOperationsTests.cs` *(new)* — in-memory
  tests for hold/release/sell transitions and availability.

Nothing else is touched. If another file turns out to be needed, stop and revisit.

## Implementation steps
Single feature slice — the endpoints only become useful together, and the change is
small. No phases.

1. **Kestrel** — in `appsettings.json` change `Kestrel.EndpointDefaults.Protocols`
   from `Http2` to `Http1AndHttp2` so REST (h1) and gRPC (h2c) share the port.
2. **DTOs + outcome** — add `PerformanceResponse`, `SeatAvailabilityResponse`,
   `SeatActionRequest`, `SeatActionOutcome` (one file each, in their concept folder).
3. **Operations** — add `SeatInventoryOperations` (static):
   - `GetAvailability`: `404` outcome if the performance doesn't exist; else the
     joined `SeatInventory → Seat → Row → Section` projection. Materialize before
     mapping `Status` enum → string to avoid provider-translation surprises.
   - `Hold`/`Release`/`Sell`: load the `SeatInventory` rows for
     `(performanceId, seatIds)`; if the count doesn't match the requested ids, or any
     row isn't in the required source status, return `Conflict` and change nothing;
     else set the target status and `SaveChanges()`. `404` if the performance is
     unknown.
4. **Endpoints** — add `PerformanceEndpoints` (GET list) and
   `SeatInventoryEndpoints` (availability GET + hold/release/sell POSTs). Thin
   `IEndpointRouteBuilder` extension methods; each handler injects
   `InventoryDbContext`, calls an operation, maps outcome → `Results.Ok/NotFound/Conflict`.
5. **Wire** — call both `Map…Endpoints()` in `Program.cs`. Serialize enums as strings
   (`ConfigureHttpJsonOptions` + `JsonStringEnumConverter`, or have the DTO already
   carry a string — pick the smaller of the two at implementation time).
6. **Tests** — add `SeatInventoryOperationsTests`: seed a small map in-memory, assert
   hold flips `Available→Held`, release flips back, sell flips `Held→Sold`,
   all-or-nothing rejects a batch containing a non-`Available` seat (nothing
   changes), and availability returns labeled seats with the right statuses.
7. **Verify** end to end (see Deliverables).

## Deliverables
- [x] `dotnet build` on `InventoryService` and `InventoryService.Tests` is green; the
      gRPC Greeter still starts. (Container starts clean; `MapGrpcService` untouched.)
- [x] `GET /api/v1/performances` returns `200` with the 3 seeded performances, each
      carrying event + venue names.
- [x] `GET /api/v1/performances/{id}/seats` returns `200` with 100 labeled seats all
      `Available` on a fresh DB; unknown `id` returns `404`.
- [x] `POST …/hold` with available seat ids returns success and those seats read
      `Held` on the next availability call.
- [x] `POST …/hold` with a batch containing an already-`Held` seat returns `409` and
      leaves **all** requested seats unchanged (all-or-nothing).
- [x] `POST …/release` returns held seats to `Available`; `POST …/sell` moves held
      seats to `Sold`; selling/ releasing a non-`Held` seat returns `409`.
- [x] `SeatInventoryOperationsTests` passes (hold/release/sell transitions +
      all-or-nothing rejection + availability projection). (8 tests, 12 total green.)
- [x] `docker compose up -d --build` brings inventory up and the REST endpoints
      answer on the mapped host port; gRPC unaffected. (Verified full REST flow on
      `:5273`; enum on the wire is the string name via string-typed DTOs.)

## Risks & open questions
- **Http1AndHttp2 on cleartext:** widening protocols lets Kestrel serve both on one
  insecure port via preface sniffing. This is supported but occasionally finicky
  behind proxies; if gRPC breaks after the change, that's the first suspect.
- **`Status.ToString()` in an EF projection** may not translate on Npgsql — the plan
  materializes rows before mapping the enum to string. Confirm the availability query
  runs server-side except the final string mapping.
- **Known double-book race:** two concurrent holds for the same seat can both pass the
  read-then-write check with no concurrency token. Deliberate — T13/T14 prove and fix
  it. Do not add locking here.
