import { renderHook, act, waitFor } from '@testing-library/react-native';
import { AppState, type AppStateStatus } from 'react-native';

import { getFeedbackDeepLink } from '../api/feedback';
import { useFeedbackChannel } from './useFeedbackChannel';

jest.mock('../api/feedback', () => ({ getFeedbackDeepLink: jest.fn() }));

const deepLinkMock = getFeedbackDeepLink as jest.MockedFunction<typeof getFeedbackDeepLink>;

/** Captures the AppState handler the hook registers, so a foreground transition can be emitted. */
function captureAppStateListener() {
  let emit: ((state: AppStateStatus) => void) | undefined;
  const remove = jest.fn();

  jest.spyOn(AppState, 'addEventListener').mockImplementation((_event, handler) => {
    emit = handler as (state: AppStateStatus) => void;
    return { remove } as unknown as ReturnType<typeof AppState.addEventListener>;
  });

  return {
    emit: (state: AppStateStatus) => emit?.(state),
    remove,
  };
}

describe('useFeedbackChannel', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
    deepLinkMock.mockReset();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('is unavailable until the server has answered, then follows the answer', async () => {
    // A channel that is off resolves to null rather than throwing, so the tab disappears.
    deepLinkMock.mockResolvedValue(null);

    const { result } = renderHook(() => useFeedbackChannel());

    // Nothing is claimed before the request lands: showing or hiding the tab on a guess is the
    // one behaviour that must not happen.
    expect(result.current.available).toBe(false);

    await waitFor(() => expect(deepLinkMock).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(result.current.available).toBe(false));
  });

  it('becomes available with the signed link when the server confirms the channel', async () => {
    deepLinkMock.mockResolvedValue({ url: 'https://t.me/palne_feedback_bot?start=payload' });

    const { result } = renderHook(() => useFeedbackChannel());

    await waitFor(() =>
      expect(result.current).toEqual({
        available: true,
        deepLink: 'https://t.me/palne_feedback_bot?start=payload',
      }),
    );
  });

  it('stays unavailable when the request fails, rather than flashing the tab', async () => {
    deepLinkMock.mockRejectedValue(new Error('offline'));

    const { result } = renderHook(() => useFeedbackChannel());

    await waitFor(() => expect(result.current.deepLink).toBeNull());
    expect(result.current.available).toBe(false);
  });

  it('re-checks on foreground so a channel enabled later appears without a relaunch', async () => {
    // The regression this exists for: an operator switching the channel on mid-session used to
    // leave the tab missing until the user killed and relaunched the app, which reads as "the
    // feature is broken" to whoever is testing it.
    jest.useFakeTimers();
    const appState = captureAppStateListener();
    deepLinkMock
      .mockResolvedValueOnce(null)
      .mockResolvedValue({ url: 'https://t.me/palne_feedback_bot?start=payload' });

    const { result } = renderHook(() => useFeedbackChannel());
    await act(async () => {});

    expect(result.current.available).toBe(false);

    // Past the throttle window, so the next foreground is allowed to re-check.
    jest.advanceTimersByTime(61_000);
    await act(async () => {
      appState.emit('active');
    });

    expect(deepLinkMock).toHaveBeenCalledTimes(2);
    expect(result.current).toEqual({
      available: true,
      deepLink: 'https://t.me/palne_feedback_bot?start=payload',
    });
  });

  it('does not re-request on rapid foreground switches', async () => {
    jest.useFakeTimers();
    const appState = captureAppStateListener();
    deepLinkMock.mockResolvedValue(null);

    renderHook(() => useFeedbackChannel());
    await act(async () => {});

    expect(deepLinkMock).toHaveBeenCalledTimes(1);

    act(() => {
      appState.emit('background');
      appState.emit('active');
      appState.emit('inactive');
      appState.emit('active');
    });

    // Only 'active' counts, and only after the throttle window - so a user alt-tabbing does not
    // fire a request per switch.
    expect(deepLinkMock).toHaveBeenCalledTimes(1);
  });

  it('unsubscribes from AppState on unmount', async () => {
    jest.useFakeTimers();
    const appState = captureAppStateListener();
    deepLinkMock.mockResolvedValue(null);

    const { unmount } = renderHook(() => useFeedbackChannel());
    await act(async () => {});

    unmount();
    expect(appState.remove).toHaveBeenCalledTimes(1);
  });
});