import { useState } from 'react';
import { Keyboard } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import { SecurityService } from '../../../core/api/securityService';
import { TokenStorage } from '../../../core/api/tokenStorage';
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
    Keyboard.dismiss();

    try {
      const { accessToken, refreshToken } = await verifyPhoneCode(phone, code);
      setStep('security_setup');

      const { publicKey, deviceId } = await SecurityService.setupDeviceSecurity();
      const metadata = await SecurityService.getDeviceMetadata();

      await registerDevice(deviceId, publicKey, metadata, accessToken);

      const challenge = await getChallenge(deviceId);
      const signature = await SecurityService.signPayload(challenge);
      const { accessToken: finalAccessToken, refreshToken: finalRefreshToken } =
        await verifyChallenge(deviceId, challenge, signature);

      if (finalAccessToken && finalRefreshToken) {
        await TokenStorage.saveTokens(finalAccessToken, finalRefreshToken);
        await queryClient.refetchQueries({ queryKey: ['/api/auth/user/me'] });
      }

      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      unlockApp();
      setStep('success');
      setTimeout(() => onSuccess(), 1000);
    } catch (err: any) {
      setError(err.message || 'ПОМИЛКА АВТОРИЗАЦІЇ');
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
    setPhone,
    setCode,
    handleSendCode,
    handleVerifyCode,
    resetToPhone,
  };
}
