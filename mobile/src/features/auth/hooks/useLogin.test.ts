import { renderHook, act, waitFor } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useLogin } from './useLogin';
import { sendVerificationCode } from '../api/sendCode';
import { verifyPhoneCode } from '../api/verifyCode';
import { verifyTwoFactor } from '../api/verifyTwoFactor';
import { SecurityService, DeviceSecurityError } from '../../../core/api/securityService';
import { useStore } from '../../../core/state/appStore';

// The network boundary is mocked: the sign-in flow is what is under test, not the transport.
jest.mock('../api/sendCode', () => ({
  sendVerificationCode: jest.fn(),
}));

jest.mock('../api/verifyCode', () => ({
  verifyPhoneCode: jest.fn(),
}));

jest.mock('../api/verifyTwoFactor', () => ({
  verifyTwoFactor: jest.fn(),
}));

// t() returns the key verbatim so the assertions check WHICH message was chosen
// without coupling to translation copy. The real hook is a store hook that takes a
// selector, so the mock has to run the selector too.
jest.mock('../../../core/i18n', () => ({
  useI18n: (selector: (state: { t: (key: string) => string }) => unknown) =>
    selector({ t: (key: string) => key }),
}));

jest.mock('../../../core/utils/haptics', () => ({
  Haptics: {
    notificationAsync: jest.fn(),
    NotificationFeedbackType: { Success: 'success', Error: 'error', Warning: 'warning' },
  },
}));

jest.mock('../../../core/observability/sentry', () => ({
  reportError: jest.fn(),
}));

jest.mock('../../../core/api/securityService', () => ({
  SecurityService: {
    setupDeviceSecurity: jest.fn(),
    getDeviceMetadata: jest.fn(),
    signPayload: jest.fn(),
  },
  DeviceSecurityError: class DeviceSecurityError extends Error {
    code: string;

    constructor(message: string, errorCode: string) {
      super(message);
      this.code = errorCode;
      this.name = 'DeviceSecurityError';
    }
  },
}));

jest.mock('../../../core/api/tokenStorage', () => ({
  TokenStorage: { saveTokens: jest.fn() },
}));

jest.mock('../../../core/api/deviceSignature', () => ({
  buildChallengeSignaturePayload: () => 'payload',
}));

jest.mock('../../../core/api/apiClient', () => ({
  BASE_URL: 'http://localhost',
}));

const sendVerificationCodeMock = sendVerificationCode as jest.Mock;
const verifyPhoneCodeMock = verifyPhoneCode as jest.Mock;
const verifyTwoFactorMock = verifyTwoFactor as jest.Mock;
const setupDeviceSecurityMock = SecurityService.setupDeviceSecurity as jest.Mock;

const REMEMBERED = '+380991234567';

/**
 * The store rehydrates from AsyncStorage after the module loads, so a test that wants to
 * start from a known state has to wait for that to finish - otherwise the hydration writes
 * over whatever the test set.
 */
async function waitForHydration() {
  if (useStore.persist.hasHydrated()) return;
  await new Promise<void>((resolve) => {
    const unsubscribe = useStore.persist.onFinishHydration(() => {
      unsubscribe();
      resolve();
    });
  });
}

function renderLogin(options?: { initialPhone?: string; initialCode?: string }) {
  const queryClient = new QueryClient();
  const wrapper = ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  return renderHook(() => useLogin(() => {}, options), { wrapper });
}

describe('useLogin', () => {
  beforeEach(async () => {
    sendVerificationCodeMock.mockReset();
    sendVerificationCodeMock.mockResolvedValue(undefined);
    await waitForHydration();
    await act(async () => {
      useStore.setState({ lastPhone: null });
    });
  });

  afterEach(async () => {
    await act(async () => {
      useStore.setState({ lastPhone: null });
    });
  });

  it('fills the form in with the remembered number', async () => {
    await act(async () => {
      useStore.setState({ lastPhone: REMEMBERED });
    });

    const { result } = renderLogin();

    // The store rehydrates asynchronously, so the number lands a tick after the mount.
    await waitFor(() => expect(result.current.phone).toBe(REMEMBERED));
  });

  it('prefers the number the push carried over the remembered one', async () => {
    await act(async () => {
      useStore.setState({ lastPhone: REMEMBERED });
    });

    const { result } = renderLogin({ initialPhone: '+380501234567' });

    await waitFor(() => expect(result.current.phone).toBe('+380501234567'));
  });

  it('never overwrites a number the person typed', async () => {
    await act(async () => {
      useStore.setState({ lastPhone: REMEMBERED });
    });

    const { result } = renderLogin();

    act(() => {
      result.current.setPhone('+380501234567');
    });

    await waitFor(() => expect(result.current.phone).toBe('+380501234567'));
  });

  it('remembers the number once a code was sent', async () => {
    const { result } = renderLogin();

    act(() => {
      result.current.setPhone(REMEMBERED);
    });
    await act(async () => {
      await result.current.handleSendCode();
    });

    expect(sendVerificationCodeMock).toHaveBeenCalledWith(REMEMBERED);
    expect(useStore.getState().lastPhone).toBe(REMEMBERED);
  });

  it('does not remember a number whose code was not sent', async () => {
    sendVerificationCodeMock.mockRejectedValue(
      Object.assign(new Error('rate limited'), { status: 429 }),
    );

    const { result } = renderLogin();

    act(() => {
      result.current.setPhone(REMEMBERED);
    });
    await act(async () => {
      await result.current.handleSendCode();
    });

    expect(useStore.getState().lastPhone).toBeNull();
  });

  /*
   * The first factor had the same defect: a refused push or SMS code was reported as
   * "could not confirm the device", naming the device instead of the digits the person
   * typed. The step has to name the code.
   */
  describe('the first factor', () => {
    const devFlag = (global as any).__DEV__;

    beforeEach(() => {
      (global as any).__DEV__ = false;
      verifyPhoneCodeMock.mockReset();
    });

    afterEach(() => {
      (global as any).__DEV__ = devFlag;
    });

    it('says the code is wrong instead of blaming the device', async () => {
      verifyPhoneCodeMock.mockRejectedValue(
        Object.assign(new Error('Invalid or expired verification code'), { status: 401 }),
      );

      const { result } = renderLogin();

      act(() => {
        result.current.setPhone(REMEMBERED);
      });
      act(() => {
        result.current.setCode('123456');
      });
      await act(async () => {
        await result.current.handleVerifyCode();
      });

      expect(result.current.error).toBe('phoneAuth.verifyFailed');
      expect(result.current.error).not.toBe('phoneAuth.deviceVerifyFailed');
    });

    it('keeps the rate-limit message for a throttled first factor', async () => {
      verifyPhoneCodeMock.mockRejectedValue(
        Object.assign(new Error('RATE_LIMITED'), { status: 429 }),
      );

      const { result } = renderLogin();

      act(() => {
        result.current.setPhone(REMEMBERED);
      });
      act(() => {
        result.current.setCode('123456');
      });
      await act(async () => {
        await result.current.handleVerifyCode();
      });

      expect(result.current.error).toBe('phoneAuth.tooManyAttempts');
    });
  });

  /*
   * The second factor has its own mistakes, and they used to be reported as "could not
   * confirm the device" - which pointed at the device instead of the code the person had
   * just typed, and pushed people to retype the push code the first factor had already
   * accepted. The message has to name the authenticator code instead.
   *
   * A dev build shows the diagnostic trail instead of the message (that is what the
   * __DEV__ branch in the hook is for), so the production wording is only reachable with
   * the flag off - which is what a release build does.
   */
  describe('the second factor', () => {
    const devFlag = (global as any).__DEV__;

    beforeEach(() => {
      (global as any).__DEV__ = false;
      verifyPhoneCodeMock.mockReset();
      verifyPhoneCodeMock.mockResolvedValue({
        accessToken: null,
        refreshToken: null,
        twoFactor: { status: 'required', challenge: 'challenge-1' },
      });
      verifyTwoFactorMock.mockReset();
      setupDeviceSecurityMock.mockReset();
    });

    afterEach(() => {
      (global as any).__DEV__ = devFlag;
    });

    async function reachTwoFactorStep(result: { current: ReturnType<typeof useLogin> }) {
      act(() => {
        result.current.setPhone(REMEMBERED);
      });
      await act(async () => {
        await result.current.handleSendCode();
      });
      act(() => {
        result.current.setCode('123456');
      });
      await act(async () => {
        await result.current.handleVerifyCode();
      });
      expect(result.current.step).toBe('two_factor');
    }

    it('hands the second factor an idle screen, not the first factor’s spinner', async () => {
      const { result } = renderLogin();
      await reachTwoFactorStep(result);

      // The step is only worth showing if it can be used: a field disabled by a leftover
      // `loading` and a spinner where Verify should be would be a screen nobody can type
      // into, and nothing would ever reach /two-factor/verify. The request that carried the
      // person here has finished by now, so its spinner must be gone with it.
      expect(result.current.loading).toBe(false);
    });

    it('says the authenticator code is wrong instead of blaming the device', async () => {
      verifyTwoFactorMock.mockRejectedValue(
        Object.assign(new Error('Invalid or expired verification code'), { status: 401 }),
      );

      const { result } = renderLogin();
      await reachTwoFactorStep(result);

      act(() => {
        result.current.setTwoFactorCode('654321');
      });
      await act(async () => {
        await result.current.handleVerifyTwoFactor();
      });

      expect(result.current.error).toBe('phoneAuth.twoFactorCodeRejected');
      expect(result.current.error).not.toBe('phoneAuth.deviceVerifyFailed');
    });

    it('keeps the rate-limit message for a throttled second factor', async () => {
      verifyTwoFactorMock.mockRejectedValue(
        Object.assign(new Error('RATE_LIMITED'), { status: 429 }),
      );

      const { result } = renderLogin();
      await reachTwoFactorStep(result);

      act(() => {
        result.current.setTwoFactorCode('654321');
      });
      await act(async () => {
        await result.current.handleVerifyTwoFactor();
      });

      expect(result.current.error).toBe('phoneAuth.tooManyAttempts');
    });

    it('does not blame the code when the failure is not about the code', async () => {
      verifyTwoFactorMock.mockRejectedValue(new Error('Network request failed'));

      const { result } = renderLogin();
      await reachTwoFactorStep(result);

      act(() => {
        result.current.setTwoFactorCode('654321');
      });
      await act(async () => {
        await result.current.handleVerifyTwoFactor();
      });

      expect(result.current.error).toBe('phoneAuth.networkError');
      expect(result.current.error).not.toBe('phoneAuth.twoFactorCodeRejected');
    });

    it('blames the device when the device fails after the code was accepted', async () => {
      verifyTwoFactorMock.mockResolvedValue({
        accessToken: 'access-token',
        refreshToken: 'refresh-token',
      });
      setupDeviceSecurityMock.mockRejectedValue(
        new DeviceSecurityError('no biometrics', 'DEVICE_AUTH_UNAVAILABLE'),
      );

      const { result } = renderLogin();
      await reachTwoFactorStep(result);

      act(() => {
        result.current.setTwoFactorCode('654321');
      });
      await act(async () => {
        await result.current.handleVerifyTwoFactor();
      });

      expect(result.current.error).toBe('phoneAuth.biometricsRequired');
      expect(result.current.error).not.toBe('phoneAuth.twoFactorCodeRejected');
    });
  });
});
