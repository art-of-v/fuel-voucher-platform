# FuelFlow — Architecture Evolution Analysis

**Author:** Solutions Architect  
**Date:** 2026-02-23  
**Assumption:** Complete system knowledge. No re-discovery.  
**Primary question:** Monolith, Modular Monolith, or Microservices?

---

## 1. Executive Decision

**Recommendation: Modular Monolith with targeted async extraction.**

Do not decompose into microservices. Do not adopt Kafka, RabbitMQ, or any distributed messaging broker. The event-driven pattern already present in the system (Redis Streams + DB Outbox) is correct in design and sufficient in capacity for the foreseeable future.

The system requires three concrete changes — none of which require microservices:

1. **Complete the modular monolith migration** that is already 80% done (finish porting `routes.ts` into `router.ts`, delete the dual-routing system)
2. **Extract the PDF import pipeline** into an isolated async worker (Cloud Run Job, triggered by Pub/Sub) — this is justified today because it blocks the event loop, not because of team scaling or service decomposition philosophy
3. **Keep the FulfillmentConsumer in-process** but make it stateless-ready by externalizing its queue (Redis Streams already does this correctly)

The system at its current and projected scale does not meet any threshold that justifies distributed services. Introducing microservices now would be a net negative: it would add thousands of lines of infrastructure code, multiply operational complexity, require a distributed tracing stack, and produce no measurable user-facing benefit at the current and near-term traffic volumes.

**When to revisit this decision:** When two or more of the thresholds in §5 are crossed simultaneously.

---

## 2. Current Risks

These are the real risks. They are all solvable within a monolith.

### Risk 1: Dual Routing System — Maintenance Hazard

The coexistence of `routes.ts` (907 lines, legacy) and `router.ts` (Clean Architecture) creates a system where authentication logic is implemented twice, endpoint coverage differs between flag states, and every developer must maintain a mental model of which file is authoritative. This is not a monolith problem — it is an incomplete refactoring problem. Two microservices with the same defect would be worse, not better.

**Resolution within monolith:** Complete the migration. Delete `routes.ts`. One code path, forever.

### Risk 2: ImportOrchestrator Blocks the Event Loop

`pdfjs-dist` renders PDF pages synchronously on the Node.js main thread. During a large PDF import (50+ pages), the Express event loop is blocked for seconds at a time, causing latency spikes for all concurrent API requests. This is a real, measurable production risk.

This is **not** a reason to introduce microservices. It is a reason to move CPU-bound work off the main thread. The solution is a Cloud Run Job with a Pub/Sub trigger — a 2-day engineering task — not a distributed system redesign.

### Risk 3: In-Process Stateful Services Prevent Horizontal Scaling

The FulfillmentConsumer and rate limiter are in-process singletons. A second backend instance creates two competing consumers and disconnected rate limit state. This prevents horizontal scaling.

Again, **not** a microservices problem. Resolution:
- Rate limiter: Replace in-memory `Map` with Redis `INCR`/`EXPIRE` (2 hours of work)
- FulfillmentConsumer: Redis Streams consumer groups are already designed for multiple consumers — remove the singleton guard, assign each instance a unique consumer name using the hostname

### Risk 4: No Admin Authentication

The admin API has zero authentication. This is a security defect, not an architectural concern. Microservices do not solve it — adding an auth middleware does.

### Risk 5: Session Coupling to Single Backend Instance

Sessions stored in PostgreSQL (via `connect-pg-simple`) are shared across instances, so this is already solved. But because `SameSite=None; Secure` cookie requirements make cross-device auth complex, replacing sessions with JWT is worth planning for medium scale. JWT is implementable in the monolith without service decomposition.

---

## 3. Microservices Analysis

### What Microservices Would Give This System

| Benefit | Applicable to FuelFlow? | Reality |
|---|---|---|
| Independent deployability per service | ❌ | There is one team. Independent deployability matters when multiple teams need to ship without coordination. One team deploying one monolith is strictly faster. |
| Independent scaling per service | ⚠️ Partially | The import worker genuinely needs independent scaling. The API and fulfillment consumer do not have meaningfully different scaling curves at current volume. |
| Technology heterogeneity | ❌ | The entire stack is TypeScript. There is no reason to use different languages or runtimes per service. |
| Fault isolation | ⚠️ Partially | If the import pipeline crashes a microservice, it doesn't take down the API. This benefit is real but achievable by running the import in a separate process (Cloud Run Job) without full service decomposition. |
| Team autonomy | ❌ | There is no evidence of multiple teams with conflicting deployment needs. |

### What Microservices Would Cost This System

| Cost | Impact |
|---|---|
| Network hops between services | Every inter-service call: +2–10ms latency, +failure mode, +retry logic needed |
| Distributed transactions | Voucher assignment today is a single DB transaction. Split across services, it requires saga pattern or two-phase commit. Both are significantly more complex and failure-prone than the current approach. |
| Distributed tracing | Without Jaeger/Zipkin/Cloud Trace, debugging a bug that spans 3 services is 10× harder than debugging it in one process. Setting up and maintaining a tracing stack is a non-trivial operational investment. |
| Service discovery & routing | Kubernetes or a service mesh, or a catalog of Cloud Run URLs. Either way: operational overhead that does not exist today. |
| Local development complexity | `npm run dev` currently starts everything. Microservices require docker-compose with 5+ services, or mocking inter-service calls, or running a partial stack. Onboarding new developers doubles in time. |
| Operational surface area | 1 monolith to monitor, alert on, and deploy. 5 microservices = 5× monitoring configurations, 5× CI pipelines, 5× secrets management surfaces. |

### The Honest Assessment

At FuelFlow's current scale and team size, microservices would deliver **zero user-facing improvement** while consuming an estimated **3-6 months of engineering time** to implement correctly (service boundaries, shared libraries, inter-service auth, distributed tracing, CI/CD per service). That engineering time spent on the monolith instead would deliver: JWT auth, push notifications, admin authentication, complete test coverage, and BullMQ job queue.

**Microservices are not wrong for FuelFlow at 500,000 users with 5 independent engineering teams. They are wrong today.**

### When Microservices Become Justified (Specific Thresholds)

All three of the following must be true simultaneously:

1. **Team size ≥ 4 independent contributors** working on different parts of the system with genuine coordination overhead (blocked deploys, merge conflicts in shared code, feature flag complexity)
2. **Monthly active users ≥ 50,000** creating measurable and distinct scaling requirements between components (e.g., the API scales to 50 instances while the import worker needs only 2)
3. **A genuine domain boundary exists** where one service's data would never be needed in another service's transaction (currently, fulfillment needs orders, vouchers, and users in a single transaction — splitting these would require distributed transactions)

If only one or two of these are true, the cost of microservices exceeds the benefit.

---

## 4. Messaging / Queue Analysis

### Current State

FuelFlow already uses an event-driven pattern for its most critical async workflow (voucher fulfillment):

```
Order created → Redis XADD → FulfillmentConsumer XREADGROUP → Voucher assigned
                    ↓ (fallback)
              DB outbox INSERT → Polling consumer → Voucher assigned
```

This is correct and well-implemented. The question is whether additional messaging infrastructure should be added, and what technology to use.

### Apache Kafka — Evaluation

**What Kafka is for:** High-throughput, ordered, replayed event streams. Financial trading systems. IoT telemetry. Audit logs with millions of events per second. Multi-consumer fan-out with guaranteed ordering.

**FuelFlow's messaging volume:**
- Order fulfillment events: estimated 100–10,000/day at medium scale
- Import completion events: < 10/day (admin-initiated)
- Voucher assignment events: 1:1 with fulfillment events

At 10,000 events/day (7 events/minute), Kafka is 3–4 orders of magnitude overprovisioned. Kafka's minimum viable deployment is 3 Kafka brokers + ZooKeeper (or KRaft mode), requiring dedicated VMs, monitoring, partition management, and consumer group coordination. This is not a managed service on GCP — Confluent Cloud starts at $0.11/CKU-hour (~$80/month minimum).

**Verdict:** Kafka is massively overengineered for FuelFlow at any scale less than hundreds of thousands of transactions per hour. Do not use it.

### RabbitMQ — Evaluation

**What RabbitMQ is for:** Complex routing topologies (topic exchanges, fanout, dead-letter queues), request-reply patterns, polyglot consumer support. Traditional task queue with acknowledgements.

**FuelFlow's routing needs:** Simple. One producer (order creation), one consumer type (fulfillment). No complex routing required.

RabbitMQ adds: a broker to manage, a monitoring stack (`rabbitmq_management`), queue durability configuration, connection pool management, and debugging complexity when messages are lost in exchanges.

**Verdict:** RabbitMQ solves problems FuelFlow does not have. Do not use it.

### Cloud Pub/Sub (GCP) — Evaluation

**What Pub/Sub is for:** Decoupling async job submission from processing in a cloud-native, managed environment. Triggering Cloud Run Jobs. Fan-out to multiple subscribers.

**FuelFlow's use case:** Admin uploads PDFs → backend publishes one message → Cloud Run Job processes it. This is exactly Pub/Sub's target use case.

Benefits: Zero infrastructure to manage, $0 at FuelFlow's volume (first 10GB/month free), native Cloud Run Job trigger, automatic retry with exponential backoff, dead-letter topic support.

**Verdict:** Use Pub/Sub for the import job trigger. This is the only workflow that needs it.

### Redis Streams — Keep

Redis is already in the system for sessions, caching, and rate limiting. Redis Streams for the fulfillment event bus is correct: low latency (sub-millisecond), persistent (configurable), consumer group support, and no additional infrastructure cost. The volume (thousands of events/day) is well within Redis Streams' capacity.

**Verdict:** Keep Redis Streams for voucher fulfillment. Do not replace it with anything.

### Decision: What MUST Be Async vs What MUST Stay Synchronous

**Must be async (and already is or should be):**

| Workflow | Reason | Mechanism |
|---|---|---|
| Voucher fulfillment assignment | Inventory may not be available at purchase time; user does not need to wait | Redis Streams + FulfillmentConsumer ✅ |
| PDF import processing | CPU-bound work that blocks the event loop; can take minutes | Cloud Run Job + Pub/Sub ⬆️ (needs extraction) |
| SMS verification sending | External API call; failure should not fail the auth request | Fire-and-forget async call; already done ✅ |
| Import backfill of pending orders | Triggered by new inventory arriving; must not block the import response | Redis VOUCHERS_IMPORTED event ✅ |

**Must stay synchronous:**

| Workflow | Reason |
|---|---|
| OTP verification + session creation | User is waiting on screen; strong consistency required (verify code, create session, return user) |
| Purchase record creation | Must return purchase ID synchronously for the next step (simulate payment) |
| Payment simulation | Must return status synchronously so the mobile app can continue the checkout flow |
| Admin station/package CRUD | Simple CRUD; async would add complexity with no benefit |
| Voucher mark-as-used | User is at the fuel pump; synchronous confirmation is required |

**Workflows that COULD be async but should not be yet:**

| Workflow | Why not yet |
|---|---|
| Push notification on fulfillment | Beneficial, but requires Expo push integration; implement as enhancement, not architectural change |
| Referral bonus crediting | Low frequency; synchronous is fine until millions of referrals/day |
| Session cleanup (old session deletion) | Already handled by PostgreSQL store pruning; no action needed |

---

## 5. Recommended Architecture

### The Boundary That Matters: One Real Service Split

The only component that has a genuine operational justification for process isolation today is the **PDF Import Pipeline**. It has:
- Different compute profile (CPU-bound vs I/O-bound)
- Different scaling requirements (burst, infrequent vs sustained, frequent)
- Different failure modes (a crashed import worker must not cascade to user-facing API)
- Different runtime requirements (potentially needs higher memory for `pdfjs-dist`)

Everything else — auth, purchases, vouchers, fulfillment, user management — shares data in tight transactional boundaries. Splitting these would require distributed transactions or eventual consistency where the business currently requires strong consistency (voucher assignment, purchase creation).

### Target State: Modular Monolith + One Isolated Worker

```
┌─────────────────────────────────────────────────────────────────┐
│                    FuelFlow Monolith (Cloud Run)                │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  HTTP Layer (router.ts — single, unified)                │  │
│  │  AuthController · PurchaseController · VoucherController │  │
│  │  UserController · AdminControllers · SyncController      │  │
│  └──────────────────────┬───────────────────────────────────┘  │
│                         │                                       │
│  ┌──────────────────────▼───────────────────────────────────┐  │
│  │  Application Services                                    │  │
│  │  AuthService · PurchaseService · VoucherService          │  │
│  └──────────────────────┬───────────────────────────────────┘  │
│                         │                                       │
│  ┌──────────────────────▼───────────────────────────────────┐  │
│  │  Infrastructure                                          │  │
│  │  Drizzle Repos · Redis Client · Pino Logger              │  │
│  └──────────────────────┬───────────────────────────────────┘  │
│                         │                                       │
│  ┌──────────────────────▼───────────────────────────────────┐  │
│  │  FulfillmentConsumer (in-process, Redis Streams)         │  │
│  │  Runs as async loop alongside Express server             │  │
│  │  Made multi-instance safe via Redis consumer groups      │  │
│  └──────────────────────────────────────────────────────────┘  │
└──────────────────────────────────┬──────────────────────────────┘
                 writes            │           reads/writes
        ┌────────────────────┐    │    ┌───────────────────────────┐
        │  Pub/Sub Topic     │    │    │     PostgreSQL (Cloud SQL) │
        │  import-jobs       │    └───▶│     Redis (Memorystore)   │
        └─────────┬──────────┘         └───────────────────────────┘
                  │ triggers
┌─────────────────▼──────────────────────────────────────────────┐
│              Import Worker (Cloud Run Job)                      │
│              Isolated process · 2 vCPU · 2Gi RAM              │
│                                                                 │
│  Receives: { jobId, gcsFilePath }                              │
│  1. Download PDF from GCS                                      │
│  2. convertPdfToImages() — pdfjs-dist, CPU-bound              │
│  3. scanQrsFromPdf() — jsqr                                   │
│  4. analyzeVoucherImage() — Gemini API (async, non-blocking)  │
│  5. vouchersRepository.create() × N                           │
│  6. Publish VOUCHERS_IMPORTED to Redis Stream                 │
│  7. Update import_jobs record                                  │
└────────────────────────────────────────────────────────────────┘
```

### Module Boundaries Within the Monolith

The partially-completed Clean Architecture provides the correct internal module boundaries. These are **enforced by convention and code review**, not by network boundaries:

| Module | Owns | Does Not Touch |
|---|---|---|
| Auth | `users`, `phone_verifications`, `sessions` | Voucher data, order data |
| Purchases | `purchases`, initiates `orders` creation | Voucher data directly |
| Vouchers | `vouchers`, `fulfillments` | Purchase flow |
| Orders | `orders`, `outbox` | User credentials, session data |
| Admin | All read, all write | Nothing off-limits (admin context) |
| Import | `import_jobs`, creates `vouchers` | Purchase or user data |

These are the service boundaries that DO NOT require network separation because they share a PostgreSQL database and must participate in transactions together. The day these boundaries require independent deployment is the day to split them.

---

## 6. Migration Plan (Incremental, No Big Bang)

### Step 1: Complete the Modular Monolith (Week 1–2)

**Objective:** One routing system. One auth middleware. No dual-system confusion.

Actions:
1. Port the 4 remaining endpoints from `routes.ts` into `router.ts` controllers
2. Delete `purchase.complete` endpoint (security hole, no replacement needed)
3. Delete `purchases/session/:sessionId` endpoint (unauthenticated data leak)
4. Delete `routes.ts` entirely
5. Remove `USE_REFACTORED_ARCHITECTURE` feature flag from `index.ts` and `render.yaml`
6. Rename `purchases.sessionId` column → `userId` with migration

**Outcome:** Single, coherent codebase. Every developer understands the full routing surface. Cognitive load drops by 50%.

### Step 2: Externalize In-Process State (Week 2–3)

**Objective:** The monolith can run as multiple instances safely.

Actions:
1. Replace in-memory rate limiter `Map` with Redis `INCR`/`EXPIRE`
2. Remove `FulfillmentConsumer`'s singleton guard; assign consumer name from `os.hostname()`
3. Replace session storage with Redis (Memorystore) — move away from PostgreSQL session table
4. Add `OTEL_RESOURCE_ATTRIBUTES=service.instance.id=$(hostname)` to Cloud Run config

**Outcome:** Any number of backend instances can run simultaneously. Horizontal scaling is now safe.

### Step 3: Extract Import Worker (Week 3–4)

**Objective:** PDF import no longer blocks the API event loop.

Actions:
1. Create `admin-panel/import-worker/` directory — a separate Node.js package
2. Move `ImportOrchestrator` + `convertPdfToImages` + `analyzeVoucherImage` + `scanQrsFromPdf` into the new package
3. Modify `ImportController` to: (a) store uploaded PDF to GCS, (b) publish message to Pub/Sub with `{ jobId, gcsPath }`, (c) return immediately
4. Import Worker: subscribe to Pub/Sub, process file from GCS, update `import_jobs` record
5. Create separate `Dockerfile` for import worker
6. Deploy as Cloud Run Job in CI/CD pipeline alongside main service

**Outcome:** A 100-page PDF import no longer degrades API response times. Import worker can be given 2 vCPU and 2Gi RAM without affecting API cost. API container stays lean at 512Mi.

This is the **only process boundary that is justified at current scale.**

### Step 4: Auth Hardening + JWT (Month 2)

**Objective:** Stateless auth that works correctly on all devices and enables future scaling.

Actions:
1. Fix OTP generation (immediate — see ARCHITECTURE_REVIEW.md)
2. Implement access token (15-minute JWT) + refresh token (30-day, stored in `user_tokens` table)
3. Mobile app: store access token in SecureStore, auto-refresh on 401
4. Admin API: validate JWT + admin claim (separate admin user table with role)
5. Retire session-based auth after 60-day deprecation window

**Outcome:** Auth works correctly on physical devices without cookie domain complexity. Enables true stateless horizontal scaling. Eliminates the `sessions` table dependency.

### Step 5: Push Notifications (Month 2–3)

**Objective:** Eliminate voucher-status polling from the mobile app.

Actions:
1. Integrate Expo Push Notifications (server-side SDK: `expo-server-sdk`)
2. Mobile app registers push token on login → stored in `users.expoPushToken`
3. FulfillmentConsumer: after assigning voucher, call Expo push API
4. Remove the `/api/sync/orders` polling loop from `my-codes.tsx`

**Outcome:** Server load from polling (at 1,000 users: ~100 RPS to sync endpoint) drops to ~0 RPS. Users get sub-second notification when their voucher is ready.

### Step 6: Full GCP Migration (Month 3)

Deploy everything from `DEPLOYMENT_STRATEGY.md`. The import worker is now already a separate Cloud Run Job. The monolith is now stateless. The migration is architectural validation, not architectural change.

---

## 7. Trade-offs and Costs

### Staying as Modular Monolith

| Benefit | Downside |
|---|---|
| One repository, one deployment | Any bug can affect all features |
| Simple local dev (`npm run dev`) | Team must coordinate on shared codebase |
| Shared DB transactions (strong consistency) | DB becomes bottleneck at very high scale |
| One tracing context per request | Cannot scale components independently |
| Low operational overhead | Hard ceiling on team autonomy |

**At current scale:** All benefits, no meaningful downsides.  
**At 10 engineers with 200,000 MAU:** Downsides start to appear, but are manageable.  
**At 30 engineers with 2,000,000 MAU:** The modular monolith becomes genuinely limiting.

### Extracting Import Worker (Recommended)

| Benefit | Cost |
|---|---|
| API unblocked during import | Two containers to build, deploy, monitor |
| Import can scale CPU independently | Pub/Sub integration adds ~200 lines of code |
| Import failures don't crash API | GCS dependency for file staging |
| Better observability per workload | Local dev requires Pub/Sub emulator or mocking |

**Net assessment:** Justified today. Low cost, high impact. This is not overengineering — it solves a real, current problem (event loop blocking).

### NOT Adopting Kafka

| Benefit of skipping Kafka | What is given up |
|---|---|
| No Kafka cluster to manage | Message retention beyond Redis Stream maxlen (not needed) |
| No ZooKeeper/KRaft complexity | Log compaction (not needed) |
| No partition key design required | 100k+ messages/second throughput (not needed) |
| No Confluent licensing cost | Consumer replay from arbitrary offsets (partially available via outbox) |

**Redis Streams provides 99% of what FuelFlow needs at 1% of the operational cost.**

### NOT Adopting Microservices

| Benefit of staying monolith | What is given up |
|---|---|
| One codebase, one context | Independent deployment per domain |
| Shared DB transactions | Independent scaling per service |
| Simple debugging | Technology heterogeneity |
| Low onboarding time | Team autonomy at scale |
| One CI/CD pipeline | Blast radius isolation |

**At < 5 engineers and < 200,000 MAU, every item in the "given up" column provides zero value.**

---

## 8. Decision Tree

```
                        START
                          │
              Is the team > 4 independent
              engineers deploying to the
              same codebase?
              /          \
            NO            YES
            │              │
            │         Are there > 50,000 MAU AND
            │         measurably different scaling
            │         curves between components?
            │              /        \
            │            NO          YES
            │             │           │
            │             │     Do business domains have
            │             │     clear bounded contexts with
            │             │     no cross-domain transactions?
            │             │           /        \
            │             │         NO          YES
            │             │          │           │
            │             │          │     → SPLIT SPECIFIC SERVICES
            │             │          │       Start with the service
            │             │          │       with the clearest boundary
            │             │          │       (Import Worker first)
            │             │          │
            │             └──────────┘
            │                   │
            └───────────────────┘
                          │
                    STAY MONOLITH
                          │
              Does any workflow block the
              main event loop (CPU-bound)?
              /          \
            NO            YES
            │              │
            │         → EXTRACT that workflow
     Continue as             to isolated process
     modular monolith        (Cloud Run Job)
            │              │
            └──────────────┘
                          │
              Is any in-process state preventing
              horizontal scaling (rate limiter,
              singleton consumers)?
              /          \
            NO            YES
            │              │
            │         → EXTERNALIZE to Redis
     No change              (not microservices)
     needed now             │
            └──────────────┘
                          │
              Is the existing async
              event pattern (Redis Streams)
              hitting throughput limits?
              (> 100,000 events/day)
              /          \
            NO            YES
            │              │
     No new messaging  → MIGRATE to Cloud Pub/Sub
     needed            or BullMQ (not Kafka)
```

---

## 8. The "Do Nothing" Scenario

If no architectural changes are made, this is what will happen at scale:

**At 5,000 MAU (1–3 months post-launch):**
- Admin imports large PDF batches → event loop blocked for 10–30 seconds → mobile users experience timeouts during import windows → trust in the platform degrades
- Rate limiter resets on every deployment → burst attacks possible post-deploy
- Dual routing system causes a silent auth regression when a developer accidentally edits the wrong route file → incident

**At 20,000 MAU (3–6 months post-launch):**
- Second backend instance deployed to handle load → two FulfillmentConsumers process the same orders → vouchers double-assigned → inventory discrepancy → fraud or shortfall
- Session store (PostgreSQL) becomes a hotspot as every request reads the `sessions` table → query latency creeps up → API P95 degrades
- Admin panel exposed to enumeration by competitors or bad actors who discover the API URL

**At 50,000 MAU (6–12 months post-launch):**
- Voucher table reaches 500,000+ rows without composite indexes → fulfillment query goes from 5ms to 800ms → user waits 1+ second after payment to see order status → checkout abandonment rises
- Import worker is running in the same process as a service under real load → a single large PDF import brings API to 0 RPS for 30 seconds → SLA breach

**None of these outcomes require microservices to prevent.** They require: extracting the import worker, adding Redis indexes, completing the monolith migration, and externalizing in-process state. All of this is 4–6 weeks of focused engineering work within the existing architecture.

The "do nothing" scenario on microservices is: nothing bad happens from not adopting them. The "do something (microservices)" scenario at current scale is: 3–6 months of infrastructure work that produces no user-facing improvement and introduces distributed system failure modes that did not previously exist.

**The correct action is not "do nothing" and not "go microservices." It is "finish what was started and fix what is broken."**
