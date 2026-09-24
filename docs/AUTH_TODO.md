# Auth — open items (from the prod manual test run)

Companion to `AUTH_SCENARIO_TEST_PLAN.md`. Tracks what is still **open** from the live
passwordless-auth test run against production (palne.shop, 2026-09-19 → 2026-09-22).

All bugs found in that run and both planned features are **fixed / shipped and merged to `main`** —
staff-login audit (#602), hardcoded staff roles (#606), 429 handled as its own error (#603),
reinstall-clears-session (#601), demotion revocation (`95ba93f`); verified email edit (#607, and
self-service #610) and the role & lifecycle authz matrix (#606). The two-device **live** confirmation
of the promote/demote scenarios below is now **done (2026-09-24)** — the full run is verified.

## ✅ Live confirmations — done (2026-09-24)

- **Scenario I — promotion must not upgrade a live session.** ✅ **Confirmed 2026-09-24.** Promoted a
  User→Manager via the panel; the existing session's refresh token kept its old `User` authorization
  and was not revoked, and only a fresh login picked up the staff role (the `RoleNameAtIssue` snapshot,
  covered by `AdminRoleDemotionIntegrationTests`).
- **Scenario J — demotion must revoke all sessions.** ✅ **Confirmed 2026-09-24** post-fix. A
  ProductOwner demoted a Manager→User via the panel; every refresh token was revoked, `token_version`
  bumped, and the demoted device was kicked to the login screen on its next call. Fixed in `95ba93f`
  and pinned by `AdminRoleDemotionIntegrationTests`.
