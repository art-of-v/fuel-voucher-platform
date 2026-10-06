import React from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react-native';

import RenewSelectScreen from './renew-select';

/**
 * The multi-select entry to voucher renewal, which had no tests.
 *
 * It lists every renewable voucher, pre-selects them all, and hands a `voucherIds`
 * CSV to the `/renew` screen. The things that matter:
 *
 * - eligibility is the shared predicate's call, not this screen's: a voucher it
 *   rejects must never be offered or sent on
 * - the batch is pre-selected once, so a background refresh cannot wipe the user's
 *   edits
 * - "continue" is disabled with nothing selected and carries exactly the chosen ids
 * - the feature flag off, and an empty candidate list, each show their own empty
 *   state rather than a dead screen
 */

const mockPush = jest.fn();
const mockGetConfig = jest.fn();
const mockIsRenewable = jest.fn();

let mockVouchers: any[] = [];
let mockUser: any = { id: 'u-1' };
let mockLoading = false;

jest.mock('expo-router', () => ({
  __esModule: true,
  router: { push: (...a: unknown[]) => mockPush(...a) },
  useFocusEffect: () => {},
}));

jest.mock('../src/features/vouchers/hooks/useMyCodes', () => ({
  useMyCodes: () => ({ vouchers: mockVouchers, user: mockUser, loading: mockLoading }),
}));

jest.mock('../src/features/vouchers/renewal/api/renewal', () => ({
  getRenewalConfig: (...a: unknown[]) => mockGetConfig(...a),
}));

jest.mock('../src/features/vouchers/renewal/eligibility', () => ({
  isRenewableVoucher: (...a: unknown[]) => mockIsRenewable(...a),
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = { t: (key: string, arg?: string) => (arg === undefined ? key : `${key}:${arg}`) };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/core/utils/formatters', () => ({
  formatExpirationDate: (d: string) => `fmt(${d})`,
}));

jest.mock('../src/core/hooks/useTheme', () => {
  const { getTokens } = jest.requireActual<typeof import('../src/core/design/tokens')>(
    '../src/core/design/tokens',
  );
  return { useDesignTokens: () => getTokens() };
});

jest.mock('../src/core/ui', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const {
    Pressable,
    Text: RNText,
    View,
  } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    PageLayout: ({ children, header, footer }: any) =>
      R.createElement(View, { testID: 'page-layout' }, header, children, footer),
    ScreenHeader: ({ title }: any) => R.createElement(RNText, { testID: 'header' }, title),
    LoadingState: () => R.createElement(RNText, { testID: 'loading' }, 'loading'),
    EmptyState: ({ title }: any) => R.createElement(RNText, { testID: 'empty-state' }, title),
    Text: ({ children }: any) => R.createElement(RNText, null, children),
    Card: ({ children, selected, onPress }: any) =>
      R.createElement(
        Pressable,
        { testID: 'card', accessibilityState: { selected: !!selected }, onPress },
        children,
      ),
    Button: ({ label, onPress, disabled }: any) =>
      R.createElement(
        Pressable,
        { testID: `btn-${label}`, onPress, accessibilityState: { disabled: !!disabled } },
        R.createElement(RNText, null, label),
      ),
  };
});

jest.mock('lucide-react-native', () => {
  const Icon = () => null;
  return { __esModule: true, Check: Icon, Circle: Icon };
});

const voucher = (id: string, over: Record<string, unknown> = {}) => ({
  id,
  provider: 'Shell',
  fuelName: 'A95',
  fuelType: 'A95',
  amount: 50,
  expirationDate: '2026-02-01',
  ...over,
});

beforeEach(() => {
  mockVouchers = [voucher('v-1'), voucher('v-2')];
  mockUser = { id: 'u-1' };
  mockLoading = false;
  mockPush.mockClear();
  mockGetConfig.mockReset();
  mockIsRenewable.mockReset();
  mockGetConfig.mockResolvedValue({ enabled: true, thresholdDays: 14 });
  mockIsRenewable.mockReturnValue(true);
});

describe('RenewSelect - the states', () => {
  it('shows a loading state while the vouchers load', async () => {
    mockLoading = true;
    render(<RenewSelectScreen />);
    expect(screen.getByTestId('loading')).toBeTruthy();
    // Flush the config read so its state update does not land outside act.
    await act(async () => {});
  });

  it('shows the disabled state when the feature is off', async () => {
    mockGetConfig.mockResolvedValue({ enabled: false, thresholdDays: 14 });
    render(<RenewSelectScreen />);

    await waitFor(() => expect(screen.getByText('renew.error.disabled')).toBeTruthy());
  });

  it('shows an empty state when nothing is renewable', async () => {
    mockIsRenewable.mockReturnValue(false);
    render(<RenewSelectScreen />);

    await waitFor(() => expect(screen.getByText('renew.select.empty')).toBeTruthy());
    expect(mockPush).not.toHaveBeenCalled();
  });

  it('survives the config request failing by showing the empty state', async () => {
    // A failed read just leaves the config null; the screen must not crash, it falls
    // through to whatever the candidates say.
    mockGetConfig.mockRejectedValue(new Error('offline'));
    mockIsRenewable.mockReturnValue(false);
    render(<RenewSelectScreen />);

    await waitFor(() => expect(screen.getByText('renew.select.empty')).toBeTruthy());
  });
});

describe('RenewSelect - eligibility is the predicate call', () => {
  it('offers only the vouchers the predicate accepts', async () => {
    mockIsRenewable.mockImplementation((v: any) => v.id === 'v-2');
    render(<RenewSelectScreen />);

    await waitFor(() => expect(screen.getAllByTestId('card')).toHaveLength(1));
  });

  it('passes the user id and config to the predicate', async () => {
    render(<RenewSelectScreen />);

    await waitFor(() => {
      const last = mockIsRenewable.mock.calls[mockIsRenewable.mock.calls.length - 1];
      expect(last[2]).toMatchObject({ enabled: true });
    });
    const last = mockIsRenewable.mock.calls[mockIsRenewable.mock.calls.length - 1];
    expect(last[1]).toBe('u-1');
  });
});

describe('RenewSelect - selecting', () => {
  it('pre-selects every candidate', async () => {
    render(<RenewSelectScreen />);

    await waitFor(() => expect(screen.getByText('2 / 2')).toBeTruthy());
    const selected = screen
      .getAllByTestId('card')
      .filter((c) => c.props.accessibilityState.selected);
    expect(selected).toHaveLength(2);
  });

  it('counts the selection against the total', async () => {
    render(<RenewSelectScreen />);
    await waitFor(() => expect(screen.getByText('2 / 2')).toBeTruthy());
  });

  it('trims a voucher out of the batch when tapped', async () => {
    render(<RenewSelectScreen />);
    await waitFor(() => expect(screen.getAllByTestId('card')).toHaveLength(2));

    fireEvent.press(screen.getAllByTestId('card')[0]);

    await waitFor(() => expect(screen.getByText('1 / 2')).toBeTruthy());
  });

  it('clears and reselects all with the toggle', async () => {
    render(<RenewSelectScreen />);
    await waitFor(() => expect(screen.getByText('2 / 2')).toBeTruthy());

    fireEvent.press(screen.getByTestId('btn-renew.select.clear'));
    await waitFor(() => expect(screen.getByText('0 / 2')).toBeTruthy());

    fireEvent.press(screen.getByTestId('btn-renew.select.selectAll'));
    await waitFor(() => expect(screen.getByText('2 / 2')).toBeTruthy());
  });
});

describe('RenewSelect - continuing', () => {
  it('hands the chosen ids to the renew screen', async () => {
    render(<RenewSelectScreen />);
    await waitFor(() => expect(screen.getByText('2 / 2')).toBeTruthy());

    fireEvent.press(screen.getByTestId('btn-renew.select.continue:2'));

    expect(mockPush).toHaveBeenCalledWith('/renew?voucherIds=v-1,v-2');
  });

  it('sends only the vouchers still selected', async () => {
    render(<RenewSelectScreen />);
    await waitFor(() => expect(screen.getAllByTestId('card')).toHaveLength(2));

    fireEvent.press(screen.getAllByTestId('card')[0]);
    await waitFor(() => expect(screen.getByText('1 / 2')).toBeTruthy());
    fireEvent.press(screen.getByTestId('btn-renew.select.continue:1'));

    expect(mockPush).toHaveBeenCalledWith('/renew?voucherIds=v-2');
  });

  it('disables continue with nothing selected', async () => {
    render(<RenewSelectScreen />);
    await waitFor(() => expect(screen.getByText('2 / 2')).toBeTruthy());
    fireEvent.press(screen.getByTestId('btn-renew.select.clear'));
    await waitFor(() => expect(screen.getByText('0 / 2')).toBeTruthy());

    expect(
      screen.getByTestId('btn-renew.select.continue:0').props.accessibilityState.disabled,
    ).toBe(true);
  });

  it('keeps the user edits when the data refreshes underneath', async () => {
    const { rerender } = render(<RenewSelectScreen />);
    await waitFor(() => expect(screen.getByText('2 / 2')).toBeTruthy());

    fireEvent.press(screen.getAllByTestId('card')[0]);
    await waitFor(() => expect(screen.getByText('1 / 2')).toBeTruthy());

    // A background refresh hands down a fresh array with the same vouchers. The
    // pre-select must not run a second time and re-tick everything the user unticked.
    mockVouchers = [voucher('v-1'), voucher('v-2')];
    rerender(<RenewSelectScreen />);

    await waitFor(() => expect(screen.getByText('1 / 2')).toBeTruthy());
  });

  it('does nothing with an empty selection', async () => {
    render(<RenewSelectScreen />);
    await waitFor(() => expect(screen.getByText('2 / 2')).toBeTruthy());
    fireEvent.press(screen.getByTestId('btn-renew.select.clear'));
    await waitFor(() => expect(screen.getByText('0 / 2')).toBeTruthy());

    fireEvent.press(screen.getByTestId('btn-renew.select.continue:0'));

    // A renew screen with no vouchers to quote is a dead end.
    expect(mockPush).not.toHaveBeenCalled();
  });
});
