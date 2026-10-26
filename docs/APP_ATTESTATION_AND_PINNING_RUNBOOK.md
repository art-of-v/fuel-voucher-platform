# App attestation and certificate pinning — mobile runbook

This is the app-side operator guide for issue #690. The threat model, protocol contract and
backend rollout are in the planning repo:
`docs/APP_ATTESTATION_AND_PINNING.md`.

The two controls:

- **App attestation** — Apple App Attest on iOS, Play Integrity on Android. The client proves the
  request came from the real app on a genuine device. The checked-in production config runs it
  report-only; enforcement is a later per-platform step.
- **Certificate pinning** — the client only trusts the pinned `api.palne.shop` chain. Pinning
  initialises at app start and fails open (with a loud log) if the native module is unavailable.

## Files

| File | Purpose |
|---|---|
| `src/core/security/attestationProtocol.ts` | Canonical request hash, proof input, protected-path matching |
| `src/core/security/optionalNativeModule.ts` | Resolves a native module at call time, so a bundle can run on a binary that predates it |
| `src/core/security/appAttestation.ts` | Apple/Google proof generation, key storage and recovery |
| `src/core/security/sslPinning.ts` | Pin set, initialisation, fail-open logging |
| `src/core/api/apiConfig.ts` | `BASE_URL` and platform constants |
| `src/core/api/apiClient.ts` | Headers, pinning init, attestation headers, 401 handling |
| `src/core/api/securityService.ts` | Clears session material on logout; the device-bound Apple key is retained |
| `app.config.js` | App Attest entitlement, environment, Google project number |
| `eas.json` | `APP_ATTEST_ENVIRONMENT` per build profile |

The pins and protected paths are compiled into the JS bundle. A signed OTA can update them only on
builds that can still reach the update host through the current pin.

## Prerequisites

### iOS

- A real iPhone (Simulator cannot do App Attest).
- The bundle id `com.fuelflowmarketplace.www` and team `TB929MCW58`.
- The App Attest capability in the Apple Developer portal and the provisioning profile.
- `APP_ATTEST_ENVIRONMENT=production` for an App Store/TestFlight build;
  `development` only for an Xcode/dev build.
- A build whose entitlement matches the profile. A mismatch fails at runtime, not at build time.

### Android

- The package must be installed from Google Play and the Play Integrity API must be enabled for
  `com.artem.vashchuk.mobileappnative`.
- A Google Cloud project **number** in `EXPO_PUBLIC_GOOGLE_CLOUD_PROJECT_NUMBER` (or
  `GOOGLE_CLOUD_PROJECT_NUMBER`) at build time.
- A real device with Google Play services. A sideloaded preview APK will normally produce
  `UNRECOGNIZED_VERSION`; that is expected until the package is in Play.

The backend needs the service-account JSON from the same Google Cloud project as
`Attestation__GoogleCredentialsJson`; it does not use the project number. Never put the JSON in
`app.config.js`, `eas.json`, `.env.example`, or git.

## Build

```powershell
cd mobile
npm install
npx tsc --noEmit
npx eslint src/core/api/apiClient.ts src/core/api/apiConfig.ts `
  src/core/security/attestationProtocol.ts src/core/security/appAttestation.ts `
  src/core/security/optionalNativeModule.ts src/core/security/sslPinning.ts `
  src/core/utils/versionCheck.ts
npx jest src/core/api/apiClient.test.ts `
  src/core/security/attestationProtocol.test.ts src/core/security/appAttestation.noNativeModule.test.ts `
  src/core/security/attestationProtocol.noNativeModule.test.ts src/core/security/sslPinning.noNativeModule.test.ts `
  src/core/utils/versionCheck.test.ts --runInBand
```

These are the local checks. The full suite runs in CI. Native modules are mocked in Jest, so none
of the above proves App Attest or pinning works on a device.

### EAS

```powershell
# iOS TestFlight/internal
eas build --profile production --platform ios
# Android internal
eas build --profile production --platform android
```

The production profile already sets `APP_ATTEST_ENVIRONMENT=production`. The preview profile is a
**simulator** iOS build (`ios.simulator: true`), so it cannot exercise App Attest; use it only for UI
work. The preview Android build is an APK and will not have Play Integrity until it is installed
through Play.

Adding `@expo/app-integrity` and `react-native-ssl-public-key-pinning` changes the native
fingerprint, so a build made before them asks for a runtime version no current publish produces.
It does **not** mean they cannot receive an OTA: the security layer resolves both modules at call
time, inside a try/catch, so today's bundle runs on those binaries - with no attestation and no
pinning, exactly as they behaved before. What needs a new native build is the *capability*, not
the code.

## Production rollout: report-only first

The checked-in production config is `ATTESTATION_ENABLED=true`,
`ATTESTATION_MODE=ReportOnly`, `ATTESTATION_ACKNOWLEDGE_DISABLED_IN_PRODUCTION=true`. ReportOnly
verifies and logs a proof that is present but never blocks a request, so the older fleet keeps
working while the new build is measured.

**Where it actually stands (2026-10-26 11:58 UTC):** production is in report-only and **measuring**.
The switch was off until that moment - the droplet's env file said `ATTESTATION_ENABLED=false` while
the checked-in template said `true`, and nothing compared the two, so three days passed with the
middleware returning before it looked at a request. The first verdict is in Loki:

```
Attestation Missing for "POST" "/api/auth/verify" from device "-" os null version null; report-only, request continues
```

A git deploy does not touch the droplet's env file, so the live value is the droplet's value. On the
droplet:

```bash
cd /root/FuelFlow/deploy
grep -E '^ATTESTATION_' .env.production
# Set ATTESTATION_ENABLED=true, ATTESTATION_MODE=ReportOnly and
# ATTESTATION_ACKNOWLEDGE_DISABLED_IN_PRODUCTION=true in .env.production if they differ.
./restart.sh --services fuelflow-backend
```

Before restarting, confirm `OTP_PEPPER` is set in the same `.env.production`: the current API refuses
to start in Production without it. The restart also applies the pending `attestation_keys` migration;
take the normal pre-deploy backup and let the deploy job's migration dry run pass.

**Use `deploy/restart.sh`, not a hand-written `docker compose up`.** It runs `preflight.sh`, pins the
image tags to the images that are running right now and restarts with
`--env-file .env.production`. A hand-written restart gets two things wrong, and both have bitten this
box:

- **No `BACKEND_IMAGE_TAG` means the mutable `:main` tag.**
  `deploy/docker-compose.prod.yml:113` resolves `${BACKEND_IMAGE_TAG:-${IMAGE_TAG:-main}}`, so
  `docker compose -f docker-compose.prod.yml up -d fuelflow-backend` recreates the container on
  whatever `:main` happens to be - not the release that was running. On 2026-10-26 a restart written
  that way pulled `:main` and replaced the pinned release container with it. The app assembly in
  `:main` happened to be byte-identical to the release (`FuelFlow.API.dll`, sha256 `612a5a03…`), so no
  code changed - but the container was left on a moving tag until it was put back on the SHA and the
  local `:main` tag was removed.
- **No `--env-file .env.production`** means the compose file reads its defaults instead of the
  tier's file. The file was renamed from `.env` to `.env.production` in commit `8b5abb19`; a command
  that still names `.env` reads nothing at all.

Verify the state and the logs:

```bash
docker compose --env-file .env.production -f docker-compose.prod.yml ps fuelflow-backend
docker compose --env-file .env.production -f docker-compose.prod.yml logs --since 15m fuelflow-backend \
  | grep -E 'Attestation (Valid|Missing|Invalid)'
```

Expected: `Missing` for the 1.1.0 fleet, no attestation-caused 401/503, and `Valid` only after a
new native build is installed. Do not set `ATTESTATION_MODE=Enforce` yet. To roll the measurement
back, set `ATTESTATION_ENABLED=false` (keep the acknowledgement true) and restart; no client
release is involved.

**What the measurement can and cannot see.** The protected set is
`backend/src/FuelFlow.API/SharedKernel/Options/AttestationOptions.cs:126`: `/api/auth/send-code`,
`/api/auth/verify`, the purchase routes and the voucher mark-used/restore routes. Ordinary app
traffic - `/api/brands`, `/api/app-version`, `/api/updates/manifest`, `/api/auth/user/me` - is **not**
protected, so a fleet that never signs in or buys produces no lines at all. The count this rollout
needs is a count of **sign-ins and purchases**, and the installed fleet sends no proof on any of them.

## Android Play and Google Cloud setup

This is the one-time bootstrap that must exist before an Android proof can be `Valid`.

1. **Put the package in Play.** In Play Console, create or verify the app for
   `com.artem.vashchuk.mobileappnative`. Build the production AAB with
   `eas build --profile production --platform android` and upload it to at least the internal
   testing track (`eas submit --profile production --platform android` uses the internal track in
   `eas.json`). Play Integrity will not return `PLAY_RECOGNIZED` for an APK installed by `adb`, a
   preview APK, or an internal-app-sharing build. Install the build from the Play test track on a
   Play-certified device.
2. **Create and link the Google Cloud project.** Create or choose a project in Google Cloud
   Console, enable **Play Integrity API**, and copy its **project number** (not the project id).
   In Play Console open **Release > App integrity > Play Integrity API** (the exact menu label
   varies by console version) and link that Cloud project to the app. Wait for the link to
   propagate.
3. **Create the backend credential.** In the same linked project, create a service account for the
   API and a JSON key. Minify the JSON to one line and put it only in the server secret store as
   `ATTESTATION_GOOGLE_CREDENTIALS_JSON` / `Attestation__GoogleCredentialsJson` (the service-account
   JSON has escaped `\n` newlines, not literal ones). The backend uses the
   `https://www.googleapis.com/auth/playintegrity` scope. There is no `roles/playintegrity.user`
   role; do not add a made-up binding. If your organization requires an IAM grant, grant only what
   the API/console documents and confirm with a decode. Never commit the key.
4. **Bake the project number into the client.** Set the EAS production environment variable
   `EXPO_PUBLIC_GOOGLE_CLOUD_PROJECT_NUMBER` to the numeric project number and rebuild the native
   app. A missing number sends no Google proof; an OTA is not enough for the native modules. The
   backend checks the package name and, when configured, the signing-certificate allowlist; use
   the Play App Signing SHA-256 if `GoogleAllowedCertificateDigests` is set.
5. **Measure before enforcing.** Install the new build from Play, sign in, and make a protected
   request. With the service-account JSON set, the server should log
   `Attestation Valid ... via google`. If it logs `GOOGLE_TEST_RESPONSE`, the token is a test
   response; do not set `GoogleAllowTestingResponses=true` in production just to pass. Use a build
   installed from a normal Play track. `UNRECOGNIZED_VERSION` means the install is not
   Play-recognized. Only after a real device shows `Valid` may `EnforcePlatforms` include
   `android`; raising `Mode` to `Enforce` also requires the service-account JSON or the API refuses
   to start.

## Smoke test on a real device

1. Start the app against the production API (or a preview API that has report-only enabled).
2. Complete login. Confirm the request succeeds.
3. Watch the logs for:
   - `[SslPinning]` — should not log an error in a production build.
   - Apple: a registered key id is present in SecureStore, and subsequent protected requests carry
     `X-Attestation-Provider: apple`.
   - Android: protected requests carry `X-Attestation-Provider: google`; no proof is sent if the
     project number is absent.
4. On the server, in report-only, look for `Attestation` result logs grouped by OS and app version.
   A healthy build should be `Valid`; `Missing` is an old build or an Android build without a project
   number. A bad current build is a bug, not a reason to enforce.
5. Force a replay (repeat a captured challenge) and confirm `ATTESTATION_CHALLENGE_INVALID`; the
   app should not clear the session for an attestation-coded 401.
6. To test key recovery, mark the key revoked in the database
   (`UPDATE attestation_keys SET revoked_at = now() WHERE device_id = '<id>'`) and make a protected
   request. The client should see `ATTESTATION_KEY_REVOKED`, clear the stored key, register a new
   one, and retry successfully. Logout is not this test: it deliberately leaves the device-bound
   key in place.

## Troubleshooting

| Symptom | Likely cause | Action |
|---|---|---|
| `AppIntegrity is not supported` on iOS | Simulator, old iOS, or missing entitlement | Use a real device; check the profile capability. Do not enforce iOS until the build is fixed. |
| `APPLE_KEY_ENVIRONMENT_INVALID` | A Development entitlement/key used against Production | Rebuild with `APP_ATTEST_ENVIRONMENT=production` and a production profile. |
| `ATTESTATION_KEY_UNKNOWN` once, then success | Reinstall/Keychain mismatch; client recovery working | Expected. If it loops, inspect SecureStore writes and device id stability. |
| `ATTESTATION_CHALLENGE_INVALID` | Challenge expired (>120s), reused, wrong device/action, or body/path changed | Check clock, retry once, and verify the exact body is hashed before send. |
| `GOOGLE_NOT_CONFIGURED` | The backend has no service-account credential | Set `Attestation__GoogleCredentialsJson`; do not enable Android enforcement until it is set. A missing client project number sends no proof and surfaces as `ATTESTATION_REQUIRED`, not this code. |
| `GOOGLE_APP_NOT_RECOGNIZED` / `UNRECOGNIZED_VERSION` | Sideloaded APK, wrong package, or not in Play | Install from Play; check package name and Play Console linkage. |
| `GOOGLE_TEST_RESPONSE` | Play marked the response as a test response | Do not set `GoogleAllowTestingResponses=true` in production; install from a normal Play track and re-test. |
| `GOOGLE_HASH_MISMATCH` | Proxy or interceptor changed the body/path after the proof | Compute the hash from the exact bytes sent; check retries that rewrite the body. |
| Pinning error / no network | Certificate changed, pin set wrong, or a proxy | Compare the live SPKI hashes (below); ship a corrected pin; if impossible, wait for the expiry dead-man switch. |
| `native module unavailable` in pinning logs | Expo Go, web, a build without the native module, or an old binary running a newer bundle | Expected in all four; a current production build logging this is broken. |

The server sends `X-Attestation-Error` on attestation refusals. The client uses that header to avoid
treating the 401 as an expired access token: it must not refresh or clear the session for an
attestation error.

## Pin verification and rotation

The pins are in `src/core/security/sslPinning.ts`; the expiry is `2027-09-01`.

```powershell
openssl s_client -servername api.palne.shop -connect api.palne.shop:443 -showcerts
```

For each certificate in the chain (use Git Bash or WSL; do not pipe binary output through a
PowerShell object pipeline):

```bash
openssl x509 -pubkey -noout -in cert.pem \
  | openssl pkey -pubin -outform DER \
  | openssl dgst -sha256 -binary \
  | base64
```

The current pins:

| Pin | Role |
|---|---|
| `s/tdAOmUzd8syaTuqfgGvFcn6DzA5Cmb+Vby1ST+U3Y=` | `CN=YE2` intermediate |
| `sCkq5UWXjg+7mKu9lMhhYF5bGLsy7VI/UNW3tccdR7w=` | `CN=Root YE` |
| `diGVwiVYbubAI3RW4hB9xU8e/CH2GnkuvVFZE8zmgzI=` | `CN=ISRG Root X2` |
| `C5+lpZ7tcVwmwQIMcRtPbsQtWLABXhQzejna0wHFr8M=` | `CN=ISRG Root X1` |

Rotation order:

1. Add the new backup pin first.
2. Ship a build/OTA and wait for the fleet.
3. Change the certificate.
4. Keep the old pins for at least one certificate lifetime.
5. Move `expirationDate` forward in the same change.

Do not pin the leaf: it rotates at renewal. Do not shorten the expiry to force a migration; that
removes the safety net for installs that cannot update.

## Rollback and kill switch

- **Attestation misbehaving**: set `ATTESTATION_MODE=ReportOnly` (or `ATTESTATION_ENABLED=false`)
  in the droplet `.env.production` and restart. The app keeps sending proofs; the server stops
  blocking. This is the fastest lever and does not need a client release.
- **Pinning misbehaving**: ship a corrected native build, or a signed OTA if the current pin still
  permits the update host. If neither is possible, the `expirationDate` stops pinning enforcement.
  There is no server-side pin kill switch.
- **Android**: keep Android out of `EnforcePlatforms` until a Play-installed build passes on a real
  device. An enforcing backend with missing Google configuration fails startup, which is
  intentional.
- **iOS**: keep `MinimumEnforcedAppVersion` at or below the delivered build. Raising it above the
  installed fleet locks out users with no recovery path.

## Evidence to attach to issue #690

- Backend targeted test output (attestation + startup validator).
- Mobile typecheck, targeted Jest output, lint output.
- A real iOS device log showing a successful registration/assertion, or a note that the device
  test is pending.
- A Play-installed Android result, or a note that Android enforcement is blocked on Play/Google
  Cloud setup.
- The report-only log sample from the deployed backend.
- The native build number/fingerprint and the app version that carries the feature.
