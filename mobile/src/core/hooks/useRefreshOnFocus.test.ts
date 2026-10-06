import { renderHook, act, waitFor } from '@testing-library/react-native';

import { useRefreshOnFocus } from './useRefreshOnFocus';

// expo-router owns focus. Capture the effect callback so a test can fire focus the way the
// navigator would, instead of asserting on an implementation detail.
let focusCallback: (() => void) | null = null;

jest.mock('expo-router', () => ({
  useFocusEffect: (cb: () => void) => {
    focusCallback = cb;
  },
}));

function fireFocus() {
  act(() => {
    focusCallback?.();
  });
}

describe('useRefreshOnFocus', () => {
  beforeEach(() => {
    focusCallback = null;
  });

  it('refreshes when the screen regains focus', async () => {
    const refresh = jest.fn().mockResolvedValue(undefined);
    renderHook(() => useRefreshOnFocus(refresh));

    fireFocus();
    await act(async () => {
      await Promise.resolve();
    });

    fireFocus();

    expect(refresh).toHaveBeenCalledTimes(2);
  });

  it('does nothing while disabled', () => {
    const refresh = jest.fn().mockResolvedValue(undefined);
    renderHook(() => useRefreshOnFocus(refresh, false));

    fireFocus();

    expect(refresh).not.toHaveBeenCalled();
  });

  it('keeps at most one request in flight, so tapping between tabs cannot stack them', async () => {
    // A burst racing apiFetch's token-refresh single-flight is the shape of the old #26
    // spurious logouts.
    let release: (() => void) | null = null;
    const refresh = jest.fn(
      () =>
        new Promise<void>((resolve) => {
          release = resolve;
        }),
    );

    renderHook(() => useRefreshOnFocus(refresh));

    fireFocus();
    fireFocus();
    fireFocus();
    expect(refresh).toHaveBeenCalledTimes(1);

    await act(async () => {
      release?.();
    });

    fireFocus();
    expect(refresh).toHaveBeenCalledTimes(2);
  });

  it('does not refetch in a loop when the caller passes a new callback every render', async () => {
    // Every caller passes an inline arrow. If the focus effect keyed on that identity, each render
    // would re-fire it while the screen is focused.
    const refresh = jest.fn().mockResolvedValue(undefined);

    const { rerender } = renderHook(
      ({ tick }: { tick: number }) => {
        void tick;
        useRefreshOnFocus(refresh);
        return null;
      },
      { initialProps: { tick: 0 } },
    );

    fireFocus();
    await act(async () => {
      await Promise.resolve();
    });
    expect(refresh).toHaveBeenCalledTimes(1);

    await act(async () => {
      rerender({ tick: 1 });
      rerender({ tick: 2 });
      await Promise.resolve();
    });

    expect(refresh).toHaveBeenCalledTimes(1);
  });
  it('clears the in-flight guard even when the refresh rejects, so focus works again', async () => {
    const refresh = jest
      .fn()
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue(undefined);

    renderHook(() => useRefreshOnFocus(refresh));

    fireFocus();
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));

    await act(async () => {
      await Promise.resolve();
    });

    fireFocus();
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(2));
  });
});
