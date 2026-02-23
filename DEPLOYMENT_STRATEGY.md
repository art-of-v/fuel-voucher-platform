# FuelFlow — Production Deployment Strategy

**Author:** Solutions Architect  
**Date:** 2026-02-23  
**Audience:** Engineering & Founding Team  
**Assumption:** Full codebase and architecture knowledge from prior review. No re-discovery.

---

## 1. Executive Summary

**Recommended stack: Google Cloud Platform (GCP) with Cloud Run + Cloud SQL + Memorystore + Pub/Sub.**

FuelFlow is a mobile-first fuel voucher commerce system serving primarily the Ukrainian market. Its critical workload characteristics are:

- **Bursty, not sustained:** Traffic spikes at purchase moments (morning commute, weekend refuels), with long idle periods. Serverless/scale-to-zero is perfect.
- **I/O-bound backend:** The Node.js API is almost entirely async I/O (DB queries, Redis, Gemini API). It does not need beefy CPUs; it needs fast cold starts and cheap idle cost.
- **One genuinely CPU-heavy job:** PDF → image rendering (`pdfjs-dist`) during voucher import. This job is infrequent (admin-initiated) and should run in isolation.
- **Strong consistency required:** Voucher assignment (`SELECT ... FOR UPDATE SKIP LOCKED`) requires a proper ACID-compliant relational database. PostgreSQL is correct. NoSQL is wrong here.
- **Single geographic market (Ukraine/EU):** One region deployment. No global CDN complexity needed for the API. Static assets on CDN only.

The Render.com setup that currently exists is appropriate for the MVP stage but will become expensive and operationally limited at scale. This document defines the path from Render → GCP at each growth stage.

**Cost summary (production-grade, low-to-medium traffic):**

| Stage | Users | Monthly Cost | Primary spend |
|---|---|---|---|
| MVP (Render, today) | < 1,000 | ~$20–50 | Render hobby tiers |
| Early (GCP min viable) | 1,000–10,000 | ~$80–150 | Cloud SQL, Cloud Run |
| Medium scale | 10,000–100,000 | ~$300–600 | Cloud SQL HA, Memorystore |
| High scale | 100,000+ | ~$1,200–2,500 | Cloud Run scaling, Load Balancer, CDN |

---

## 2. Cloud Provider Selection

### Winner: Google Cloud Platform (GCP)

#### Why GCP beats AWS and Azure for this system:

**Cloud Run is uniquely suited to Node.js bursty APIs:**  
Cloud Run bills per 100ms of CPU time, scales to zero between requests, and has cold start times of 200–400ms for a Node.js container. This directly matches FuelFlow's traffic pattern: bursts of API calls, long idle periods. AWS Lambda has similar economics but with strict 15-minute execution limits (which conflicts with the ImportOrchestrator's potentially long PDF jobs) and a more complex VPC/ALB setup for HTTP. AWS ECS Fargate is closer to Cloud Run but significantly more expensive at low scale.

**Cloud SQL (PostgreSQL) is the managed database with the least operational overhead:**  
Single-click HA, automated backups, point-in-time recovery, and built-in connection pooling via the Cloud SQL Auth Proxy. At small scale (~10 connections needed), Cloud SQL's `db-f1-micro` costs $7/month. Equivalent RDS on AWS starts at $15/month for `db.t3.micro`. Azure Database for PostgreSQL is comparable in price but has a weaker ecosystem and slower support response times.

**Gemini AI is Google's own product:**  
FuelFlow already uses Gemini for the OCR import pipeline. Running on GCP means Gemini API calls stay on Google's internal network, reducing latency and eliminating egress charges for those API calls.

**GCP's free tier is more useful for this workload:**  
Cloud Run: 2 million requests/month free. Cloud Logging: first 50 GiB/month free. These are meaningful numbers for an early-stage product.

**Firebase Auth as a future option:**  
The current OTP/phone auth system is custom and has critical weaknesses (covered in ARCHITECTURE_REVIEW.md). Firebase Authentication provides production-grade phone OTP out of the box, integrates natively on GCP, and is free up to 10,000 verifications/month. This is a natural future migration path.

#### When to choose AWS instead:

- If the team has deep AWS expertise and existing AWS accounts with consolidated billing discounts
- If HIPAA/SOC2 compliance is required and the team already has an AWS compliance framework
- At very high scale (>1M users), AWS's breadth of services (SQS, ElastiCache, EKS) has more operational tooling

#### When to choose Azure instead:

- Never, for this stack. Azure's managed Node.js/PostgreSQL/Redis story is the weakest of the three, and its pricing at small scale is not competitive.

---

## 3. Infrastructure Architecture

```
                        ┌─────────────────────────────────────────┐
                        │          USERS (Mobile App)             │
                        │     iOS / Android (Expo native)         │
                        └──────────────────┬──────────────────────┘
                                           │ HTTPS
                        ┌──────────────────▼──────────────────────┐
                        │      Google Cloud CDN + Load Balancer   │
                        │  Global HTTPS LB · SSL cert managed     │
                        │  Static file cache (admin frontend)     │
                        └──────┬───────────────────┬──────────────┘
                               │                   │
                ┌──────────────▼───────┐  ┌────────▼──────────────┐
                │   Public API         │  │   Admin API            │
                │   Cloud Run          │  │   Cloud Run            │
                │   (min 0, max 10)    │  │   (min 0, max 2)       │
                │   Node.js backend    │  │   Same codebase        │
                │   /api/auth          │  │   /api/admin/*         │
                │   /api/purchases     │  │   API key protected    │
                │   /api/vouchers      │  └────────┬──────────────┘
                └──────┬───────────────┘           │
                       │                           │
                ┌──────▼───────────────────────────▼──────────────┐
                │                Cloud SQL (PostgreSQL 16)         │
                │              Private IP · HA standby            │
                │            Connection via Cloud SQL Proxy        │
                └────────────────────────┬─────────────────────────┘
                                         │
                ┌────────────────────────▼─────────────────────────┐
                │              Memorystore for Redis               │
                │     Session store · Redis Streams · Cache        │
                │              Private IP access only              │
                └────────────────────────┬─────────────────────────┘
                                         │
                ┌────────────────────────▼─────────────────────────┐
                │         Import Worker (Cloud Run Job)            │
                │   Triggered by Cloud Pub/Sub · CPU-isolated      │
                │   Handles pdfjs-dist rendering + Gemini OCR      │
                └──────────────────────────────────────────────────┘

          ┌────────────────────────────────────────────────────────┐
          │           Cloud Storage (GCS)                          │
          │   Uploaded PDFs · Processed voucher CSVs · Exports     │
          └────────────────────────────────────────────────────────┘

          ┌────────────────────────────────────────────────────────┐
          │           Observability Stack                          │
          │   Cloud Logging · Cloud Monitoring · Cloud Trace       │
          │   Error Reporting (via Pino + structured JSON logs)    │
          └────────────────────────────────────────────────────────┘
```

**Region:** `europe-west3` (Frankfurt) — closest to Ukrainian users with best latency. Supabase's EU West (Ireland) is 30–40ms from Frankfurt; acceptable for DB connections with the Cloud SQL Auth Proxy.

---

## 4. Service-by-Service Breakdown

### 4.1 API Backend — Cloud Run

**Current:** Render web service (single instance, always-on)  
**Target:** GCP Cloud Run

Cloud Run is the correct choice because:
- The Node.js Express app is already containerized (Dockerfile exists)
- It spawns instances per-request and scales to zero — ideal for the current traffic pattern
- No Kubernetes complexity; no cluster to manage; no node pool to size
- VPC connector enables private IP access to Cloud SQL and Memorystore

**Configuration:**
```yaml
# cloud-run-api.yaml (conceptual)
service: fuelflow-api
image: europe-west3-docker.pkg.dev/fuelflow/api:latest
memory: 512Mi         # Node.js with Drizzle + pino; 256Mi is too tight
cpu: 1                # 1 vCPU per instance
concurrency: 80       # Express handles concurrent requests; 80 is safe
min-instances: 0      # Scale to zero in off-peak hours (saves ~$40/month)
max-instances: 10     # Hard ceiling; adjust based on DB connection limit
timeout: 300s         # Extended for potential long import status checks
```

**Cost (low scale, ~50k requests/month):**  
Cloud Run pricing: $0.00002400/vCPU-second + $0.00000250/GB-second  
At 100ms average response time, 512Mi, 1 vCPU:  
50,000 requests × 0.1s × ($0.000024 + $0.00000125) ≈ **$0.13/month**  
Plus free tier (first 180,000 vCPU-seconds/month free):  
**Effective cost at early stage: $0**

**At medium scale (5M requests/month):** ~$55/month

**Cold start concern:** Node.js on Cloud Run cold starts in 300–600ms. With `min-instances: 0`, the first request after idle will feel slow. For a mobile app this is acceptable. If P99 latency is a product requirement, set `min-instances: 1` (~$12/month additional cost).

---

### 4.2 Admin Frontend — Cloud Storage + Cloud CDN

**Current:** Render static service  
**Target:** GCS bucket + Cloud CDN

The admin frontend is a Vite-built static SPA. It does not need a server. Host it in a GCS bucket with a Cloud CDN frontend.

```bash
# Deploy command
vite build
gsutil rsync -r -d dist/ gs://fuelflow-admin-frontend/
```

**Cost:**  
GCS storage: negligible (< 1MB of HTML/JS/CSS)  
GCS operations: $0.004/10k read operations  
Cloud CDN: $0.0075/GB served (admin panel is < 500KB, so essentially free)  
**Total: < $1/month**

Benefits: Zero cold starts, instant global delivery, no server to patch.

---

### 4.3 Database — Cloud SQL for PostgreSQL

**Current:** Supabase PostgreSQL (session-mode pooler, eu-west-1)  
**Target:** Cloud SQL for PostgreSQL 16 in `europe-west3`

**Why migrate from Supabase:**
Supabase is excellent for greenfield projects but adds an abstraction layer and billing overhead at scale. Cloud SQL gives more control over instance sizing, backup windows, maintenance schedules, and pg extensions. The connection via Cloud SQL Auth Proxy handles IAM-based authentication, eliminating credentials in environment variables.

**Why not self-hosted PostgreSQL on a VM:**  
Automated failover, point-in-time recovery, and read replica creation all require significant operational investment on a VM. Cloud SQL handles these for ~25% cost premium over a raw VM — worth it for a startup.

**Instance sizing:**

| Stage | Instance | vCPU | RAM | Storage | Monthly cost |
|---|---|---|---|---|---|
| Early | `db-f1-micro` | 1 (shared) | 0.6 GB | 10 GB SSD | ~$9 |
| Medium | `db-g1-small` | 1 (shared) | 1.7 GB | 20 GB SSD | ~$28 |
| Scale | `db-custom-2-4096` | 2 | 4 GB | 50 GB SSD | ~$90 |
| HA | + HA standby | same | same | same | 2× price |

**Connection pooling:**  
Use [Cloud SQL Auth Proxy](https://cloud.google.com/sql/docs/postgres/sql-proxy) sidecar in Cloud Run. It handles connection multiplexing and IAM auth. Set max pool connections to `(max-instances × concurrency) / 5` to prevent connection exhaustion.

For medium+ scale, add **PgBouncer** (transaction-mode pooling) as a Cloud Run sidecar, reducing connections from 80 per instance to ~10.

**Backup strategy:**
- Automated daily backups (Cloud SQL managed): 7-day retention included in cost
- Point-in-time recovery window: 7 days
- Weekly manual export to GCS bucket for 90-day cold retention: ~$0.02/month

**Read replica for admin dashboard:**  
At medium scale, add one `db-f1-micro` read replica for admin panel queries (voucher listing, purchase history). Routes all `GET /api/admin/*` queries to the replica.  
Cost: ~$9/month additional.

---

### 4.4 Redis — Memorystore for Redis

**Current:** Redis on Render (separate service) or absent  
**Target:** GCP Memorystore for Redis 7

Used for:
1. Session storage (replacing connect-pg-simple at scale)
2. Redis Streams for FulfillmentConsumer
3. Application-level caching (voucher lists, package catalog)
4. Distributed rate limiting

**Why not self-hosted Redis on a VM:**  
Memorystore provides automatic failover, persistence configuration, and private IP access with zero operational overhead. At the `BASIC` tier (no replication), it costs $0.016/GB-hour.

**Sizing:**
| Stage | Tier | Size | Monthly cost |
|---|---|---|---|
| Early | BASIC | 1 GB | ~$11 |
| Medium | STANDARD (HA) | 1 GB | ~$70 |
| Scale | STANDARD | 4 GB | ~$210 |

**Note:** At early stage, Memorystore is the second-largest cost item. If cost is paramount, use a `redis:alpine` container as a Cloud Run sidecar (no HA, no persistence, data lost on restart). For a startup, this is an acceptable risk given that Redis data (rate limits, streams) is ephemeral by design. Promotes to Memorystore when monthly revenue justifies $11/month.

**Cache strategy:**

```
Package catalog:   TTL 1 hour   (changes only on admin action)
Station list:      TTL 24 hours (rarely changes)
User vouchers:     TTL 60 seconds (balance between freshness and load)
Inventory counts:  TTL 30 seconds (admin dashboard)
Rate limit keys:   TTL = window duration (native)
Session store:     TTL = 24 hours (matches session maxAge)
```

---

### 4.5 PDF Import Worker — Cloud Run Jobs

**Current:** In-process `ImportOrchestrator` singleton in the main backend  
**Target:** Cloud Run Job (separate container)

The PDF import pipeline is the most operationally dangerous part of the system:
- `pdfjs-dist` renders PDFs into images — CPU-bound, blocks the Node.js event loop
- Gemini API calls are slow (2–10 seconds per image)
- A 100-page PDF can take 5–20 minutes to fully process
- Currently runs in the same process as the Express server, degrading all API response times during import

**Migration design:**

1. Admin uploads PDFs → backend creates import job record → publishes message to **Cloud Pub/Sub**
2. **Cloud Run Job** subscribes to Pub/Sub, processes the import in isolation
3. Progress updates written to Cloud SQL (`import_jobs` table)
4. Admin frontend polls `GET /api/vouchers/import-status/:id`

**Cloud Run Job config:**
```yaml
job: fuelflow-import-worker
image: europe-west3-docker.pkg.dev/fuelflow/import-worker:latest
memory: 2Gi       # pdfjs-dist is memory-hungry for large PDFs
cpu: 2            # 2 vCPU for render performance
timeout: 3600s    # Up to 1 hour for large batches
max-retries: 2
```

**Cost:**  
Cloud Run Jobs bill per CPU-second only when running.  
One 10-minute import job: 600s × 2 vCPU × $0.000024 = **$0.029**  
10 imports/month: **$0.29/month**

This is essentially free compared to the alternative of running a dedicated always-on worker.

---

### 4.6 Async Messaging — Cloud Pub/Sub

**Current:** Redis Streams (or DB outbox fallback)  
**Target:** Keep Redis Streams for fulfillment; add Cloud Pub/Sub for import jobs

**For voucher fulfillment (keep Redis Streams):**  
Redis Streams with consumer groups are the right tool here. The volume is low (hundreds to thousands of events/month), latency matters (users want vouchers fast), and Redis is already required for sessions and caching. Adding Pub/Sub for fulfillment would be over-engineering.

**For import jobs (add Pub/Sub):**  
Pub/Sub decouples the import API endpoint from the processing job, survives backend restarts, and triggers Cloud Run Jobs natively. It's the GCP-native event bus.

```
Cost: First 10 GB message data free/month
Volume at scale: < 100 messages/month (admin-initiated)
Cost: Effectively $0
```

---

### 4.7 Object Storage — Cloud Storage (GCS)

**Current:** Local filesystem (`uploads/` directory on Render, lost on redeploy)  
**Target:** GCS bucket

Used for:
- Uploaded PDF files (input to import pipeline)
- Processed voucher data archives (audit logs)
- Admin frontend static assets

**Bucket design:**
```
gs://fuelflow-uploads/           # PDF uploads, Pub/Sub triggers import job
gs://fuelflow-exports/           # Import results, error logs
gs://fuelflow-admin-frontend/    # Static SPA assets (public-read)
gs://fuelflow-backups/           # DB exports (private, lifecycle to Nearline after 30 days)
```

**Cost:**  
Standard storage: $0.020/GB/month  
At 10GB of PDFs: $0.20/month  
Nearline (>30 days): $0.010/GB/month — auto-lifecycle from Standard → Nearline

---

### 4.8 Networking — Global HTTPS Load Balancer

**Current:** Render handles TLS and routing  
**Target:** GCP Global HTTPS Load Balancer + Cloud Armor

**Why a Load Balancer:**
Cloud Run services are individually internet-accessible via their `.run.app` domain, but a Load Balancer provides:
1. Custom domain with managed Let's Encrypt certificate (`api.fuelflow.ua`)
2. Cloud CDN for static assets
3. URL-based routing (mobile API → Cloud Run API service; `/admin` → Cloud Run Admin service)
4. Cloud Armor WAF (rate limiting, bot protection, geo-blocking)

**Cost:**  
Load Balancer: $18/month (forwarding rule) + $0.008/GB processed  
Cloud Armor: $5/month + $0.75/million requests for managed rule sets  
**Total: ~$25–40/month**

**This is a real cost line.** At early stage, bypass the LB and use Cloud Run's direct `.run.app` URLs. Add the LB when: (a) custom domain is needed, (b) Cloud Armor WAF is needed, or (c) CDN for admin frontend hits noticeable spend.

---

## 5. Cost Optimization Plan

### Biggest Cost Drivers (in order)

1. **Cloud SQL** — always-on even with zero traffic. Primary cost at low scale.
2. **Global HTTPS Load Balancer** — $18/month baseline, independent of traffic.
3. **Memorystore** — always-on Redis instance.
4. **Cloud Run** — scales to zero; nearly free at low volume.
5. **Egress** — GCP charges $0.085/GB out of `europe-west3`. Keep the mobile app pointed at the same region. Gemini API calls stay internal (no egress).

### Concrete Savings

**Committed Use Discounts (CUDs):**  
Cloud SQL committed use: 25% discount for 1-year commit, 52% for 3-year commit.  
When: Apply after 3 months of stable usage with known DB size.  
Savings: $3–50/month depending on tier.

**Cloud Run min-instances = 0 during off-peak:**  
Ukrainian mobile app. Prime usage: 07:00–22:00 local time. Almost no traffic 23:00–06:00.  
Set `min-instances: 0` unconditionally. Accept the 300–600ms cold start for the first request after idle. This saves the cost of one always-warm instance (~$12–24/month).

**Cloud SQL tiered storage:**  
Storage automatically grows in Cloud SQL but does not shrink. Archive large tables (old `import_jobs` records, old `outbox` events) to GCS via a weekly cron job. Avoids paying SSD prices for cold historical data.

**Autoscaling caps:**  
Set hard `max-instances: 10` on the API Cloud Run service. This prevents runaway costs from a traffic spike or a bug causing an infinite request loop. At 100ms average response, 80 concurrency per instance = 8,000 RPS max throughput — more than adequate.

**GCS Lifecycle policies:**
```yaml
# Automatically move old PDFs to cheaper storage
lifecycle:
  rule:
    - condition: { age: 30, matchesStorageClass: ["STANDARD"] }
      action: { type: "SetStorageClass", storageClass: "NEARLINE" }
    - condition: { age: 365 }
      action: { type: "Delete" }
```

---

## 6. Performance Strategy

### Latency Budget (target: P95 < 300ms API response)

| Component | Expected latency | Optimization |
|---|---|---|
| TLS handshake | 10–30ms | Cloud CDN keeps connections warm |
| Load Balancer routing | 1–2ms | GCP global anycast |
| Cloud Run cold start | 0ms (warm) / 400ms (cold) | `min-instances: 1` if P95 matters |
| Node.js request processing | 1–5ms | Non-blocking; already good |
| Cloud SQL query (indexed) | 2–10ms | With proxy: +5ms overhead vs direct |
| Redis cache hit | 0.5–2ms | Private IP, same zone |
| Total (warm, cache hit) | **~20–50ms** | — |
| Total (warm, DB query) | **~30–60ms** | — |

### Caching Hierarchy

```
Layer 1: In-process (Node.js Map)
  → Use for: compiled phone normalization regex, Drizzle query builders
  → TTL: process lifetime (no explicit cache needed)

Layer 2: Redis (Memorystore)
  → Use for: package catalog, station list, user vouchers, sessions, rate limits
  → TTL: 30s–24h depending on staleness tolerance

Layer 3: Cloud CDN
  → Use for: admin frontend static assets only
  → TTL: 1 year for hashed assets, 5 minutes for index.html
```

**Cache miss handling:** Never cache-or-fail. Always fall through to the database on a Redis miss. Redis unavailability degrades performance, not correctness.

### Mobile App Performance

The mobile app polls `/api/sync/orders` and `/api/vouchers/my` continuously to show fulfillment status. At 1,000 concurrent users polling every 10 seconds = 100 RPS to those two endpoints. This is the actual peak load at medium scale.

**Optimization:** Cache `/api/vouchers/my` in Redis with a 30-second TTL keyed by `userId`. Cache miss rate: ~3.3% (one real DB query per 30 seconds per user). At 1,000 users: 1,000 requests/10s = 100 RPS, with only ~3.3 DB queries/second. Negligible load.

**Long-term:** Replace polling with **Expo Push Notifications**. When a voucher is fulfilled (FulfillmentConsumer assigns a voucher), publish a push notification via Expo's push API. Eliminates polling entirely. Users get instant notification. Server load drops from 100 RPS to 0 RPS for the voucher fetch path.

### Regional Deployment

Single region (`europe-west3`) is correct. The user base is Ukrainian. Frankfurt has:
- 25–40ms ping from Kyiv
- 30–50ms ping from Lviv
- Lower latency than us-east or us-central by 100ms+

Do not add multi-region until there is evidence of user demand from outside Europe. Multi-region means 2× database costs and complex session routing.

---

## 7. Scaling Plan

### Stage 0 → 1: Current State (< 100 users, Render)

**Keep Render.** It is free/cheap, works, and requires zero ops effort.  
Monthly cost: $0–25 on hobby tier.  
Action required: Fix security issues (OTP, admin auth) regardless of platform.

### Stage 1 → 10: Early Growth (100–2,000 users/month)

**Migrate to GCP minimal viable setup (see §9).**  
Monthly cost: $50–120.  
Trigger: Render's hobby tier becomes unreliable (cold starts, memory limits), OR monthly active users exceed 500, OR the business has first revenue.

**Key actions:**
- Containerize backend (Dockerfile already exists ✅)
- Deploy to Cloud Run with `min-instances: 0`
- Migrate DB to Cloud SQL `db-f1-micro`
- Add Redis (Memorystore BASIC 1GB or sidecar)
- Move static admin frontend to GCS bucket

### Stage 2 → Medium Scale (2,000–20,000 users/month)

**Optimize existing GCP setup.**  
Monthly cost: $200–500.

**Key actions:**
- Upgrade Cloud SQL to `db-g1-small` with HA standby
- Add read replica for admin panel queries
- Set Cloud Run `min-instances: 1` to eliminate cold starts for mobile users
- Enable Redis cache for package catalog + user vouchers
- Move ImportOrchestrator to Cloud Run Job + Pub/Sub
- Add Global HTTPS Load Balancer + custom domain
- Apply 1-year Cloud SQL CUD

### Stage 3 → High Scale (20,000–200,000 users/month)

**Stateless API + dedicated fulfillment worker + push notifications.**  
Monthly cost: $800–2,500.

**Key actions:**
- Replace session cookies with JWT (eliminates Redis session dependency for horizontal scaling)
- Separate admin API into its own Cloud Run service with distinct IAM + scaling profile
- Move FulfillmentConsumer from in-process to dedicated Cloud Run service consuming Pub/Sub
- Add Expo Push Notifications (eliminates voucher-status polling)
- Enable Cloud CDN + Cloud Armor WAF on the Load Balancer
- Add Cloud SQL `db-custom-4-8192` with PgBouncer sidecar (transaction-mode pooling)
- Apply 3-year Cloud SQL CUD on primary instance

### Stage 4 → 1M+ users

At this scale, architecture decisions change fundamentally:
- Consider GKE Autopilot for the API (more control, better resource efficiency at high sustained load)
- Evaluate Cloud Spanner for the `vouchers` table if cross-region consistency is needed
- Implement explicit circuit-breakers between API and downstream services (DB, Redis, Gemini)
- Hire a dedicated SRE

This stage is out of scope for current planning.

---

## 8. Risks & Trade-offs

| Decision | Risk | Trade-off / Mitigation |
|---|---|---|
| Cloud Run scale-to-zero | 300–600ms cold start on first request after idle | Acceptable for mobile app; set `min-instances: 1` ($12/month) when product demands it |
| Single region (europe-west3) | Outage affects all users; GCP `europe-west3` SLA is 99.95% | Ukraine regulatory environment makes multi-region complex anyway; accept single-region risk |
| Cloud SQL instead of Supabase | Less built-in tooling (no auto-generated REST API, no real-time subscriptions) | FuelFlow doesn't use Supabase's realtime features; just uses raw PostgreSQL |
| Managed Cloud SQL vs self-hosted | 25–40% price premium vs raw VM | Eliminates backup/failover/maintenance overhead; correct for startup |
| Memorystore BASIC (no HA) at early stage | Redis data lost on maintenance event (~monthly) | Redis data is ephemeral by design (rate limits, streams); sessions survive in PG; acceptable |
| Pub/Sub for import jobs | Additional service dependency | Low volume (< 100 events/month); Pub/Sub is extremely reliable; low risk |
| Staying on GCP long-term | Vendor lock-in on managed services | Mitigated by containerized workloads (Cloud Run container can run anywhere); DB is standard PostgreSQL |

---

## 9. Minimal Viable Production Setup

**Cheapest possible setup that is safe for real users with real money.**

### Infrastructure

| Service | Config | Monthly cost |
|---|---|---|
| Cloud Run (API) | 512Mi, 1 vCPU, min=0, max=5 | ~$5–15 (traffic-dependent) |
| Cloud SQL | `db-f1-micro`, PostgreSQL 16, 10GB SSD, no HA | ~$9 |
| Memorystore Redis | BASIC 1GB (or Redis sidecar if cost-sensitive) | ~$11 (or ~$0) |
| GCS (static + uploads) | < 5GB total | ~$1 |
| Cloud Build (CI/CD) | 120 free build-minutes/day | ~$0 |
| Cloud Logging | First 50GB free | ~$0 |
| Secret Manager | < 6 secrets free | ~$0 |
| **Total (with Memorystore)** | | **~$26–36/month** |
| **Total (Redis sidecar)** | | **~$15–25/month** |

**This is the cost of a cheap VPS — with enterprise-grade managed DB, autoscaling, and monitoring.**

### Deployment Manifest (CI/CD via Cloud Build)

```yaml
# cloudbuild.yaml
steps:
  # Build and push the Docker image
  - name: 'gcr.io/cloud-builders/docker'
    args:
      - build
      - -t
      - 'europe-west3-docker.pkg.dev/$PROJECT_ID/fuelflow/api:$COMMIT_SHA'
      - ./admin-panel/backend
    id: 'build-api'

  - name: 'gcr.io/cloud-builders/docker'
    args: ['push', 'europe-west3-docker.pkg.dev/$PROJECT_ID/fuelflow/api:$COMMIT_SHA']
    id: 'push-api'

  # Run DB migrations
  - name: 'europe-west3-docker.pkg.dev/$PROJECT_ID/fuelflow/api:$COMMIT_SHA'
    entrypoint: 'node'
    args: ['node_modules/.bin/drizzle-kit', 'push']
    env:
      - 'DATABASE_URL=$$DATABASE_URL'
    secretEnv: ['DATABASE_URL']
    id: 'migrate-db'

  # Deploy to Cloud Run
  - name: 'gcr.io/cloud-builders/gcloud'
    args:
      - run
      - deploy
      - fuelflow-api
      - '--image=europe-west3-docker.pkg.dev/$PROJECT_ID/fuelflow/api:$COMMIT_SHA'
      - '--region=europe-west3'
      - '--platform=managed'
      - '--memory=512Mi'
      - '--cpu=1'
      - '--concurrency=80'
      - '--max-instances=5'
      - '--set-secrets=DATABASE_URL=database-url:latest,REDIS_URL=redis-url:latest,...'
    id: 'deploy-api'

  # Deploy admin frontend to GCS
  - name: 'node:20-alpine'
    dir: admin-panel/frontend
    entrypoint: 'sh'
    args: ['-c', 'npm ci && npm run build']
    id: 'build-frontend'

  - name: 'gcr.io/cloud-builders/gsutil'
    args: ['-m', 'rsync', '-r', '-d', 'admin-panel/frontend/dist/', 'gs://fuelflow-admin-frontend/']
    id: 'deploy-frontend'

availableSecrets:
  secretManager:
    - versionName: projects/$PROJECT_ID/secrets/database-url/versions/latest
      env: DATABASE_URL

triggers:
  - push to main branch → full deploy
  - PR → build + test only (no deploy)
```

### Secret Management

All secrets stored in **GCP Secret Manager**. Cloud Run accesses them at boot via `--set-secrets`. No secrets in environment variables on the Render dashboard, no `.env` files, no risk of accidental git commits.

```bash
# One-time setup
gcloud secrets create database-url --data-file=- <<< "$DATABASE_URL"
gcloud secrets create redis-url --data-file=- <<< "$REDIS_URL"
gcloud secrets create session-secret --data-file=- <<< "$(openssl rand -hex 32)"
gcloud secrets create qr-encryption-key --data-file=- <<< "$(openssl rand -hex 32)"
gcloud secrets create gemini-api-key --data-file=- <<< "$GEMINI_API_KEY"
gcloud secrets create admin-api-key --data-file=- <<< "$(openssl rand -hex 24)"
```

### Observability (Minimal, Zero Cost)

**Logging:** Pino's structured JSON output is automatically ingested by Cloud Logging. No additional library needed. Filter noise:
```javascript
// Only log API requests with errors (>=400) in production
if (res.statusCode >= 400 || isDev) {
    log.info({ ... }, `${method} ${path} ${statusCode}`);
}
```

**Metrics:** Cloud Monitoring collects Cloud Run metrics (request count, latency, instance count, memory) automatically. Create two alert policies:
1. P95 latency > 2,000ms → PagerDuty/email
2. 5xx error rate > 1% over 5 minutes → PagerDuty/email

Cost: First 5 alert policies free. $0.

**Error tracking:** Cloud Error Reporting parses structured logs automatically. Groups similar errors, tracks occurrence frequency, sends email on new error types. $0 cost, requires zero code change.

**Tracing:** Add `--set-env-vars=OTEL_TRACES_SAMPLER=parentbased_traceidratio,OTEL_TRACES_SAMPLER_ARG=0.01` to sample 1% of traces into Cloud Trace. Cost: First 2.5M spans/month free.

**Do not add:** Datadog, New Relic, or any third-party APM at this stage. The GCP-native stack is sufficient and free.

---

## Appendix: Migration Checklist (Render → GCP)

```
□ Create GCP project with billing account
□ Enable APIs: Cloud Run, Cloud SQL, Memorystore, Secret Manager, Artifact Registry, Cloud Build, Pub/Sub
□ Create Artifact Registry repository (Docker)
□ Create Cloud SQL instance (europe-west3, db-f1-micro, PostgreSQL 16)
□ Create Memorystore Redis instance (or use Redis sidecar initially)
□ Load all secrets into Secret Manager
□ Create GCS buckets (uploads, admin-frontend, backups)
□ Test Docker build locally: docker build ./admin-panel/backend
□ Push image to Artifact Registry
□ Deploy to Cloud Run with Cloud SQL proxy and secrets
□ Run DB migrations (db:push) via Cloud Build step
□ Verify health check: GET /api/health
□ Update mobile app .env: EXPO_PUBLIC_API_URL=https://fuelflow-api-xxx.run.app
□ Deploy admin frontend to GCS
□ Point DNS: api.fuelflow.ua → Cloud Run URL (or LB IP)
□ Sunset Render services
□ Verify Cloud Logging streams correctly
□ Set up error rate alert in Cloud Monitoring
□ Apply Cloud SQL CUD after 30 days stable
```
