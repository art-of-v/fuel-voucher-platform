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

export type DeviceSecurityErrorCode = 'DEVICE_AUTH_UNAVAILABLE' | 'KEY_GENERATION_FAILED';

/**
 * Typed failure from device-security setup so the login screen can tell apart the two
 * cases that used to look identical to the user:
 *  - DEVICE_AUTH_UNAVAILABLE — the device has no passcode set at all (and no enrolled
 *    biometrics), so there is no credential to gate the device key. The user can fix this in
 *    Settings, so it is not worth a Sentry event.
 *  - KEY_GENERATION_FAILED  — an unexpected Keychain failure; the raw OSStatus (e.g. the
 *    real customer's -25293 "failed to add key to keychain") is kept in `nativeCause` for
 *    Sentry but never shown to the user.
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

  async setupDeviceSecurity(): Promise<{ publicKey: string; deviceId: string }> {
    const deviceId = await this.getDeviceId();

    const cachedPublicKey = await SecureStore.getItemAsync(PUBLIC_KEY_KEY);
    const { keysExist } = await rnBiometrics.biometricKeysExist();

    if (keysExist && cachedPublicKey) {
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

    if (keysExist) {
      console.warn('[SecurityService] Keys exist but cached public key is missing — recreating');
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
    const { success, signature } = await rnBiometrics.createSignature({
      promptMessage: 'Підтвердіть особу для підпису запиту',
      payload,
    });

    if (!success || !signature) {
      throw new Error('Biometric signing failed or cancelled');
    }

    return signature;
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
