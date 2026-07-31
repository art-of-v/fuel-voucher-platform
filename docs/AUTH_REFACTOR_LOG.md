# Auth Refactor Log (July 2026)

A hardening pass over the entire auth stack (backend + mobile). This log captures every change made, the rationale, and what remains intentionally as-is.

## Intentional "test-mode" decisions (NOT changed)

These were deliberate cost-saving measures for a testing phase and are kept working:

- **Twilio is off** — no money for real SMS. `FakeSmsService` logs the code instead.
- **Verification code is `000000`** — hardcoded while test mode is active.
- **Render runs the Development profile** — so the dev bypass stays active in the hosted environment.

What changed is that these are now **explicit and controlled by one flag** instead of accidentally coupled to the environment profile.

## 1. Explicit `Auth:DevBypass` flag (new)

Previously the `000000` code and fake SMS were tied to `IsDevelopment()`, so the bypass was silently coupled to `ASPNETCORE_ENVIRONMENT`. Switching Render to Production would have made codes **random but SMS still fake** — a total login lockout.

**Files changed:**
- `backend/src/FuelFlow.API/SharedKernel/Options/AuthOptions.cs` — **new**, `AuthOptions { bool DevBypass }`.
- `backend/src/FuelFlow.API/Extensions/ServiceSetup.cs` — registers `AuthOptions`; `AddSmsService` now selects the SMS implementation from `Auth:DevBypass` (FakeSmsService) vs. real Twilio config. Removed the dead `AddFakeSmsService` that always registered the fake.
- `backend/src/FuelFlow.API/Features/Auth/SendCode/SendCodeCommand.cs` — `SendCodeCommandHandler` now takes `IOptions<AuthOptions>` and uses `DevBypass ? "000000" : GenerateCode()` instead of `IHostEnvironment.IsDevelopment()`.
- `backend/src/FuelFlow.API/appsettings.Development.json` — `"Auth": { "DevBypass": true }`.
- `backend/src/FuelFlow.API/appsettings.Production.json` — `"Auth": { "DevBypass": false }`.

**Net effect:** test mode follows the `Auth:DevBypass` flag, not the environment profile. When you're ready for real SMS, set `Auth:DevBypass=false` and real Twilio credentials — the app then issues random codes via Twilio. If Twilio isn't configured and bypass is off, the app still falls back to fake SMS with a warning (deliberate: avoids hard failure mid-testing).

## 2. Persist refresh token issued by device-login verify (bug fix)

**Bug:** `VerifyChallengeCommandHandler` returned a fresh `RefreshToken` but never saved it to the DB. `RefreshTokenCommandHandler` looks tokens up in `RefreshTokens`, so the device-verify refresh token could never be used — mobile would be force-logged-out once the access token expired.

**File changed:**
- `backend/src/FuelFlow.API/Features/Auth/VerifyChallenge/VerifyChallengeCommandHandler.cs` — now creates and persists a `RefreshToken` row (with `ExpiresAtUtc` from `JwtOptions.RefreshTokenExpirationDays`) before returning the response.

## 3. JWT signing key hardening

**Problem:** `appsettings.Production.json` contained a literal placeholder `CHANGE_THIS_TO_A_SECURE_RANDOM_KEY...`, and `AuthSetup` fell back to a hardcoded `"test-secret-key..."` whenever the secret was missing. Either value is publicly known, so anyone could forge a valid Admin JWT.

**File changed:**
- `backend/src/FuelFlow.API/Extensions/AuthSetup.cs`:
  - Requires a real `Jwt:Secret` (min 32 chars) in non-development environments; fails fast with a clear message instead of silently using a known key.
  - In Development/`Testing` it still allows the dev secret so local/test flows keep working.
  - The resolved secret is pushed back into `JwtOptions` so token *signing* (`JwtTokenService`) and *validation* use the same key.
- `backend/src/FuelFlow.API/Program.cs` — `AddJwtAuth` now receives the environment.

**Action required before real production:** set `Jwt__Secret` (a random key, >= 32 chars) as an environment variable in Render.

## 4. Hangfire dashboard authorization

**Problem:** `UseHangfireDashboard()` was unauthenticated — anyone could view/trigger background jobs.

**Files changed:**
- `backend/src/FuelFlow.API/Middleware/HangfireDashboardAuthorizationFilter.cs` — **new**, `IDashboardAuthorizationFilter` that allows requests when the caller is authenticated as `Admin`, or when the dev bypass is active.
- `backend/src/FuelFlow.API/Program.cs` — dashboard wired with the filter and `IgnoreAntiforgeryToken`; bypass is driven by `Auth:DevBypass`.

## 5. Gate the `verify-raw` diagnostic endpoint

**Problem:** `POST /api/auth/device/verify-raw` was public, unthrottled, and leaked key fingerprints/crypto diagnostics to anyone.

**File changed:**
- `backend/src/FuelFlow.API/Features/Auth/DeviceAuthController.cs` — `VerifyRaw` returns `404` unless `Auth:DevBypass` is on.

## 6. Remove `[ResponseCache]` from admin endpoints

**Problem:** `[ResponseCache(Duration = 300)]` on admin controllers turned authenticated admin responses into public cacheable responses — a cross-user data-leak vector under `AddResponseCaching()`.

**Files changed** (removed the attribute):
- `backend/src/FuelFlow.API/Features/Stations/AdminStationController.cs`
- `backend/src/FuelFlow.API/Features/Stations/AdminPackageController.cs`
- `backend/src/FuelFlow.API/Features/Stations/AdminFuelTypeController.cs`

Public station/package endpoints keep their caching; only admin-scoped endpoints were affected.

## 7. Stop recreating the biometric keypair on every login

**Problem:** `setupDeviceSecurity` deleted and recreated the keypair on every login, producing a new public key each time. This was the root cause of the original registration bug, and it re-prompts for biometrics repeatedly.

**File changed:**
- `mobile/src/core/api/securityService.ts` — creates keys only when absent; caches the public key in `SecureStore` (`device_public_key`) and reuses it on later logins. If keys exist but the cached public key is gone (rare), it recreates. `revokeSecurity` now also clears the cached key.

## 8. Session revocation (delete/deactivate user or device → real lockout)

**Gap (user-proven):** deleting a device row, refresh tokens, or the user row did NOT lock anyone out of an already-open (or next) session. JWT is stateless, `VerifyChallengeCommandHandler` issued tokens even for a missing user row, the mobile hook cached `/user/me` with a 5-min stale time and no foreground refetch, and `User` had no `IsActive`/`TokenVersion`.

**Design (agreed):** short tokens + rotating refresh + per-request revalidation. The token proves identity; existence/active/version is re-checked against the DB on every authenticated request. One extra indexed SELECT per request is the accepted tradeoff for revocation.

**Files changed:**
- `backend/src/FuelFlow.API/SharedKernel/Domain/User.cs` + `Features/Auth/Configurations/UserConfiguration.cs` — added `IsActive` (default true) and `TokenVersion` (int, default 1).
- `backend/src/FuelFlow.API/SharedKernel/Services/JwtTokenService.cs` + `Abstractions/IJwtTokenService.cs` — `GenerateAccessToken` now takes `int tokenVersion = 1` and emits a signed `token_version` claim.
- `backend/src/FuelFlow.API/Middleware/SessionValidationMiddleware.cs` — **new**; on every authenticated request: parse `sub`, 401 if the user row is missing/inactive, 401 if the `token_version` claim differs from the DB.
- `backend/src/FuelFlow.API/Extensions/PipelineSetup.cs` — middleware registered after `UseAuthentication()`, before `UseAuthorization()`.
- `backend/src/FuelFlow.API/Features/Auth/VerifyChallenge/VerifyChallengeCommandHandler.cs` — loads the user with `Include(u => u.Role)` and rejects (`IsValid=false`, "User not found or inactive") when missing/inactive.
- `backend/src/FuelFlow.API/Features/Auth/Verify/VerifyCodeCommand.cs` — throws `UnauthorizedAccessException("Account is deactivated")` for inactive users (new users still auto-created).
- `backend/src/FuelFlow.API/Features/Auth/Refresh/RefreshTokenCommand.cs` — throws `UnauthorizedAccessException("Account is deactivated")` for inactive users; new access tokens carry the user's current `TokenVersion`.
- `backend/src/FuelFlow.API/Features/Auth/AuthController.cs` — `/user/me` returns 401 (not 404) when the user is missing/inactive.
- `mobile/src/features/auth/hooks/useAuth.ts` — `staleTime: 0`, `refetchOnMount: 'always'`, and an `AppState` foreground listener that invalidates `/user/me` on resume. Combined with `AuthSync` (`_layout.tsx`), a 401 → `null` → `logout()`.
- `backend/src/FuelFlow.API/Migrations/20260731103209_AddUserIsActiveAndTokenVersion.cs` — adds `is_active` (default true) and `token_version` (default 1) to `users`.

**Effect:** deactivating a user (`IsActive=false`), bumping `TokenVersion`, or deleting the user/device/refresh rows now revokes live sessions within one request (or on next app foreground), and the refresh path also refuses deactivated users.

**Security property:** `token_version` is signed into the JWT, so it cannot be forged; bumping it invalidates all outstanding access + refresh tokens for that user.

## 9. Remove production-path diagnostic call in mobile login

**Problem:** `useLogin.ts` called `verify-raw` on every production login purely for diagnostics.

**File changed:**
- `mobile/src/features/auth/hooks/useLogin.ts` — the `verify-raw` call is now guarded to `__DEV__` builds only; removed the `diagSummary` production branch.

## 10. Soft-delete users (self-service + admin)

**Requirement (user):** both the user themselves and an admin can delete a user. The delete is **soft** — the row stays in the DB and the deleted user isn't told anything special; they just get locked out. Every delete marks the user deleted, sets `IsActive=false`, and bumps `TokenVersion`.

**Decision (user, clarified):** a deleted phone number can **re-register** — verifying a new code creates a brand-new user row (old data stays hidden). To allow this, the `users.phone_number` unique index is now a **filtered index** (`WHERE is_deleted = false`).

**Files changed:**
- `backend/src/FuelFlow.API/SharedKernel/Domain/User.cs` + `Features/Auth/Configurations/UserConfiguration.cs` — added `IsDeleted` (default false); `phone_number` unique index now filtered on `is_deleted = false`.
- `backend/src/FuelFlow.API/Features/Auth/DeleteUser/DeleteUserCommandHandler.cs` — **new**; sets `IsDeleted=true`, `IsActive=false`, `TokenVersion++`, revokes all active devices and un-revoked refresh tokens, then saves. Returns `Success=false, Error="User not found"` for missing/already-deleted users.
- `backend/src/FuelFlow.API/Features/Users/UserController.cs` — **`DELETE /api/users/me`** (self-service, `[Authorize]`).
- `backend/src/FuelFlow.API/Features/Auth/AdminUser/AdminUserController.cs` — **`DELETE /api/admin/users/{id}`** (`[Authorize(Roles="Admin")]`).
- `backend/src/FuelFlow.API/Features/Auth/AdminUser/GetAdminUsers/GetAdminUsersQueryHandler.cs` — `AdminUserDto` now includes `IsDeleted` so admins can see the state.
- `backend/src/FuelFlow.API/Features/Auth/Verify/VerifyCodeCommand.cs` — user lookup now excludes deleted rows (`&& !u.IsDeleted`), so a deleted phone is treated as a fresh registration and a new row is created.
- `backend/src/FuelFlow.API/Migrations/20260731133808_AddUserSoftDelete.cs` — adds `is_deleted` and converts the phone index to filtered.
- `admin/src/pages/admin.tsx` + `admin/src/lib/i18n.ts` — Users table shows a Status badge (Active/Deleted) and a Delete button with confirm.
- `mobile/app/profile.tsx` + `mobile/src/core/i18n/translations/{en,uk,de,es}.ts` — "DELETE ACCOUNT" button with an `Alert` confirm; on success it signs out locally and redirects to `/`.

**Effect:** deleting a user instantly locks them out (token bumped + inactive + devices/refresh revoked). Existing sessions die on next request via `SessionValidationMiddleware`. The user can re-register with the same phone to start fresh.

**Security property:** the filtered index is the only schema change; everything else reuses the existing revocation machinery from §8 — no new session/kill path.

## Verification

- `dotnet build -c Release src/FuelFlow.API` — **Build succeeded** (0 errors; only pre-existing NU1603 warnings).
- `npx tsc --noEmit` (mobile) — **clean**.
- Pre-existing failures in `FuelFlow.UnitTests` are unrelated to this refactor (stale references to removed `Order`/handler signatures).

## Remaining (deferred, intentionally not changed)

- **Refresh tokens stored in plaintext** and no reuse/family revocation — flagged, not changed here.
- **Per-IP-only rate limiting** trusting `X-Forwarded-For` — flagged, not changed here.
- **OTP via `Random.Shared`** (not CSPRNG) and plaintext verification codes in DB.
- **5-minute signature timestamp tolerance**.
- **`Testing` config flag** disabling issuer/audience validation.
- **HSTS/security headers** not configured in-app (Render provides TLS).
- **`MigrateDatabaseOnStartup`** auto-runs migrations.
- **Dev-profile 200-minute access token**.
