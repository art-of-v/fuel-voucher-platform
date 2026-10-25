import { renderHook, act, waitFor } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useLogin } from './useLogin';
import { sendVerificationCode } from '../api/sendCode';
import { useStore } from '../../../core/state/appStore';

// The network boundary is mocked: the sign-in flow is what is under test, not the transport.
jest.mock('../api/sendCode', () => ({
  sendVerificationCode: jest.fn(),
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
  DeviceSecurityError: class DeviceSecurityError extends Error {},
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
});
