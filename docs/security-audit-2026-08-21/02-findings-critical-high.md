# 4a. Findings in detail — Critical and High

Most severe first. Each finding states the attack, the guard that should have stopped it (read, not assumed), and the fix.

---

## FF-03 — Expo OTA updates are unsigned — **Critical, CONFIRMED, OPEN**

**Attack.** The mobile app ships with `expo-updates`, which fetches a JavaScript bundle at launch and runs it. Update code signing is not enabled, so the client accepts whatever bundle the update server serves and verifies only that it came over TLS. Anyone who obtains the EAS publish token — from a developer machine, a CI log, a screenshot, or the token's own history — publishes a bundle of their choosing to every installed app. That bundle is the whole application: the checkout screen, the OTP entry field, the deep-link handler for `fuelflow://payment-result`. There is no store review in this path and no user action required.

**Why this outranks everything else.** Every other control in this system assumes the client is the client. Server-authoritative pricing (FF-02) stops a *patched* app; it does not stop an attacker who is serving the *official* app's code. Device signing (FF-04) binds a request to a device key that the substituted bundle can read and use. The blast radius is the entire installed base simultaneously, and the compromise persists across restarts.

**Guard sought.** `expo-updates` supports `codeSigningCertificate` / `codeSigningMetadata` in `app.json`'s `updates` block, which makes the client refuse a bundle whose manifest is not signed by a key the binary was built with. Neither key is present in the configuration.

**Why it is not fixed here.** Enabling code signing requires generating a keypair, embedding the certificate in a **new native build**, and shipping that build to the stores. Adding `codeSigningCertificate` to `app.json` while pointing at a file that does not exist breaks the build, and enabling it for clients that were not built with the certificate breaks their updates. This is a release-sequenced operational change, not an edit.

**Required actions (operator).**
1. Rotate the EAS token now, and audit which CI logs and machines have held it.
2. Generate an update signing keypair; store the private key in the EAS secret store, never in the repo.
3. Add `codeSigningCertificate` to the `updates` block and produce a new native build.
4. Release that build to both stores. Only after it is the dominant install base does OTA signing actually protect anyone.
5. Until step 4 completes, treat the OTA channel as a trusted-publisher channel and restrict who can publish to it.

---

## FF-01 — Voucher catalogue readable without authentication — **Critical, CONFIRMED, FIXED**

**Attack.** `GET` on the voucher catalogue endpoint served the voucher list to an unauthenticated caller. The response carried voucher numbers and QR payloads. A FuelFlow voucher is a **bearer instrument**: the QR is what the pump at WOG/OKKO/KLO scans. Possession is redemption. There is no revocation story once a payload is public — the station honours the code, not our database.

So the attack is not "read some data": it is walk the catalogue anonymously, redeem the codes at a pump, and leave FuelFlow holding the supplier invoice with no order, no payment, and no attributable user.

**Guard sought.** `[Authorize]` on the controller or the endpoint, or a role filter in the query handler. Neither was present; the endpoint inherited no policy, and `Program.cs` sets no global `FallbackPolicy`, so absent an attribute the endpoint is anonymous.

**Fix.** Authorization enforced on the catalogue query, and the projection reduced so the QR payload is not returned on list responses at all — defence in depth, so a future authorization mistake on this route leaks metadata rather than redeemable codes. `Features/Vouchers/Import/GetVouchersQuery.cs`.

---

## FF-02 — Bulk checkout quantity unvalidated → integer overflow — **Critical, CONFIRMED, FIXED**

**Attack.** `POST /api/purchases/bulk` accepted a per-item `quantity` with no upper bound and no validator. The line total is `unitPrice × quantity` in kopecks. With a large enough quantity the multiplication overflows, and the total the customer is asked to pay wraps to a small or negative number while the *quantity of vouchers to be delivered* stays enormous. The buyer pays a few kopecks — or the invoice is created for a nonsense amount that Monobank accepts — and fulfilment then tries to hand over the full quantity of real vouchers.

This is worse than a pricing bug because the two halves of the transaction disagree: the money side sees a wrapped value, the inventory side sees the literal quantity.

**Guard sought.** A FluentValidation validator for the bulk DTO (the pipeline exists — `AddValidatorsFromAssemblyContaining`, per `TODO.md:30`), or a range check in the handler, or a checked-arithmetic context. None applied to `quantity`. `ServerPricing` correctly recomputes the *unit price* from `FuelPackages` — that part was already right — but it multiplied by a client-supplied count.

**Fix.** Per-item quantity and liters are range-validated, the aggregate item count is bounded, and the total is accumulated in a width that cannot wrap at the permitted maxima. `BulkCheckoutCommandHandler.cs`.

---

## FF-05 — Monobank merchant token committed to git history — **High, SUSPECTED (live), OPEN**

**Location and class.** `3cb50dc197918b771ec912bd21514d9e9c7e6412` → `appsettings.Production.json`, key `Monobank:Token`. Class: payment-provider merchant API token, 34 characters. Per ROE the value is not reproduced here or anywhere else in this report; it was handled by hash only.

**Correction to my own earlier reporting.** I previously described this commit as containing a live JWT signing secret *and* the Monobank token. **The JWT part was overstated and is withdrawn.** That file is a committed **template**, and I verified each value:

| Key in `3cb50dc:appsettings.Production.json` | Finding |
|---|---|
| `Monobank:PublicKey` | literal `PRODUCTION_PUBLIC_KEY_HERE` — placeholder |
| `Twilio:PhoneNumber` | `+1234567890` — placeholder |
| `Twilio:AccountSid` / `AuthToken` | both begin with placeholder markers |
| `Jwt:Secret` | the exact 71-character constant named `PlaceholderSecret` at `Extensions/AuthSetup.cs:13`, which `AuthSetup.cs:31-42` **refuses to boot with in Production**. Burned by design, not exploitable. |
| `Monobank:Token` | 34 chars, entropy 3.75, **no placeholder marker** — cannot be dismissed |

So one value survives scrutiny, not three.

**Independent corroboration.** This is not only my conclusion. `docs/FRAUD_ANALYSIS.md:62` lists as open work: *"Rotate committed Monobank token — Token currently committed; rotate after webhook verification ships."* And `:309` repeats it. The project's own documentation says this token was committed and needs rotating.

**Why it stays SUSPECTED rather than CONFIRMED.** Confirming it is live means calling Monobank's API with it, which the ROE forbids. Confirming it is dead means trusting an attestation. The operator stated on 2026-08-21 that *"all secrets were rotated yesterday"* — recorded here as an attestation dated **2026-08-20**, unverifiable within this engagement.

**Why it is still open despite the attestation.** Three reasons. First, the value remains in history, so the exposure is permanent for anyone who cloned before rotation. Second, **repository visibility is undetermined** — I could not establish whether this repo is public; if it is, the population of people who have read that commit is unbounded and unknowable. Third, rotation is exactly the kind of claim that should be verified by the person who can log into the Monobank merchant console, not inferred.

**Required actions (operator).**
1. Confirm in writing that this specific token was rotated on or after 2026-08-20, and that the old one is revoked merchant-side (not merely replaced in config).
2. State whether the repository is public or private, and whether it ever was public.
3. Decide on history: rewrite (`git filter-repo`) and force-push, or formally accept the residue. Rewriting invalidates every existing clone and fork — a real cost. Accepting is defensible **only** if rotation is confirmed and the repo was never public.
4. Note that the two commits are allowlisted by commit in `.gitleaks.toml` so the scanner's signal stays meaningful; that allowlist is documented in-file and is not a substitute for either decision above.

---

## FF-06 — `/api/purchases/simulate` production-reachable and bypassed the state machine — **High, CONFIRMED, FIXED**

**This was worse than originally reported.** The handoff described a test endpoint being reachable in production. The real defect is what the handler did once reached: `SimulatePaymentCommandHandler` **did not go through `OrderStateMachine`**. It force-assigned `Status = PendingFulfillment` from *any* prior state, including terminal ones.

**Attack.** An Admin (or anyone with an admin token) takes an order that is already `Fulfilled` — vouchers delivered, payment settled — and calls simulate. The order returns to `PendingFulfillment` and re-enters the outbox. The fulfilment job's own guards do not save us here: the row-level conditional `UPDATE ... WHERE status='Available'` (`FulfillmentService.cs:474-481`) prevents double-assigning *the same voucher*, and the skip at `:249-257` only skips orders currently `Fulfilled`/`Cancelled` — which this order no longer is. So fulfilment does exactly what it is told: it finds *fresh* `Available` vouchers and assigns **a second complete set** against a single payment.

That is voucher minting from an admin session, and it leaves a plausible-looking audit trail because every individual step is legitimate.

**Guard sought.** `OrderStateMachine.TryTransition(from, to)` exists precisely for this, and `docs/FRAUD_ANALYSIS.md:232-238` specifies `Fulfilled` and `Cancelled` as terminal — *"no webhook may ever re-flip them."* The webhook handler honours that. The simulate handler did not use the state machine at all.

**Fix.** Two independent changes, because either alone would be insufficient. (1) The endpoint is unreachable in production — gated on the same non-production condition as the other development affordances, so it cannot be re-enabled by a single stray environment variable. (2) The handler now routes through `OrderStateMachine`, so even in development it cannot move a terminal order. `SimulatePaymentCommandHandler.cs`, `PurchaseController.cs`.

---

## FF-04 — Trailing-slash device-signature bypass — **High, CONFIRMED, FIXED**

**Attack.** `DeviceSignatureMiddleware` decided whether a request needed a signature by comparing the request path against `DeviceAuth:RequireSignatureForEndpoints`, which lists `/api/purchases` and `/api/purchases/bulk`. The comparison used `PathString.Equals(..., StringComparison)`. That is an **exact** comparison: unlike `StartsWithSegments`, it does not normalise a trailing slash. ASP.NET Core routing, however, matches `POST /api/purchases/` to the same action as `POST /api/purchases`.

So `POST /api/purchases/` — one extra character — reached the checkout action with the signature check skipped entirely. Device binding on the money endpoint was optional at the attacker's discretion. A stolen access token, with no device key at all, buys vouchers.

**Guard sought.** This *is* the guard. There was no second layer: the checkout handler does not independently require a device-bound principal.

**Fix.** Path matching normalised so that trailing-slash and case variants of a protected path are treated as the protected path. `DeviceSignatureMiddleware.cs`.

**Client compatibility — required by the operator's deploy-order note.** **No impact on installed mobile clients.** The released app signs `/api/purchases` without a trailing slash, which was already enforced and continues to be. The fix only adds enforcement to a variant no legitimate client emits. The signing payload format is unchanged by this and by every other fix in this audit, so nothing here can lock existing users out of checkout. The separate pre-existing gate at `TODO.md:82` — do not enable enforcement before the signing build is released — is unaffected and is carried into the deploy checklist.

---

## FF-07 — Per-phone OTP rate limit never functioned — **High, CONFIRMED, FIXED**

**The handoff's premise was inverted, and the truth is worse.** The handoff supposed an attacker could *choose* their rate-limit partition by varying the phone number in the body. What actually happened: the partition key was derived by reading the request body synchronously via `reader.ReadToEnd()`. Kestrel sets `AllowSynchronousIO = false` by default, so that call **threw on every request**. The exception was swallowed and the partition silently degraded to per-IP.

The per-phone OTP limit therefore **never worked at all**, from the first deploy. Not bypassable — absent.

**Attack.** Pump OTP sends at one phone number from a pool of IPs, or simply at the per-IP ceiling, indefinitely. Twilio charges $0.2268 per SMS to Ukraine (`TODO.md:89`). This is direct, unbounded, attacker-controlled spend, plus SMS-bombing a victim's phone.

**Fix, in three parts.**
1. The partition is now computed without synchronous body reads — `IHttpBodyControlFeature.AllowSynchronousIO` is set where a read is genuinely required, and the phone-probe read is bounded by `MaxPhoneProbeBytes = 4096` so the partition key cannot itself be a memory-exhaustion vector.
2. `OtpPerIpPermitLimit = 12` and a real per-phone partition, chained via `PartitionedRateLimiter.CreateChained` so both apply.
3. **`SmsBudgetGuard`** (new file) — a singleton daily spend ceiling. A rate limit caps velocity; it does not cap total cost. A singleton is required for the ceiling to be a ceiling: a scoped counter would reset per request.

`RateLimiterSetup.cs`, `Features/Auth/SendCode/Services/SmsBudgetGuard.cs`.

---

## FF-08 — API published on the admin origin — **High, CONFIRMED, FIXED**

**Attack.** The admin origin proxied the entire API surface. Any control scoped to the API hostname — CORS decisions, origin allow-lists, per-origin rate limits, WAF rules — could be sidestepped by issuing the same request against the admin hostname instead. Notably `/api/auth/refresh` relies on an Origin allow-list (`AuthController.cs:127-128`) as its CSRF defence because the refresh cookie is `SameSite=None`; a second origin that serves the same API is exactly the condition that makes such a list hard to reason about.

**Fix.** Both edges now allow-list rather than blanket-proxy. In `deploy/Caddyfile`, `@admin_api` and `@any_api` matchers sit inside a `route { }` block so directive order is preserved and evaluation is explicit. In `admin/nginx.conf`, a `map` produces `$fuelflow_api_allowed` and a single `if (...) { return 404; }` rejects everything else — one of the few uses of `if` in nginx that is safe. Only the specific endpoints the admin SPA actually calls are reachable through the admin hostname; the rest return 404 rather than 403, so the edge does not confirm what exists.

Also in the same pass: `server_tokens off`, `client_max_body_size 25m` (FF-29), 300 s proxy timeouts for imports, and removal of the dead `/uploads/` route (FF-30).
