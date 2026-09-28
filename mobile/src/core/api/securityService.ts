import ReactNativeBiometrics from 'react-native-biometrics';
import * as SecureStore from 'expo-secure-store';
import DeviceInfo from 'react-native-device-info';
import 'react-native-get-random-values';
import { v4 as uuidv4 } from 'uuid';
import { TokenStorage } from './tokenStorage';

// allowDeviceCredentials makes the device keypair usable with the device passcode as a fallback,
// not enrolled biometrics only. On iOS this switches the Keychain ACL from
// kSecAccessControlBiometryAny to kSecAccessControlUserPresence (biometrics OR passcode) and the
// availability probe from a biometrics-only LAPolicy to LAPolicyDeviceOwnerAuthentication, so a
// customer with Face ID / Touch ID turned off but a device passcode set can still sign in.
const rnBiometrics = new ReactNativeBiometrics({ allowDeviceCredentials: true });

const PUBLIC_KEY_KEY = 'device_public_key';

export type DeviceSecurityErrorCode =
  | 'DEVICE_AUTH_UNAVAILABLE'
  | 'KEY_GENERATION_FAILED'
  | 'SIGNING_FAILED'
  | 'SIGNING_CANCELLED';

/**
 * Typed failure from device-security setup/signing so the login screen can tell apart the
 * cases that used to look identical to the user:
 *  - DEVICE_AUTH_UNAVAILABLE — the device has no passcode set at all (and no enrolled
 *    biometrics), so there is no credential to gate the device key. The user can fix this in
 *    Settings, so it is not worth a Sentry event.
 *  - KEY_GENERATION_FAILED  — an unexpected Keychain failure; the raw OSStatus (e.g. the
 *    real customer's -25293 "failed to add key to keychain") is kept in `nativeCause` for
 *    Sentry but never shown to the user.
 *  - SIGNING_FAILED         — createSignature rejected natively (e.g. errSecAuthFailed when
 *    the key's ACL demands biometrics that are no longer available). The raw message
 *    ("Key not found: error item authentication failed") is kept in `nativeCause`, never
 *    shown; the caller may recreate the key so the device passcode can unlock it.
 *  - SIGNING_CANCELLED      — the user dismissed the authentication prompt. Expected, so the
 *    caller leaves the (still-good) key in place and just shows a generic message.
 */
export class DeviceSecurityError extends Error {
  constructor(
    message: string,
    readonly code: DeviceSecurityErrorCode,
    readonly nativeCause?: unknown,
  ) {
    super(message);
    this.name = 'DeviceSecurityError';
  }
}

export const SecurityService = {
  async getDeviceId(): Promise<string> {
    let deviceId = await SecureStore.getItemAsync('device_id');
    if (!deviceId) {
      const uniqueId = await DeviceInfo.getUniqueId();
      deviceId = uniqueId && String(uniqueId) !== 'unknown'
        ? String(uniqueId)
        : uuidv4();
      await SecureStore.setItemAsync('device_id', deviceId);
    }
    return deviceId;
  },

  /**
   * Ensures a device keypair exists and returns its public key. Pass
   * `{ forceRecreate: true }` to discard any existing key and mint a fresh one — used to
   * recover a device whose key was created by an older build under a biometrics-only ACL
   * (kSecAccessControlBiometryAny): once the customer turns Face ID / Touch ID off, that old
   * key can never be unlocked and signing rejects with errSecAuthFailed. Recreating it under
   * the current biometrics-OR-passcode ACL lets the passcode unlock it. The caller must
   * re-register the returned public key.
   */
  async setupDeviceSecurity(
    options?: { forceRecreate?: boolean },
  ): Promise<{ publicKey: string; deviceId: string }> {
    const deviceId = await this.getDeviceId();

    const cachedPublicKey = await SecureStore.getItemAsync(PUBLIC_KEY_KEY);
    const { keysExist } = await rnBiometrics.biometricKeysExist();

    if (!options?.forceRecreate && keysExist && cachedPublicKey) {
      return { publicKey: cachedPublicKey, deviceId };
    }

    // The device keypair is stored under a Keychain ACL of
    // kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly + kSecAccessControlUserPresence (via the
    // allowDeviceCredentials instance above), so it can be created and used with EITHER enrolled
    // biometrics OR the device passcode. It still needs a passcode to be set; on a device with no
    // passcode at all, createKeys() fails deep in the Keychain with errSecAuthFailed (-25293).
    // Preflight so we can show actionable guidance instead of a raw OSStatus, and never delete an
    // existing key we then can't recreate.
    const { available } = await this.isBiometricAvailable();
    if (!available) {
      throw new DeviceSecurityError(
        'No device passcode or biometrics available',
        'DEVICE_AUTH_UNAVAILABLE',
      );
    }

    // Drop the old key when forcing a fresh one (passcode-fallback recovery), or when the cached
    // public key was lost — a key whose public half we can't reproduce is useless for registration.
    if (keysExist) {
      await rnBiometrics.deleteKeys();
    }

    try {
      const { publicKey } = await rnBiometrics.createKeys();
      await SecureStore.setItemAsync(PUBLIC_KEY_KEY, publicKey);
      return { publicKey, deviceId };
    } catch (err) {
      // Wrap the raw native error so it never reaches the UI; the caller maps this to a
      // friendly message and forwards the cause to Sentry for triage.
      throw new DeviceSecurityError(
        err instanceof Error ? err.message : 'Key generation failed',
        'KEY_GENERATION_FAILED',
        err,
      );
    }
  },

  async isBiometricAvailable(): Promise<{ available: boolean; biometryType: string | undefined }> {
    const { available, biometryType } = await rnBiometrics.isSensorAvailable();
    return { available, biometryType };
  },

  async hasKeys(): Promise<boolean> {
    const { keysExist } = await rnBiometrics.biometricKeysExist();
    return keysExist;
  },

  async signPayload(payload: string): Promise<string> {
    try {
      const { success, signature } = await rnBiometrics.createSignature({
        promptMessage: 'Підтвердіть особу для підпису запиту',
        payload,
      });

      if (!success || !signature) {
        // The prompt was dismissed/cancelled. Distinct from a native failure so the caller
        // can leave a still-usable key in place instead of recreating it.
        throw new DeviceSecurityError('Signing was cancelled', 'SIGNING_CANCELLED');
      }

      return signature;
    } catch (err) {
      if (err instanceof DeviceSecurityError) {
        throw err;
      }
      // createSignature REJECTED natively — e.g. errSecAuthFailed / "Key not found: error item
      // authentication failed" when the key's ACL demands biometrics that are off/unenrolled.
      // Wrap it so the raw OSStatus string can never reach the UI; the caller may recreate the
      // key so the device passcode can unlock it.
      throw new DeviceSecurityError(
        err instanceof Error ? err.message : 'Device signing failed',
        'SIGNING_FAILED',
        err,
      );
    }
  },

  async revokeSecurity(): Promise<void> {
    const { keysExist } = await rnBiometrics.biometricKeysExist();
    if (keysExist) {
      await rnBiometrics.deleteKeys();
    }
    await SecureStore.deleteItemAsync('device_id');
    await SecureStore.deleteItemAsync(PUBLIC_KEY_KEY);
    await SecureStore.deleteItemAsync('soft_private_key');
    await TokenStorage.clearTokens();
  },

  async getDeviceMetadata() {
    return {
      deviceModel: await DeviceInfo.getModel(),
      osVersion: await DeviceInfo.getSystemVersion(),
      appVersion: await DeviceInfo.getVersion(),
    };
  },
};
