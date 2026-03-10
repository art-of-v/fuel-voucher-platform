import { Platform } from 'react-native';
import ReactNativeBiometrics from 'react-native-biometrics';
import * as SecureStore from 'expo-secure-store';
import DeviceInfo from 'react-native-device-info';
import 'react-native-get-random-values';
import { v4 as uuidv4 } from 'uuid';

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

        // Delete existing key if present, then create fresh
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

    // ─── Signing (with Biometric prompt) ────────────────────────────────────

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

    /**
     * Silent signing for routine API requests.
     * 
     * react-native-biometrics always prompts the user on iOS (Secure Enclave policy).
     * On Android, depending on config, it may be silent. 
     * For production fintech, every sensitive request SHOULD prompt biometrics.
     */
    static async signRequestSilent(payload: string): Promise<string> {
        return this.signPayload(payload);
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
