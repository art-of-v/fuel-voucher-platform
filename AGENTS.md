# Always

- NEVER merge pull requests and NEVER push directly to `main`. Open the branch + PR, wait for CI to be green, then stop and leave the merge decision to the human. (Policy set 2026-08-25 after a session of AI self-merges; enforced locally by `.git/hooks/pre-push`.)
- Before pushing, ALWAYS verify the build compiles: run `npx tsc --noEmit` (or equivalent) in the relevant project directory (admin, mobile, backend).
- Before pushing, ALWAYS rebase onto `origin/main` first: `git fetch origin && git rebase origin/main`
