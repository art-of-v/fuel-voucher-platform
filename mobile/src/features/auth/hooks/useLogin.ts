import { useEffect, useRef, useState } from 'react';
import { Keyboard } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import { SecurityService, DeviceSecurityError } from '../../../core/api/securityService';
import { TokenStorage } from '../../../core/api/tokenStorage';
import { BASE_URL } from '../../../core/api/apiClient';
import { buildChallengeSignaturePayload } from '../../../core/api/deviceSignature';
import { useStore } from '../../../core/state/appStore';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { reportError } from '../../../core/observability/sentry';
import { sendVerificationCode } from '../api/sendCode';
import { verifyPhoneCode } from '../api/verifyCode';
import { verifyTwoFactor } from '../api/verifyTwoFactor';
import { registerDevice, getChallenge, verifyChallenge } from '../api/registerDevice';
import type { AuthResponse, AuthStep } from '../types';

/** The outstanding second factor of a staff sign-in. */
interface TwoFactorState {
  challenge: string;
  needsEnrollment: boolean;
  enrollmentSecret?: string | null;
}

interface UseLoginReturn {
  step: AuthStep;
  phone: string;
  code: string;
  twoFactorCode: string;
  twoFactor: TwoFactorState | null;
  loading: boolean;
  error: string;
  diagResult: string;
  setPhone: (value: string) => void;
  setCode: (value: string) => void;
  setTwoFactorCode: (value: string) => void;
  handleSendCode: () => Promise<void>;
  handleVerifyCode: () => Promise<void>;
  handleVerifyTwoFactor: () => Promise<void>;
  resetToPhone: () => void;
}

/** What the sign-in form starts with. A code that arrived by push lands here. */
interface UseLoginOptions {
  initialPhone?: string;
  initialCode?: string;
}

export function useLogin(onSuccess: () => void, options?: UseLoginOptions): UseLoginReturn {
  const [step, setStep] = useState<AuthStep>(options?.initialCode ? 'code' : 'phone');
  const [phone, setPhoneState] = useState(options?.initialPhone ?? '+380');
  const [code, setCodeState] = useState(options?.initialCode ?? '');
  const [twoFactorCode, setTwoFactorCodeState] = useState('');
  const [twoFactor, setTwoFactor] = useState<TwoFactorState | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [diagResult, setDiagResult] = useState('');
  const unlockApp = useStore((state) => state.unlockApp);
  const lastPhone = useStore((state) => state.lastPhone);
  const setLastPhone = useStore((state) => state.setLastPhone);
  // Whether the person has typed their own number. The remembered number is a convenience,
  // not an override: a number typed by hand always wins.
  const phoneTouched = useRef(false);
  const queryClient = useQueryClient();
  /*
   * The four user-facing failure strings below used to be hardcoded Ukrainian
   * literals in a four-language app, so a German or Spanish user hit Ukrainian at
   * the exact moment sign-in went wrong. This is a hook, so it can read the store
   * directly rather than having the caller thread `t` in.
   */
  const t = useI18n((state) => state.t);

  // The remembered number arrives AFTER the first render - zustand rehydrates the store from
  // AsyncStorage - so the prefill is applied when it lands, not at mount. A number that came
  // with the push (initialPhone) wins outright, and a number the person typed is never
  // overwritten.
  useEffect(() => {
    if (options?.initialPhone || phoneTouched.current) return;
    if (lastPhone && lastPhone !== phone) {
      setPhoneState(lastPhone);
    }
  }, [lastPhone, options?.initialPhone, phone]);

  const setPhone = (value: string) => {
    phoneTouched.current = true;
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

  const setTwoFactorCode = (value: string) => {
    const digitsOnly = value.replace(/\D/g, '').slice(0, 6);
    setTwoFactorCodeState(digitsOnly);
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
      // The number worked: remember it, so the next sign-in on this device starts from the
      // number instead of asking for it again. It survives the logout on purpose.
      setLastPhone(phone);
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

    // The code and the device are two different failures with two different messages, so
    // they are two different try blocks: a refused code must not be reported as a device
    // problem, and a device that cannot sign must not be reported as a wrong code.
    let response: AuthResponse;
    try {
      logs.push('--- STEP: verifyPhoneCode ---');
      response = await verifyPhoneCode(phone, code);
      logs.push('OK phone verified');
    } catch (err: any) {
      setError(describeCodeFailure(err, t, logs));
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setStep('code');
      setLoading(false);
      return;
    }

    try {
      // A staff account does not sign in with the first factor alone: the backend answers
      // with a challenge, and the sign-in continues at the authenticator app.
      if (response.twoFactor) {
        setTwoFactor({
          challenge: response.twoFactor.challenge,
          needsEnrollment: response.twoFactor.status === 'enroll',
          enrollmentSecret: response.twoFactor.enrollmentSecret,
        });
        setTwoFactorCodeState('');
        setStep('two_factor');
        return;
      }

      await completeSignIn(
        response.accessToken as string,
        response.refreshToken as string,
        response.deviceRegistrationNonce ?? '',
        logs,
      );
    } catch (err: any) {
      setError(describeFailure(err, t, logs));
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setStep('code');
    } finally {
      setLoading(false);
    }
  };

  const handleVerifyTwoFactor = async () => {
    if (!twoFactor) {
      return;
    }
    if (twoFactorCode.length !== 6) {
      setError(t('phoneAuth.codeRequired'));
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      return;
    }

    setLoading(true);
    setError('');
    setDiagResult('');
    Keyboard.dismiss();

    const logs: string[] = [];

    // The code and the device are two different failures with two different messages, so
    // they are two different try blocks: a refused code must not be reported as a device
    // problem, and a device that cannot sign must not be reported as a wrong code.
    let response: AuthResponse;
    try {
      logs.push('--- STEP: verifyTwoFactor ---');
      response = await verifyTwoFactor(twoFactor.challenge, twoFactorCode);
      logs.push('OK second factor verified');
    } catch (err: any) {
      setError(describeTwoFactorFailure(err, t, logs));
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setStep('two_factor');
      setLoading(false);
      return;
    }

    try {
      await completeSignIn(
        response.accessToken as string,
        response.refreshToken as string,
        response.deviceRegistrationNonce ?? '',
        logs,
      );
    } catch (err: any) {
      setError(describeFailure(err, t, logs));
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setStep('two_factor');
    } finally {
      setLoading(false);
    }
  };

  /**
   * The rest of the sign-in, once the session exists: the device is registered, the challenge
   * is signed, and the app is unlocked.
   *
   * <p>
   * Shared by the two factors, because the device binding says nothing about which factor
   * produced the session - and a copy of it would be a copy that drifts.
   * </p>
   */
  const completeSignIn = async (
    accessToken: string,
    refreshToken: string,
    deviceRegistrationNonce: string,
    logs: string[],
  ) => {
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
    // The server reconstructs this exact string from the device id and the challenge.
    let challengePayload = buildChallengeSignaturePayload(deviceId, challenge);
    logs.push(`challenge=${challenge}`);

    logs.push('--- STEP: signPayload ---');
    let signature: string;
    try {
      signature = await SecurityService.signPayload(challengePayload);
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
        challengePayload = buildChallengeSignaturePayload(deviceId, challenge);
        signature = await SecurityService.signPayload(challengePayload);
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
          body: JSON.stringify({ challenge: challengePayload, signature, publicKey }),
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
  };

  const resetToPhone = () => {
    setStep('phone');
    setError('');
    setTwoFactor(null);
    setTwoFactorCodeState('');
  };

  return {
    step,
    phone,
    code,
    twoFactorCode,
    twoFactor,
    loading,
    error,
    diagResult,
    setPhone,
    setCode,
    setTwoFactorCode,
    handleSendCode,
    handleVerifyCode,
    handleVerifyTwoFactor,
    resetToPhone,
  };
}

/**
 * The failure the user sees at the second factor.
 *
 * <p>
 * The second factor has its own mistakes: the code from the authenticator app can be wrong,
 * and the challenge can be spent. Both used to be reported as "could not confirm the device",
 * which pointed at a device problem instead of the code the person had just typed - and pushed
 * people to retype the push code that the first factor had already accepted.
 * </p>
 *
 * <p>
 * Only the code's own step is described here. The device binding that follows it is a separate
 * step with its own messages (see {@link describeFailure}).
 * </p>
 */
function describeTwoFactorFailure(err: any, t: (key: string) => string, logs: string[]): string {
  logs.push(`ERROR: ${err?.message}`);
  if (__DEV__) {
    // The diagnostic trail is the developer's window into the failure - and it is the only
    // place the raw cause appears at all.
    return logs.join('\n');
  }
  if (err?.status === 429) {
    return t('phoneAuth.tooManyAttempts');
  }
  if (err?.status === 401 || err?.status === 400) {
    // The server refuses a wrong code, a spent challenge and an expired one with the same
    // 401 - on purpose, so the endpoint cannot be used to tell them apart. To the person at
    // the screen the outcome is the same anyway: the code they entered was not accepted.
    return t('phoneAuth.twoFactorCodeRejected');
  }
  // Anything else is the network's or the server's fault - never the person's code.
  reportError(err);
  return t('phoneAuth.networkError');
}

/**
 * The failure the user sees at the first factor.
 *
 * <p>
 * The code that arrives by push or SMS is refused with a 401 - and it used to be reported as
 * "could not confirm the device", which names the device instead of the six digits the person
 * typed.
 * </p>
 */
function describeCodeFailure(err: any, t: (key: string) => string, logs: string[]): string {
  logs.push(`ERROR: ${err?.message}`);
  if (__DEV__) {
    // The diagnostic trail is the developer's window into the failure - and it is the only
    // place the raw cause appears at all.
    return logs.join('\n');
  }
  if (err?.status === 429) {
    return t('phoneAuth.tooManyAttempts');
  }
  if (err?.status === 401 || err?.status === 400) {
    // The server refuses a wrong code and an expired one with the same 401 - on purpose, so
    // the endpoint cannot be used to tell them apart. To the person at the screen the outcome
    // is the same anyway: the code they entered was not accepted.
    return t('phoneAuth.verifyFailed');
  }
  // Anything else is the network's or the server's fault - never the person's code.
  reportError(err);
  return t('phoneAuth.networkError');
}

/**
 * The failure the user sees.
 *
 * <p>
 * Never a raw error string: a 429 is the rate limit, a device-security error has its own
 * messages, and anything else is the network's fault - the real cause goes to Sentry.
 * </p>
 */
function describeFailure(err: any, t: (key: string) => string, logs: string[]): string {
  logs.push(`ERROR: ${err?.message}`);
  if (__DEV__) {
    // The diagnostic trail is the developer's window into the failure - and it is the only
    // place the raw cause appears at all.
    return logs.join('\n');
  }
  if (err?.status === 429) {
    return t('phoneAuth.tooManyAttempts');
  }
  if (err instanceof DeviceSecurityError) {
    if (err.code === 'DEVICE_AUTH_UNAVAILABLE') {
      // The user can fix this themselves — tell them how, don't page Sentry.
      return t('phoneAuth.biometricsRequired');
    }
    if (err.code === 'SIGNING_CANCELLED') {
      // The user dismissed the authentication prompt; expected, not worth paging Sentry.
      return t('phoneAuth.deviceVerifyFailed');
    }
    // Unexpected Keychain/native failure (e.g. -25293, or signing still failing after the
    // passcode-fallback retry). Show a clean message; keep the raw native cause for triage
    // instead of leaking the OSStatus to the customer.
    reportError(err);
    return t('phoneAuth.deviceVerifyFailed');
  }
  // Any other failure (network/server/unexpected). Never surface its raw message: forward
  // the real cause to Sentry and show a localized fallback.
  reportError(err);
  return t('phoneAuth.deviceVerifyFailed');
}
