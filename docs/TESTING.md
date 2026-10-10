# Testing

We keep the product releasable by covering behavior with automated tests. This document states the policy and where tests live.

## Policy

Every meaningful behavior ships with the tests that fit it:

- **Unit tests** for logic — pricing, validation, state transitions, mappers with real branching, and other pure/near-pure code.
- **Integration tests** for wiring — command/query handlers, API endpoints, persistence, and provider adapters (HTTP, DB, outbox).
- **End-to-end or other kinds** where a change warrants it (e.g. a full checkout → voucher flow, an auth round-trip).

Add tests where they buy confidence, not everywhere. Skip self-evident code where a test would only restate the implementation:

- plain getters/setters and constructors,
- DTO shaping / response projection with no logic,
- framework glue and generated files (migrations, scaffolding),
- trivial pass-through wrappers.

When in doubt, test it. Money paths, auth, and access control are never "unnecessary" — always cover them.

## Bug fixes

A bug fix should include a **regression test** that fails before the fix and passes after. If a fix is only verifiable manually (e.g. on-device mobile behavior), say so in the PR and note what was checked.

## Where tests live and how to run them

**Backend (.NET, xUnit)** — `backend/tests/`:

- `FuelFlow.UnitTests` — domain/application unit tests
- `FuelFlow.IntegrationTests` — API + persistence integration tests
- `FuelFlow.JobsWorker.UnitTests` — background-job tests
- `FuelFlow.Providers.UnitTests` — external-provider adapter tests

```bash
dotnet test backend
```

**Admin (Vitest)** — `admin/`:

```bash
npm --prefix admin test        # vitest run
```

**Mobile (Jest / jest-expo)** — `mobile/`:

```bash
npm --prefix mobile test       # jest
npm --prefix mobile run typecheck
```

**Website** — `website/` currently has lint only (`npm --prefix website run lint`); add tests here when it grows behavior worth covering.

## CI

These suites run as required checks on every PR to `main` (see the `CI required` gate). A red suite blocks merge — fix the test or the code, don't skip the gate.

### A red integration job is usually the registry, not the change

The integration suite pulls `postgres:16-alpine` from Docker Hub. GitHub-hosted runners share egress IPs, so this repo competes with every other job on the runner pool for the anonymous pull quota, and loses intermittently. Two runs in three can pass with identical code.

The failure is easy to misread, because the pull happens inside Testcontainers: the fixture times out, and the whole assembly is reported as failed — including tests that have nothing to do with the change. **A failure mentioning `registry-1.docker.io`, `DockerApiException`, or "context deadline exceeded" from `TestDatabaseFixture` is infrastructure. Re-run it; do not investigate it as a code fault.**

The image is pulled in its own step before any test runs, so that failure is now reported as `Pull the Testcontainers Postgres image` and cannot be confused with a genuine test failure. If that step is the thing that keeps failing, the fixes are:

- **Authenticate** — add a `DOCKERHUB_TOKEN` secret and a `docker/login-action` step. Raises the quota from anonymous to an account quota. Smallest change that removes the problem outright.
- **Mirror** — point the fixture at an image the repo already trusts, so the quota is not shared with the world.

Until one of those lands, the retries handle the transient case and the split step handles the diagnosis.
