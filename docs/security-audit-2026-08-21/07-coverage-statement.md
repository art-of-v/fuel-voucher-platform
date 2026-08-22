# 8. Coverage statement

The brief's requirement, verbatim: *"Silent partial coverage reads as 'all clear' and is worse than an admitted gap."* This section is the admission. Everything below is either not audited, audited incompletely, or audited under a constraint that limits the conclusion.

Read this section before treating any part of this report as an all-clear.

---

## What was audited thoroughly

- **Money paths, end to end.** Checkout (single and bulk), server-side pricing, Monobank invoice creation, webhook processing and signature verification, order state machine, fulfilment and voucher assignment, refunds. Attacked with absent, empty, negative, enormous, duplicated, replayed, reordered, concurrent and someone-else's values.
- **Voucher lifecycle.** Import, assignment, `mark-used`, blocking/unblocking, deletion, bulk actions, audit-event coverage, uniqueness constraints.
- **Authentication.** OTP send/verify, refresh-token rotation and hashing, family revocation, session validation, `DevBypass`, test phones.
- **Device signing middleware** — the path-matching logic in particular.
- **Rate limiting.** Every named policy, the partition logic, the global limiter, and the forwarded-headers chain the partitions depend on.
- **PDF import.** Renderer allocation, QR decode, concurrency, request limits.
- **Git history for secrets.** 2,622 text blobs, 672 commits, plus unreachable and dangling objects (see caveat below).
- **CI workflow.** Action pinning, tool download integrity, gitleaks configuration — the last validated with positive, negative, false-positive and regression controls.
- **Deploy tooling.** Compose file, Caddyfile, nginx config, backup and restore scripts — as *text*; see the unverifiable-locally list below.

---

## Not audited at all

Each of these is a real gap, not a formality. I am naming what an attacker would look at, so the absence is legible.

| Area | What was not examined | Why it matters |
|---|---|---|
| **Device register / challenge / verify handlers** | I audited *whether* signature verification runs (FF-04) but not the enrolment protocol itself | If a device can be registered against another user's account, or a challenge replayed, FF-04's fix protects a broken primitive. **This is the most consequential gap in the list**, because the rest of the device-binding story rests on it |
| **Company invitation → member → gift / recall flows** | Tenancy isolation on the multi-user company features | The brief's insider case explicitly includes *"a company member wanting more than granted"*. I verified cross-tenant purchase isolation via `LegalEntityId`, but not the invitation and gifting graph. An IDOR here is a High and I did not look |
| **Referral system** | Self-referral, referral loops, reward double-claim | Direct money-out path if rewards are financial. Unexamined |
| **Admin SPA internals** | `zustand persist` token storage (localStorage vs memory), `dangerouslySetInnerHTML` usage, which `VITE_*` values end up in the shipped bundle | Token in localStorage turns any admin-origin XSS into full admin takeover. I hardened the SPA's *edge* (FF-08, FF-17) and its *exports* (FF-13), not its internals |
| **Mobile app internals** | secure-store usage consistency, `EXPO_PUBLIC_*` secrets in the bundle, `fuelflow://payment-result` deep-link trust, biometric gate, certificate pinning, `__DEV__` branches surviving into release | `FRAUD_ANALYSIS.md:188` raises a mobile signing-key concern that remains **UNVERIFIED**. The deep-link handler is the one I would look at first: if it is trusted as a payment outcome rather than a navigation hint, that is a money finding |
| **Refund 24-hour timeout job** | `RefundStatusSyncService` reconciliation logic | The refund *command* is well guarded (verified). The background reconciliation that runs every minute is not audited |
| **Effective merged production config** | The GO criterion asks for env + `appsettings.Production.json` evaluated as one unit | Could not be done — the production `.env` does not exist in the repository. `Program.cs:167-255` now enforces the critical properties at startup, which substitutes a runtime gate for a static review, but it is not the same thing. Carried as checklist item #7 |

---

## Audited under a constraint that limits the conclusion

**Secrets in git history — pre-rewrite objects may be unrecoverable.** `.git/refs/original` exists, which is evidence of a past `filter-branch`-style rewrite. It is an empty directory. I swept reachable objects (`git rev-list --objects --all`) *and* unreachable ones (`git fsck --unreachable --dangling`, 10 blobs, 2 flagged, both benign — Azure CDKTF env-var *names* and Key Vault secret-*name* constants). But objects orphaned by that rewrite and since garbage-collected are **gone and unauditable**. If a live secret existed before the rewrite and was collected, no local analysis can find it. The only mitigation is rotation, which is why FF-05 asks for written confirmation rather than accepting a clean scan as proof.

**`SUSPECTED (live)` on FF-05 cannot be resolved within the ROE.** Confirming the Monobank token is live means calling Monobank's API — forbidden. Confirming it is dead means trusting an attestation. It stays SUSPECTED by construction, not by lack of effort.

**Dependency vulnerabilities are triaged on incomplete data.** `npm audit` and `nuget audit` were both unreachable (restricted egress). FF-33 (`SSH.NET` GHSA-q939-rpr3-3284) was found in build warnings, not in an advisory sweep. **There may be other vulnerable dependencies I did not see.** Running both audits is on the week-one watchlist.

**Repository visibility is undetermined.** I could not establish whether this repository is or ever was public. This materially changes FF-05's severity — private-and-rotated is a hygiene issue; ever-public is an incident — and it is checklist item #4 for that reason.

**gitleaks was run in git mode only.** A working-tree scan (`--no-git`) timed out and was not completed. Git mode is what CI runs, so the CI gate is validated; **uncommitted or gitignored files in the working tree were not scanned for secrets.**

**Static analysis only, throughout.** No deployed host, database, Monobank endpoint or Twilio account was contacted, per the ROE. Every "FIXED" claim means *the code now contains the guard and the test suite passes*, not *the guard was observed stopping a live attack*. Concurrency findings in particular (advisory locks, conditional updates, the refund unique-index race) were verified by reading the SQL semantics, not by running a race.

**No secret value appears anywhere in this report.** Locations and classes only. Where a value had to be identified — the 23 history credentials, the `Jwt:Secret` template comparison — it was done by hash and by statistical profile (length, distinct-character count, maximum repeat, Shannon entropy), never by reproducing the string.

---

## Could not be verified locally — deferred to the checklist

Environment limitations, not omissions. Each has a corresponding checklist item.

| Item | Blocker | Checklist |
|---|---|---|
| Caddyfile syntax validity | No `caddy` binary available | #10 |
| Redis container reaching `healthy` after the argv change | Docker daemon unavailable (`npipe:////./pipe/dockerDesktopLinuxEngine`) | #11 |
| Whole-stack `docker compose up` behaviour | Same | #6, #11 |
| Admin dashboard loading with no CSP violation | Requires a browser against the deployed edge. The 0-inline-scripts finding is from the built bundle, which makes it likely, not certain | #12 |
| A >1 MB voucher PDF succeeding through the admin origin | Requires the deployed nginx | #13 |
| Backup restore | Requires a real database and the encryption keypair | #3 (blocker) |

---

## Remediation was verified to this extent, and no further

| Check | Result |
|---|---|
| `dotnet build FuelFlow.slnx` | 0 errors, 3 warnings — all in `FuelFlow.IntegrationTests` (SSH.NET CVE ×2, obsolete `PostgreSqlBuilder()`) |
| `dotnet test FuelFlow.UnitTests` | Failed 0, **Passed 340**, Skipped 0 |
| `npx vitest run` (admin) | **14 passed**, 1 file |
| `npm run build` (admin) | Succeeds — 1812 modules, 561 kB; rebuilt `dist/index.html` has **0 inline scripts** |
| `gitleaks detect`, full history, new config | 672 commits, **no leaks found** |
| gitleaks synthetic positive control | **4/4** caught by the intended rule IDs |
| gitleaks negative control | **5/5** placeholders correctly ignored |
| gitleaks false-positive control | `builder.cs` → 0 findings |

**The integration test suite was not run** — it requires Testcontainers and a Docker daemon. So the 340 passing tests are unit tests only. Anything that only breaks when a real Postgres is attached would not have been caught by this verification.

**Verification predates the merge.** Every check above was run against the working tree before the repository owner merged the remediation as PR #322 (`bac99db`, 17 commits). The merge carried that same content, so the results still describe `main` — but they were **not re-run after the merge**, and no post-merge CI result was observed. Re-running the build, the unit tests, the admin build and `gitleaks detect` on `main` is the first thing to do before working through the deploy checklist.
