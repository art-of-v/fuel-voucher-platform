import Constants from 'expo-constants';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import * as AppIntegrity from '@expo/app-integrity';
import { BASE_URL, CLIENT_PLATFORM_HEADER, NATIVE_CLIENT_PLATFORM } from '../api/apiConfig';
import { SecurityService } from '../api/securityService';
import { computeProofRequestHash, computeRequestHash } from './attestationProtocol';
import { ensureSslPinningInitialized } from './sslPinning';

export { isAttestationProtected } from './attestationProtocol';

const APPLE_KEY_ID = 'attestation_apple_key_id';
const CHALLENGE_TIMEOUT_MS = 15_000;

type ChallengeResponse = { challenge?: unknown };
type AttestationHeaders = Record<string, string>;

export class AttestationError extends Error {
  constructor(
    message: string,
    readonly code: string,
  ) {
    super(message);
    this.name = 'AttestationError';
  }
}

let appleRegistrationPromise: Promise<string | null> | null = null;
let googleProviderPromise: Promise<boolean> | null = null;

function googleCloudProjectNumber(): string | null {
  const fromExtra = Constants.expoConfig?.extra?.googleCloudProjectNumber;
  const fromEnv = process.env.EXPO_PUBLIC_GOOGLE_CLOUD_PROJECT_NUMBER;
  const value = fromEnv ?? fromExtra;
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

function appVersionHeader(): Record<string, string> {
  const version = Constants.expoConfig?.version;
  return version ? { 'X-App-Version': version } : {};
}

async function postAttestationJson<T>(path: string, body: unknown): Promise<T> {
  await ensureSslPinningInitialized(BASE_URL);

  const deviceId = await SecurityService.getDeviceId();
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), CHALLENGE_TIMEOUT_MS);

  try {
    const response = await fetch(`${BASE_URL}${path}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'x-device-id': deviceId,
        [CLIENT_PLATFORM_HEADER]: NATIVE_CLIENT_PLATFORM,
        ...(Platform.OS === 'ios' || Platform.OS === 'android'
          ? { 'X-Client-OS': Platform.OS }
          : {}),
        ...appVersionHeader(),
      },
      body: JSON.stringify(body),
      signal: controller.signal,
    });

    if (!response.ok) {
      let code = `ATTESTATION_HTTP_${response.status}`;
      try {
        const errorBody = (await response.json()) as { code?: unknown };
        if (typeof errorBody?.code === 'string') code = errorBody.code;
      } catch {
        // Non-JSON error body; keep the HTTP-derived code.
      }
      throw new AttestationError(`Attestation request failed with ${code}`, code);
    }

    return (await response.json()) as T;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function requestChallenge(action: string, requestHash: string): Promise<string> {
  const data = await postAttestationJson<ChallengeResponse>('/api/attestation/challenge', {
    action,
    requestHash,
  });

  if (typeof data.challenge !== 'string' || !data.challenge) {
    throw new AttestationError('The attestation challenge response was empty', 'ATTESTATION_INVALID');
  }

  return data.challenge;
}

async function registerAppleKey(): Promise<string | null> {
  if (appleRegistrationPromise) return appleRegistrationPromise;

  appleRegistrationPromise = (async () => {
    if (Platform.OS !== 'ios' || !AppIntegrity.isSupported) return null;

    const challenge = await requestChallenge('apple-attest', '');
    const keyId = await AppIntegrity.generateKeyAsync();
    const attestationObject = await AppIntegrity.attestKeyAsync(keyId, challenge);

    await postAttestationJson('/api/attestation/apple/register', {
      keyId,
      attestationObject,
      challenge,
    });

    await SecureStore.setItemAsync(APPLE_KEY_ID, keyId);
    return keyId;
  })().finally(() => {
    appleRegistrationPromise = null;
  });

  return appleRegistrationPromise;
}

async function getAppleKeyId(): Promise<string | null> {
  if (Platform.OS !== 'ios' || !AppIntegrity.isSupported) return null;

  const existing = await SecureStore.getItemAsync(APPLE_KEY_ID);
  if (existing) return existing;

  return registerAppleKey();
}

async function getAppleHeaders(requestHash: string): Promise<AttestationHeaders | null> {
  let keyId = await getAppleKeyId();
  if (!keyId) return null;

  for (let attempt = 0; attempt < 2; attempt++) {
    const challenge = await requestChallenge('mobile-request', requestHash);

    try {
      const proof = await AppIntegrity.generateAssertionAsync(
        keyId,
        `${challenge}:${requestHash}`,
      );

      return {
        'X-Attestation-Provider': 'apple',
        'X-Attestation-Challenge': challenge,
        'X-Attestation-Proof': proof,
        'X-Attestation-Key-Id': keyId,
      };
    } catch (error) {
      // The Keychain survives reinstall but the Secure Enclave key may not; the server may
      // also have lost the registration. Mint a fresh key once instead of failing forever.
      if (attempt > 0) throw error;

      await SecureStore.deleteItemAsync(APPLE_KEY_ID);
      keyId = await registerAppleKey();
      if (!keyId) return null;
    }
  }

  return null;
}

async function ensureGoogleProvider(): Promise<boolean> {
  if (Platform.OS !== 'android') return false;
  if (googleProviderPromise) return googleProviderPromise;

  const projectNumber = googleCloudProjectNumber();
  if (!projectNumber) {
    console.warn('[Attestation] Google Cloud project number is not configured; Android proof skipped');
    return false;
  }

  googleProviderPromise = AppIntegrity.prepareIntegrityTokenProviderAsync(projectNumber)
    .then(() => true)
    .catch((error) => {
      console.error('[Attestation] Play Integrity provider preparation failed:', error);
      return false;
    });

  return googleProviderPromise;
}

async function getGoogleHeaders(requestHash: string): Promise<AttestationHeaders | null> {
  if (!(await ensureGoogleProvider())) return null;

  const challenge = await requestChallenge('mobile-request', requestHash);
  const proofRequestHash = await computeProofRequestHash(challenge, requestHash);
  const proof = await AppIntegrity.requestIntegrityCheckAsync(proofRequestHash);

  return {
    'X-Attestation-Provider': 'google',
    'X-Attestation-Challenge': challenge,
    'X-Attestation-Proof': proof,
  };
}

/**
 * Best-effort proof for one protected request. Returns null when the platform cannot attest;
 * the server's rollout mode decides whether that is fatal.
 */
export async function getAttestationHeaders(
  method: string,
  path: string,
  body: string,
): Promise<AttestationHeaders | null> {
  const requestHash = await computeRequestHash(method, path, body);

  if (Platform.OS === 'ios') return getAppleHeaders(requestHash);
  if (Platform.OS === 'android') return getGoogleHeaders(requestHash);
  return null;
}

/** The device id is being replaced, so a key bound to the old id must not be reused. */
export async function clearAppleAttestationKey(): Promise<void> {
  await SecureStore.deleteItemAsync(APPLE_KEY_ID);
}
