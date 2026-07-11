# T01 — Docker Compose: Postgres per service (booking, inventory)

## Summary
Add a Docker Compose stack that runs one Postgres container per service —
`postgres-booking` and `postgres-inventory` — each with its own volume, on a
shared network, with health checks. This gives Phase 1 a local database-per-service
setup that later tasks (Dockerfiles in T02, migrations in T03/T06, Redis in T10)
extend rather than replace.

## Context
- **Repo state:** fresh scaffolding only. Services exist (`BookingService` on host
  port 5007, `InventoryService` on 5273) with empty `appsettings.json` — no
  connection strings, no EF Core, no compose file yet.
- **Architecture (README):** explicit database-per-service — the diagram shows a
  separate Postgres for Inventory, Booking, and Payment. T01 scopes to booking +
  inventory only.
- **Decisions (interview):**
  - *Strict scope* — two Postgres containers now; Payment Postgres deferred to T27
    (smallest change that satisfies T01).
  - *Committed defaults with env override* — `${POSTGRES_PASSWORD:-postgres}` style,
    working out-of-the-box for local dev, overridable via `.env`. Local-only, nothing
    secret.
  - *Health checks now* — `pg_isready` per DB so T02's app containers can
    `depends_on: condition: service_healthy`; small addition that saves rework.

## Scope
### In scope
- A single `compose.yaml` at repo root defining two Postgres services with pinned
  image, named volumes, a shared network, host port mappings, and health checks.
- A committed `.env.example` documenting the overridable variables (defaults live in
  `compose.yaml`, so the stack runs with no `.env` present).

### Out of scope
- **Payment Postgres** — deferred to T27 (Phase 4).
- **Dockerfiles / app containers** — T02 owns building and wiring Booking/Inventory
  images into this compose file.
- **EF Core, migrations, connection strings in appsettings** — T03 (inventory) and
  T06 (booking) own schema and app-side wiring.
- **Redis, RabbitMQ, Kafka, Mongo** — T10, T18, T52 add these services to compose later.
- **Seed data** — T04.
- No k8s manifests (Phase 5), no pgAdmin/tooling containers, no backup/tuning config.

## Files affected
- `compose.yaml` *(new)* — two Postgres services (`postgres-booking`,
  `postgres-inventory`), one named volume each, one named network, `pg_isready`
  health checks, host ports 5432 and 5433, env-var-driven credentials with defaults.
- `.env.example` *(new)* — documents `POSTGRES_USER`, `POSTGRES_PASSWORD`, and the
  two DB names / host ports with their default values.

Nothing else is touched. Empty `appsettings.json` files stay empty until T03/T06.

## Implementation steps
Single-file infra change, no phases needed.

1. Write `compose.yaml` at repo root:
   - `postgres-booking`: image `postgres:17-alpine`, `POSTGRES_DB=booking`,
     `POSTGRES_USER=${POSTGRES_USER:-postgres}`,
     `POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-postgres}`, volume
     `postgres-booking-data:/var/lib/postgresql/data`, host port
     `${BOOKING_DB_PORT:-5432}:5432`, healthcheck
     `pg_isready -U ${POSTGRES_USER:-postgres} -d booking`.
   - `postgres-inventory`: same shape, `POSTGRES_DB=inventory`, volume
     `postgres-inventory-data`, host port `${INVENTORY_DB_PORT:-5433}:5432`.
   - Named network `ticketing` attached to both; named volumes declared.
   - `restart: unless-stopped` on both.
2. Write `.env.example` listing every overridable variable with its default and a
   one-line comment that copying to `.env` is optional (defaults work as-is).
3. Verify: `docker compose config` parses, `docker compose up -d` brings both
   containers to healthy, and a `psql`/`pg_isready` check against each host port
   succeeds. Then `docker compose down` (keep volumes).

## Deliverables
- [x] `compose.yaml` exists at repo root and `docker compose config` validates with
      no errors or warnings.
- [x] `docker compose up -d` starts `postgres-booking` and `postgres-inventory`; both
      reach `healthy` per `docker compose ps`.
- [x] `booking` DB reachable on host port 5432, `inventory` DB on 5433
      (`pg_isready -h localhost -p <port>` returns accepting).
- [x] Named volumes `postgres-booking-data` and `postgres-inventory-data` persist data
      across `docker compose down` / `up` (a test row survives a restart).
- [x] Stack starts with **no** `.env` file present (defaults apply); creating `.env`
      from `.env.example` overrides credentials/ports.
- [x] `.env.example` committed; `.env` is gitignored (already covered by
      `.gitignore:7` — no edit needed).

## Risks & open questions
- **Host port conflicts:** a local Postgres already on 5432 collides with
  `postgres-booking`. Mitigated by `${BOOKING_DB_PORT:-5432}` override; noted in
  `.env.example`.
- **`.gitignore` coverage:** already ignores `.env` (line 7), so no third file is
  touched — the change is exactly `compose.yaml` + `.env.example`.
