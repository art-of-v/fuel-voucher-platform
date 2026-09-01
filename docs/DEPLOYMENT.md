# Deploying FuelFlow to Hetzner

Handoff document for whoever operates the deployment. The local stack described in
[OBSERVABILITY.md](OBSERVABILITY.md) is fully working; this document covers what has to
change to run the same thing on a server.

Nothing here has been executed against a real Hetzner host. Treat it as a specification
to validate on a staging box first, not a script that is known to work.

---

## What exists today

| Component | State | Location |
| --- | --- | --- |
| API image | Dockerfile ready | `backend/src/FuelFlow.API/Dockerfile` |
| JobsWorker image | **Missing — must be written** | – |
| Observability stack | Working locally | `backend/docker-compose.observability.yml` |
| Telegram alert overlay | Working, opt-in | `backend/docker-compose.telegram.yml` |
| Prometheus scrape config | Points at `host.docker.internal` | `backend/observability/prometheus/prometheus.yml` |
| Alert rules | Validated with `promtool` | `backend/observability/prometheus/rules/fuelflow-alerts.yml` |
| Grafana dashboards | Provisioned from source control | `backend/observability/grafana/provisioning/dashboards/` |
| Postgres | Local container only, not in compose | – |

The two gaps that block a deployment are the **worker Dockerfile** and a
**compose file for the applications themselves**. The observability compose file
deliberately contains only observability services.

---

## Prerequisites

1. A Hetzner Cloud server (CX22 or larger; Loki and Prometheus retention are the
   main drivers of disk usage). Docker Engine and the Compose plugin installed.
2. A DNS record pointing at the server for the API and, if exposed, Grafana.
3. A managed Postgres instance or a Postgres container **with a persistent volume
   and a backup job**. Do not run the database on an ephemeral volume.
4. A firewall allowing only 22, 80 and 443 from the internet. Everything else
   stays on the internal Docker network — see [Network exposure](#network-exposure).

---

## Step 1 — Build a JobsWorker Dockerfile

The worker is a `WebApplication` (it exposes `/metrics`, `/health/live` and
`/health/ready` on `Observability:Prometheus:WorkerPort`, default `9091`). It does
**not** need PDFium, so it is simpler than the API image:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY src/FuelFlow.JobsWorker/FuelFlow.JobsWorker.csproj src/FuelFlow.JobsWorker/
RUN dotnet restore src/FuelFlow.JobsWorker/FuelFlow.JobsWorker.csproj
COPY . .
RUN dotnet publish src/FuelFlow.JobsWorker/FuelFlow.JobsWorker.csproj \
	-c Release --self-contained false -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN useradd --create-home --shell /bin/false appuser
COPY --chown=appuser:appuser --from=build /app/out .
USER appuser
EXPOSE 9091
ENTRYPOINT ["dotnet", "FuelFlow.JobsWorker.dll"]
```

Pin the base images by digest, as the API Dockerfile already does, so rebuilds are
reproducible.

> The worker binds `0.0.0.0:{WorkerPort}` itself in `Program.cs`, so do **not** set
> `ASPNETCORE_URLS` for it — that would fight the explicit `UseUrls` call.

Build both images from `backend/` (the API Dockerfile expects that build context):

```bash
docker build -f src/FuelFlow.API/Dockerfile -t fuelflow-api:$(git rev-parse --short HEAD) .
docker build -f src/FuelFlow.JobsWorker/Dockerfile -t fuelflow-jobs:$(git rev-parse --short HEAD) .
```

Tag by commit SHA rather than `latest`, so a rollback is a tag change.

---

## Step 2 — Application compose file

Create `backend/docker-compose.apps.yml`. The key difference from local is that the
apps join the same network as the observability stack, so Prometheus can reach them
by service name instead of `host.docker.internal`.

```yaml
services:
  fuelflow-api:
	image: fuelflow-api:${TAG}
	restart: unless-stopped
	env_file: [.env.production]
	expose: ["8080"]
	healthcheck:
	  test: ["CMD", "curl", "-f", "http://localhost:8080/health/live"]
	  interval: 30s
	  timeout: 5s
	  retries: 3

  fuelflow-jobs:
	image: fuelflow-jobs:${TAG}
	restart: unless-stopped
	env_file: [.env.production]
	expose: ["9091"]
```

Run it together with the observability stack so they share a default network:

```bash
docker compose -f docker-compose.observability.yml -f docker-compose.apps.yml up -d
```

---

## Step 3 — Configuration and secrets

All settings are overridable by environment variable using `__` as the section
separator. **No secret belongs in `appsettings.json`** — those files are committed.

Required in `.env.production` (git-ignored, `chmod 600`):

| Variable | Notes |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT=Production` | Must not be `Development` — that enables auth bypass |
| `Database__ConnectionString` | Points at the production Postgres |
| `Jwt__Secret` | Long random value; rotating it invalidates all tokens |
| `Monobank__Token` | Production Monobank token |
| `Monobank__PublicKey` | Used for webhook signature verification |
| `Monobank__WebhookUrl` | Public HTTPS URL of the API webhook endpoint |
| `Hangfire__Password` | Worker dashboard; default is `changeme123` — **must** be changed |
| `Cors__AllowedOrigins__0` | Production front-end origin |
| `AllowedHosts` | Production hostnames only |

Observability variables for both services:

```
Observability__Environment=Production
Observability__StructuredLogs=true
Observability__Loki__Enabled=true
Observability__Loki__Url=http://loki:3100
```

`Observability__Environment` is what tags metrics and logs as Production. Getting it
wrong makes Production alerts indistinguishable from Development ones in the shared
Telegram group, which is the failure mode most likely to cause a missed incident.

With the apps containerised, Alloy's Docker scrape would collect their stdout as
well as the in-app Loki sink — producing **duplicate log lines**. Pick one: either
leave `Loki__Enabled=true` and drop the app containers from Alloy's scrape, or set
it to `false` and rely on Alloy with `StructuredLogs=true`.

### Dangerous development defaults

Verify each of these is overridden before exposing the service:

- `Auth:DevBypass` — `true` in Development. Must be absent/false in Production.
- `DeviceAuth:AllowDevelopmentBypass` — same.
- `Hangfire:Password` — committed default `changeme123`.
- `AllowedHosts` — includes `host.docker.internal` and `10.0.2.2` locally.

---

## Step 4 — Prometheus targets

Replace the host-based targets in `observability/prometheus/prometheus.yml`. Keep the
`service` labels identical, because the dashboards and alert rules select on them:

```yaml
global:
  external_labels:
	environment: production

scrape_configs:
  - job_name: fuelflow-api
	metrics_path: /metrics
	static_configs:
	  - targets: ["fuelflow-api:8080"]
		labels: { service: fuelflow-api, environment: production }

  - job_name: fuelflow-jobs
	metrics_path: /metrics
	static_configs:
	  - targets: ["fuelflow-jobs:9091"]
		labels: { service: fuelflow-jobs, environment: production }
```

Do not add a label named `job` or `instance` — Prometheus reserves both and silently
renames collisions to `exported_job`. This has already bitten us once; see
OBSERVABILITY.md.

---

## Step 5 — Network exposure

Only Grafana and the API should be reachable from the internet, both behind a
reverse proxy (Caddy or nginx) terminating TLS.

| Service | Port | Exposure |
| --- | --- | --- |
| API | 8080 | Public via reverse proxy, HTTPS only |
| Grafana | 3000 | Public via reverse proxy + strong admin password |
| Prometheus | 9090 | Internal only |
| Loki | 3100 | Internal only |
| Alloy | 12345 | Internal only |
| Worker `/metrics` + Hangfire | 9091 | **Internal only** |

Remove the `ports:` mappings for Prometheus, Loki and Alloy in production and rely
on `expose:`. Publishing a port on Docker bypasses `ufw` on most setups, so an
unremoved mapping is genuinely reachable from the internet even with a firewall
configured.

The worker port is especially important: it serves the Hangfire dashboard, which can
enqueue and delete jobs.

Change Grafana's `admin/admin` credentials via `GF_SECURITY_ADMIN_PASSWORD`.

---

## Step 6 — Telegram alerting

Create `backend/.env` with `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID`, then include
the overlay:

```bash
docker compose -f docker-compose.observability.yml \
			   -f docker-compose.apps.yml \
			   -f docker-compose.telegram.yml up -d
```

Grafana validates the token at startup and refuses to boot on an empty value, which
is why this is a separate overlay rather than part of the base stack.

The overlay mounts `observability/grafana/provisioning/alerting-telegram/`, which
provisions both the Telegram contact point and the Grafana-managed alert rules
(`alert-rules.yml`). These rules cover conditions the application cannot report about
itself:

| Alert | Condition | Severity |
| --- | --- | --- |
| `ServiceDown` | `up == 0` for 2m (resolved notification = "app is back up") | critical |
| `HighErrorRate` | 5xx ratio > 5% for 5m | critical |
| `HighRequestLatency` | p95 > 2s for 10m | warning |
| `HighCpuUsage` | process CPU > 85% of a core for 10m | warning |
| `HighMemoryUsage` | working set > 1.5 GB for 10m | warning |
| `LogErrorBurst` | > 5 error-level log lines/min for 5m (Loki) | warning |
| `LogFatal` | any fatal-level log line (Loki) | critical |
| `VoucherPoolLow` | available vouchers < 10 for 5m | warning |
| `FulfillmentFailures` | any fulfillment failures for 10m | critical |
| `HangfireJobFailures` | any job failures for 10m | warning |

Grafana sends both firing and resolved notifications, so "app is down" and "app is
up" are the same rule rather than two. The Prometheus rules under
`observability/prometheus/rules/` mirror these thresholds but only display state in
the Prometheus UI — no Alertmanager is deployed, so they do not notify.

`LogErrorBurst` and `LogFatal` query Loki rather than Prometheus. They exist because
several controllers catch their own exceptions, log them and return a response
themselves, so those failures never reach `GlobalExceptionHandler` and never trigger
the application's own Telegram dispatcher. Alerting on the log stream catches them
regardless of how the exception was handled.

---

## Step 7 — Database migrations

Confirm how migrations are applied before first deploy — if the API runs them at
startup, two replicas starting simultaneously can race. Prefer a one-shot migration
step that completes before the new version starts serving traffic.

---

## Post-deploy verification

1. `docker compose ps` — all services `running`, none restarting.
2. `curl -f https://<api-host>/health/ready` returns 200.
3. Prometheus → Status → Targets: `fuelflow-api` and `fuelflow-jobs` both **UP**.
4. Prometheus → Alerts: all rules loaded, none in unexpected `FIRING`.
5. Grafana → FuelFlow folder: three dashboards present, Service Health populated.
6. Grafana → Explore → Loki: `{service="fuelflow-api", environment="production"}`
   returns lines, and no duplicates (see Step 3).
7. Fire a test alert and confirm it lands in the Telegram group tagged `Production`.
8. Confirm `/hangfire` and `:9090` are **not** reachable from outside the server.

---

## Rollback

Images are tagged by commit SHA, so:

```bash
TAG=<previous-sha> docker compose -f docker-compose.observability.yml \
								  -f docker-compose.apps.yml up -d
```

Roll back database migrations separately and deliberately — a schema change that a
previous application version cannot read will not be fixed by reverting the image.

---

## Open items

- [ ] Write `src/FuelFlow.JobsWorker/Dockerfile`
- [ ] Write `docker-compose.apps.yml`
- [ ] Decide the log path (in-app sink vs. Alloy scrape) and remove the duplicate
- [ ] Provision Postgres with a persistent volume and verified restore procedure
- [ ] Choose and configure the reverse proxy / TLS certificates
- [ ] Set Prometheus and Loki retention to match the server's disk
- [ ] Confirm the migration strategy
