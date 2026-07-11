# Implementation Plan

Derived from [README.md](../README.md). One row = one GitHub issue.

**Mapping to a GitHub Projects board:**
- **Phase** → milestone
- **Service** → label `service:<name>` (`booking`, `inventory`, `payment`, `gateway`, `notification`, `analytics`, `cross` for cross-cutting, `infra` for platform)
- **Type** → label `type:<name>` (`feature`, `contract`, `infra`, `test`, `observability`, `ops`)
- **Blocked by** → issue dependency / "blocked by" relation. Tasks with no entry can start as soon as their phase does.
- IDs are stable references (`T01`…); keep them in issue titles, e.g. `[T07] Inventory: hold endpoints`.

Scaffolding (solution, projects, test projects, Go/Python skeletons) is already done and not listed.

---

## Phase 1 — Two services, REST only

Booking + Inventory, one Postgres each, Docker Compose. Goal: service boundaries and clean contracts.

| ID | Task | Service | Type | Blocked by |
|---|---|---|---|---|
| T01 | Docker Compose: Postgres instance per service (booking, inventory), volumes, networking | infra | infra | — |
| T02 | Dockerfiles for Booking and Inventory, wired into Compose | infra | infra | — |
| T03 | Inventory: domain model + EF Core (Venue, seat map, Performance, seat inventory with `available/held/sold`) + migrations | inventory | feature | T01 |
| T04 | Inventory: seed data (one venue, seat map, a few performances) | inventory | feature | T03 |
| T05 | Inventory: REST API v1 — list performances, get seat availability, create hold (naive, DB-only), release hold, mark sold | inventory | feature | T03 |
| T06 | Booking: domain model + EF Core (Order, Booking, Ticket) + migrations | booking | feature | T01 |
| T07 | Booking: REST API v1 — create order (calls Inventory to hold seats), confirm booking (naive, no payment yet) | booking | feature | T05, T06 |
| T08 | OpenAPI documents for Booking and Inventory REST APIs (contract-first: write before/with the endpoints) | cross | contract | — |
| T09 | Integration test: happy-path booking end to end against Compose | cross | test | T07 |

## Phase 2 — Real concurrency

Seat holds in Redis with TTL, idempotency, and the double-booking race. Core design lesson of the project.

| ID | Task | Service | Type | Blocked by |
|---|---|---|---|---|
| T10 | Add Redis to Docker Compose | infra | infra | T01 |
| T11 | Inventory: move holds to Redis with 10-min TTL; Postgres stays source of truth for `sold` | inventory | feature | T05, T10 |
| T12 | Inventory: idempotency keys on hold/sell operations (Redis) | inventory | feature | T11 |
| T13 | Inventory: optimistic locking on seat rows (concurrency token, retry-on-conflict) | inventory | feature | T05 |
| T14 | Concurrency test: two parallel requests for the same seat — prove the race, then prove the fix | inventory | test | T11, T13 |
| T15 | Contract: `inventory.proto` — hold/release/sell/availability RPCs (shared source of truth for C#/Go/Python) | inventory | contract | T05 |
| T16 | Inventory: serve gRPC from the proto; keep REST or retire it | inventory | feature | T15 |
| T17 | Booking: switch Inventory calls from REST to gRPC | booking | feature | T16 |

## Phase 3 — Go async

RabbitMQ commands + Kafka events via MassTransit; first cross-language consumer.

| ID | Task | Service | Type | Blocked by |
|---|---|---|---|---|
| T18 | Add RabbitMQ and Kafka to Docker Compose | infra | infra | T01 |
| T19 | Contract: domain event schemas v1 (JSON) — `SeatHeld`, `SeatReleased`, `SeatSold`, `PaymentSucceeded`, `PaymentFailed`, `BookingConfirmed`; document money-as-minor-units, UTC ISO-8601, casing, enum, and null rules | cross | contract | — |
| T20 | Contract: `ProcessPayment` command (MassTransit message) | cross | contract | — |
| T21 | Booking: MassTransit + RabbitMQ setup; send payment command on order creation | booking | feature | T18, T20 |
| T22 | Inventory: publish Kafka events (`SeatHeld`, `SeatReleased`, `SeatSold`) via Confluent.Kafka | inventory | feature | T18, T19 |
| T23 | Notification (Go): Kafka consumer for domain events, log "sent" emails | notification | feature | T19, T22 |
| T24 | Notification (Go): idempotent consumption (dedup store) + at-least-once semantics | notification | feature | T23 |
| T25 | RabbitMQ retries + dead-letter queue for the payment command | booking | feature | T21 |
| T26 | Test: kill/restart the Go consumer mid-stream, verify no lost or double-processed events | notification | test | T24 |

## Phase 4 — The saga

Booking becomes a MassTransit saga state machine with compensating actions; mock Payment appears.

| ID | Task | Service | Type | Blocked by |
|---|---|---|---|---|
| T27 | Payment: mock service — consume `ProcessPayment` from RabbitMQ, simulate success/failure, publish `PaymentSucceeded`/`PaymentFailed` to Kafka | payment | feature | T20, T18 |
| T28 | Payment: idempotency keys on command handling | payment | feature | T27 |
| T29 | Booking: saga state machine (`SeatsHeld → PaymentAuthorised → Confirmed`, failures → `Released`) with MassTransit; persist saga state in Postgres | booking | feature | T21, T27 |
| T30 | Booking: compensating action — release hold on payment failure or timeout | booking | feature | T29 |
| T31 | Booking: transactional outbox built by hand (understand the dual-write problem) | booking | feature | T29 |
| T32 | Booking: replace hand-built outbox with MassTransit outbox | booking | feature | T31 |
| T33 | Booking: publish `BookingConfirmed` to Kafka; issue Tickets on confirmation | booking | feature | T29 |
| T34 | Saga tests: happy path, payment failure, hold expiry mid-payment | booking | test | T30 |
| T35 | Gateway: route client REST to Booking (REST) and Inventory (gRPC); anti-corruption mapping of external ↔ internal contracts | gateway | feature | T16 |

## Phase 5 — Kubernetes + observability

Same images, now on k8s; make one request visible across five services.

| ID | Task | Service | Type | Blocked by |
|---|---|---|---|---|
| T36 | Dockerfiles for all remaining services (Payment, Gateway, Go, Python) — multi-stage, small images | infra | infra | — |
| T37 | Health probes: liveness + readiness endpoints on every service | cross | feature | — |
| T38 | k8s: Deployments + Services for all stateless services | infra | ops | T36 |
| T39 | k8s: Ingress exposing only the Gateway | infra | ops | T35, T38 |
| T40 | k8s: ConfigMaps/Secrets for connection strings and keys | infra | ops | T38 |
| T41 | k8s: StatefulSets (or Helm charts) for Postgres, Redis, Kafka, RabbitMQ | infra | ops | — |
| T42 | Hold-expiry job: small .NET console project calling Inventory to release lapsed holds; run as k8s CronJob | inventory | feature | T11 |
| T43 | k8s: HPA on Booking + a load test that triggers it | infra | ops | T38 |
| T44 | Structured logging + correlation IDs propagated across REST/gRPC/RabbitMQ/Kafka, all languages | cross | observability | — |
| T45 | OpenTelemetry tracing in all services + Jaeger; one trace across the whole booking flow | cross | observability | T44 |
| T46 | Graceful shutdown: drain in-flight messages/requests on SIGTERM in every service | cross | feature | — |
| T47 | Circuit breakers (Polly) on Booking→Inventory and Gateway→downstream calls | cross | feature | T17, T35 |

## Phase 6 — Real payments + analytics

Stripe test mode replaces the mock; Python consumer arrives once event schemas are stable.

| ID | Task | Service | Type | Blocked by |
|---|---|---|---|---|
| T48 | Payment: Stripe test mode — authorise on command, capture on saga confirmation, void/refund on compensation | payment | feature | T27, T29 |
| T49 | Payment: Stripe webhook endpoint — signature verification, webhook as the authoritative result | payment | feature | T48 |
| T50 | Payment: Stripe idempotency keys on every call | payment | feature | T48 |
| T51 | Verify: hold TTL outlives the payment window (test the expiry-mid-payment bug) | cross | test | T42, T48 |
| T52 | Add Mongo to Compose/k8s | infra | infra | — |
| T53 | Analytics (Python): Kafka consumer building read models in Mongo (tickets per performance, revenue per event) | analytics | feature | T19, T33, T52 |
| T54 | Analytics (Python): REST endpoint serving the read models | analytics | feature | T53 |
| T55 | Analytics (Python): pytest suite for aggregation logic | analytics | test | T53 |

## Stretch (backlog, no ordering)

| ID | Task | Service | Type | Blocked by |
|---|---|---|---|---|
| T56 | Schema registry for Kafka; migrate events JSON → Protobuf/Avro with compatibility checks | cross | contract | T53 |
| T57 | Contract tests (Pact) between C# producers and Go/Python consumers | cross | test | T53 |
| T58 | Service mesh (Linkerd): mTLS + traffic shifting | infra | ops | T38 |
| T59 | Event sourcing for one aggregate | booking | feature | T34 |

---

## Dependency spine

The critical path, phase to phase:

```
T01 → T03/T06 → T05 → T07 → T11/T13 → T15 → T16 → T17
    → T21/T22 → T27 → T29 → T30/T31 → T48 → T49 → T53
```

Parallel-friendly at any time: T08, T19, T20 (contracts), T36, T37, T44, T46 (cross-cutting).
