import ReactNativeBiometrics from 'react-native-biometrics';
import * as SecureStore from 'expo-secure-store';
import DeviceInfo from 'react-native-device-info';
import 'react-native-get-random-values';
import { v4 as uuidv4 } from 'uuid';
import { TokenStorage } from './tokenStorage';

const rnBiometrics = new ReactNativeBiometrics();

const PUBLIC_KEY_KEY = 'device_public_key';

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

    if (keysExist) {
      console.warn('[SecurityService] Keys exist but cached public key is missing — recreating');
      await rnBiometrics.deleteKeys();
    }

    const { publicKey } = await rnBiometrics.createKeys();
    await SecureStore.setItemAsync(PUBLIC_KEY_KEY, publicKey);

    return { publicKey, deviceId };
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
