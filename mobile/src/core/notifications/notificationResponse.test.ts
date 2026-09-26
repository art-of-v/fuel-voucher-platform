import { renderHook, act, waitFor } from '@testing-library/react-native';
import * as Notifications from 'expo-notifications';
import { useNotificationTapRouting } from './notificationResponse';

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

// Only the shape the hook touches. The tap target is fixed today, so nothing is
// read off it yet — but keep a realistic-ish object for a future data-based branch.
function fakeResponse() {
  return { notification: { request: { content: { data: {} } } } };
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
