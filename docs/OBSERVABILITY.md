# Observability

Metrics, logs and alerting for `FuelFlow.API` and `FuelFlow.JobsWorker`.

Everything is driven from the `Observability` and `Telegram` sections of
`appsettings.json`, so there is a single place to look regardless of environment.

## Design notes

- **Exporters are opt-in.** With the committed defaults, `Otlp.Enabled` is `false`
  and no OTLP exporter is registered. The apps start and run normally with no
  collector present, so nothing here becomes a new startup dependency.
- **`/metrics` is always on** (`Prometheus.Enabled: true`) because a scrape
  endpoint costs nothing when nobody scrapes it.
- **Every signal is labelled** with `deployment.environment` and `service`. This is
  what lets one Telegram group safely receive both Development and Production
  alerts without confusion.
- **The worker is now a web host.** It previously used `Host.CreateApplicationBuilder`
  and had no HTTP surface at all, so there was nowhere to serve a scrape endpoint.
  It now runs a minimal `WebApplication` bound only to `Prometheus.WorkerPort`.

## Endpoints

| Service   | Port | Endpoints                                  |
|-----------|------|--------------------------------------------|
| API       | 5202 | `/metrics`, `/health`, `/health/live`, `/health/ready` |
| JobsWorker| 9091 | `/metrics`, `/health/live`, `/health/ready`            |

`/health/live` answers "is the process up" and intentionally runs no checks, so a
database blip never causes a container restart loop. `/health/ready` runs the
Postgres and Redis checks and is the one to wire to a load balancer.

## Running the stack locally

```powershell
cd C:\FuelFlow\FuelFlow\backend
docker compose -f docker-compose.observability.yml up -d
```

| UI         | URL                     | Credentials   |
|------------|-------------------------|---------------|
| Grafana    | http://localhost:3000   | admin / admin |
| Prometheus | http://localhost:9090   | –             |
| Loki       | http://localhost:3100   | –             |
| Alloy      | http://localhost:12345  | –             |

Prometheus and Loki are pre-provisioned as Grafana datasources.

The API and worker normally run on the host (F5 from Visual Studio), so Prometheus
scrapes them via `host.docker.internal` rather than a compose network. If you later
containerise the apps, move them onto this compose network and change the targets in
`observability/prometheus/prometheus.yml` to service names.

### Shipping logs to Loki

There are two independent paths into Loki, and which one applies depends on
*where the process runs*:

| How the app runs | Path | Configured by |
| --- | --- | --- |
| On the host (F5 / `dotnet run`) | Serilog writes to Loki directly | `Observability:Loki` |
| In a container | Alloy scrapes the Docker socket | `observability/alloy/config.alloy` |

The in-app sink exists because Alloy's `loki.source.docker` can only see
*containers*. When the API and worker run on the host — which is the normal local
setup — their logs were silently absent from Loki entirely. The sink closes that
gap so local and containerised runs look the same in Grafana.

It is enabled in both `appsettings.Development.json` files already:

```json
"Observability": {
  "Loki": { "Enabled": true, "Url": "http://localhost:3100" }
}
```

Delivery is best-effort: the sink batches in memory and drops on sustained
backpressure. A Loki outage must never be able to take the API down, so it is
deliberately not durable — if you need guaranteed log delivery, run the apps in
containers and let Alloy handle it.

Set `StructuredLogs: true` to also switch the *console* to compact JSON. That
only matters for containers (where Alloy parses stdout); leave it `false` locally
so console output stays readable. The Loki sink sends structured events either way.

#### Labels vs. fields

Loki creates one stream per unique label combination, so only low-cardinality
values are labels:

- **Labels:** `service`, `environment`, `level`
- **Structured metadata:** `TraceId`, `SpanId`
- **Everything else:** part of the log line, searchable but not indexed

Do not promote things like user id, order id or request path to labels. Each
distinct value would create a new stream and degrade the whole Loki instance.
Query them with `| json` instead:

```logql
{service="fuelflow-api", level="error"} | json | UserId="..."
```

`TraceId` is structured metadata rather than a label for the same reason — it
stays available for pivoting from a log to its request, without one stream per
trace.

## Telegram alerts

Two independent paths, both pointing at the same group:

1. **Grafana alerting** — threshold alerts from the rules in
   `observability/prometheus/rules/`. Provisioned in
   `observability/grafana/provisioning/alerting/contact-points.yml`.
2. **In-app `IAlertNotifier`** — for events that are not naturally a metric
   threshold (e.g. a specific failed payment reconciliation).

### Setup

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy the token.
2. Create a private group, add the bot, and grant it permission to post.
3. Get the group ID: send a message in the group, then open
   `https://api.telegram.org/bot<TOKEN>/getUpdates` and read `result[].chat.id`.
   Group IDs are negative, e.g. `-1001234567890`.

For Grafana, create `backend/.env` (already git-ignored):

```
TELEGRAM_BOT_TOKEN=123456:ABC-your-token
TELEGRAM_CHAT_ID=-1001234567890
```

For the apps, **do not put the token in `appsettings.json`.** Use user-secrets
locally and environment variables in deployment:

```powershell
cd backend\src\FuelFlow.API
dotnet user-secrets set "Telegram:Enabled" "true"
dotnet user-secrets set "Telegram:BotToken" "123456:ABC-your-token"
dotnet user-secrets set "Telegram:ChatIds:0" "-1001234567890"
```

The section *shape* stays in `appsettings.json` so the configuration surface is
discoverable in one place; only the secret value lives elsewhere. If `BotToken` or
`ChatIds` are empty, `TelegramOptions.IsConfigured` is false and a `NullAlertNotifier`
is registered instead — call sites need no null checks and local dev never sends.

### Sending an alert from code

```csharp
public sealed class SomeHandler(IAlertNotifier alerts)
{
	public async Task HandleAsync(CancellationToken ct)
	{
		await alerts.SendAsync(
			AlertSeverity.Critical,
			"Fulfillment stalled",
			"No vouchers available to fulfil paid orders.",
			new Dictionary<string, string> { ["OrderId"] = orderId.ToString() },
			ct);
	}
}
```

`SendAsync` never throws — alerting is a side-channel and must not fail the
operation that triggered it. Delivery problems are logged as warnings.

## Emitting business metrics

Inject `FuelFlowMetrics` and call the intent-named methods:

```csharp
public sealed class VoucherService(FuelFlowMetrics metrics)
{
	public void Assign() => metrics.VoucherAssigned();
}
```

### Metric naming: the unit is part of the exported name

The Prometheus exporter rewrites instrument names, and the rule is easy to get wrong:

1. dots become underscores,
2. **the instrument's unit is appended**,
3. counters then get a `_total` suffix.

So `fuelflow.vouchers.assigned`, declared with unit `vouchers`, is **not** queried as
`fuelflow_vouchers_assigned_total` but as `fuelflow_vouchers_assigned_vouchers_total`.
Units are also expanded (`ms` becomes `milliseconds`, `s` becomes `seconds`).

This matters because an alert rule referencing a non-existent series does not error --
it silently matches nothing forever, which is indistinguishable from "healthy". Changing
an instrument's unit renames the exported metric and breaks any rule using it.

`MetricNameContractTests` guards this: it derives the exported names from the instruments
and fails the build if `fuelflow-alerts.yml` references a name nothing exports. When adding
an alert, confirm the name against a live scrape of `/metrics` rather than deriving it by
hand. Prefer omitting the unit when it merely repeats the metric noun, to avoid names like
`fuelflow_orders_by_status_orders`.

### Reserved label names

Prometheus owns `job` and `instance` on every scraped series. A metric tag with either name
is **silently renamed** to `exported_job` / `exported_instance` on ingestion, so a query
written against the original name matches nothing. Hangfire job metrics therefore tag
`job_name` rather than `job`. Avoid both names when adding tags.

## Dashboards

Provisioned from `backend/observability/grafana/provisioning/dashboards` into the
**FuelFlow** folder:

- **FuelFlow / Service Health** -- target availability, request rate, 5xx ratio, latency
  percentiles, slowest routes, DB query time, GC and Kestrel connections.
- **FuelFlow / Business** -- voucher pool and lifecycle, orders, fulfillment outcome and
  duration, Monobank invoices/webhooks, and background job failures.
- **FuelFlow / Logs** -- log volume by level, error count, and searchable log panels,
  with `Service` and `Search` variables.

`allowUiUpdates` is off: edits in the UI are transient. Change a dashboard by exporting its
JSON and committing it, otherwise the next provisioning cycle reverts it. On the Hetzner server
that commit does not apply on its own — the CI deploy never touches the observability stack, so
you must re-provision Grafana by hand (see [Deploying to Hetzner](#deploying-to-hetzner) below).

A panel showing "No data" is ambiguous -- it means either the event genuinely has not
occurred, or the query is wrong. Counters are not exported until first incremented, so an
idle system legitimately shows empty panels. Confirm against `/metrics` before assuming a
panel is broken.

## Sentry (error tracking)

The Prometheus/Grafana/Loki stack answers *"is the system healthy"* (metrics, log search). Sentry
answers a different question — *"what exactly threw, with the stack trace and the request that
caused it"* — and groups recurring exceptions. The two are complementary, not alternatives.

Sentry is **optional and off by default**: with no DSN the SDK is never initialised and nothing
leaves the box. There are three separate projects — backend (.NET), admin SPA (browser JS) and
mobile (React Native) — each with its own DSN and each sending **errors only**, with
`SendDefaultPii` hard-disabled in code (the API and app carry phone numbers and voucher QR
payloads). Turn-on steps, the restart-vs-rebuild-vs-native-build differences, and the
CI-cannot-prove-mobile caveat live in [DEPLOYMENT.md](DEPLOYMENT.md) → "Sentry (error tracking)".

## Deploying to Hetzner

The production arrangement is deployed from `deploy/docker-compose.observability.yml`
(+ optional `docker-compose.observability.telegram.yml`); the runbook section is
[DEPLOYMENT.md](DEPLOYMENT.md) → "Logs and monitoring". What differs from local:

1. Prometheus targets come from `deploy/observability/prometheus/prometheus.yml`
   (`dotnet-backend:8080` by service name on the `fuelflow_default` network, labelled
   `environment: production`). There is no `fuelflow-jobs` target — Hangfire jobs run
   in-process in production.
2. Logs reach Loki through the **in-app Serilog sink** (`Observability__Loki__Enabled=true`
   in `deploy/docker-compose.prod.yml`). The sink sends HTTP Basic credentials to
   `loki-gateway:8080`; that gateway is the only bridge from `fuelflow_default` to the
   private `observability` network and permits only `POST /loki/api/v1/push`. Loki's query
   API is reachable only by Grafana, so a compromised application container cannot read logs.
   There is deliberately **no Alloy** in production: its Docker-socket mount is root-equivalent
   on the host, and with the sink enabled it would duplicate every log line. `StructuredLogs`
   and the environment label come from `appsettings.Production.json`.
3. Nothing is exposed publicly: Grafana binds `127.0.0.1:3000` (SSH tunnel); Prometheus,
   Loki, and the gateway publish no ports. Caddy additionally denies `/metrics` and
   `/hangfire` at the edge as defense in depth.
4. `LOKI_PUSH_USERNAME` / `LOKI_PUSH_PASSWORD` live in `deploy/.env`. Generate the password
   with `openssl rand -base64 32`; rotate it by recreating the backend and gateway together.
5. **Dashboards and alert rules are not deployed by CI.** The automated pipeline only manages
   `docker-compose.prod.yml`; the observability stack is brought up by hand. After committing a
   dashboard JSON or alert-rule change, apply it on the box by force-recreating Grafana:
   ```bash
   cd /root/FuelFlow/deploy && docker compose --env-file .env -f docker-compose.observability.yml up -d --force-recreate grafana
   ```
