# T02 — Dockerfiles for Booking and Inventory, wired into Compose

## Summary
Add a multi-stage Dockerfile (plus `.dockerignore`) to the Booking and Inventory
services and wire both as build-and-run services into the existing `compose.yaml`.
After this, `docker compose up --build` brings up the two Postgres containers (from
T01) plus the two app containers, each depending on its database being healthy. No
schema, connection strings, or health-probe endpoints yet — those are owned by
later tasks.

## Context
- **Services (repo state):** both are default .NET 10 `Microsoft.NET.Sdk.Web`
  scaffolds. `BookingService` is a REST minimal-API app (OpenAPI + weatherforecast,
  `app.UseHttpsRedirection()`), host dev port 5007. `InventoryService` is a gRPC app
  (Greeter service, `Protos/greet.proto`), host dev port 5273, with
  `appsettings.json` forcing `Kestrel:EndpointDefaults:Protocols: Http2` — so gRPC
  works over plaintext HTTP/2 (h2c) on the container's default 8080 with no extra
  config.
- **No solution-wide build files:** no `Directory.Build.props`, `global.json`, or
  `NuGet.config`; neither service has a `ProjectReference` to a shared library. Each
  csproj restores standalone, so a per-service build context is sufficient.
- **Compose (from T01):** `compose.yaml` at repo root already defines
  `postgres-booking` (host 5432) and `postgres-inventory` (host 5433), each with a
  `pg_isready` healthcheck, on the `ticketing` network — T01 added the healthchecks
  specifically so T02 can use `depends_on: condition: service_healthy`.
- **Decisions (interview):**
  - *Runtime image `aspnet:10.0` non-root* (`USER $APP_UID`) — VS template default,
    builds anywhere, no musl/chiseled edge cases; the "small images" push is T36's job.
  - *Per-service build context* — Dockerfile in each service folder, context = that
    folder. Smallest, cleanest choice given no shared projects; revisit when a shared
    lib lands (T15 proto / T19 contracts).
  - *Auto-start + `depends_on` healthy* — both app services build and start with the
    stack, each waiting on its Postgres being healthy, host ports 5007/5273 matching
    the dev convention.

## Scope
### In scope
- `src/BookingService/Dockerfile` — multi-stage (`sdk:10.0` build → `aspnet:10.0`
  runtime), non-root, `ENTRYPOINT ["dotnet","BookingService.dll"]`.
- `src/InventoryService/Dockerfile` — same shape,
  `ENTRYPOINT ["dotnet","InventoryService.dll"]` (copies `Protos/` so the proto
  compiles at publish).
- A `.dockerignore` in each service folder excluding `bin/`, `obj/`, and other host
  cruft so Windows build output never poisons the Linux image build.
- Two new services in `compose.yaml` (`booking`, `inventory`) with `build.context`
  pointing at each service folder, host port mappings `5007:8080` / `5273:8080`,
  `depends_on` the matching Postgres with `condition: service_healthy`, on the
  `ticketing` network, `restart: unless-stopped`.

### Out of scope
- **Connection strings / EF Core / migrations** — T03 (inventory), T06 (booking).
  App containers start but do not yet talk to their database.
- **Health-probe endpoints** (liveness/readiness) — T37. No container `healthcheck`
  is added for the app services here.
- **Image-size optimization** (chiseled/alpine/trimming) and Dockerfiles for the
  other services (Payment, Gateway, Go, Python) — T36.
- **Fixing `UseHttpsRedirection`** in Booking — harmless in an HTTP-only container
  (logs a warning, passes traffic through); left as-is. Marked with a `ponytail:`
  note if touched, but no code change planned.
- **gRPC over the network / retiring REST** — T15/T16. Inventory still serves the
  scaffold Greeter.
- k8s manifests (Phase 5), Redis/RabbitMQ/Kafka/Mongo (T10/T18/T52).

## Files affected
- `src/BookingService/Dockerfile` *(new)* — multi-stage build+publish of
  `BookingService.csproj`, non-root runtime, `EXPOSE 8080`.
- `src/BookingService/.dockerignore` *(new)* — ignore `bin/`, `obj/`, `.vs/`, etc.
- `src/InventoryService/Dockerfile` *(new)* — same, for `InventoryService.csproj`
  (includes `Protos/`).
- `src/InventoryService/.dockerignore` *(new)* — same ignore set.
- `compose.yaml` *(modified)* — add `booking` and `inventory` services with build
  context, ports, `depends_on` healthy, network, restart policy.

Nothing else is touched. `Program.cs`, `appsettings.json`, and `.gitignore`
(already ignores `bin/`/`obj/`) stay as they are.

## Implementation steps
Single infra change, no phases. Order chosen so each service is independently
buildable before compose references it.

1. Add `.dockerignore` + `Dockerfile` to `src/BookingService/`:
   - Build stage `mcr.microsoft.com/dotnet/sdk:10.0`: `COPY BookingService.csproj`,
     `dotnet restore`, `COPY . .`, `dotnet publish -c Release -o /app --no-restore`.
   - Runtime stage `mcr.microsoft.com/dotnet/aspnet:10.0`: `WORKDIR /app`,
     `COPY --from=build /app .`, `EXPOSE 8080`, `USER $APP_UID`,
     `ENTRYPOINT ["dotnet","BookingService.dll"]`.
   - Verify standalone: `docker build -t booking src/BookingService`.
2. Add the same pair to `src/InventoryService/` (entrypoint `InventoryService.dll`).
   Verify: `docker build -t inventory src/InventoryService`.
3. Add `booking` and `inventory` services to `compose.yaml` (build context, ports
   `5007:8080` / `5273:8080`, `depends_on` matching Postgres `service_healthy`,
   `networks: [ticketing]`, `restart: unless-stopped`).
4. Verify: `docker compose config` parses; `docker compose up -d --build` brings
   both Postgres to healthy and both app containers to running; check logs and the
   HTTP endpoints (see Deliverables); `docker compose down` (keep volumes).

## Deliverables
- [x] `src/BookingService/Dockerfile` and `src/InventoryService/Dockerfile` each build
      successfully with `docker build` (green publish, no restore errors).
- [x] Each service folder has a `.dockerignore` excluding `bin/` and `obj/`.
- [x] `docker compose config` validates with no errors or warnings after the edits.
- [x] `docker compose up -d --build` starts four containers; both Postgres reach
      `healthy` and `booking` + `inventory` reach `running` (`docker compose ps`),
      each app container's logs show `Now listening on: http://[::]:8080`.
- [x] `booking` app comes up only after `postgres-booking` is healthy (and same for
      `inventory`/`postgres-inventory`) — confirmed by `depends_on` in
      `docker compose config` and startup ordering.
- [x] Booking REST reachable: `curl http://localhost:5007/weatherforecast` returns
      HTTP 200 with a JSON array.
- [x] Inventory gRPC endpoint reachable over h2c:
      `curl --http2-prior-knowledge http://localhost:5273/` returns the scaffold
      "Communication with gRPC endpoints…" text (proves the Http2 container endpoint
      serves).
- [x] Both app containers run as non-root (`docker compose exec booking id` shows a
      non-zero UID).

## Risks & open questions
- **Windows→Linux build context:** copying host `bin/`/`obj/` into the image can
  break the build; the `.dockerignore` files are the mitigation and must land with
  the Dockerfiles.
- **Booking `UseHttpsRedirection`:** in an HTTP-only container it logs "Failed to
  determine the https port for redirect" and passes requests through — cosmetic, left
  as-is (no HTTPS wired until it's actually needed).
- **Inventory over HTTP/1.1:** because Kestrel is Http2-only, a plain
  `curl http://localhost:5273/` (HTTP/1.1) fails the protocol negotiation — expected;
  use `--http2-prior-knowledge`. Not a defect, just a verification detail.
- **Host port conflicts:** 5007/5273 could collide with a locally-running dev
  instance; stop the dev process or adjust the mapping if so (not parameterized here
  to keep the change minimal — T01 already parameterizes the DB ports).
