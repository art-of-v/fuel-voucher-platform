import React from 'react';
import { ScrollView } from 'react-native';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react-native';

import SavingsScreen from './savings';

/**
 * The savings report, which had no tests.
 *
 * Two things here are easy to get wrong and expensive to get wrong visibly:
 *
 * - the **period filter** maps to a date range. "Last 3 months" that quietly sends
 *   the wrong window shows a customer a savings figure for a period they did not
 *   ask about, and they will act on it.
 * - "you have never bought" is only true for the **all-time** view. A narrow period
 *   with no orders is not an empty account — it is an empty month — and must still
 *   render the filter so the customer can switch back out of it. Getting this wrong
 *   strands someone in a dead end with no way to widen the range.
 */

/**
 * RNTL advances the fake clock while `findBy*` waits, so the instant the screen
 * captured as "now" drifts by a few tens of milliseconds. The calendar parts are
 * what the range actually depends on, so those are what gets compared.
 */
function expectEndOfRange(to: Date | undefined, iso = '2026-03-15T10:00:00.000Z') {
  expect(to).toBeDefined();
  const a = to as Date;
  const b = new Date(iso);
  expect(a.getFullYear()).toBe(b.getFullYear());
  expect(a.getMonth()).toBe(b.getMonth());
  expect(a.getDate()).toBe(b.getDate());
  expect(a.getHours()).toBe(b.getHours());
}
/** The range the screen sent for the period currently under test. */
function lastRange(): { from: Date; to?: Date } {
  const calls = mockGetMySavings.mock.calls;
  const [from, to] = calls[calls.length - 1] as [string | undefined, string | undefined];
  return { from: new Date(from as string), to: to ? new Date(to) : undefined };
}
const mockGetMySavings = jest.fn();

jest.mock('../src/features/savings/api/getSavings', () => ({
  __esModule: true,
  getMySavings: (...a: unknown[]) => mockGetMySavings(...a),
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({
    t: (key: string, arg?: string) => (arg === undefined ? key : `${key}${arg}`),
    language: 'uk',
    setLanguage: jest.fn(),
  }),
}));

jest.mock('../src/core/utils/currency', () => ({
  formatMoney: (n: number) => `${n} UAH`,
}));

jest.mock('../src/core/hooks/useTheme', () => {
  const { getTokens } = jest.requireActual<typeof import('../src/core/design/tokens')>(
    '../src/core/design/tokens',
  );
  return { useDesignTokens: () => getTokens() };
});

jest.mock('../src/core/ui', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText, View } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    GridBackground: () => null,
    GridPageLayout: ({ children, header }: any) =>
      R.createElement(View, { testID: 'grid-layout' }, header, children),
    ScreenHeader: ({ title }: any) => R.createElement(RNText, { testID: 'screen-header' }, title),
    LoadingState: () => R.createElement(RNText, { testID: 'loading' }, 'loading'),
    ErrorState: ({ detail, onRetry }: any) =>
      R.createElement(
        View,
        null,
        R.createElement(RNText, { testID: 'error-state' }, detail),
        R.createElement(RNText, { testID: 'retry', onPress: onRetry }, 'retry'),
      ),
    EmptyState: ({ title, action }: any) =>
      R.createElement(
        View,
        null,
        R.createElement(RNText, { testID: 'empty-state' }, title),
        action
          ? R.createElement(
              RNText,
              { testID: 'empty-retry', onPress: action.onPress },
              action.label,
            )
          : null,
      ),
    useContentInsets: () => ({ top: 0, bottom: 34, left: 0, right: 0 }),
  };
});

jest.mock('lucide-react-native', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  const icon = (n: string) => () => R.createElement(RNText, { testID: `icon-${n}` });
  return {
    __esModule: true,
    Wallet: icon('Wallet'),
    PiggyBank: icon('PiggyBank'),
    Fuel: icon('Fuel'),
    Ticket: icon('Ticket'),
    Calendar: icon('Calendar'),
  };
});

const report = (over: Record<string, unknown> = {}) => ({
  ordersCount: 4,
  totalPaid: 8000,
  totalLiters: 200,
  totalSavings: 1200,
  remainingVouchers: 3,
  remainingLiters: 95.6,
  monthly: [
    { month: '2026-02', paid: 3000, saved: 400, liters: 80 },
    { month: '2026-03', paid: 5000, saved: 800, liters: 120 },
  ],
  ...over,
});

beforeEach(() => {
  mockGetMySavings.mockReset();
  mockGetMySavings.mockResolvedValue(report());
});

describe('Savings — the period filter drives the date range', () => {
  beforeEach(() => {
    // A fixed clock, because every one of these assertions is about *which* window
    // was requested. A real clock would make the suite pass in most months and fail
    // near a boundary.
    jest.useFakeTimers().setSystemTime(new Date('2026-03-15T10:00:00.000Z'));
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('asks for everything when no period is chosen', async () => {
    render(<SavingsScreen />);

    await waitFor(() => expect(mockGetMySavings).toHaveBeenCalledWith(undefined, undefined));
  });

  it('asks for this month from the first of it', async () => {
    render(<SavingsScreen />);
    fireEvent.press(await screen.findByText('savings.thisMonth'));

    await waitFor(() => {
      const { from, to } = lastRange();
      expect(from.getDate()).toBe(1);
      expect(from.getMonth()).toBe(2); // March, 0-indexed
      expect(from.getFullYear()).toBe(2026);
      expectEndOfRange(to);
    });
  });

  it('asks for the last three months, not one', async () => {
    render(<SavingsScreen />);
    fireEvent.press(await screen.findByText('savings.last3Months'));

    await waitFor(() => {
      const { from } = lastRange();
      // January, because three months back from March inclusive of March itself.
      // A `- 3` here would silently report on a four-month window.
      expect(new Date(from).getMonth()).toBe(0);
      expect(new Date(from).getDate()).toBe(1);
    });
  });

  it('reaches back into the previous year in January', async () => {
    jest.setSystemTime(new Date('2026-01-10T10:00:00.000Z'));
    render(<SavingsScreen />);
    fireEvent.press(await screen.findByText('savings.last3Months'));

    // January minus two months is November of the year before. Getting this wrong
    // would show a customer a window that starts after some of their own orders.
    await waitFor(() => {
      const { from } = lastRange();
      const d = new Date(from);
      expect(d.getFullYear()).toBe(2025);
      expect(d.getMonth()).toBe(10); // November
      expect(d.getDate()).toBe(1);
    });
  });

  it('asks for this year from January the first', async () => {
    render(<SavingsScreen />);
    fireEvent.press(await screen.findByText('savings.thisYear'));

    await waitFor(() => {
      const { from, to } = lastRange();
      expect(new Date(from).getMonth()).toBe(0);
      expect(new Date(from).getDate()).toBe(1);
      expectEndOfRange(to, '2026-03-15T10:00:00.000Z');
    });
  });

  it('re-queries when the period changes, rather than filtering locally', async () => {
    render(<SavingsScreen />);
    await waitFor(() => expect(mockGetMySavings).toHaveBeenCalledTimes(1));

    fireEvent.press(await screen.findByText('savings.thisYear'));

    // The server holds the history; the client only asks for a different window.
    await waitFor(() => expect(mockGetMySavings).toHaveBeenCalledTimes(2));
  });
});

describe('Savings — the states', () => {
  it('shows a loading state first', () => {
    mockGetMySavings.mockReturnValue(new Promise(() => {}));
    render(<SavingsScreen />);

    expect(screen.getByTestId('loading')).toBeTruthy();
  });

  it('shows the failure and offers a retry that reloads', async () => {
    mockGetMySavings.mockRejectedValueOnce(new Error('Service unavailable'));
    render(<SavingsScreen />);

    await waitFor(() => expect(screen.getByTestId('error-state')).toBeTruthy());
    expect(screen.getByText('Service unavailable')).toBeTruthy();

    fireEvent.press(screen.getByTestId('retry'));
    await waitFor(() => expect(mockGetMySavings).toHaveBeenCalledTimes(2));
  });

  it('falls back to a message when the failure carries none', async () => {
    mockGetMySavings.mockRejectedValueOnce({});
    render(<SavingsScreen />);

    await screen.findByText('Failed to load savings');
  });

  it('says there is nothing to show for an account that never bought', async () => {
    mockGetMySavings.mockResolvedValue(report({ ordersCount: 0 }));
    render(<SavingsScreen />);

    await waitFor(() => expect(screen.getByTestId('empty-state')).toBeTruthy());
    expect(screen.getByText('savings.noData')).toBeTruthy();
  });

  it('still offers the filter when only the chosen period is empty', async () => {
    // Orders exist overall, but not in this month. The all-time view sends no date
    // range at all, which is how the two are told apart here.
    mockGetMySavings.mockImplementation((from?: string) =>
      from === undefined ? report() : report({ ordersCount: 0, totalPaid: 0, totalSavings: 0 }),
    );

    render(<SavingsScreen />);
    await screen.findByText('savings.allTime');

    fireEvent.press(await screen.findByText('savings.thisMonth'));

    // An empty *month* is not an empty account. Showing the full-screen empty state
    // here would strand the customer with no way to widen the range back.
    await waitFor(() => expect(screen.queryByTestId('empty-state')).toBeNull());
    expect(screen.getByText('savings.allTime')).toBeTruthy();
    expect(screen.getByText('savings.totalPaid')).toBeTruthy();
  });
});

describe('Savings — the figures', () => {
  it('states what was paid and what was saved', async () => {
    render(<SavingsScreen />);

    await screen.findByText('8000 UAH');
    expect(screen.getByText('savings.totalPaid')).toBeTruthy();
    expect(screen.getByText('1200 UAH')).toBeTruthy();
    expect(screen.getByText('savings.totalSaved')).toBeTruthy();
  });

  it('rounds remaining litres to whole ones', async () => {
    render(<SavingsScreen />);

    await screen.findByText('savings.totalPaid');

    // Matched on the rounded value: the unit is a mocked translation key appended
    // straight to the number, so the whole label is '96' + 'common.liter'.
    expect(screen.getByText(/96/)).toBeTruthy();
    // 95.6 rendered as-is would be a promise the pump cannot keep.
    expect(screen.queryByText(/95\.6/)).toBeNull();
    expect(screen.getByText('savings.remainingLiters')).toBeTruthy();
  });

  it('states the voucher count as a whole number', async () => {
    render(<SavingsScreen />);

    await screen.findByText('savings.totalPaid');
    expect(screen.getByText('3')).toBeTruthy();
    expect(screen.getByText('savings.remainingVouchers')).toBeTruthy();
  });

  it('reloads on pull to refresh', async () => {
    render(<SavingsScreen />);
    await waitFor(() => expect(mockGetMySavings).toHaveBeenCalledTimes(1));

    // The refresh control is what the customer pulls; re-querying is the whole point.
    // Read off the ScrollView's prop: the control itself renders as a host element
    // with no onRefresh of its own.
    // Wrapped: the refresh flips the refreshing flag immediately and clears it
    // re-query settles, both outside React's event system. The repo's Jest setup
    // escalates an unwrapped state update into a test failure, which is the point.
    await act(async () => {
      await screen.UNSAFE_getByType(ScrollView).props.refreshControl.props.onRefresh();
    });

    await waitFor(() => expect(mockGetMySavings).toHaveBeenCalledTimes(2));
  });
});
