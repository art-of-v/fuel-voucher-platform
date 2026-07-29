# Always

- Before pushing, ALWAYS verify the build compiles: run `npx tsc --noEmit` (or equivalent) in the relevant project directory (admin, mobile, backend).
- Before pushing to an existing (non-new) branch, ALWAYS rebase onto `origin/main` first: `git fetch origin && git rebase origin/main`
