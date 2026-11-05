# The acquirer will not open a payment — operator runbook

Checkout is dead and nothing was sold: Monobank answers the invoice request with a refusal, so no
customer can be given a payment page. This is the shape of planning **#873**, first seen in production
on 2026-11-05.

It is not the same problem as *paid, no voucher* (`docs/PAID_ORDER_NO_VOUCHER_RUNBOOK.md`) and not the
same as a lost webhook. **No money moved here.** The refusal happens before a payment page exists, so
there is nothing to refund, nothing to reconcile and no capture to book.

## What the bank's words mean

The sentence is in the API log, on the line that starts
`Monobank invoice creation failed. Status: …, Error: …`, and it is also typed onto the exception
(`MonobankInvoiceRejectedException.ErrCode` / `.ErrText`).

| `errCode` / text | What it is | Who fixes it |
|---|---|---|
| `FORBIDDEN` — `you don't have active terminal for internet acquiring` | The merchant's terminal has no **internet acquiring** switched on. The bank or the merchant cabinet switched it off (contract change, unpaid invoice, a re-key). | Nobody in this repo. Merchant cabinet, or the call to Monobank. |
| `INVALID_PARAM` | We are sending something the bank rejects — a new regression from our side, not the bank. | Read the field in `errText`, then the checkout code. |
| `401` / signature errors | The token or the public-key pin no longer matches (`MONOBANK_TOKEN`, `MONOBANK_PUBLIC_KEY`). | Key rotation — see the planning repo's `docs/ROTATION.md`; the statement API fails alongside it. |
| A 5xx from Monobank, or a proxy page | The bank is down, or between us and the bank. | Wait; the retry is safe because nothing was charged. |

The distinguishing test for the first row: **the statement API still works.** The hourly
`Reconciled N capture payment(s) against the Live statement` lines keep appearing during a terminal
outage, because they use the same credentials and a different capability. If those lines are healthy
and the invoice calls are refused, the credentials are fine and the terminal is not.

## What the customer sees

`503` with `code = payment_provider_unavailable` and one sentence, in their own language where the app
has one: the payment service is not accepting payments right now, nothing has been charged, try again
in a few minutes. The bank's own words never reach the phone — the operator needs them, the customer
would only learn our contract details.

Before #873 this path answered `500`, and an app bundle that is already on a phone retries a `>= 500`
with the **same** device-signature headers. The first attempt burns the replay nonce, so the retry was
refused as a replay of itself and the customer read "Request already processed" — for a request the
bank never accepted — and ended up signed out. A bundle carrying the fix (#1699) re-signs every
attempt; until a phone has it, expect those 401s in the log as noise next to the real refusal.

## What the orders do

Each refused attempt leaves one `PendingPayment` order with `monobank_invoice_id IS NULL`. That is by
design — the attempt is the record of what was tried — and the basket reservation is released
(`CheckoutGuard.Release`), so the customer's next tap works and no stock is held hostage.

Nothing ages those rows out, which is worth knowing before reading the admin purchases view:
`MonobankReconciliationService` polls only orders that HAVE an invoice
(`BackgroundJobs/MonobankReconciliationService.cs:75`), so an attempt that never got one is never
driven to a terminal state, and `DeletedUnpaidOrderCleanupService` hard-deletes rows that are already
`Cancelled` *and* soft-deleted. A refusal therefore leaves `PendingPayment` behind until staff cancel
it. Those rows hold no money and no stock; they are noise, and their count is the count of refused
attempts, which makes them a useful cross-check against the metric. **No manual SQL on them.**

Note that `fuelflow_payments_stuck_pending_payment` does **not** see them: that gauge only counts
orders that have an invoice. It read `0` through the whole 2026-11-05 incident.

## The alerts that should speak first

| Signal | Fires at |
|---|---|
| `AcquirerRefusesPayments` (Grafana → Telegram) | more than 2 refusals per 10 minutes, sustained 5 minutes |
| `HighErrorRate` (Grafana) | the API's own 5xx rate — the 503s land here too, but a checkout outage is a small share of all traffic |
| `PaymentsStuck` (Grafana) | **not this failure** — see the note above |

`AcquirerRefusesPayments` queries
`sum(increase(fuelflow_monobank_invoice_failed_invoices_total[10m])) or vector(0)`. The metric name has
the unit in it — `_invoices_total` — and `backend/tests/FuelFlow.UnitTests/Observability/MetricNameContractTests.cs`
is what keeps this rule honest when the instrument changes. A rule that names a metric nobody exports
is silent forever, which looks exactly like a healthy checkout.

Loading a changed `alert-rules.yml` in production needs a Grafana restart — Grafana reads its alerting
provisioning only at startup. Check what is actually loaded before assuming a rule can fire (on the
box; the password is the one in `/root/FuelFlow/deploy/.env.production`, never in a shell history or a
document):

```bash
P=$(grep -m1 '^GRAFANA_ADMIN_PASSWORD=' /root/FuelFlow/deploy/.env.production | cut -d= -f2-)
curl -s -u "admin:$P" http://127.0.0.1:3000/api/v1/provisioning/alert-rules | \
  node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>console.log(JSON.parse(s).map(r=>r.uid).sort().join('\n')))"
```

On 2026-11-05 this is not academic: the repo carries 21 rules and production served 16, with
`fuelflow-webhook-delivery-failed`, `fuelflow-webhook-duplicate-burst`,
`fuelflow-unbooked-captures` and `fuelflow-ota-manifest-rollback` absent — four payment and OTA alerts
that were written, merged and never loaded.

## Read, do not write

```bash
# The refusals, newest first, with the bank's own text.
# Loki keeps 15 days: {service="fuelflow-api", environment="Production"} |= "Monobank invoice creation failed"
# On the box:
docker logs fuelflow-backend --since 6h 2>&1 | grep -F "invoice creation failed" | tail -20

# Is the bank otherwise reachable? These lines prove the credentials still work.
docker logs fuelflow-backend --since 6h 2>&1 | grep -F "against the Live statement" | tail -5

# What the platform has committed as attempts, and how many got an invoice.
docker exec fuelflow-postgres psql -U fuelflow -d fuelflow -c \
  "select status, count(*) as orders, count(*) filter (where monobank_invoice_id is null) as without_invoice,
          min(created_at_utc) as first_attempt, max(created_at_utc) as last_attempt
     from orders where created_at_utc > now() - interval '1 day' group by status order by status;"
```

Then the fix is a phone call or a cabinet login, not a deploy. When the terminal is back, the next
tap succeeds and the alert resolves on its own; check one real purchase end to end before closing the
incident, because the bank can leave a terminal half-enabled.
