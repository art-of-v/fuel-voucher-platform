import { useState } from 'react';
import { Keyboard } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import { SecurityService } from '../../../core/api/securityService';
import { TokenStorage } from '../../../core/api/tokenStorage';
import { BASE_URL } from '../../../core/api/apiClient';
import { useStore } from '../../../core/state/appStore';
import { Haptics } from '../../../core/utils/haptics';
import { sendVerificationCode } from '../api/sendCode';
import { verifyPhoneCode } from '../api/verifyCode';
import { registerDevice, getChallenge, verifyChallenge } from '../api/registerDevice';
import type { AuthStep } from '../types';

interface UseLoginReturn {
  step: AuthStep;
  phone: string;
  code: string;
  loading: boolean;
  error: string;
  diagResult: string;
  setPhone: (value: string) => void;
  setCode: (value: string) => void;
  handleSendCode: () => Promise<void>;
  handleVerifyCode: () => Promise<void>;
  resetToPhone: () => void;
}

export function useLogin(onSuccess: () => void): UseLoginReturn {
  const [step, setStep] = useState<AuthStep>('phone');
  const [phone, setPhoneState] = useState('+380');
  const [code, setCodeState] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [diagResult, setDiagResult] = useState('');
  const unlockApp = useStore(state => state.unlockApp);
  const queryClient = useQueryClient();

  const setPhone = (value: string) => {
    setPhoneState(value);
    if (value.length >= 13) {
      Keyboard.dismiss();
    }
  };

  const setCode = (value: string) => {
    const digitsOnly = value.replace(/\D/g, '').slice(0, 6);
    setCodeState(digitsOnly);
    if (digitsOnly.length === 6) {
      Keyboard.dismiss();
    }
  };

  const handleSendCode = async () => {
    if (!phone.trim() || phone.length < 10) {
      setError('ВВЕДІТЬ КОРЕКТНИЙ НОМЕР');
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      return;
    }

    setLoading(true);
    setError('');
    Keyboard.dismiss();

    try {
      await sendVerificationCode(phone);
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setStep('code');
    } catch (err: any) {
      setError(err.message || 'ПОМИЛКА МЕРЕЖІ');
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    } finally {
      setLoading(false);
    }
  };

  const handleVerifyCode = async () => {
    if (!code.trim() || code.length !== 6) {
      setError('ВВЕДІТЬ КОД');
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      return;
    }

    setLoading(true);
    setError('');
    setDiagResult('');
    Keyboard.dismiss();

    const logs: string[] = [];

    try {
      logs.push('--- STEP: verifyPhoneCode ---');
      const { accessToken, refreshToken } = await verifyPhoneCode(phone, code);
      logs.push('OK phone verified');
      setStep('security_setup');

      logs.push('--- STEP: setupDeviceSecurity ---');
      const { publicKey, deviceId } = await SecurityService.setupDeviceSecurity();
      logs.push(`deviceId=${deviceId}`);
      logs.push(`publicKey (first 64)=${publicKey.slice(0, 64)}`);
      logs.push(`publicKey length=${publicKey.length}`);

      const metadata = await SecurityService.getDeviceMetadata();

      logs.push('--- STEP: registerDevice ---');
      await registerDevice(deviceId, publicKey, metadata, accessToken);
      logs.push('OK device registered');

      logs.push('--- STEP: getChallenge ---');
      const challenge = await getChallenge(deviceId, accessToken);
      logs.push(`challenge=${challenge}`);

      logs.push('--- STEP: signPayload ---');
      const signature = await SecurityService.signPayload(challenge);
      logs.push(`signature (first 32)=${signature.slice(0, 32)}`);
      logs.push(`signature length=${signature.length}`);

      if (__DEV__) {
        logs.push('--- STEP: verify-raw (diagnostic) ---');
        try {
          const diagResp = await fetch(`${BASE_URL}/api/auth/device/verify-raw`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ challenge, signature, publicKey }),
          });
          const diagData: any = await diagResp.json();
          logs.push(`verify-raw result: ${JSON.stringify(diagData)}`);
          setDiagResult(JSON.stringify(diagData));
        } catch (e: any) {
          logs.push(`verify-raw error: ${e.message}`);
        }
      }

      logs.push('--- STEP: verifyChallenge ---');
      const { accessToken: finalAccessToken, refreshToken: finalRefreshToken } =
        await verifyChallenge(deviceId, challenge, signature);
      logs.push('OK challenge verified');

      if (finalAccessToken && finalRefreshToken) {
        await TokenStorage.saveTokens(finalAccessToken, finalRefreshToken);
        await queryClient.refetchQueries({ queryKey: ['/api/auth/user/me'] });
      }

      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      unlockApp();
      setStep('success');
      setTimeout(() => onSuccess(), 1000);
    } catch (err: any) {
      logs.push(`ERROR: ${err.message}`);
      if (__DEV__) {
        setError(logs.join('\n'));
      } else {
        setError(err.message || 'ПОМИЛКА ПІДПИСУ');
      }
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setStep('code');
    } finally {
      setLoading(false);
    }
  };

  const resetToPhone = () => {
    setStep('phone');
    setError('');
  };

  return {
    step,
    phone,
    code,
    loading,
    error,
    diagResult,
    setPhone,
    setCode,
    handleSendCode,
    handleVerifyCode,
    resetToPhone,
  };
}
