import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import HomeScreen from './index';

/**
 * The stations list — the first screen a user sees after the auth gate.
 *
 * Two things here have bitten the app before, and both are silent:
 *
 * - The station list used to be filtered through a hardcoded
 *   `['okko','wog','upg','klo']` allowlist, so every provider an admin added after
 *   that list was written simply did not appear. Nothing errored; the station was
 *   just absent. So the suite asserts on *count and identity*, not on the presence of
 *   a few known names.
 *
 * - The sort used to run against the array straight out of the query cache, which
 *   mutates the cached payload. That produces an order that depends on what the
 *   screen did before, and it leaks across screens sharing the cache. The spread is
 *   load-bearing, so the suite pins that the caller's array comes back untouched.
 *
 * Selecting a station has to reach the cart store *before* navigating, because the
 * detail screen reads the station from there rather than re-fetching it.
 */

const mockSelectStation = jest.fn();
const mockPush = jest.fn();
const mockRefetch = jest.fn();

let mockStations: any[] | undefined = [];
let mockStationsLoading = false;
let mockStationsError: unknown = null;
let mockStoreAuth = true;
let mockAuthLoading = false;

jest.mock('expo-router', () => ({
  __esModule: true,
  useRouter: () => ({ push: mockPush, replace: jest.fn(), back: jest.fn() }),
  useFocusEffect: () => {},
}));

jest.mock('../src/features/stations/hooks/useStations', () => ({
  useStations: () => ({
    data: mockStations,
    isLoading: mockStationsLoading,
    error: mockStationsError,
    refetch: mockRefetch,
  }),
}));

jest.mock('../src/features/cart/store/cartStore', () => ({
  useCartStore: () => ({ selectStation: mockSelectStation }),
}));

jest.mock('../src/core/state/appStore', () => ({
  useStore: (selector?: (s: any) => unknown) => {
    const state = { isAuthenticated: mockStoreAuth };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/features/auth/hooks/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: mockStoreAuth, isLoading: mockAuthLoading }),
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/core/hooks/useTheme', () => ({
  // Resolved inside the factory: the screen reads dozens of colour roles off it.
  useDesignTokens: () =>
    jest
      .requireActual<typeof import('../src/core/design/tokens')>('../src/core/design/tokens')
      .getTokens(),
}));

jest.mock('../src/core/hooks/usePulseAnimation', () => ({ usePulseAnimation: () => 1 }));

jest.mock('../src/components/glow-text', () => {
  const { Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return { __esModule: true, GlowText: ({ children }: any) => <Text>{children}</Text> };
});

jest.mock('../src/features/stations/components/StationCard', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Pressable, Text } = jest.requireActual<typeof import('react-native')>('react-native');
  // Renders the station name *and its position*, so both identity and ordering can
  // be asserted without depending on how a card is drawn.
  return {
    __esModule: true,
    StationCard: ({ station, index, onPress }: any) =>
      R.createElement(
        Pressable,
        { testID: `station-${station.id}`, onPress: () => onPress(station) },
        R.createElement(Text, { testID: `name-${station.id}` }, station.name),
        R.createElement(Text, { testID: `pos-${station.id}` }, String(index)),
      ),
  };
});

jest.mock('../src/core/ui', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText, View } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    GridPageLayout: ({ children, header }: any) =>
      R.createElement(View, { testID: 'grid-layout' }, header, children),
    LoadingState: () => R.createElement(RNText, { testID: 'loading' }, 'loading'),
    ErrorState: ({ detail, onRetry }: any) =>
      R.createElement(
        View,
        null,
        R.createElement(RNText, { testID: 'error-state' }, detail ?? 'error'),
        R.createElement(RNText, { testID: 'retry', onPress: onRetry }, 'retry'),
      ),
    EmptyState: ({ title, description }: any) =>
      R.createElement(
        View,
        null,
        R.createElement(RNText, { testID: 'empty-state' }, title),
        R.createElement(RNText, { testID: 'empty-desc' }, description),
      ),
  };
});

jest.mock('lucide-react-native', () => {
  const Icon = () => null;
  return { __esModule: true, Fuel: Icon };
});

const station = (id: string, name: string, sortOrder?: number) => ({
  id,
  name,
  sortOrder,
  color: '#ff0000',
  logoText: name.slice(0, 2),
});

beforeEach(() => {
  mockStations = [station('st-a', 'Shell', 2), station('st-b', 'OKKO', 1)];
  mockStationsLoading = false;
  mockStationsError = null;
  mockStoreAuth = true;
  mockAuthLoading = false;
  mockSelectStation.mockClear();
  mockPush.mockClear();
  mockRefetch.mockClear();
});

describe('Home — the cold-start gate', () => {
  it('waits on the first frame while the auth query is in flight', () => {
    mockAuthLoading = true;
    mockStoreAuth = false;
    render(<HomeScreen />);

    // Without this the very first thing painted had no safe-area insets, so the
    // spinner sat centred in the physical screen rather than the content area.
    expect(screen.getByTestId('loading')).toBeTruthy();
    expect(screen.getByTestId('grid-layout')).toBeTruthy();
  });

  it('does not wait when the store already knows the session', () => {
    mockAuthLoading = true;
    mockStoreAuth = true;
    render(<HomeScreen />);

    // Making a signed-in user stare at a spinner on every cold start.
    expect(screen.queryByTestId('loading')).toBeNull();
  });

  it('does not wait once the query has settled', () => {
    mockAuthLoading = false;
    mockStoreAuth = false;
    render(<HomeScreen />);

    expect(screen.queryByTestId('loading')).toBeNull();
  });
});

describe('Home — every provider is rendered', () => {
  it('renders each station the server sent', () => {
    mockStations = [
      station('st-1', 'Shell'),
      station('st-2', 'OKKO'),
      station('st-3', 'WOG'),
      station('st-4', 'UPG'),
      station('st-5', 'KLO'),
      // Not on any historical allowlist. Dropping it is the regression.
      station('st-6', 'SOCAR'),
    ];
    render(<HomeScreen />);

    expect(screen.getAllByTestId(/^station-/)).toHaveLength(6);
    expect(screen.getByTestId('station-st-6')).toBeTruthy();
  });

  it('does not mutate the array it was handed', () => {
    // The sort used to run against the cached payload directly, so the order depended
    // on what the screen had already done and leaked to other cache consumers.
    const source = [station('st-a', 'Shell', 2), station('st-b', 'OKKO', 1)];
    const snapshot = source.map((s) => s.id);
    mockStations = source;

    render(<HomeScreen />);

    expect(source.map((s) => s.id)).toEqual(snapshot);
    // ...while the screen still shows them sorted.
    expect(screen.getByTestId('pos-st-b')).toHaveTextContent('0');
    expect(screen.getByTestId('pos-st-a')).toHaveTextContent('1');
  });
});

describe('Home — the order stations appear in', () => {
  it('follows the server-managed priority', () => {
    mockStations = [
      station('st-a', 'Shell', 2),
      station('st-b', 'OKKO', 1),
      station('st-c', 'WOG', 0),
    ];
    render(<HomeScreen />);

    expect(screen.getByTestId('pos-st-c')).toHaveTextContent('0');
    expect(screen.getByTestId('pos-st-b')).toHaveTextContent('1');
    expect(screen.getByTestId('pos-st-a')).toHaveTextContent('2');
  });

  it('breaks a priority tie by name', () => {
    mockStations = [
      station('st-a', 'Zebra', 1),
      station('st-b', 'Alpha', 1),
      station('st-c', 'Mango', 1),
    ];
    render(<HomeScreen />);

    expect(screen.getByTestId('pos-st-b')).toHaveTextContent('0');
    expect(screen.getByTestId('pos-st-c')).toHaveTextContent('1');
    expect(screen.getByTestId('pos-st-a')).toHaveTextContent('2');
  });

  it('puts a station with no priority last, and orders those by name', () => {
    // A payload cached before the field existed must not jump the queue; 999 puts it
    // behind everything the admin has actually prioritised.
    mockStations = [
      station('st-none-b', 'Yankee', undefined),
      station('st-first', 'Shell', 1),
      station('st-none-a', 'Xray', undefined),
    ];
    render(<HomeScreen />);

    expect(screen.getByTestId('pos-st-first')).toHaveTextContent('0');
    expect(screen.getByTestId('pos-st-none-a')).toHaveTextContent('1');
    expect(screen.getByTestId('pos-st-none-b')).toHaveTextContent('2');
  });

  it('survives a payload where every priority is missing', () => {
    mockStations = [station('st-b', 'Beta'), station('st-a', 'Alpha')];
    render(<HomeScreen />);

    expect(screen.getByTestId('pos-st-a')).toHaveTextContent('0');
    expect(screen.getByTestId('pos-st-b')).toHaveTextContent('1');
  });

  it('copes with no stations at all', () => {
    mockStations = undefined;
    render(<HomeScreen />);

    expect(screen.queryByTestId(/^station-/)).toBeNull();
  });
});

describe('Home — choosing a station', () => {
  it('puts the station in the store before navigating to it', () => {
    mockStations = [station('st-a', 'Shell', 1)];
    render(<HomeScreen />);

    const order: string[] = [];
    mockSelectStation.mockImplementation(() => order.push('store'));
    mockPush.mockImplementation(() => order.push('navigate'));

    fireEvent.press(screen.getByTestId('station-st-a'));

    // The detail screen reads the station from the cart store rather than
    // re-fetching, so navigating first lands on a screen with nothing to show.
    expect(mockSelectStation).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'st-a', name: 'Shell' }),
    );
    expect(order).toEqual(['store', 'navigate']);
  });

  it('navigates to the station that was pressed, not the first one', () => {
    mockStations = [
      station('st-c', 'WOG', 0),
      station('st-a', 'Shell', 1),
      station('st-b', 'OKKO', 2),
    ];
    render(<HomeScreen />);

    fireEvent.press(screen.getByTestId('station-st-b'));

    expect(mockPush).toHaveBeenCalledWith('/station/st-b');
  });
});

describe('Home — when the list cannot be loaded', () => {
  it('shows the failure with a retry that refetches', () => {
    mockStations = undefined;
    mockStationsError = new Error('Service unavailable');
    render(<HomeScreen />);

    expect(screen.getByTestId('error-state')).toBeTruthy();
    expect(screen.getByText('Service unavailable')).toBeTruthy();

    fireEvent.press(screen.getByTestId('retry'));
    expect(mockRefetch).toHaveBeenCalled();
  });

  it('does not show a failure over a list that is still arriving', () => {
    mockStations = undefined;
    mockStationsLoading = true;
    mockStationsError = new Error('Service unavailable');
    render(<HomeScreen />);

    // Showing an error mid-load replaces a list that was about to arrive.
    expect(screen.queryByTestId('error-state')).toBeNull();
  });

  it('says so when there are genuinely no stations', () => {
    mockStations = [];
    render(<HomeScreen />);

    expect(screen.getByTestId('empty-state')).toBeTruthy();
    expect(screen.getByText('stations.empty')).toBeTruthy();
    expect(screen.getByTestId('empty-desc')).toHaveTextContent('stations.emptyHint');
  });

  it('prefers the failure over the empty state', () => {
    // Both conditions can hold at once; the error is the one worth telling the user
    // about, since "no stations" would read as an accusation about their account.
    mockStations = [];
    mockStationsError = new Error('Service unavailable');
    render(<HomeScreen />);

    expect(screen.getByTestId('error-state')).toBeTruthy();
    expect(screen.queryByTestId('empty-state')).toBeNull();
  });

  it('shows no empty state while the list is still loading', () => {
    mockStations = [];
    mockStationsLoading = true;
    render(<HomeScreen />);

    // "No stations" is the wrong thing to tell someone whose list is on its way.
    expect(screen.queryByTestId('empty-state')).toBeNull();
  });
});
