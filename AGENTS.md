# Always

- NEVER merge pull requests and NEVER push directly to `main`. Open the branch + PR, wait for CI to be green, then stop and leave the merge decision to the human. (Policy set 2026-08-25 after a session of AI self-merges; enforced locally by `.git/hooks/pre-push`.)
- Before pushing, ALWAYS verify the build compiles: run `npx tsc --noEmit` (or equivalent) in the relevant project directory (admin, mobile, backend).
- Before pushing, ALWAYS rebase onto `origin/main` first: `git fetch origin && git rebase origin/main`
- **EF Core model changes MUST include a migration**: run `dotnet ef migrations add <Name> --project src/FuelFlow.API` and commit the generated file *before* merging. Auto-deploy runs `Migrate()` on startup; missing migration = crash.

# Known accepted trade-offs

Deliberate design decisions, not bugs — do not "fix" them without a discussion:

| Item | Reason |
|---|---|
| Natural keys → surrogate UUIDs (stations, fuel_types, fuel_packages) | Massive refactor cascading across 10+ tables |
| DB-level cascading deletes on existing relationships | Current defaults are safe; changing risks breaking queries |
| Thin query handler ceremony | Style choice; 30+ handlers would churn with zero runtime benefit |
| Price precision inconsistency (`int` vs `decimal`) | Needs business decision on fractional UAH support |
| Merge outbox tables (`outbox_events` vs `provider_event_outbox`) | Transactional vs immutable audit — different concerns |
| `000000` DevBypass OTP | Intentional during the testing period to avoid SMS costs; `Auth:DevBypass` is `false` in production. Revisit when flipping it off |
