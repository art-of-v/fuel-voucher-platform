import React from 'react';
import { Text } from 'react-native';
import { render, screen, waitFor } from '@testing-library/react-native';

import { AuthSync, AppLockGuard } from './_layout';

/**
 * The app's front door.
 *
 * This is the screen that matters most and is the least covered: a tab-bar change
 * once shipped with a green suite while the app rendered no layout at all, because
 * nothing here was under test. Both guards have a failure mode that locks a paying
 * customer out, or shows the app to someone signed out.
 *
 * `AuthSync` reconciles two sources of truth — the auth query and the store — and
 * redirects when neither says the user is signed in. The dangerous cases are the
 * *near misses*: redirecting while a fetch is in flight, or on a network error, would
 * bounce a signed-in customer to the landing screen mid-session.
 *
 * `AppLockGuard` re-checks the session behind the biometric lock. It takes two
 * different code paths depending on whether the device holds keys, and only one of
 * them forces the signature header — a mix-up there either locks people out or lets a
 * stale session through.
 *
 * Both are exported rather than reached through the root layout, which would mean
 * mocking fonts, splash, the version check and the gesture handler — mocks that could
 * hide real behaviour. Exporting is the only production change here and it changes
 * nothing at runtime.
 */

const mockLogin = jest.fn();
const mockLogout = jest.fn();
const mockUnlockApp = jest.fn();
const mockReplace = jest.fn();
const mockPush = jest.fn();
const mockHasKeys = jest.fn();
const mockApiFetch = jest.fn();

let mockAuth: Record<string, unknown> = {};
let mockStore: Record<string, unknown> = {};
let mockPathname = '/my-codes';

jest.mock('expo-router', () => ({
  Stack: { Screen: () => null },
  Tabs: { Screen: () => null },
  useRouter: () => ({ replace: mockReplace, push: jest.fn(), back: jest.fn() }),
  usePathname: () => mockPathname,
  useLocalSearchParams: () => ({}),
  useGlobalSearchParams: () => ({}),
  useSegments: () => [],
  useFocusEffect: () => {},
  useIsFocused: () => true,
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = { t: (key: string) => key, language: 'uk', setLanguage: jest.fn() };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/features/auth/hooks/useAuth', () => ({ useAuth: () => mockAuth }));

jest.mock('../src/core/state/appStore', () => ({
  useStore: Object.assign(
    (selector?: (s: any) => unknown) => {
      const state = {
        isAuthenticated: false,
        isAppUnlocked: false,
        login: mockLogin,
        logout: mockLogout,
        unlockApp: mockUnlockApp,
        ...mockStore,
      };
      return selector ? selector(state) : state;
    },
    { getState: () => ({ logout: mockLogout }) },
  ),
}));

jest.mock('../src/core/notifications/push', () => ({
  registerForPushNotifications: () => mockPush(),
}));

jest.mock('../src/core/notifications/notificationResponse', () => ({
  useNotificationTapRouting: () => {},
}));

jest.mock('../src/core/api/securityService', () => ({
  SecurityService: { hasKeys: () => mockHasKeys() },
}));

jest.mock('../src/core/api/apiClient', () => ({
  apiFetch: (...a: unknown[]) => mockApiFetch(...a),
}));

describe('AuthSync — reconciling the auth query with the store', () => {
  /** Settles the query: fetched, idle, no error — the only state that acts. */
  function auth(over: Record<string, unknown> = {}) {
    mockAuth = {
      isAuthenticated: false,
      isLoading: false,
      isFetching: false,
      isFetched: true,
      isError: false,
      user: null,
      ...over,
    };
    mockStore = {};
    mockPathname = '/my-codes';
    mockLogin.mockClear();
    mockLogout.mockClear();
    mockReplace.mockClear();
  }

  it('signs the store in when the query says authenticated but the store does not', async () => {
    auth({ isAuthenticated: true });
    render(<AuthSync />);

    await waitFor(() => expect(mockLogin).toHaveBeenCalled());
    expect(mockLogout).not.toHaveBeenCalled();
  });

  it('signs the store out when the store says authenticated but the query does not', async () => {
    // A stale session in the store with no live one behind it must not survive.
    auth({ isAuthenticated: false });
    mockStore = { isAuthenticated: true };
    render(<AuthSync />);

    await waitFor(() => expect(mockLogout).toHaveBeenCalled());
  });

  it('sends a signed-out user to the landing screen', async () => {
    auth({ isAuthenticated: false });
    render(<AuthSync />);

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/landing'));
  });

  it('leaves the landing screen alone', async () => {
    // Redirecting from /landing to /landing would be a loop.
    auth({ isAuthenticated: false });
    mockPathname = '/landing';
    render(<AuthSync />);

    await waitFor(() => expect(mockAuth.isFetched).toBe(true));
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('does not redirect while a fetch is still in flight', async () => {
    // The dangerous near-miss: a refetch on app foreground would bounce a signed-in
    // customer to the landing screen mid-session.
    auth({ isAuthenticated: false, isFetching: true });
    render(<AuthSync />);

    await waitFor(() => expect(mockAuth.isFetching).toBe(true));
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('does not redirect when the auth query errored', async () => {
    // A network blip is not a logout. Redirecting here would sign people out
    // whenever their connection hiccuped.
    auth({ isAuthenticated: false, isError: true });
    render(<AuthSync />);

    await waitFor(() => expect(mockAuth.isError).toBe(true));
    expect(mockReplace).not.toHaveBeenCalled();
    expect(mockLogout).not.toHaveBeenCalled();
  });

  it('does nothing at all while the query is still loading', async () => {
    auth({ isLoading: true, isFetched: false });
    render(<AuthSync />);

    await waitFor(() => expect(mockAuth.isLoading).toBe(true));
    expect(mockLogin).not.toHaveBeenCalled();
    expect(mockLogout).not.toHaveBeenCalled();
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('registers for push notifications once authenticated', async () => {
    auth({ isAuthenticated: true });
    render(<AuthSync />);

    await waitFor(() => expect(mockPush).toHaveBeenCalled());
  });

  it('does not register for push while signed out', async () => {
    auth({ isAuthenticated: false });
    render(<AuthSync />);

    await waitFor(() => expect(mockAuth.isFetched).toBe(true));
    expect(mockPush).not.toHaveBeenCalled();
  });
});

describe('AppLockGuard — the biometric lock', () => {
  /**
   * The lock screen reads colours and spacing straight off `tokens`, so an empty
   * object fails at render rather than at an assertion — and the resulting error
   * ("can't access .root on unmounted test renderer") says nothing about the guard.
   */
  const tokens = {
    colors: { background: '#000', primary: '#0f0', foreground: '#fff' },
    spacing: { containerPadding: 16, '3xl': 24, sm: 4 },
  };

  function lock(over: Record<string, unknown> = {}) {
    mockStore = { isAuthenticated: true, isAppUnlocked: false, ...over };
    mockPathname = '/my-codes';
    mockUnlockApp.mockClear();
    mockLogout.mockClear();
    mockReplace.mockClear();
    mockHasKeys.mockClear();
    mockApiFetch.mockClear();
    mockApiFetch.mockResolvedValue({ ok: true, status: 200 });
  }

  it('re-checks the session and unlocks when the device holds keys', async () => {
    lock();
    mockHasKeys.mockResolvedValue(true);
    render(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);

    await waitFor(() => expect(mockUnlockApp).toHaveBeenCalled());
    // The signed request is what proves the session is still the user's own.
    expect(mockApiFetch).toHaveBeenCalledWith('/api/auth/user/me', {
      headers: { 'x-force-signature': 'true' },
    });
  });

  it('unlocks without forcing a signature when the device holds no keys', async () => {
    lock();
    mockHasKeys.mockResolvedValue(false);
    render(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);

    await waitFor(() => expect(mockUnlockApp).toHaveBeenCalled());
    expect(mockApiFetch).toHaveBeenCalledWith('/api/auth/user/me');
  });

  it('signs the user out when the signed re-check returns 401', async () => {
    lock();
    mockHasKeys.mockResolvedValue(true);
    mockApiFetch.mockResolvedValue({ ok: false, status: 401 });
    render(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);

    await waitFor(() => expect(mockLogout).toHaveBeenCalled());
    expect(mockUnlockApp).not.toHaveBeenCalled();
  });

  it('signs the user out when an unsigned device gets a non-ok response', async () => {
    lock();
    mockHasKeys.mockResolvedValue(false);
    mockApiFetch.mockResolvedValue({ ok: false, status: 500 });
    render(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);

    await waitFor(() => expect(mockLogout).toHaveBeenCalled());
    expect(mockUnlockApp).not.toHaveBeenCalled();
  });

  it('sends the user to the landing screen when the identity is missing', async () => {
    // IDENTITY_MISSING means the biometric identity was removed — the session cannot
    // be recovered, and leaving them on a lock screen that can never pass is worse
    // than a clean sign-out.
    lock();
    mockHasKeys.mockResolvedValue(true);
    mockApiFetch.mockRejectedValue(new Error('IDENTITY_MISSING'));
    render(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/landing'));
    expect(mockLogout).toHaveBeenCalled();
  });

  it('shows the lock screen instead of the children while locked', async () => {
    lock();
    mockHasKeys.mockResolvedValue(true);
    render(
      <AppLockGuard tokens={tokens}>
        <Text testID="app-child">app</Text>
      </AppLockGuard>,
    );

    // Let the re-check settle: it flips `isPrompting`, and asserting mid-flight
    // would read a state update that never happened under a real unlock.
    await waitFor(() => expect(mockUnlockApp).toHaveBeenCalled());

    // The gate must not let the app render behind it.
    expect(screen.getByText('appLock.title')).toBeTruthy();
    expect(screen.queryByTestId('app-child')).toBeNull();
  });

  it('renders the children without prompting once unlocked', async () => {
    lock({ isAppUnlocked: true });
    render(
      <AppLockGuard tokens={tokens}>
        <Text testID="app-child">app</Text>
      </AppLockGuard>,
    );

    expect(mockApiFetch).not.toHaveBeenCalled();
    expect(screen.getByTestId('app-child')).toBeTruthy();
    expect(screen.queryByText('appLock.title')).toBeNull();
  });

  it('does not prompt while signed out', async () => {
    lock({ isAuthenticated: false });
    mockHasKeys.mockResolvedValue(true);
    render(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);

    expect(mockApiFetch).not.toHaveBeenCalled();
  });

  it('does not start a second biometric while the first is still running', async () => {
    // Two concurrent prompts make the user dismiss a system dialog that reappears,
    // which reads as a broken lock screen and can strand people outside the app.
    lock();
    let releaseKeys: (v: boolean) => void = () => {};
    mockHasKeys.mockImplementation(
      () =>
        new Promise<boolean>((r) => {
          releaseKeys = r;
        }),
    );

    const { rerender } = render(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);

    // Walk the effect's own dependencies so it genuinely re-runs while the first
    // prompt is still in flight: onto /landing, then back.
    mockPathname = '/landing';
    rerender(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);
    mockPathname = '/my-codes';
    rerender(<AppLockGuard tokens={tokens}>{null}</AppLockGuard>);

    releaseKeys(true);
    await waitFor(() => expect(mockApiFetch).toHaveBeenCalledTimes(1));
    expect(mockHasKeys).toHaveBeenCalledTimes(1);
  });

  it('does not gate the landing screen even when signed in and locked', async () => {
    // /landing is the front door for someone with no session; gating it would strand
    // a signed-in user behind a lock screen with no way past.
    lock();
    mockPathname = '/landing';
    mockHasKeys.mockResolvedValue(true);

    render(
      <AppLockGuard tokens={tokens}>
        <Text testID="app-child">app</Text>
      </AppLockGuard>,
    );

    expect(screen.getByTestId('app-child')).toBeTruthy();
    expect(screen.queryByText('appLock.title')).toBeNull();
    expect(mockApiFetch).not.toHaveBeenCalled();
  });
});
