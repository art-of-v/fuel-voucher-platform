# FuelFlow — Fraud Analysis

> Security review focused on the question: **"Where does money disappear?"**
> Answers are based on the current codebase and cite exact files/lines. Severity = likelihood × impact.

> **Status re-verified 2026-08-22** by the pre-production security audit
> (`docs/SECURITY_AUDIT_2026-08-21.md`). Closed vectors have been removed from this file rather
> than annotated — what remains is what is still soft, by design or by backlog.

---

## Executive summary

| # | Question | Verdict | Severity |
|---|---|---|---|
| 1 | Can supplier upload fake QR? | ⚠️ Partially — QR authenticity is a heuristic, not cryptographic | Medium |
| 2 | Can voucher already be redeemed? | ⚠️ Soft — redemption is **self-reported**, no POS verification | Medium |
| 3 | Can same voucher appear in two PDFs? | ✅ **No** — DB-global dedup by number OR QR payload | Low (protected) |
| 4 | Can insider steal inventory? | ⚠️ Not silently — admin voucher ops write audit events; nothing *alerts* on them yet | Low |
| 5 | Can admin manipulate margins? | ⚠️ Yes by design, but **audited**; margin is cosmetic for money | Low |

Closed since the original review (removed from this file; details in git history and the audit):
unverified Monobank webhook, client-supplied checkout price, unguarded callback duplication,
race-safe redemption (WP-4). Each is enforced in code and covered by the audit's refuted-hypotheses
appendix ("Appendix — refuted hypotheses" in `SECURITY_AUDIT_2026-08-21.md`).

**Still soft, by design rather than by defect:** redemption is self-reported (no POS verification) and
QR authenticity is heuristic. Neither moves money on its own; both are recorded as accepted risks.

---

## 🚫 DO NOT LAUNCH until these are fixed

| # | Blocker | Work package | Status |
|---|---------|--------------|--------|
| 1 | Automated backup with a demonstrated restore | WP-7 | ⚠️ Tooling shipped (`deploy/backup.sh`, `deploy/restore.sh`, age-encrypted off-host) — **a restore has never been demonstrated**, which is what this gate actually requires |
| 2 | Basic monitoring / alerting | WP-6 | ⬜ TODO (partial: request logging + error logs already shipped) |
| 3 | Signed OTA mobile updates | FF-03 (audit 2026-08-21) | ⬜ **TODO — Critical.** `mobile/app.json` sets `expo.updates.url` with no `codeSigningCertificate`, so whoever holds the EAS publish token can push arbitrary JS — including checkout screens — to every installed app. Needs a keypair, a certificate embedded in a **new native build**, and a store release. Gated in CI by `mobile/scripts/check-update-signing.mjs` |

**Gate:** production is not "live" until all three above are done and verified. Server-side pricing
(WP-2), webhook signature verification (WP-1), admin audit events (WP-3) and race-safe redemption
(WP-4) are implemented and no longer gate. WP-5 (signed vouchers) is *not* a launch blocker; it
hardens afterwards.

**Config required before launch:** set the real `Monobank:PublicKey` (and `Monobank:Token`) as
**environment variables on the DigitalOcean Droplet** (via `deploy/.env`, consumed by
`deploy/docker-compose.prod.yml`) — production fails fast on startup if Monobank is enabled with a
placeholder/empty key, and unverified callbacks are rejected with 401.

To get the public key, call the Monobank acquiring API with the merchant token (no base64-decoding needed — the app accepts the value verbatim, including base64-of-PEM):

```
curl -H "X-Token: $MONOBANK_TOKEN" https://api.monobank.ua/api/merchant/pubkey
# -> {"key":"<base64-encoded PEM>"}
```

Then set the returned `key` value as the `Monobank__PublicKey` env var in `deploy/.env` on the Droplet (underscore path; overrides `appsettings.json`). If Monobank signs callbacks with the `X-Key-Id` header during key rotation, set `Monobank__PublicKeys__<keyId>` for each active key — the controller resolves `X-Key-Id` against that map and falls back to `PublicKey` when the header is absent.

---

## Remaining work to address

| # | Item | Work package | Priority | Notes |
|---|------|--------------|----------|-------|
| 1 | Cryptographically signed voucher payloads (HMAC) | WP-5 | Medium (long-term) | Add `Signature` column; validate on import; supplier process change. |
| 2 | Monitoring alerts | WP-6 | Supporting | Alert on webhook signature failures, amount mismatches, admin voucher deletions/bulk actions, price mismatches. Request logging + Error Logs UI already shipped. |
| 3 | Daily reconciliation as incident source | WP-6 | Supporting | Treat non-zero reconciliation differences as incidents. |
| 4 | Automated backup & restore drill | WP-7 | Supporting (launch blocker) | Nightly encrypted off-site `pg_dump` + retention + monthly restore drill; runbook in `DEPLOY_DIGITALOCEAN.md` / `DIGITALOCEAN_OPERATIONS.md`. Tooling exists; the first restore is still owed. |
| 5 | Written confirmation that the committed Monobank token was rotated merchant-side | Config | High | Tracked in the audit as **FF-05**. Webhook verification has shipped, so the precondition is met. The owner attests all secrets were rotated **2026-08-20**; that is unverifiable without touching live systems, so what is still owed is a *written* confirmation that the token was revoked **merchant-side** (not merely replaced in config), plus an answer on whether this repository is or ever was public. |
| 6 | Set real `Monobank:PublicKey`/`Monobank:Token` as env vars in `deploy/.env` | Config | High | Fetch via `GET /api/merchant/pubkey` with `X-Token`; set `Monobank__PublicKey`. Placeholder key in `appsettings.Production.json` blocks startup in Production. |
| 7 | Redeploy backend + admin and verify in prod | Deploy | High | Apply EF migration on deploy; watch `RequestLoggingMiddleware` `Error`/`Warning` lines. |
| 8 | End-to-end verification with a real Monobank test payment | Deploy | High | Confirm signed callback reaches `Fulfilled` exactly once; forged/tampered callbacks rejected 401/400. |

### How to test what's already implemented

**Server-side pricing (WP-2):**
1. Start backend locally (`dotnet run` in `backend/src/FuelFlow.API`).
2. POST `/api/purchases` with a `Price` that differs from the package price (e.g., `Price: 1`) for a known `(stationId, fuelTypeId, liters)`.
3. Expect the response order to be priced at the server-computed value; a `Client price X does not match server price Y` warning is logged.

**Monobank webhook (WP-1):**
- Signature is **ECDSA (secp256k1) SHA-256 over the raw body** (`X-Sign` = base64 ASN.1 DER).
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

- Each QR is decoded and **heuristically verified** with `QrMatrixVerifier` (`ImportVouchersCommand.cs`) — it compares the decoded bit matrix against the rendered page and reports a mismatch **percentage**.
- Vouchers are still persisted even when verification fails — just tagged `VerificationFailed`.

There is **no cryptographic authenticity**: a voucher QR is not signed/HMAC'd by the supplier or by us, so a carefully fabricated QR that round-trips through our decoder cannot be reliably distinguished from a genuine one. The mismatch % is a proxy for "was this printed properly", not "was this issued by OKKO".

**Risk:** Medium. Requires an admin insider (see #4), and detection relies on a fuzzy heuristic, not proof.

## 2. Can voucher already be redeemed?

**Soft / self-reported.** `MarkVoucherAsUsedCommandHandler`:
- Requires the voucher to belong to the caller (`WorkerUserId`, then `AssignedToUserId`).
- Requires `Status == Assigned`; already-`Used` returns success (idempotent).
- The Assigned → Used transition is an atomic conditional `UPDATE ... WHERE Status == Assigned`
  (WP-4, 2026-08-22), so concurrent redemptions cannot double-write; covered by
  `MarkVoucherAsUsedConcurrencyIntegrationTests` against real Postgres.

The real issue: **redemption is honor-system**. The user calls `PATCH /api/vouchers/{id}/mark-used` themselves after fueling. There is no station/POS/pump integration that proves the fuel was actually dispensed. A user can:
- fuel and not mark → we lose a voucher to the supplier but don't consume it, or
- mark used without fueling → consumes stock with no revenue impact.

**Risk:** Medium. Doesn't move money out, but silently corrupts inventory/reconciliation.

## 3. Can same voucher appear in two PDFs?

**No.** Dedup is **global across the whole table**, not per import:
- `ImportVouchersCommand.cs`: `existsInDb` checks `VoucherNumber == X || QrPayload == X` against **all** `fuel_vouchers`, plus in-batch hash sets (`addedNumbers`, `addedPayloads`).
- A voucher number or QR payload that already exists is counted as a duplicate and skipped — regardless of which PDF or which import it arrives in.

This vector is well-protected.

## 4. Can insider steal inventory?

**Not silently.** Admin endpoints (`AdminVoucherController.cs`, all `[Authorize(Roles="Admin")]`):
- `GET /api/admin/vouchers` — full list including **QR payloads** and voucher numbers.
- `PUT /api/admin/vouchers/{id}` — set `Status` and `AssignedToUserId` directly.
- `DELETE /api/admin/vouchers/{id}` — soft delete.
- `POST /api/admin/vouchers/bulk-action` — bulk assign/delete/activate/expire.

Five handlers covering every state-changing path call `RecordEventAsync`:
`BulkActionVouchersCommandHandler`, `DeleteVoucherCommandHandler`,
`Import/VouchersController`, `UnblockVoucherCommand`, `UpdateVoucherCommandHandler`.

There is no separate assign endpoint — assignment flows through the audited `PUT /{id}` and the audited
bulk-action, so there is no unaudited mutation path left.

**Residual risk:** Low. An admin can still move inventory, but not silently. The remaining exposure is
detection rather than prevention: nothing currently *alerts* on these events, which is WP-6 (monitoring).

## 5. Can admin manipulate margins?

**Yes — but it's by design and audited.** Margin lives on `FuelPackage.MarginUahPerLiter`, editable only by Admin, and every price/margin change is recorded via `_eventService.RecordEventAsync(..., "PriceChanged", oldValue, newValue, user, ...)`. It's visible in the Providers tab history.

**Nuance:** margin only feeds the **Profit report** (`margin × liters × quantity`). It does **not** gate what users pay, because the checkout price is recomputed server-side from `FuelPackages` (WP-2). So margin tampering inflates/deflates *reported* profit but does not, by itself, let money leave.

**Risk:** Low (auditable, cosmetic).

---

## Remediation roadmap (open packages only)

Principles driving every fix: server authority, fail closed, verify against raw bytes, guarded state
changes via one concurrency-safe place, auditable by default, detect and alert.

| # | Work package | Closes | Severity | Effort |
|---|--------------|--------|----------|--------|
| 1 | Detection, alerting, daily reconciliation | insider/detection gap | Supporting | S–M |
| 2 | Automated backup & restore drill | operational | Supporting (launch blocker) | S |
| 3 | Cryptographically signed voucher payloads | fake-QR vector | Medium (long-term) | L |

### WP-5 (Medium, long-term) — Cryptographically signed voucher payloads

- Add a `Signature` column to vouchers; HMAC-SHA256 over `(provider, fuelTypeId, liters, expiresAtUtc, voucherNumber)` with a server-held secret.
- On import, when a signature is present it must verify (else `VerificationFailed`); this upgrades the heuristic `QrMatrixVerifier` from "looks like a QR" to "authenticated payload". Unsigned batches remain importable behind a config flag so the supplier can migrate.
- This is a **process change** (supplier starts signing payloads); ship the validator first, enforce signing later.

**Acceptance criteria:** a forged-but-valid-QR voucher with no valid HMAC is tagged `VerificationFailed`; signed vouchers with a single bit flipped fail.

### WP-6 (Supporting) — Detection, alerting, daily reconciliation

- Alerts on: webhook signature failures, webhook `Amount` mismatches, admin voucher deletions/bulk actions, rapid voucher-to-new-user assignments.
- The daily reconciliation report (imported vs assigned vs used vs redeemed) is the safety net for anything that slips through — treat a non-zero difference as an incident, not a log line.
- Surface `RequestLoggingMiddleware` `Error`/`Warning` output into the admin Error Logs tab so operators see these signatures without log access.

### WP-7 (Supporting, launch blocker) — Automated backup & restore

- **Tooling shipped**: nightly `pg_dump` via cron on the Droplet (`deploy/backup.sh`), `age`-encrypted, off-host upload via rclone (`BACKUP_REMOTE`), retention pruning, TOC validation; restore via `deploy/restore.sh`.
- **Still owed:** schedule the cron, configure `BACKUP_REMOTE`, and perform one documented restore into a scratch instance asserting row counts — before launch. Repeat monthly/quarterly after.
- **Secrets**: backup keys in env vars, never in the repo.

**Acceptance criteria:** a nightly backup exists off-site with retention; a documented restore was executed successfully at least once from the production backup before launch.

### Config & secrets hygiene

- `Monobank:PublicKey`, `Monobank:Token` must come from **environment variables in `deploy/.env`**, not committed placeholders. Production already fails fast if `Monobank:Enabled=true` while `PublicKey` is empty/placeholder.
- Rotate-and-confirm the Monobank token per FF-05 (row 5 above); never log raw bodies or full `X-Sign` — log fingerprints only.
