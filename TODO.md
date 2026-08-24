# FuelFlow — Open Work

Everything below is unfinished. Completed work lives in git history, not here.

## ❌ Skipped (design decisions, not bugs)

| Item | Reason |
|---|---|
| Natural keys → surrogate UUIDs (stations, fuel_types, fuel_packages) | Massive refactor cascading across 10+ tables |
| DB-level cascading deletes on existing relationships | Current defaults are safe; changing risks breaking queries |
| Thin query handler ceremony | Style choice; 30+ handlers would churn with zero runtime benefit |
| Price precision inconsistency (`int` vs `decimal`) | Needs business decision on fractional UAH support |
| Merge outbox tables (`outbox_events` vs `provider_event_outbox`) | Transactional vs immutable audit — different concerns |

---

# Security & Test-Coverage Audit — 2026-08-15 (open items)

## 🟡 Medium

- [ ] **Verify production env overrides on the Droplet** — `appsettings.Production.json` points `Monobank:WebhookUrl` / `RedirectUrl` at `api.fuelflow.com`. Both must be set as `Monobank__WebhookUrl` / `Monobank__RedirectUrl` in `deploy/.env` and confirmed in the running container after the first DigitalOcean deploy (wrong webhook URL = payment callbacks silently lost). The 2026-08-16 confirmation was against the old Render host and no longer counts; `deploy/docker-compose.prod.yml` now uses a `:?` guard so an empty `MONOBANK_REDIRECT_URL` fails the compose run instead of booting a broken payment flow (FF-18)
- [ ] **Device-signature scope mismatch** — code merged on both sides (#304 mobile signs `/api/purchases` + `/bulk`, #305 backend enforces), but DEPLOY GATED: the backend must NOT go live on the Droplet with `/api/purchases` enforced until the app build containing #304 is released to both stores, else store installs get 401 at checkout. Escape hatch: `DeviceAuth__Enabled=false` in `deploy/.env` (overriding `DeviceAuth__RequireSignatureForEndpoints` does not work — the config binder appends to list defaults). Note FF-21's fix: device binding can still be disabled in Production, but only by setting `DeviceAuth__AcknowledgeDisabledInProduction=true`, which is logged at startup — it can no longer be off by omission

## 🟢 Low

- [ ] **Cheaper UA SMS provider for OTP** — Twilio is $0.2268/SMS to UA. Alternatives researched 2026-08-20: BudgetSMS €0.052–0.15 (Kyivstar from €0.052, OTP routes cost more); DecisionTelecom €0.137 flat (no platform fee); Messaggio €0.145 (all UA operators); sms.to Verify API — channel fallback (SMS→WhatsApp→Viber→Telegram), pay per *delivered* channel + verify fee, often cheapest/most reliable for UA; local UA providers (TurboSMS, Telq) worth a quote. Swapping = implement new `ISmsService` + replace Twilio config (abstraction already in `ServiceSetup.cs`; startup guard + OTP flow untouched). Consider Viber/WhatsApp-first for UA where SMS penetration is low.

## ⚪ Accepted (no action)

- `000000` DevBypass OTP — intentional during the testing period to avoid Twilio costs. Revisit when flipping `Auth:DevBypass` off
