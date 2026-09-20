# Outbox Pattern — Migration Plan & Agent Prompt

> Goal: evolve FuelFlow's event handling to a **transactional outbox feeding a message
> broker**, built to the highest solution-architecture standards. The agent must
> **recommend** the broker/library, deliver a **design document for approval before
> writing any code**, and — above all — **not break existing behaviour**.

This file has two parts:
- **Part 1 — Plan**: the grounded analysis and the shape of the work (context for the human).
- **Part 2 — The Prompt**: a self-contained prompt to hand to an implementing agent.

---

## Part 1 — Plan

### 1.1 Where we are today (verified against the codebase)

**Stack** (`backend/`):
- .NET 10, EF Core 10, `Npgsql.EntityFrameworkCore.PostgreSQL` 10, PostgreSQL 16.
- **Hangfire.PostgreSql** present in both `FuelFlow.API` and `FuelFlow.JobsWorker` (job scheduling).
- Redis (StackExchange) for caching; Serilog + OpenTelemetry (OTLP / Prometheus / Loki) with health checks.
- Deploy: Docker Compose locally; production on Hetzner. **No message broker exists today.**
- `FuelFlow.JobsWorker` references `FuelFlow.API` (shared `ApplicationDbContext` + domain), so a shared relay component is feasible.

**Two things are called "outbox", only one is:**

1. **`OutboxEvent`** — a genuine transactional outbox.
   - Model: [OutboxEvent.cs](backend/src/FuelFlow.API/Features/Orders/SharedModels/OutboxEvent.cs) —
     `Id (int)`, `EventType (enum)`, `Payload (jsonb string)`, `Processed (bool)`, `ProcessedAtUtc`, `CreatedAtUtc`.
   - Event types: [OutboxEventType.cs](backend/src/FuelFlow.API/Features/Orders/SharedModels/OutboxEventType.cs).
   - DbSet: [ApplicationDbContext.cs:37](backend/src/FuelFlow.API/Persistence/ApplicationDbContext.cs).
   - **Producers**: [SimulatePaymentCommandHandler](backend/src/FuelFlow.API/Features/Orders/SimulatePayment/SimulatePaymentCommandHandler.cs)
     and [ProcessMonobankWebhookCommandHandler](backend/src/FuelFlow.API/Features/Monobank/ProcessWebhook/ProcessMonobankWebhookCommandHandler.cs) (`OrderCreated`),
     [BulkActionVouchersCommandHandler](backend/src/FuelFlow.API/Features/Vouchers/BulkActionVouchers/BulkActionVouchersCommandHandler.cs) (`VoucherActivated`),
     [FulfillmentService](backend/src/FuelFlow.API/BackgroundJobs/FulfillmentService.cs) (`OrderFulfilled`).
   - **Consumers / relay**: `FulfillmentService` and `NotificationService` exist **twice** —
     [FuelFlow.API/BackgroundJobs](backend/src/FuelFlow.API/BackgroundJobs/FulfillmentService.cs) and
     [FuelFlow.JobsWorker/Services](backend/src/FuelFlow.JobsWorker/Services/FulfillmentService.cs) — near-identical copies.

2. **`ProviderEventOutbox`** — **named** "outbox" but is actually an **audit/history log**.
   - Model: [ProviderEventOutbox.cs](backend/src/FuelFlow.API/SharedKernel/Domain/ProviderEventOutbox.cs);
     written by [ProviderEventService](backend/src/FuelFlow.API/Features/Providers/ProviderEventService.cs);
     read by `GetProviderHistory`. No `Processed`/relay — it is never dispatched anywhere.

### 1.2 Gaps a "highest-standard" outbox + broker should close
(Stated as observations; the agent chooses how to solve them and justifies it in the design doc.)

- **Duplicated relay** across two hosts → drift risk; needs one shared, tested component.
- **No broker** → events cannot be delivered at-least-once to external/decoupled consumers today.
- Relay is **poll + `Take(50)`** with no row-level claim (`FOR UPDATE SKIP LOCKED`), lease, **attempt counter, error capture, retry/backoff, or dead-letter/poison handling**. Concurrency safety currently leans entirely on `pg_advisory_xact_lock` + idempotent SQL claims downstream, not on the outbox row itself.
- **Untyped JSON payload**, no message envelope (id, type, version, `occurredAt`, `traceparent`), no schema versioning.
- `int` PK ordering with no monotonic/global-ordering guarantee; **no partial index** on `WHERE NOT processed`.
- In places the `OrderFulfilled` event is written in a **separate `SaveChanges` outside the business transaction** — atomicity nuance to preserve/repair.
- **Naming collision**: `ProviderEventOutbox` is not an outbox — decide whether to rename/repurpose or route it through the new pipeline.

### 1.3 Shape of the engagement (what the prompt enforces)

1. **Learn first** — the outbox pattern (references below) and *all* relevant repo code + docs
   (`docs/DESIGN.md`, `docs/OBSERVABILITY.md`, `docs/DEPLOYMENT.md`, `docs/SECURITY.md`, `README.md`, `AGENTS.md`, `FuelFlow_Audit_Report.md`).
2. **Design document FIRST, no code** — broker/library recommendation with rejected alternatives,
   target architecture, contracts, delivery guarantees, migration/coexistence, security, observability,
   testing, rollout/rollback, and an explicit *don't-break* checklist. **Stop for human approval.**
3. **Only after approval** — implement **incrementally, one slice at a time**, building + running the
   full test suites between slices and reporting results (matches the team's one-item-at-a-time flow).
4. The current relay stays until the new path proves parity behind a feature flag; every step reversible.

### 1.4 Suggested design-doc location
`docs/adr/ADR-0001-transactional-outbox-and-broker.md` (or `docs/OUTBOX_DESIGN.md`). Diagrams in Mermaid.

### 1.5 References the agent should read (outbox pattern)
- microservices.io — Transactional Outbox: https://microservices.io/patterns/data/transactional-outbox.html
- microservices.io — Polling Publisher: https://microservices.io/patterns/data/polling-publisher.html
- microservices.io — Transaction Log Tailing (CDC): https://microservices.io/patterns/data/transaction-log-tailing.html
- Debezium Outbox Event Router: https://debezium.io/documentation/reference/stable/transformations/outbox-event-router.html
- MassTransit Transactional Outbox: https://masstransit.io/documentation/patterns/transactional-outbox
- .NET microservices architecture (event-driven): https://learn.microsoft.com/dotnet/architecture/microservices/

---

## Part 2 — The Prompt (hand this to the implementing agent)

> Paste everything below into a fresh agent session running in the FuelFlow repo.

---

You are working in the **FuelFlow** repository (`.NET 10`, EF Core 10, PostgreSQL 16, Redis, Hangfire,
Serilog + OpenTelemetry; API in `backend/src/FuelFlow.API`, background worker in
`backend/src/FuelFlow.JobsWorker`; production runs on Hetzner via Docker Compose).

### Mission
Evolve FuelFlow's event handling into a **transactional outbox that publishes to a message broker**,
designed to the **highest solution-architecture standards**, optimising for **efficiency, performance,
security, robustness, and code that is maintainable and readable for humans**.

### THE ONE RULE THAT OVERRIDES EVERYTHING
**Do not break existing functionality.** No regression in current behaviour, data, or contracts is
acceptable. Concretely, you must:
- Keep all current HTTP endpoints, order/voucher/fulfilment/notification behaviour, and observable
  outcomes identical unless a change is explicitly approved.
- Make **only additive, backward-compatible, reversible** database migrations. No destructive schema
  changes or data loss. Every migration must have a tested down-path or a documented rollback.
- Keep the **existing `OutboxEvent` relay running** until the new path is proven at parity behind a
  feature flag; support coexistence/dual-run, then cut over, then retire the old path — never a big-bang swap.
- After every change, **build and run the full test suites** (`FuelFlow.UnitTests`,
  `FuelFlow.JobsWorker.UnitTests`, `FuelFlow.Providers.UnitTests`, `FuelFlow.IntegrationTests`) and
  **report the real results**. If something fails or is skipped, say so with the output — never claim green without proof.
- If a step turns out to be risky or ambiguous, stop and ask rather than guessing.

### Step 1 — Learn (no code yet)
1. Study the transactional outbox pattern from the references in `outbox_pattern_prompt.md` §1.5
   (polling publisher vs transaction-log tailing/CDC, delivery guarantees, idempotency, dead-lettering).
2. Read the current implementation end to end: `OutboxEvent`, `OutboxEventType`, `OutboxEventConfiguration`,
   `ApplicationDbContext`, every producer that adds an `OutboxEvent`, and **both** copies of
   `FulfillmentService`/`NotificationService` (in `FuelFlow.API/BackgroundJobs` and `FuelFlow.JobsWorker/Services`).
   Also review `ProviderEventOutbox` + `ProviderEventService` (note: it is named "outbox" but is really an
   audit log — decide its fate).
3. Read the docs: `docs/DESIGN.md`, `docs/OBSERVABILITY.md`, `docs/DEPLOYMENT.md`, `docs/SECURITY.md`,
   `README.md`, `AGENTS.md`, and `FuelFlow_Audit_Report.md`. Note how the app is deployed and how
   observability is wired (OTel → Prometheus/Loki/Grafana). Grafana dashboard changes are **not** auto-deployed
   by CI and require a manual `docker compose ... up -d --force-recreate grafana` — account for this if you add dashboards.

### Step 2 — Produce a DESIGN DOCUMENT and STOP for approval
Write `docs/adr/ADR-0001-transactional-outbox-and-broker.md` (Mermaid diagrams welcome). **Do not modify
any application code or migrations until a human approves this document.** It must cover:

1. **Current-state analysis** — how events flow today, the duplicated relay, the polling limitations
   (no claim/lease, no retry/attempt/error/dead-letter, untyped payload, `int` ordering, missing partial index),
   and the `ProviderEventOutbox` naming/role issue. Cite files.
2. **Broker & library recommendation** — you choose. Compare realistic candidates against this exact stack
   and a single-VM Hetzner deployment (e.g. RabbitMQ vs Kafka/Redpanda vs NATS vs a Postgres-native/Redis-Streams
   option; and libraries such as MassTransit, Wolverine, or hand-rolled). State the recommendation, the rejected
   alternatives, and **why** (operational cost, ordering, throughput, delivery guarantees, .NET 10 support,
   observability integration, team maintainability). Prefer the simplest option that meets the requirements.
3. **Target architecture** — producer writes the domain change **and** the outbox row in **one DB transaction**;
   a **single-flight relay** (across API replicas + JobsWorker) publishes to the broker with **at-least-once**
   delivery; consumers are **idempotent**. Specify how single-flight is achieved (`FOR UPDATE SKIP LOCKED`
   batch claim / leader election / dedicated relay process) and how it unifies the duplicated relay into one
   shared, tested component.
4. **Message contract** — a versioned envelope (message id, type, version, `occurredAt`, aggregate id,
   `traceparent` for trace continuity) + serialization choice + a schema-evolution policy.
5. **Guarantees & failure handling** — ordering scope, idempotency/dedup keys, retry with backoff,
   poison-message handling, dead-letter store/queue, and how the outbox row lifecycle (attempts, last error,
   processed/failed) is modelled.
6. **Migration & coexistence** — a strangler-style, feature-flagged plan that keeps the existing relay working,
   runs old and new in parallel, verifies parity, then cuts over and retires the old path. Every phase reversible.
   Explicitly address the `OrderFulfilled` event currently written outside the business transaction.
7. **Security** — broker authn/z, TLS in transit, secret handling (respect `.gitleaks.toml`; the repo has a
   prior committed-secret incident, so **no secrets in code, config, or logs**), least-privilege broker
   credentials, and PII in payloads.
8. **Observability** — OTel spans spanning publish→consume, metrics for backlog/lag/DLQ/throughput, log
   correlation, Grafana dashboards + alerts, health checks.
9. **Testing strategy** — existing suites must stay green; add integration tests (consider Testcontainers for
   the broker) and contract tests; describe how you prove at-least-once + idempotency + no-regression.
10. **Rollout / rollback** — Docker Compose + Hetzner changes, infra-as-code, migration ordering, and the
    exact rollback procedure.
11. **Don't-break checklist** — the concrete list of behaviours, endpoints, and data you will verify unchanged.

End the document with **open questions / decisions needed from the human**, then stop and wait.

### Step 3 — Implement (only after approval), incrementally
Work in **small, independently shippable slices**, each behind a feature flag where behaviour changes.
After each slice: build, run all test suites, report results, and confirm no regression before the next slice.
Keep new code idiomatic to the surrounding codebase (naming, structure, comment density), thoroughly but not
excessively commented, and covered by tests. Do not introduce new dependencies beyond what the approved design
specifies. Never enable auto-merge; open a PR per slice with a clear summary of what was tested.

### Quality bar (apply throughout)
- **Efficiency/performance**: batch claims, partial/covering indexes, avoid N+1, bounded polling or push,
  backpressure-aware; no hot loops hammering the DB or broker.
- **Robustness**: correct under concurrency (multiple API replicas + the worker), crash-safe, exactly the
  intended delivery semantics, self-healing on transient failures, poison messages isolated not looping.
- **Security**: least privilege, TLS, no secrets in VCS/logs, validated/authenticated broker access.
- **Maintainability/readability**: one shared relay (kill the duplication), clear separation of publish vs
  consume vs business logic, typed contracts, documented decisions in the ADR.

Report honestly at every step. When the outbox+broker path is proven at parity and the old relay retired,
summarise what changed, what was tested, and how to operate/monitor the new pipeline.

---

*Generated as a planning artifact; contains no code changes.*
