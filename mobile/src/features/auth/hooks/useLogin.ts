import { useState } from 'react';
import { Keyboard } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import { SecurityService, DeviceSecurityError } from '../../../core/api/securityService';
import { TokenStorage } from '../../../core/api/tokenStorage';
import { BASE_URL } from '../../../core/api/apiClient';
import { useStore } from '../../../core/state/appStore';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { reportError } from '../../../core/observability/sentry';
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
  /*
   * The four user-facing failure strings below used to be hardcoded Ukrainian
   * literals in a four-language app, so a German or Spanish user hit Ukrainian at
   * the exact moment sign-in went wrong. This is a hook, so it can read the store
   * directly rather than having the caller thread `t` in.
   */
  const t = useI18n(state => state.t);

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
      setError(t('phoneAuth.invalidPhone'));
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
      // Never surface a raw error string to the user. A 429 is the OTP rate limit; anything
      // else gets a localized network message with the real cause forwarded to Sentry.
      if (err?.status === 429) {
        setError(t('phoneAuth.tooManyAttempts'));
      } else {
        reportError(err);
        setError(t('phoneAuth.networkError'));
      }
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    } finally {
      setLoading(false);
    }
  };

  const handleVerifyCode = async () => {
    if (!code.trim() || code.length !== 6) {
      setError(t('phoneAuth.codeRequired'));
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
      const { accessToken, refreshToken, deviceRegistrationNonce } = await verifyPhoneCode(phone, code);
      logs.push('OK phone verified');
      setStep('security_setup');

      logs.push('--- STEP: setupDeviceSecurity ---');
      const security = await SecurityService.setupDeviceSecurity();
      const deviceId = security.deviceId;
      // Tracked in a let: the passcode-fallback recovery below can recreate the key, which
      // yields a new public key that must then be re-registered.
      let publicKey = security.publicKey;
      logs.push(`deviceId=${deviceId}`);
      logs.push(`publicKey (first 64)=${publicKey.slice(0, 64)}`);
      logs.push(`publicKey length=${publicKey.length}`);

      const metadata = await SecurityService.getDeviceMetadata();

      logs.push('--- STEP: registerDevice ---');
      await registerDevice(deviceId, publicKey, metadata, accessToken, deviceRegistrationNonce);
      logs.push('OK device registered');

      logs.push('--- STEP: getChallenge ---');
      let challenge = await getChallenge(deviceId, accessToken);
      logs.push(`challenge=${challenge}`);

      logs.push('--- STEP: signPayload ---');
      let signature: string;
      try {
        signature = await SecurityService.signPayload(challenge);
      } catch (signErr) {
        // A device key minted by an older build under a biometrics-only ACL cannot be unlocked
        // once the customer turns Face ID / Touch ID off, so signing rejects (errSecAuthFailed).
        // Recreate the key under the current biometrics-OR-passcode ACL, re-register it (same
        // user, no nonce — the backend allows this), fetch a fresh challenge, and sign once more
        // so a Face-ID-off customer can still get in. Only one retry: a second failure falls
        // through to the catch below and a localized message.
        if (signErr instanceof DeviceSecurityError && signErr.code === 'SIGNING_FAILED') {
          logs.push('signPayload rejected — recreating device key for passcode fallback');
          const recreated = await SecurityService.setupDeviceSecurity({ forceRecreate: true });
          publicKey = recreated.publicKey;
          await registerDevice(deviceId, publicKey, metadata, accessToken);
          challenge = await getChallenge(deviceId, accessToken);
          signature = await SecurityService.signPayload(challenge);
          logs.push('OK signed after key recreation');
        } else {
          throw signErr;
        }
      }
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
      } else if (err?.status === 429) {
        setError(t('phoneAuth.tooManyAttempts'));
      } else if (err instanceof DeviceSecurityError) {
        if (err.code === 'DEVICE_AUTH_UNAVAILABLE') {
          // The user can fix this themselves — tell them how, don't page Sentry.
          setError(t('phoneAuth.biometricsRequired'));
        } else if (err.code === 'SIGNING_CANCELLED') {
          // The user dismissed the authentication prompt; expected, not worth paging Sentry.
          setError(t('phoneAuth.deviceVerifyFailed'));
        } else {
          // Unexpected Keychain/native failure (e.g. -25293, or signing still failing after the
          // passcode-fallback retry). Show a clean message; keep the raw native cause for triage
          // instead of leaking the OSStatus to the customer.
          reportError(err);
          setError(t('phoneAuth.deviceVerifyFailed'));
        }
      } else {
        // Any other failure (network/server/unexpected). Never surface its raw message: forward
        // the real cause to Sentry and show a localized fallback.
        reportError(err);
        setError(t('phoneAuth.deviceVerifyFailed'));
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
