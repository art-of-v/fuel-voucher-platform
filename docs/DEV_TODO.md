# Dev TODO — issues & refactors found while fixing the demotion bug

Captured during the `fix/demotion-session-revocation` work (2026-09-23, PR #616).
These are **out of scope** for that PR (which stays focused on the auth demotion fix)
and are recorded here so they aren't lost. Legend: 🔴 bug · 🧪 test · 🧹 refactor ·
🔐 security/deps · ✅ done · ⏳ open

---

## 🧪🔴 1. `MetricNameContractTests` fails in a git worktree (the "one failing test")

**Status:** ✅ **fixed in this PR.** Was failing **only** in a git *worktree* checkout
(it passed in a normal clone and in CI, so it never blocked PR #616); `RepositoryRoot()`
now accepts `.git` as a file or a directory, so the full unit suite is green from a
worktree too.

**Symptom:** `EveryFuelFlowMetricReferencedByAlertRules_ExistsAsAnInstrument` (and the
other tests sharing the helper) throw `NullReferenceException`-style failure at
`MetricNameContractTests.RepositoryRoot()`.

**Root cause:** the helper locates the repo by walking parents looking for a `.git`
**directory**:

```csharp
while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
    dir = dir.Parent;
dir.Should().NotBeNull("the tests must run from inside the repository");
```

In a **worktree** (`C:\Projects\ff-demotion-fix`) `.git` is a **file** (a gitdir
pointer), not a directory — so the walk never matches, reaches the drive root, and the
`NotBeNull` assertion fails. In the primary checkout `.git` is a real directory, so it
passes; CI clones normally, so CI is green.

**Fix:** accept `.git` as a file *or* directory (and ideally stop at either):

```csharp
while (dir is not null &&
       !Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
       !File.Exists(Path.Combine(dir.FullName, ".git")))
    dir = dir.Parent;
```

Better still: resolve the root once via `git rev-parse --show-toplevel`, or pin it with
a `[CallerFilePath]` anchor, so the test is independent of checkout layout.

## 🧹🧪 2. Security-critical EF handlers need Testcontainers coverage, not just InMemory

**Status:** ⏳ open (partially addressed for the demotion path in PR #616).

The demotion-revocation bug was a real production defect that the existing
`SetUserRoleCommandHandlerTests` (EF **InMemory** provider) could **not** catch, because
InMemory does not model:
- the global `NoTracking` default → **identity-map conflicts** on re-fetch + `.Update()`;
- **transactions** (it silently ignores them);
- provider-specific SQL / constraint behavior.

PR #616 adds `AdminRoleDemotionIntegrationTests` (Testcontainers → real Postgres) for the
demotion/promotion paths only. **Refactor to consider:** give the other admin mutation
handlers that rely on tracking/transaction semantics (`SetUserBannedCommandHandler`,
`SetUserActiveCommandHandler`, `UpdateUserCommandHandler`, and anything wrapping
`BeginTransaction`) the same real-Postgres coverage, and treat InMemory tests as
logic-only. A shared Testcontainers base fixture would keep this cheap.

## 🔐 3. `Newtonsoft.Json` 11.0.1 — known high-severity vulnerability (NU1903)

**Status:** ✅ **fixed** (branch `chore/test-deps-newtonsoft-vuln`). The build emitted
`NU1903` for `Newtonsoft.Json` 11.0.1 in `FuelFlow.IntegrationTests`, `FuelFlow.UnitTests`,
and `FuelFlow.JobsWorker.UnitTests` (advisory GHSA-5crp-9r3c-p9vr — a StackOverflow/DoS on
deeply nested JSON during deserialization).

**Root cause:** not a test dependency. It comes from **`Hangfire.Core` 1.8.25** (pulled by
`Hangfire.AspNetCore` + `Hangfire.PostgreSql`), which declares `Newtonsoft.Json >= 11.0.1` and
uses it to serialize background-job arguments and state. The three test projects only inherited
it transitively through their `ProjectReference`s to the API / JobsWorker.

**Fix:** pin `Newtonsoft.Json` **13.0.4** directly in **`FuelFlow.API`** and
**`FuelFlow.JobsWorker`** — the two shipped images that actually run Hangfire. NuGet version
unification then lifts the whole closure (including every test project) to 13.0.4, so the
patched serializer resolves everywhere and NU1903 clears without editing the test csprojs.
Remove the pins once Hangfire.Core resolves `Newtonsoft.Json >= 13` itself. (Pinning 13.0.3
first tripped `NU1605`: something already in the graph resolved 13.0.4, so the lower pin was a
package downgrade — hence 13.0.4.)

**CI gate (also done):** added a *Vulnerable dependency scan* step to the required `backend`
job — `dotnet list package --vulnerable --include-transitive` over all six projects. Its exit
code is always 0, so the step greps the output for the finding marker and fails the build on
any hit. This would have caught the advisory on the PR that introduced it.

## 🧹 4. Obsolete `PostgreSqlBuilder()` ctor in `TestDatabaseFixture` (CS0618)

**Status:** ⏳ open. `TestDatabaseFixture.cs:36` uses the obsolete parameterless
`new PostgreSqlBuilder()`. Switch to the image-parameter constructor per the
Testcontainers guidance to silence the warning and stay forward-compatible.

## 🧹 5. Local branch tracked `origin/main` as upstream (foot-gun)

**Status:** ✅ resolved for this branch. The `fix/demotion-session-revocation` local
branch was created tracking `origin/main`, so a bare `git push` would have targeted
`main` (blocked by branch protection + the pre-push hook, but still a trap). Fixed by
pushing with an explicit `git push -u origin fix/demotion-session-revocation`. Worth
checking the branch-creation flow/alias that set the wrong upstream so new branches don't
inherit it.

