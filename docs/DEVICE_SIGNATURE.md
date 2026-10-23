# Device signature enrollment and rotation

Every device that can spend money holds a keypair created by `react-native-biometrics`; the private
key stays in the Secure Enclave / Keystore, and the server verifies what only that key could have
signed. This is the operator's guide to enrolling a key, rotating it, and reading the failures.

Code lives in one place:

| Piece | File |
| --- | --- |
| Crypto (RSA/ECDSA, PEM parsing) | `backend/src/FuelFlow.API/SharedKernel/Security/AsymmetricSignatureVerifier.cs` |
| Payload shapes and per-device pins | `backend/src/FuelFlow.API/Features/Auth/SharedServices/DeviceSignatureVerifier.cs` |
| Request verification | `backend/src/FuelFlow.API/Middleware/DeviceSignatureMiddleware.cs` |
| Challenge verification | `backend/src/FuelFlow.API/Features/Auth/VerifyChallenge/VerifyChallengeCommandHandler.cs` |
| Enrollment | `backend/src/FuelFlow.API/Features/Auth/RegisterDevice/RegisterDeviceCommandHandler.cs` |
| Client payload builders | `mobile/src/core/api/deviceSignature.ts` |

## What the device signs

The server pins the payload **version** on the device row, because the bytes changed once and the
installed base still signs the old shape.

| Version | Challenge payload | Request payload |
| --- | --- | --- |
| `1` (legacy) | the raw challenge | `METHOD + path + body + timestamp` |
| `2` (current) | `FuelFlow-DeviceSignature-v2\nchallenge\n{deviceId}\n{challenge}` | `FuelFlow-DeviceSignature-v2\nrequest\n{deviceId}\n{method}\n{path}\n{timestamp}\n{body}` |

Version 2 exists so a challenge signature cannot be presented as a request signature (the prefix
names the context) and so fields cannot slide across a delimiter. The `\n` separators are literal;
any change to the string is a new version, not an edit.

Request headers are `X-Device-Id`, `X-Timestamp` (Unix milliseconds) and `X-Signature` (base64).
The middleware also stores `nonce:{deviceId}:{timestamp}` in Redis for at least twice the timestamp
tolerance, which is what makes a captured request non-replayable; the mobile client sends the same
payload its platform signs, with the query string removed because the server signs the path only.

## What is pinned, and why

Each `devices` row carries:

- `signature_algorithm` - exactly one of `RsaPkcs1Sha256`, `RsaPssSha256`, `EcdsaSha256Der`,
  `EcdsaSha256P1363`. `NULL` means a row enrolled before the pin existed; it may try every scheme
  once and is pinned to whichever one verified.
- `signature_payload_version` - `1` or `2`, defaulting to `1` for existing rows.

A pinned device never falls back: a PKCS#1 signature presented to a device pinned to PSS is invalid,
even though the same key could produce it. That is the point - accepting "whatever verifies" is how
an attacker downgrades a scheme.

Enrollment resolves the scheme from the key when the client does not name one:

| Key | Default scheme |
| --- | --- |
| RSA | `RsaPkcs1Sha256` (what the shipped mobile app produces) |
| ECDSA | `EcdsaSha256Der` (what mainstream stacks emit) |

An explicit scheme that does not belong to the key's family (an EC key pinned to RSA, or the
reverse) is refused with `400 SignatureAlgorithmDoesNotMatchKey`; storing it would lock the device
out on its next login. An unknown name is `400 UnsupportedSignatureAlgorithm`, an unreadable key is
`400 InvalidPublicKey`, and a version other than 1 or 2 is
`400 UnsupportedSignaturePayloadVersion`.

## Enrollment (first login)

1. The app calls `SecurityService.ensureKeys()`; on first run `react-native-biometrics` creates an
   RSA-2048 key and the app keeps the device id it already generated.
2. The user verifies the phone (`/api/auth/verify`). The response carries
   `deviceRegistrationNonce`.
3. The app calls `POST /api/auth/device/register` with the bearer access token, `deviceId`,
   `publicKey` (PEM/SPKI), `signatureAlgorithm: "RsaPkcs1Sha256"`,
   `signaturePayloadVersion: 2`, and the nonce.
4. The handler validates the key and pins both values on the new `devices` row.

Both fields are omitted by older builds, so the server keeps accepting them: the key decides the
scheme and the version defaults to `1`.

## Rotation (new key, same device)

Biometric re-enrollment, an OS keychain reset or a reinstall destroys the private key. The app
detects the mismatch, generates a new keypair and calls `register` again:

- **Same user** - the handler overwrites the public key, resets `Status` to `Active` and re-pins the
  scheme/version. No nonce is needed. The old private key is already useless; the new one is what
  the next login signs with.
- **Different user, previous owner live** - refused with `409 DeviceAlreadyRegistered` unless the
  caller presents a fresh `deviceRegistrationNonce` from the new account's OTP verification. That
  nonce is single-use and bound to the user id.
- **Different user, previous owner deleted their account** - the row is reclaimed without a nonce;
  the deleted owner can never authenticate again to release it.

To move a device deliberately, sign in with the new account, take the nonce from the
`/api/auth/verify` response, and pass it as `registrationNonce`. Never edit `devices` by hand: the
nonce path is audited (`SECURITY-AUDIT` log line), a manual edit is not.

## Changing the payload version

`1` is frozen for the installed base. A client moves to `2` by sending
`signaturePayloadVersion: 2` at registration, and only then signs v2 bytes. The server never
downgrades a pinned device and never reinterprets a stored signature.

Rollout order is therefore: ship the client that registers v2, then let old devices keep logging in
on v1. There is no server flag and no bulk update. A device that registers v2 and then runs an old
build can no longer sign in - it must re-register, which the app does automatically after a key
mismatch.

Rolling back means shipping a client that registers `1`; the rows it touches go back to the raw
payload. Do not flip versions on the server side.

## Diagnosing a rejected signature

Production logs name the attempted scheme(s) but never the key:

```
Invalid signature for device {DeviceId} (tried RsaPkcs1Sha256)
```

Common causes, in order:

| Symptom | Cause |
| --- | --- |
| `400 InvalidPublicKey` at registration | PEM is truncated, or is a private key, or is not SPKI |
| `400 SignatureAlgorithmDoesNotMatchKey` | client pinned the wrong family for the key it generated |
| `401 Invalid signature` on login | client signed raw v1 bytes for a v2 row (or the reverse) |
| `401 Request expired` | device clock outside `DeviceAuth:TimestampToleranceMs` (5 min) |
| `401 Request already processed` | the timestamp was reused (replay protection) |
| `401 Device is not bound to this user` | JWT and `X-Device-Id` belong to different accounts |

In a Development environment with `Auth:DevBypass` on, `POST /api/auth/device/verify-raw` accepts
`{ "challenge": "<exact payload>", "signature": "<base64>", "publicKey": "<PEM>" }` and returns the
same attempts the login path runs (`valid`, `method`, `rsaPkcs1Valid`, `rsaPssValid`,
`ecdsaDerValid`, `ecdsaIeeeValid`, plus attempt errors). This endpoint is 404 in production by
design. Paste the exact v2 payload - not just the challenge - when checking a v2 device.

Useful queries:

```sql
-- what is pinned on a device (replace the id)
SELECT device_id, status, signature_algorithm, signature_payload_version, last_seen_at
FROM devices WHERE device_id = '<id>';

-- legacy rows that still need a first successful login to pin their scheme
SELECT count(*) FROM devices WHERE signature_algorithm IS NULL;
```

## Tests that guard the contract

- `DeviceSignatureVerifierTests` - fixed RSA PKCS#1/PSS and ECDSA DER/P1363 vectors, wrong key,
  wrong scheme, replayed challenge, another device's challenge, malformed base64.
- `DeviceAuthControllerVerifyRawTests` - the diagnostic reports what the login path would decide.
- `MiddlewareTests` - signed request happy path and the reject paths.
- `DeviceRebindTests`, `AuthCommandHandlersTests` - enrollment pins and cross-user refusals.
