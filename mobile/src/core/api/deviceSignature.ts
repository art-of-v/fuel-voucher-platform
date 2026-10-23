/**
 * The bytes the device signs, and the contract it registers with.
 *
 * The server pins both values per device, so changing either one here means the device must
 * re-register before it can sign in again. Version 1 is the raw legacy payload the installed base
 * still uses; this build registers version 2, whose prefix and delimiters separate a challenge
 * signature from a request signature and make the field boundaries unambiguous.
 *
 * Keep this in lockstep with backend/src/FuelFlow.API/Features/Auth/SharedServices/DeviceSignatureVerifier.cs
 */

export const DEVICE_SIGNATURE_PAYLOAD_VERSION = 2;

/**
 * react-native-biometrics creates an RSA-2048 key and signs with SHA-256 / PKCS#1 v1.5 on both
 * platforms. Naming it explicitly means a future library change fails at enrollment instead of
 * producing signatures the server silently refuses.
 */
export const DEVICE_SIGNATURE_ALGORITHM = 'RsaPkcs1Sha256';

const VERSION_2_PREFIX = 'FuelFlow-DeviceSignature-v2';

export function buildChallengeSignaturePayload(deviceId: string, challenge: string): string {
  return `${VERSION_2_PREFIX}\nchallenge\n${deviceId}\n${challenge}`;
}

/**
 * The server signs the request path without its query string (the signed checkout routes do not
 * read one), so the query is dropped here too. Everything else is byte-for-byte what was sent.
 */
export function buildRequestSignaturePayload(
  deviceId: string,
  method: string,
  endpoint: string,
  body: string,
  timestamp: string,
): string {
  const queryStart = endpoint.indexOf('?');
  const path = queryStart >= 0 ? endpoint.slice(0, queryStart) : endpoint;

  return `${VERSION_2_PREFIX}\nrequest\n${deviceId}\n${method}\n${path}\n${timestamp}\n${body}`;
}
