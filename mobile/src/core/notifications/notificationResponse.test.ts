import { renderHook, act, waitFor } from '@testing-library/react-native';
import * as Notifications from 'expo-notifications';
import { useNotificationTapRouting } from './notificationResponse';
import { useStore } from '../state/appStore';

// The native module cannot load under Jest, so mock the SDK surface we call.
// Mirrors the mocking style of push.test.ts.
jest.mock('expo-notifications', () => ({
  setNotificationHandler: jest.fn(),
  getLastNotificationResponseAsync: jest.fn(),
  addNotificationResponseReceivedListener: jest.fn(),
}));

// Router is the navigation boundary; assert on push() rather than driving a real
// navigation tree.
const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  useRouter: () => ({ push: mockPush }),
}));

const setHandler = Notifications.setNotificationHandler as jest.Mock;
const getLastResponse = Notifications.getLastNotificationResponseAsync as jest.Mock;
const addResponseListener = Notifications.addNotificationResponseReceivedListener as jest.Mock;

// Only the shape the hook touches: the tap handler reads
// `notification.request.content.data` to decide where to route, and the notification's own
// `date` when a sign-in code needs its age measured.
function fakeResponse(data: Record<string, unknown> = {}, date?: number) {
  return { notification: { date, request: { content: { data } } } };
}

describe('useNotificationTapRouting', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    getLastResponse.mockResolvedValue(null);
    addResponseListener.mockReturnValue({ remove: jest.fn() });
  });

  it('configures the foreground notification handler on mount', () => {
    renderHook(() => useNotificationTapRouting());

    expect(setHandler).toHaveBeenCalledTimes(1);
  });

  it('routes to the notifications list when a tap wakes a running app', async () => {
    let captured: ((response: unknown) => void) | undefined;
    addResponseListener.mockImplementation((listener: (r: unknown) => void) => {
      captured = listener;
      return { remove: jest.fn() };
    });

    renderHook(() => useNotificationTapRouting());
    // Let the cold-start check (resolves null here) settle first.
    await waitFor(() => expect(getLastResponse).toHaveBeenCalled());

    act(() => {
      captured?.(fakeResponse());
    });

    expect(mockPush).toHaveBeenCalledWith('/notifications');
  });

  it('replays a cold-start launch tap once, routing to the list', async () => {
    getLastResponse.mockResolvedValue(fakeResponse());

    renderHook(() => useNotificationTapRouting());

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/notifications'));
    expect(mockPush).toHaveBeenCalledTimes(1);
  });

  it('deep-links a tapped order-fulfilled push to that order in the wallet', async () => {
    let captured: ((response: unknown) => void) | undefined;
    addResponseListener.mockImplementation((listener: (r: unknown) => void) => {
      captured = listener;
      return { remove: jest.fn() };
    });

    renderHook(() => useNotificationTapRouting());
    await waitFor(() => expect(getLastResponse).toHaveBeenCalled());

    act(() => {
      captured?.(fakeResponse({ type: 'order_fulfilled', orderId: 'order-123' }));
    });

    expect(mockPush).toHaveBeenCalledWith({
      pathname: '/my-codes',
      params: { orderId: 'order-123' },
    });
  });

  it('deep-links a cold-start order-fulfilled launch tap to that order', async () => {
    getLastResponse.mockResolvedValue(
      fakeResponse({ type: 'order_fulfilled', orderId: 'order-777' }),
    );

    renderHook(() => useNotificationTapRouting());

    await waitFor(() =>
      expect(mockPush).toHaveBeenCalledWith({
        pathname: '/my-codes',
        params: { orderId: 'order-777' },
      }),
    );
    expect(mockPush).toHaveBeenCalledTimes(1);
  });

  // #150 W3. A worker belongs to several companies now, so a push that only opened the wallet
  // would show whichever context the app happened to have open. The company in the payload has to
  // become the active context BEFORE navigating.
  it('switches into the issuing company before opening the wallet on a voucher-issued push', async () => {
    let captured: ((response: unknown) => void) | undefined;
    addResponseListener.mockImplementation((listener: (r: unknown) => void) => {
      captured = listener;
      return { remove: jest.fn() };
    });

    useStore.setState({ currentLegalEntityId: 'some-other-company' });
    renderHook(() => useNotificationTapRouting());
    await waitFor(() => expect(getLastResponse).toHaveBeenCalled());

    act(() => {
      captured?.(fakeResponse({ type: 'voucher_issued', legalEntityId: 'company-abc' }));
    });

    expect(useStore.getState().currentLegalEntityId).toBe('company-abc');
    expect(mockPush).toHaveBeenCalledWith('/my-codes');
  });

  it('leaves the context alone when a voucher-issued push has no company', async () => {
    let captured: ((response: unknown) => void) | undefined;
    addResponseListener.mockImplementation((listener: (r: unknown) => void) => {
      captured = listener;
      return { remove: jest.fn() };
    });

    useStore.setState({ currentLegalEntityId: 'keep-me' });
    renderHook(() => useNotificationTapRouting());
    await waitFor(() => expect(getLastResponse).toHaveBeenCalled());

    act(() => {
      captured?.(fakeResponse({ type: 'voucher_issued' }));
    });

    // A malformed payload must not wipe the worker's context - it falls back to the list.
    expect(useStore.getState().currentLegalEntityId).toBe('keep-me');
    expect(mockPush).toHaveBeenCalledWith('/notifications');
  });

  it('opens the sign-in form with the code a push carried', async () => {
    let captured: ((response: unknown) => void) | undefined;
    addResponseListener.mockImplementation((listener: (r: unknown) => void) => {
      captured = listener;
      return { remove: jest.fn() };
    });

    renderHook(() => useNotificationTapRouting());
    await waitFor(() => expect(getLastResponse).toHaveBeenCalled());

    act(() => {
      captured?.(fakeResponse({ type: 'auth_otp', code: '123456', phone: '+380991234567' }));
    });

    expect(mockPush).toHaveBeenCalledWith({
      pathname: '/landing',
      params: { otpPhone: '+380991234567', otpCode: '123456' },
    });
  });

  /*
   * A banner can sit in Notification Center for an hour, and the digits it carries live five
   * minutes. The push publishes that lifetime and the notification knows when it was delivered,
   * so the tap can hand the form a deadline instead of letting it submit a code that is
   * already dead. Both values come from this device, so the clock skew between phone and
   * server never enters the subtraction.
   */
  it('hands the form a deadline that a late tap will find already passed', async () => {
    let captured: ((response: unknown) => void) | undefined;
    addResponseListener.mockImplementation((listener: (r: unknown) => void) => {
      captured = listener;
      return { remove: jest.fn() };
    });

    const deliveredAt = 1_700_000_000_000;
    renderHook(() => useNotificationTapRouting());
    await waitFor(() => expect(getLastResponse).toHaveBeenCalled());

    act(() => {
      captured?.(
        fakeResponse(
          { type: 'auth_otp', code: '123456', phone: '+380991234567', ttlSeconds: 300 },
          deliveredAt,
        ),
      );
    });

    expect(mockPush).toHaveBeenCalledWith({
      pathname: '/landing',
      params: {
        otpPhone: '+380991234567',
        otpCode: '123456',
        otpExpiresAt: String(deliveredAt + 300_000),
      },
    });
  });

  it('still opens the form when the push carries no lifetime, as an older server sends', async () => {
    // A server older than the deadline publishes no ttlSeconds. The tap must still sign in -
    // without a deadline the form simply shows no countdown, which is what every client did
    // before this change.
    let captured: ((response: unknown) => void) | undefined;
    addResponseListener.mockImplementation((listener: (r: unknown) => void) => {
      captured = listener;
      return { remove: jest.fn() };
    });

    renderHook(() => useNotificationTapRouting());
    await waitFor(() => expect(getLastResponse).toHaveBeenCalled());

    act(() => {
      captured?.(
        fakeResponse(
          { type: 'auth_otp', code: '123456', phone: '+380991234567' },
          1_700_000_000_000,
        ),
      );
    });

    expect(mockPush).toHaveBeenCalledWith({
      pathname: '/landing',
      params: { otpPhone: '+380991234567', otpCode: '123456' },
    });
  });

  it('falls back to the list when an otp push carries no code', async () => {
    getLastResponse.mockResolvedValue(fakeResponse({ type: 'auth_otp', phone: '+380991234567' }));

    renderHook(() => useNotificationTapRouting());

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/notifications'));
  });

  it('falls back to the list when an order-fulfilled push carries no orderId', async () => {
    getLastResponse.mockResolvedValue(fakeResponse({ type: 'order_fulfilled' }));

    renderHook(() => useNotificationTapRouting());

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith('/notifications'));
  });

  it('does not navigate when the app was not launched from a tap', async () => {
    getLastResponse.mockResolvedValue(null);

    renderHook(() => useNotificationTapRouting());
    await waitFor(() => expect(getLastResponse).toHaveBeenCalled());

    expect(mockPush).not.toHaveBeenCalled();
  });

  it('removes the response subscription on unmount', () => {
    const remove = jest.fn();
    addResponseListener.mockReturnValue({ remove });

    const { unmount } = renderHook(() => useNotificationTapRouting());
    unmount();

    expect(remove).toHaveBeenCalledTimes(1);
  });
});
