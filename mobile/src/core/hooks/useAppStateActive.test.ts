import { renderHook, act } from '@testing-library/react-native';
import { AppState, type AppStateStatus } from 'react-native';
import { useAppStateActive } from './useAppStateActive';

describe('useAppStateActive', () => {
  it('starts active, tracks transitions, and unsubscribes on unmount', () => {
    let emit: ((state: AppStateStatus) => void) | undefined;
    const remove = jest.fn();
    const addEventListener = jest
      .spyOn(AppState, 'addEventListener')
      .mockImplementation((_event, handler) => {
        emit = handler as (state: AppStateStatus) => void;
        return { remove } as unknown as ReturnType<typeof AppState.addEventListener>;
      });

    const { result, unmount } = renderHook(() => useAppStateActive());

    // Subscribes once to the AppState 'change' event and starts foregrounded.
    expect(addEventListener).toHaveBeenCalledWith('change', expect.any(Function));
    expect(result.current).toBe(true);

    // Backgrounding (or the transient 'inactive') pauses; only 'active' resumes.
    act(() => emit?.('background'));
    expect(result.current).toBe(false);

    act(() => emit?.('inactive'));
    expect(result.current).toBe(false);

    act(() => emit?.('active'));
    expect(result.current).toBe(true);

    // The subscription is torn down exactly once when the consumer unmounts.
    unmount();
    expect(remove).toHaveBeenCalledTimes(1);
  });
});
