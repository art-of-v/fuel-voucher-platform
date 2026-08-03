# FuelFlow — Fraud Analysis

> Security review focused on the question: **"Where does money disappear?"**
> Answers are based on the current codebase and cite exact files/lines. Severity = likelihood × impact.

---

## Executive summary

| # | Question | Verdict | Severity |
|---|---|---|---|
| 1 | Can supplier upload fake QR? | ⚠️ Partially — QR authenticity is a heuristic, not cryptographic | Medium |
| 2 | Can voucher already be redeemed? | ⚠️ Soft — redemption is **self-reported**, no POS verification | Medium |
| 3 | Can same voucher appear in two PDFs? | ✅ **No** — DB-global dedup by number OR QR payload | Low (protected) |
| 4 | Can insider steal inventory? | ❌ **Yes** — admin voucher ops are **not audited** | **High** |
| 5 | Can admin manipulate margins? | ⚠️ Yes by design, but **audited**; margin is cosmetic for money | Low |
| 6 | Can webhook replay happen? | ❌ **Yes** — signature **not verified** | **Critical** |
| 7 | Can Mono callback be duplicated? | ⚠️ Partially idempotent; no state-machine guard | Medium |

**The two places money actually disappears today:**
1. **Unverified Monobank webhook** (`MonobankWebhookController.cs`) — anyone who knows an `InvoiceId` can forge a `success` callback and get real vouchers **without paying**.
2. **Client-supplied order price** (`CreateCheckoutCommandHandler.cs:80`, `BulkCheckoutCommandHandler.cs:52`) — the server trusts `command.Price` and never recomputes it from server-side `FuelPackages`. A patched client can pay ~0 ₴ for real fuel vouchers.

---

## 🚫 DO NOT LAUNCH until these are fixed

| # | Blocker | Work package | Status |
|---|---------|--------------|--------|
| 1 | ✅ Server-side pricing | WP-2 | ✅ Implemented (`ServerPricing` recomputes from `FuelPackages` in `CreateCheckout`/`BulkCheckout`) |
| 2 | ✅ Monobank signature verification | WP-1 | ✅ Implemented (shared `AsymmetricSignatureVerifier`, `X-Sign` verified over raw body, fail-closed) |
| 3 | ✅ Admin audit | WP-3 | ✅ Implemented (`ProviderEventService` events for update/delete/bulk/import) |
| 4 | ✅ Automated backup | WP-7 (new) | ⬜ TODO |
| 5 | ✅ Basic monitoring | WP-6 | ⬜ TODO (partial: request logging + error logs already shipped) |

**Gate:** production is not "live" until all five above are done and verified (per the acceptance criteria in their WPs). WP-4 (race-safe redemption) and WP-5 (signed vouchers) are *not* launch blockers — they harden afterwards.

**Config required before launch (WP-1):** set the real `Monobank:PublicKey` (and `Monobank:Token`) as **Render env vars** — production now fails fast on startup if Monobank is enabled with a placeholder/empty key, and unverified callbacks are rejected with 401.

To get the public key, call the Monobank acquiring API with the merchant token (no base64-decoding needed — the app accepts the value verbatim, including base64-of-PEM):

```
curl -H "X-Token: $MONOBANK_TOKEN" https://api.monobank.ua/api/merchant/pubkey
# -> {"key":"<base64-encoded PEM>"}
```

Then set the returned `key` value as the `Monobank__PublicKey` env var on Render (underscore path; overrides `appsettings.json`). If Monobank signs callbacks with the `X-Key-Id` header during key rotation, set `Monobank__PublicKeys__<keyId>` for each active key — the controller resolves `X-Key-Id` against that map and falls back to `PublicKey` when the header is absent.

---

## Remaining work to address

Implemented (2026-08-03): WP-1, WP-2, WP-3. Everything below is still open.

| # | Item | Work package | Priority | Notes |
|---|------|--------------|----------|-------|
| 1 | Race-safe, reference-bound voucher redemption | WP-4 | Medium | Rewrite `MarkVoucherAsUsed` as conditional `UPDATE ... WHERE status='Assigned' AND assigned_to_user_id=@user` → `409` on 0 rows. |
| 2 | Cryptographically signed voucher payloads (HMAC) | WP-5 | Medium (long-term) | Add `Signature` column; validate on import; supplier process change. |
| 3 | Monitoring alerts | WP-6 | Supporting | Alert on webhook signature failures, amount mismatches, admin voucher deletions/bulk actions, price mismatches. Request logging + Error Logs UI already shipped. |
| 4 | Daily reconciliation as incident source | WP-6 | Supporting | Treat non-zero reconciliation differences as incidents. |
| 5 | Automated backup & restore | WP-7 | Supporting (launch blocker) | Nightly encrypted off-site `pg_dump` + retention + monthly restore drill; runbook in `DEPLOY.md`. |
| 6 | Rotate committed Monobank token | Config | High | Token currently committed; rotate after webhook verification ships. |
| 7 | Set real `Monobank:PublicKey`/`Monobank:Token` as Render env vars | Config | High | Fetch via `GET /api/merchant/pubkey` with `X-Token`; set `Monobank__PublicKey`. Placeholder key in `appsettings.Production.json` blocks startup in Production. |
| 8 | Redeploy backend + admin and verify in prod | Deploy | High | Apply EF migration on deploy; watch `RequestLoggingMiddleware` `Error`/`Warning` lines. |
| 9 | Refund unfulfilled value of partial orders | Feature | Medium | ✅ Implemented (2026-08-03, uncommitted): `POST /api/admin/orders/{id}/refund` + auto-refund on `PartiallyFulfilled`; server-computed `(ordered − fulfilled) × unit_price` kopecks via `POST /api/merchant/invoice/cancel`; `refunds` table; audit events. Pending: commit/deploy + handle `cancelList` finalization. |
| 9 | End-to-end verification with a real Monobank test payment | Deploy | High | Confirm signed callback reaches `Fulfilled` exactly once; forged/tampered callbacks rejected 401/400. |

### How to test what's already implemented

**Server-side pricing (WP-2):**
1. Start backend locally (`dotnet run` in `backend/src/FuelFlow.API`).
2. POST `/api/orders/checkout` with a `Price` that differs from the package price (e.g., `Price: 1`) for a known `(stationId, fuelTypeId, liters)`.
3. Expect the response order to be priced at the server-computed value; a `Client price X does not match server price Y` warning is logged.

**Monobank webhook (WP-1):**
- Signature is **ECDSA (secp256k1) SHA-256 over the raw body** (`X-Sign` = base64 ASN.1 DER). secp256k1 is supported on Linux (Render); on Windows the verifier degrades to RSA/other ECDSA curves only.
- With `Monobank:Enabled=true` (dev has a bypass only for device auth, not webhooks):
  - POST `/api/monobank/webhook` without `X-Sign` → `401`.
  - Valid signature + `Amount == order.Price * 100` → order → `PendingFulfillment`.
  - Same signature/body replayed with an older `ModifiedDate` → `200`, no transition.
  - `Amount` mismatch → `400`, no transition.
- Generate a test key pair to sign the raw body (see `AsymmetricSignatureVerifierTests`).

**Admin audit (WP-3):**
1. In the admin panel, update a voucher's status, delete a voucher, or run a bulk action.
2. Open the **Audit Log** tab — expect `VoucherUpdated` / `VoucherDeleted` / `VoucherBulkAction` rows with the acting admin and old/new values.
3. Import a PDF of vouchers → expect a `VoucherImported` row with counts.

---

## 1. Can supplier upload fake QR?

**Partially.** Import is Admin-only (`[Authorize(Roles = "Admin")]` on `VouchersController.cs:29`), so a "supplier" needs admin access first. Inside import:

- Each QR is decoded and **heuristically verified** with `QrMatrixVerifier` (`ImportVouchersCommand.cs:265-308`) — it compares the decoded bit matrix against the rendered page and reports a mismatch **percentage**.
- Vouchers are still persisted even when verification fails — just tagged `VerificationFailed` (`ImportVouchersCommand.cs:273-280`).

There is **no cryptographic authenticity**: a voucher QR is not signed/HMAC'd by the supplier or by us, so a carefully fabricated QR that round-trips through our decoder cannot be reliably distinguished from a genuine one. The mismatch % is a proxy for "was this printed properly", not "was this issued by OKKO".

**Risk:** Medium. Requires an admin insider (see #4), and detection relies on a fuzzy heuristic, not proof.

## 2. Can voucher already be redeemed?

**Soft / self-reported.** `MarkVoucherAsUsedCommandHandler.cs:28-46`:
- Requires `voucher.AssignedToUserId == command.UserId` (user can only mark their own vouchers used).
- Requires `Status == Assigned`; already-`Used` returns success (idempotent).
- **No conditional `UPDATE ... WHERE status='Assigned'`** (unlike fulfillment, `FulfillmentService.cs:474-481`), so there is a small TOCTOU race on concurrent marks — benign (no double payout).

The real issue: **redemption is honor-system**. The user calls `PATCH /api/vouchers/{id}/mark-used` themselves after fueling. There is no station/POS/pump integration that proves the fuel was actually dispensed. A user can:
- fuel and not mark → we lose a voucher to the supplier but don't consume it, or
- mark used without fueling → consumes stock with no revenue impact.

**Risk:** Medium. Doesn't move money out, but silently corrupts inventory/reconciliation (which is the same problem the partially-fulfilled order in the logs hints at).

## 3. Can same voucher appear in two PDFs?

**No.** Dedup is **global across the whole table**, not per import:
- `ImportVouchersCommand.cs:201-212`: `existsInDb` checks `VoucherNumber == X || QrPayload == X` against **all** `fuel_vouchers`, plus in-batch hash sets (`addedNumbers`, `addedPayloads`).
- A voucher number or QR payload that already exists is counted as a duplicate and skipped — regardless of which PDF or which import it arrives in.

This vector is well-protected.

## 4. Can insider steal inventory?

**Yes — the highest-likelihood vector.**

Admin endpoints (`AdminVoucherController.cs`, all `[Authorize(Roles="Admin")]`):
- `GET /api/admin/vouchers` — full list including **QR payloads** and voucher numbers.
- `PUT /api/admin/vouchers/{id}` — set `Status` and `AssignedToUserId` directly.
- `DELETE /api/admin/vouchers/{id}` — soft delete (`DeleteVoucherCommandHandler.cs:23`).
- `POST /api/admin/vouchers/bulk-action` — bulk assign/delete/activate/expire.

**Critical gap:** these voucher mutations write **no audit events**. There is no `RecordEventAsync`/`IEventService` anywhere under `Features/Vouchers/`. A rogue (or compromised) admin can assign vouchers to a controlled account, delete stock, or flip statuses with **no trail** to detect it. Compare with provider pricing, which IS audited (`ProvidersController.cs:328-333`, `PriceChanged` with old/new values).

**Risk:** High. Requires one admin credential; silent and unrecoverable.

## 5. Can admin manipulate margins?

**Yes — but it's by design and audited.** Margin lives on `FuelPackage.MarginUahPerLiter`, editable only by Admin (`ProvidersController.cs:16`), and every price/margin change is recorded via `_eventService.RecordEventAsync(..., "PriceChanged", oldValue, newValue, user, ...)` (`ProvidersController.cs:328-333`). It's visible in the Providers tab history.

**Nuance:** margin only feeds the **Profit report** (`GetReportQueryHandler.cs:103-111`: `margin × liters × quantity`). It does **not** gate what users pay, because the checkout price is **client-supplied** (see #8). So margin tampering inflates/deflates *reported* profit but does not, by itself, let money leave.

**Risk:** Low (auditable, cosmetic until pricing is server-authoritative).

## 6. Can webhook replay happen?

**Yes — critical.**

`MonobankWebhookController.cs`:
- `POST /api/monobank/webhook` is **unauthenticated** (no `[Authorize]` — normal for webhooks, but then the signature is the only defense).
- The `X-Sign` header is read and **only logged**; signature verification is an explicit `// TODO: Verify signature using Monobank public key` (`MonobankWebhookController.cs:43-51`).
- The payload's **`Amount` is never compared** to `order.Price` (`MonobankWebhookController.cs:64-73`, handler ignores it).

Consequence: anyone who knows/guesses an `InvoiceId` can POST `{ status: "success", invoiceId: ... }` and the handler:
1. sets the order to `PendingFulfillment` (`ProcessMonobankWebhookCommandHandler.cs:56`),
2. emits an `ORDER_CREATED` outbox event,
3. fulfillment assigns **real vouchers** to the buyer's account.

`InvoiceId` is not secret — it's echoed back to the mobile client in the checkout response and embedded in the Monobank payment URL.

Replay of a stale `failure`/`reversed` also forces `Status = Cancelled` **unconditionally** (`:94`), even on an already-`Fulfilled` order (vouchers aren't un-assigned, but order state corrupts and reports break).

**Risk:** Critical. This is the #1 money-out vector.

## 7. Can Mono callback be duplicated?

**Partially handled, not fully.**

Good news:
- Duplicate `ORDER_CREATED` outbox events are deduped by payload-contains-order-id (`ProcessMonobankWebhookCommandHandler.cs:61-89`).
- Fulfillment is idempotent at the row level via conditional `UPDATE ... WHERE status='Available'` (`FulfillmentService.cs:474-481`) and `WHERE status IN ('PendingFulfillment','PartiallyFulfilled')` for marking fulfilled (`:465-472`).
- `ProcessOrderCreatedEventAsync` skips orders already `Fulfilled`/`Cancelled` (`FulfillmentService.cs:249-257`).

Bad news:
- The webhook itself has **no idempotency key** and **no state-machine guard** on transitions. A late duplicate `success` arriving after a legitimate `failure`/`reverse` will flip the order **back** to `PendingFulfillment` and re-enqueue fulfillment.
- Order of webhook delivery isn't honored — last-writer-wins on `Status` regardless of `ModifiedDate`.

**Risk:** Medium. Doesn't allow double-spend of a voucher, but allows status flips (order cancelled ↔ pending) and — combined with #6 — forgery.

## 8. (Bonus) Client-supplied checkout price

Not in the original list, but this is the other money-out hole:

- `CreateCheckoutCommandHandler.cs:80` → `Price = command.Price` (client value), `:95` → `UnitPrice = command.Price / quantity`.
- `BulkCheckoutCommandHandler.cs:52` → `totalPrice = command.Items.Sum(i => i.Price)`.
- The only server validation is that the `FuelTypeId` exists for the station (`CreateCheckoutCommandHandler.cs:48-54`). The price is **never recomputed** from `FuelPackages`.

`/api/orders/checkout` is HMAC-signed by the device (`DeviceAuth` config), but the signature authenticates the device, **not** the price value. A patched app (or the mobile **web build**, where the signing key lives in browser storage) can submit `Price ≈ 0`, pay ~nothing, and receive vouchers worth the real amount. Fixing this is what makes #5's margins meaningful.

---

## Remediation plan (best practice)

### Principles that drive every fix

- **Server authority** — money-affecting values (price, amount, status) are computed and enforced server-side; the client only ever *proposes*.
- **Fail closed** — a request that can't be authenticated is rejected (401/400), never passed through or silently logged in production.
- **Verify against the raw bytes** — signatures are checked over the original body stream, never over a re-serialized JSON object.
- **Guarded state changes** — every order/voucher status transition goes through one place that enforces legal edges and is concurrency-safe (conditional `UPDATE`).
- **Auditable by default** — every privileged mutation writes an event; absence of a trail is itself treated as a defect.
- **Detect and alert** — a control that only logs but never alerts is a forensics tool, not a control.

### Roadmap (ship order — severity, not effort)

| # | Work package | Closes | Severity | Effort |
|---|--------------|--------|----------|--------|
| 1 | Webhook authenticity (signature + amount + state machine) | #6, #7, forgery sub-case | Critical | M |
| 2 | Server-authoritative checkout pricing | #8 | Critical | S |
| 3 | Audit every admin voucher mutation | #4 | High | S |
| 4 | Race-safe, reference-bound redemption | #2 | Medium | S |
| 5 | Cryptographically signed voucher payloads | #1 | Medium (long-term) | L |
| 6 | Detection, alerting, daily reconciliation | all | Supporting | S–M |
| 7 | Automated backup & restore | operational | Supporting | S–M |

---

### WP-1 (Critical) — Webhook authenticity: signature + amount + state machine

**Signature verification (`X-Sign`).**
- Monobank signs the **raw callback body** with their private key; `X-Sign` is the base64 signature, verified against Monobank's published public key (`MonobankOptions.PublicKey` — already configured).
- Extract the existing `VerifySignature` helper (`DeviceSignatureMiddleware.cs:197`, RSA SHA-256 PKCS#1 primary, ECDSA DER/IEEE-P1363 fallback) into a shared `IWebhookSignatureVerifier` and reuse it in `MonobankWebhookController.cs:41-51`. Do **not** duplicate crypto.
- Capture the raw body with `EnableBuffering()` and verify before any deserialization — re-serializing JSON breaks the signature.
- **Fail closed:** missing/malformed `X-Sign` → `401`; invalid signature → `400`; always `LogWarning` (these are probe indicators). Dev-only bypass via `Monobank:Enabled` + an explicit dev flag; never available in Production.

**Amount validation.**
- Monobank sends `Amount` in **kopecks**. Reject the callback unless `webhook.Amount == order.Price * 100` (`MonobankWebhookController.cs:64-73` currently ignores it). Mismatch → log at `Error`, do not transition the order.

**Freshness + idempotency.**
- Persist `LastWebhookProcessedUtc` / `LastWebhookModifiedDate` on the order. Reject any callback whose `ModifiedDate` is **older than or equal to** the stored one (blocks replay of stale states).
- Add an idempotency key per delivery (`invoiceId | status | ModifiedDate`) held in cache with a TTL (mirrors the device nonce pattern at `DeviceSignatureMiddleware.cs:105-118`) so duplicated deliveries can't double-transition.

**Guarded state machine.**
- Centralize transitions in an `OrderStateMachine.TryTransition(from, to)` with an explicit edge table — no direct status assignment outside it:
  - `PendingPayment → PendingFulfillment` (webhook success **and** amount matches)
  - `PendingPayment → Cancelled` (webhook `failure`/`reverse`)
  - `PendingFulfillment / PartiallyFulfilled → Fulfilled` (fulfillment job only)
  - `Fulfilled` and `Cancelled` are **terminal** — no webhook may ever re-flip them.
- Persist each transition through the existing outbox/audit path, and keep the conditional-`UPDATE` concurrency guard style already used in `FulfillmentService.cs:465-481`.

**Acceptance criteria:** replaying a recorded webhook changes nothing; tampered `Amount` or `X-Sign` yields 4xx + no transition; a `success` callback after `Fulfilled` is a no-op.

---

### WP-2 (Critical) — Server-authoritative checkout pricing

- In `CreateCheckoutCommandHandler.cs:80` and `BulkCheckoutCommandHandler.cs:52,103-106`, **drop `command.Price` entirely**: look up the `FuelPackage` by `(stationId, fuelTypeId, liters)` and compute
  `Price = round(FinalPricePerLiter × liters, 2)`, `LineTotal = Price × quantity`, `totalPrice = Σ`.
- Add FluentValidation to the checkout DTOs (registered via `AddValidatorsFromAssemblyContaining<Program>`): reject unknown stations/fuel types, out-of-range `liters`/`quantity`.
- If the client's proposed price differs from the recomputed one, log it (`Warning`) for monitoring — the request is still priced server-side. This also makes the Profit report (margin formula, `GetReportQueryHandler.cs:103-111`) truthful.
- Device-signature middleware stays as anti-automation/anti-replay (timestamp + nonce); it no longer needs to protect a price that the server ignores.

**Acceptance criteria:** a patched client submitting `Price=0` receives a PendingPayment order priced at the package rate; no order can ever carry a price not derived from `FuelPackages`.

---

### WP-3 (High) — Audit every admin voucher mutation

- Inject `IEventService` into the voucher handlers (`AdminVoucherController.cs:69-96`, `DeleteVoucherCommandHandler.cs:23`, assign, bulk actions, import) and `RecordEventAsync` with **actor + old value + new value + affected count**, exactly mirroring the audited margin changes (`ProvidersController.cs:328-333`).
- Events to emit: `VoucherImported`, `VoucherAssigned`, `VoucherDeleted`, `VoucherStatusChanged`, `VoucherBulkAction`.
- Confirm the admin Audit tab already renders these event types; add types if not.

**Acceptance criteria:** every voucher mutation in admin is searchable by actor in the audit store; a `VoucherDeleted` event exists for every deleted voucher.

---

### WP-4 (Medium) — Race-safe, reference-bound redemption

- Rewrite `MarkVoucherAsUsedCommandHandler.cs:28-46` as a single conditional
  `UPDATE ... SET status='Used', used_by_user_id=@user, used_at=@now WHERE id=@id AND status='Assigned' AND assigned_to_user_id=@user`; **0 rows → `409 Conflict`** (already used or not yours). Closes the check-then-set window.
- When a real station/POS integration exists, add `StationId`/`RedemptionReference` to the redemption record; until then keep redemption as a low-trust input consumed only by reconciliation (never by fulfillment).
- Optionally bound the redemption window (`assigned_at + N days`).

**Acceptance criteria:** two concurrent redemption calls yield exactly one success; a voucher cannot be redeemed by anyone but its assigned user.

---

### WP-5 (Medium, long-term) — Cryptographically signed voucher payloads

- Add a `Signature` column to vouchers; HMAC-SHA256 over `(provider, fuelTypeId, liters, expiresAtUtc, voucherNumber)` with a server-held secret.
- On import, when a signature is present it must verify (else `VerificationFailed`); this upgrades the heuristic `QrMatrixVerifier` (`ImportVoucherCommand.cs:265-308`) from "looks like a QR" to "authenticated payload". Unsigned batches remain importable behind a config flag so the supplier can migrate.
- This is a **process change** (supplier starts signing payloads); ship the validator first, enforce signing later.

**Acceptance criteria:** a forged-but-valid-QR voucher with no valid HMAC is tagged `VerificationFailed`; signed vouchers with a single bit flipped fail.

---

### WP-6 (Supporting) — Detection, alerting, daily reconciliation

- Alerts on: webhook signature failures, webhook `Amount` mismatches, orders priced differently than client-proposed, admin voucher deletions/bulk actions, rapid voucher-to-new-user assignments.
- The daily reconciliation report (imported vs assigned vs used vs redeemed) is the safety net for anything that slips through — treat a non-zero difference as an incident, not a log line.
- Surface `RequestLoggingMiddleware` `Error`/`Warning` output (already deployed) into the admin Error Logs tab so operators see these signatures without log access.

---

### WP-7 (Supporting, launch blocker) — Automated backup & restore

- **Postgres**: nightly `pg_dump` (or `pg_dump -Fc` for point-in-time restores) scheduled via a Render Cron Job; upload to off-site object storage (S3/R2/Wasabi) with versioning + 30-day retention.
- **Encrypt** dumps at rest (KMS/SSE); backups are useless if they leak customer/voucher data.
- **Verify** restores regularly — schedule a monthly restore into a throwaway instance and assert row counts + a query smoke test. An unverified backup is a belief, not a control.
- **Secrets**: backup keys in env vars, never in the repo. Document the restore runbook in `docs/DEPLOY.md`.

**Acceptance criteria:** a nightly backup exists off-site with retention; a documented restore was executed successfully at least once from the production backup before launch.

---

### Config & secrets hygiene (do before/with WP-1)

- `Monobank:PublicKey`, `Monobank:Token`, and the voucher signing secret must come from **Render env vars**, not committed placeholders. Add a startup guard that **fails fast in Production** if `Monobank:Enabled=true` while `PublicKey` is empty/placeholder.
- Rotate the Monobank token after shipping WP-1 (it is currently committed and has no signature check protecting its callbacks).
- Never log raw bodies or full `X-Sign`; log `X-Sign` fingerprints only.

---

### Testing strategy (each WP ships with tests)

- `SignatureVerifier`: valid, missing, malformed, tampered-body, wrong-key — positive and negative.
- Webhook flow (Testcontainers integration): success (happy path), `Amount` mismatch, replay of a stored callback, callback after `Fulfilled`, duplicate delivery (idempotency).
- State machine: every edge and every illegal/stale transition.
- Checkout: client `Price=0` ignored, unknown fuel type rejected.
- Voucher audit: events emitted with correct actor/old/new for assign/delete/bulk.
- `mark-used`: two concurrent calls → one `Conflict`.

---

### Rollout & verification

1. Ship **WP-1 + WP-2 together** (they both gate money-out) and **WP-3**; redeploy backend.
2. Canary: watch `RequestLoggingMiddleware` `Error`/`Warning` lines + error_logs for 24 h; run one real Monobank test payment end-to-end; confirm signature passes and order reaches `Fulfilled` exactly once.
3. Then WP-4, WP-5, WP-6. Re-run reconciliation daily and treat differences as incidents.
4. **Definition of done:** the "money disappears" vectors (#6, #7, #8) are closed by code + tests + config, and admin voucher mutations are auditable end-to-end.

---

### Evidence index

- Webhook unsigned / amount ignored: `Features/Monobank/ProcessWebhook/MonobankWebhookController.cs:41-51,64-73`
- Reusable RSA/ECDSA verifier (extract for webhooks): `Middleware/DeviceSignatureMiddleware.cs:197-250`
- Monobank config already present: `SharedKernel/Options/MonobankOptions.cs` (`PublicKey`, `Token`, `Enabled`)
- Status transitions unguarded: `.../ProcessMonobankWebhookCommandHandler.cs:53-97`
- Fulfillment idempotent guards (pattern to copy): `BackgroundJobs/FulfillmentService.cs:249-257,465-481`
- Global voucher dedup: `Features/Vouchers/Import/ImportVouchersCommand.cs:201-212`
- Heuristic QR verification: `.../ImportVouchersCommand.cs:265-308`
- Client-supplied price: `Features/Orders/CreateCheckout/CreateCheckoutCommandHandler.cs:80,95`; `BulkCheckoutCommandHandler.cs:52,80,103-106`
- Self-reported redemption: `Features/Vouchers/MarkVoucherAsUsed/MarkVoucherAsUsedCommandHandler.cs:28-46`
- Un-audited admin voucher ops: `Features/Vouchers/AdminVoucherController.cs:69-96`, `DeleteVoucherCommandHandler.cs:23`
- Audited margin changes (pattern to mirror): `Features/Providers/ProvidersController.cs:328-333`
- Profit = margin formula: `Features/Report/GetReport/GetReportQueryHandler.cs:103-111`
