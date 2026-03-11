import { Platform } from 'react-native';
import ReactNativeBiometrics from 'react-native-biometrics';
import * as SecureStore from 'expo-secure-store';
import DeviceInfo from 'react-native-device-info';
import 'react-native-get-random-values';
import { v4 as uuidv4 } from 'uuid';
import { TokenStorage } from './token.storage';

export class SecurityService {
    private static rnBiometrics = new ReactNativeBiometrics();

    // ─── Device ID ──────────────────────────────────────────────────────────

    static async getDeviceId(): Promise<string> {
        let deviceId = await SecureStore.getItemAsync('device_id');
        if (!deviceId) {
            const uniqueId = await DeviceInfo.getUniqueId();
            deviceId = (uniqueId && String(uniqueId) !== 'unknown') ? String(uniqueId) : uuidv4();
            await SecureStore.setItemAsync('device_id', deviceId);
        }
        return deviceId;
    }

    // ─── Keypair Setup (Hardware-backed) ────────────────────────────────────
 
    static async setupDeviceSecurity(): Promise<{ publicKey: string; deviceId: string }> {
        const deviceId = await this.getDeviceId();
 
        // Hardware Key (Biometric-gated)
        const { keysExist } = await this.rnBiometrics.biometricKeysExist();
        if (keysExist) {
            await this.rnBiometrics.deleteKeys();
        }
        const { publicKey } = await this.rnBiometrics.createKeys();
 
        return { publicKey, deviceId };
    }

    // ─── Biometric check ────────────────────────────────────────────────────

    static async isBiometricAvailable(): Promise<{ available: boolean; biometryType: string | undefined }> {
        const { available, biometryType } = await this.rnBiometrics.isSensorAvailable();
        return { available, biometryType };
    }
 
    static async hasKeys(): Promise<boolean> {
        const { keysExist } = await this.rnBiometrics.biometricKeysExist();
        return keysExist;
    }

    // ─── Signing (with Biometric prompt) ────────────────────────────────────

    // ─── Simple Prompt (Gating only) ─────────────────────────────────────────
 
    static async authenticate(reason: string = 'Підтвердіть особу'): Promise<boolean> {
        try {
            const { success } = await this.rnBiometrics.simplePrompt({
                promptMessage: reason,
            });
            return success;
        } catch (error) {
            console.error('Biometric authentication error:', error);
            return false;
        }
    }
 
    static async signPayload(payload: string): Promise<string> {
        const { success, signature } = await this.rnBiometrics.createSignature({
            promptMessage: 'Підтвердіть особу для підпису запиту',
            payload: payload,
        });

        if (!success || !signature) {
            throw new Error('Biometric signing failed or cancelled');
        }

        return signature;
    }

    static async signRequestSilent(payload: string): Promise<string> {
        return this.signPayload(payload);
    }
 
    static async revokeSecurity(): Promise<void> {
        const { keysExist } = await this.rnBiometrics.biometricKeysExist();
        if (keysExist) {
            await this.rnBiometrics.deleteKeys();
        }
        await SecureStore.deleteItemAsync('device_id');
        // Clear soft key if it exists from previous versions
        await SecureStore.deleteItemAsync('soft_private_key');
        // Clear old session tokens
        await TokenStorage.clearTokens();
    }

    // ─── Device Metadata ────────────────────────────────────────────────────

    static async getDeviceMetadata() {
        return {
            deviceModel: await DeviceInfo.getModel(),
            osVersion: await DeviceInfo.getSystemVersion(),
            appVersion: await DeviceInfo.getVersion(),
        };
    }
}
