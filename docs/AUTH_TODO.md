# Auth — open items (from the prod manual test run)

Companion to `AUTH_SCENARIO_TEST_PLAN.md`. Tracks what is still **open** from the live
passwordless-auth test run against production (palne.shop, 2026-09-19 → 2026-09-22).

All bugs found in that run and both planned features are **fixed / shipped and merged to `main`** —
staff-login audit (#602), hardcoded staff roles (#606), 429 handled as its own error (#603),
reinstall-clears-session (#601), demotion revocation (`95ba93f`); verified email edit (#607, and
self-service #610) and the role & lifecycle authz matrix (#606). The only remaining work is the
two-device **live** confirmation of the promote/demote scenarios below.

## 🧪 Live confirmations still to run

- **Scenario I — promotion must not upgrade a live session.** Promote User→Staff; the existing
  session stays non-staff until re-login. Code is implemented, unit-tested, and covered by
  `AdminRoleDemotionIntegrationTests` (`RoleNameAtIssue` snapshot); only the live two-device check
  is pending. *Needs a second, non-PO account* (e.g. the User account +380970011771).
- **Scenario J — demotion must revoke all sessions.** Demote staff→User; every session for that
  user revokes immediately. Fixed (`95ba93f`) and pinned by `AdminRoleDemotionIntegrationTests`
  (demotion revokes every refresh token + device, bumps `TokenVersion`, old token → 401); a live
  two-device re-confirm is recommended now that it is deployed.
