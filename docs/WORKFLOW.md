# Working in this repository

How changes get made here, and the two practices that most often cost time when
they are skipped.

> This file is tracked on purpose. The AI agent operating policy (language rules,
> branch and merge policy, EF Core migration rule, the testing plan requirement)
> lives in the per-machine `AGENTS.md`, which is deliberately **not** in git —
> see `.gitignore` and `2c6880a2`. The practices below are engineering policy, so
> they belong with the rest of `docs/`.

## Use a separate worktree, never the primary checkout

The primary working tree at `C:\Projects\FuelFlow` usually carries uncommitted
work in progress, often from a concurrent session. Editing or switching branches
there risks clobbering it — and git will refuse the switch anyway, which is the
symptom:

```
error: Your local changes to the following files would be overwritten by checkout
fatal: 'main' is already used by worktree at '...'
```

Create one worktree per task:

```bash
git worktree add <temp-dir> -b <branch> origin/main
# edit, commit and push from <temp-dir>
git worktree remove <temp-dir>
```

Three rules that follow from this:

- **Branch, do not check out `main`.** `git worktree add <dir> main` occupies
  `main` and then nobody — including you — can switch to it. The `-b <branch>`
  form exists precisely so it does not.
- **Never `git stash` the primary tree** to make room for your own work. That is
  the data loss this avoids.
- **`node_modules` is often a junction** into another checkout, to avoid a second
  install. Remove it with `cmd /c rmdir <junction>` (not a recursive delete, which
  would follow the link) and verify the real directory survived before you touch
  the worktree again.

## Clean up what you created, in the same session

The moment something you made has served its purpose, delete it:

- `git worktree remove <dir>` once its PR is merged
- local branches whose work landed on `main`
- scratch files written outside the repo — commit-message drafts, PR bodies,
  screenshots, throwaway scripts

Never leave a merged branch, an abandoned worktree or a temp artifact behind. A
stale checkout in the primary tree is what forces a human to fight
`git checkout` later.

Two carve-outs:

- Never delete or overwrite uncommitted work that is not yours, and never stash
  the primary tree to make room for your own cleanup. If a leftover cannot be
  removed without touching someone else's work in progress, leave it and say so
  explicitly.
- If something you created is genuinely still useful — a script, a harness —
  commit it rather than leaving it in a temp directory, so it survives.

## Check CSS visually with the harness, not a hand-written page

`admin`'s theming is token-driven and effectively invisible in review: one edit to
`src/index.css` can square every corner or flatten every bevel in the whole app.
Seeing that requires a render, and assembling one by hand means rewriting the
markup every time Vite changes a hashed asset name.

```bash
cd admin
npm run build
npm run preview:theme -- --theme mercury
```

It renders the real shell against the **built** CSS and screenshots it with headless
Chrome. Flags: `--theme --out --width --height --scale --browser --keep`.

One limitation, documented in the script: it opens the page over `file://`, where
Chrome treats a CSS `mask-image` as a broken reference and suppresses the element
entirely. To check a mask, build with `--keep` and serve `dist` over HTTP.

## A broken build in one component no longer blocks the others

Images are built by one job per component (`image-backend`, `image-admin`,
`image-website`), each gating only on its own checks, all in parallel. Deploys
resolve the tag per component with `deploy/resolve-image-tags.sh`: this commit's
SHA for anything that built, `main` — that component's last successful build —
for anything that did not.

What that means in practice:

- **A failed *check* still blocks everything.** Code that does not compile or
  does not pass tests never reaches a deploy.
- **A failed *image build* holds only its own component.** The rest of the
  release ships; the held component keeps running its previous version until a fix
  reaches `main` and deploys in a later run.
- **A held component is announced, never silent**: CI emits a `Partial release`
  workflow warning, and both deploys print the running digests
  (`docker compose … images`) afterwards.
- **If nothing built, the release fails.** It does not degrade into quietly
  redeploying the previous version.

The trade-off is real: a held component is a version you did not intend to ship,
so read the deploy log rather than assuming all three landed. If you need one
component moved independently, `IMAGE_TAG` still sets all three at once and
`BACKEND_IMAGE_TAG` / `ADMIN_IMAGE_TAG` / `WEBSITE_IMAGE_TAG` override
individually — the manual rollback path in docs/DEPLOYMENT.md is unchanged.

Two habits survive the change:

- A red main is not always your change. Check whether the failing component's
  source differs between the last green build and the failing one before going
  looking for your own regression.
- Keep component builds hermetic. Anything that reaches the network at build time
  is a release-path dependency; `next/font/google` fetching woff2 from
  `fonts.gstatic.com` has already taken staging and prod down once.