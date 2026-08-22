# 6. Deploy-blocking checklist

Ordered. Each item is independently verifiable by someone other than its author — that is the point of the "how to verify" column. Nothing here says "review" or "make sure"; each is a command to run or a document to sign.

## Blockers — deploy cannot proceed

| # | Action | Why | How to verify |
|---|---|---|---|
| **1** | Rotate the EAS publish token; restrict who can publish to the OTA channel | [FF-03](02-findings-critical-high.md#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-open). Until signing ships, the token *is* the trust boundary for the entire installed base | New token issued after 2026-08-22; old token shows revoked in the Expo console; publisher list enumerated in writing |
| **2** | Generate an update signing keypair; add `codeSigningCertificate` to the `updates` block; build and release a new native build to both stores | [FF-03](02-findings-critical-high.md#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-open). This is the actual fix. **Do not add the config key before the certificate file exists — it breaks the build** | A build from the new binary rejects an unsigned manifest. Private key lives in the EAS secret store, and `git log -- <cert path>` shows no private key was ever committed |
| **3** | Perform a restore drill: restore the latest encrypted backup to a scratch database and compare row counts | [FF-14](03-findings-medium-low.md#ff-14--backups-unencrypted-on-host-restore-never-exercised--medium-confirmed-fixed-in-tooling-restore-drill-open). Also this project's own launch gate, `docs/FRAUD_ANALYSIS.md:33,36` | A dated note recording: archive timestamp, `pg_restore` exit 0, and row counts for `Users`, `FuelVouchers`, `Orders` matching production ±expected drift |
| **4** | Confirm in writing that the Monobank merchant token in `3cb50dc` was rotated on or after 2026-08-20 and revoked **merchant-side**, and state whether this repository is or ever was public | [FF-05](02-findings-critical-high.md#ff-05--monobank-merchant-token-committed-to-git-history--high-suspected-live-open). Replaced-in-config is not revoked. If the repo was ever public, the reader population is unbounded | A dated attestation naming the rotation date, plus the repo's visibility setting and history |
| **5** | Decide git history: rewrite with `git filter-repo` and force-push, **or** formally accept the residue | [FF-05](02-findings-critical-high.md#ff-05--monobank-merchant-token-committed-to-git-history--high-suspected-live-open). Accepting is defensible only if #4 confirms rotation **and** the repo was never public. Rewriting invalidates every clone and fork | Either the rewritten history verified clean, or a signed acceptance recording both preconditions |

## Required before first real traffic

| # | Action | Why | How to verify |
|---|---|---|---|
| 6 | Set every `:?`-guarded variable in the production `.env`, including `MONOBANK_REDIRECT_URL` | [FF-18](03-findings-medium-low.md#ff-18--monobank_redirect_url-had-no--guard--low-confirmed-fixed) | `docker compose -f deploy/docker-compose.prod.yml config` exits 0 with no substitution warnings |
| 7 | Evaluate the **merged** production config (env + `appsettings.Production.json`) as a single unit | GO criterion. Neither file is meaningful alone — `Auth:DevBypass` from either source is a full auth bypass | Boot in Production and confirm `ValidateSecurityConfiguration` (`Program.cs:167-255`) passes; capture the startup log and check for any `SECURITY:` warning |
| 8 | Decide device binding explicitly: `DeviceAuth__Enabled=true` **only if** a signing mobile build is released, otherwise `DeviceAuth__AcknowledgeDisabledInProduction=true` | `TODO.md:82` — enabling early locks existing users out of checkout. [FF-21](03-findings-medium-low.md#ff-21--deviceauthoptions-defaults-fail-open--low-confirmed-fixed) makes this a stated choice rather than a default | Startup either passes silently (enabled) or logs the `SECURITY: DeviceAuth is disabled` warning (`Program.cs:235-237`). **A recorded decision either way — no third outcome exists**, because the app refuses to start |
| 9 | Review and prune `Auth:TestPhones` | Each entry is a permanent, non-rotating OTP for that number — a production credential | The startup warning at `Program.cs:243-254` reports the expected count. If it reports 0, better |
| 10 | Validate the Caddyfile | Could not be verified locally (no `caddy` binary in this environment) | `docker compose exec caddy caddy validate --config /etc/caddy/Caddyfile` exits 0 |
| 11 | Confirm the redis container reaches `healthy` | The healthcheck argv changed and the Docker daemon was unavailable locally (`npipe:////./pipe/dockerDesktopLinuxEngine`) | `docker compose ps` shows `redis` as `healthy`, not `starting` or `unhealthy` |
| 12 | Load the admin dashboard and confirm zero CSP violations | [FF-17](03-findings-medium-low.md#ff-17--no-security-response-headers--low-confirmed-fixed). The build has 0 inline scripts so `script-src 'self'` should hold — should, verified locally, not observed in production | Browser console shows no `Content Security Policy` errors on login and on every tab |
| 13 | Import a real voucher PDF **larger than 1 MB** through the admin origin | [FF-29](03-findings-medium-low.md#ff-29--nginx-client_max_body_size-would-413-voucher-imports--low-confirmed-fixed). The 413 would have appeared on day one of real use | Import completes; no 413 in the nginx access log |
| 14 | Confirm `/health` returns 200 and that something actually watches it | `TODO.md:35` confirmed the endpoint; `FRAUD_ANALYSIS.md:34` shows nothing monitors it | `curl -f https://<host>/health` exits 0, **and** an alert destination is named and tested with a deliberate failure |
| 15 | Verify a Monobank webhook end-to-end in production with the real public key | `Program.cs:177-185` blocks a placeholder key, but "not a placeholder" is not "the correct key" | One real sandbox-or-live payment transitions an order to `PendingFulfillment`; a request with a tampered body is rejected |
| 16 | Confirm the two `.gitleaks.toml` commit allowlist entries are documented and time-bound | The allowlist keeps the scanner's signal meaningful; it must not become permanent amnesia | Both entries carry an in-file comment naming FF-05 and the decision from #5 |

## Explicitly *not* blockers

Recorded so nobody treats them as gates and so nobody quietly drops them:

- **[FF-23](03-findings-medium-low.md#ff-23--voucher-import-runs-synchronously-in-request--medium-confirmed-partial)** (synchronous import) — bounded by `ImportConcurrencyGuard`; a single large import still occupies a thread. Backlog, not a gate.
- **[FF-31](03-findings-medium-low.md#ff-31--anonymous-cacheable-apistationsfuel-types-returns-the-raw-entity--info-confirmed-open-hardening)** (raw entity on an anonymous cacheable endpoint) — nothing sensitive on it today. Hardening.
- **[FF-33](03-findings-medium-low.md#ff-33--sshnet-202510-high-severity-cve--info-confirmed-open-accepted)** (`SSH.NET` CVE) — test-project-only, CI-bounded. Watchlist.
- **[FF-15](03-findings-medium-low.md#ff-15--tokenversion-latent-trap--low-suspected-fixed-documented)** (`TokenVersion`) — documented; no current impact.
- **WP-4** atomic `mark-used` (`FRAUD_ANALYSIS.md:57`) — genuinely unimplemented, genuinely benign: idempotent outcome, no money moves.
