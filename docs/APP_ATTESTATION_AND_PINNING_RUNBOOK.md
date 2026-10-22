# App attestation and certificate pinning — mobile runbook

This is the app-side operator guide for issue #690. The threat model, protocol contract and
backend rollout are in the planning repo:
`docs/APP_ATTESTATION_AND_PINNING.md`.

The two controls:

- **App attestation** — Apple App Attest on iOS, Play Integrity on Android. The client proves the
  request came from the real app on a genuine device. Server enforcement is report-only today.
- **Certificate pinning** — the client only trusts the pinned `api.palne.shop` chain. Pinning
  initialises at app start and fails open (with a loud log) if the native module is unavailable.

## Files

| File | Purpose |
|---|---|
| `src/core/security/attestationProtocol.ts` | Canonical request hash, proof input, protected-path matching |
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
  src/core/security/sslPinning.ts src/core/utils/versionCheck.ts
npx jest src/core/api/apiClient.test.ts `
  src/core/security/attestationProtocol.test.ts `
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
fingerprint. Existing builds cannot receive an OTA containing this feature; this is a new native
build, not an OTA-only change.

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
| `GOOGLE_HASH_MISMATCH` | Proxy or interceptor changed the body/path after the proof | Compute the hash from the exact bytes sent; check retries that rewrite the body. |
| Pinning error / no network | Certificate changed, pin set wrong, or a proxy | Compare the live SPKI hashes (below); ship a corrected pin; if impossible, wait for the expiry dead-man switch. |
| `native module unavailable` in pinning logs | Expo Go, web, or a build without the native module | Expected outside a native build; a production build logging this is broken. |

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

- **Attestation misbehaving**: set the backend `Attestation:Mode` to `ReportOnly` (and restart). The
  app keeps sending proofs; the server stops blocking. This is the fastest lever and does not need
  a client release.
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
