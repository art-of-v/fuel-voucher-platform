import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react-native';

import LandingScreen from './landing';
import PaymentResultScreen from './payment-result';
import StationDetailScreen from './station/[id]';

/**
 * The three smallest routes in the app, which had no tests between them.
 *
 * They are grouped because they are one thing: the entry and hand-off edges, where a
 * bug strands a user on a blank screen rather than showing them something wrong.
 *
 * - `landing` decides between the first-launch disclosure, the sign-in form, and
 *   bouncing an already-signed-in user home. Redirecting *while the auth query is
 *   still loading* would bounce a signed-in customer to their home screen on every
 *   cold start; showing the sign-in form to one who is signed in would strand them.
 * - `payment-result` renders nothing at all and exists only to hand off to the
 *   wallet. It is the last frame of a purchase, so a missing redirect leaves the app
 *   on an empty white screen with no way forward.
 * - `station/[id]` reads the station from the cart store rather than re-fetching it,
 *   so a missing or wrong id must render nothing rather than an empty station page.
 *   Its fuel ordering is a business rule -- diesel first, gas last, AdBlue very last
 *   -- because those are the fuels a driver needs to reach without scrolling.
 */

const mockReplace = jest.fn();
const mockPush = jest.fn();
const mockLogin = jest.fn();
const mockCompleteOnboarding = jest.fn();
const mockSelectStation = jest.fn();
const mockSelectFuel = jest.fn();

let mockStoreAuth = false;
let mockHookAuth = false;
let mockAuthLoading = false;
let mockOnboarded = true;
let mockStations: any[] | undefined = [];
let mockParams: Record<string, unknown> = {};

jest.mock('expo-router', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    useRouter: () => ({ replace: mockReplace, push: mockPush, back: jest.fn() }),
    useLocalSearchParams: () => mockParams,
    useFocusEffect: () => {},
    Redirect: ({ href }: { href: string }) => R.createElement(RNText, { testID: 'redirect' }, href),
  };
});

jest.mock('../src/features/auth/hooks/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: mockHookAuth, isLoading: mockAuthLoading }),
}));

jest.mock('../src/core/state/appStore', () => ({
  // Called both with no argument and with a selector on this screen.
  useStore: (selector?: (s: any) => unknown) => {
    const state = {
      isAuthenticated: mockStoreAuth,
      login: mockLogin,
      hasCompletedOnboarding: mockOnboarded,
      completeOnboarding: mockCompleteOnboarding,
    };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/features/auth/components/PhoneAuthForm', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Pressable, Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    PhoneAuthForm: ({ onSuccess }: any) =>
      R.createElement(
        Pressable,
        { testID: 'phone-auth-form', onPress: onSuccess },
        R.createElement(Text, null, 'sign in'),
      ),
  };
});

jest.mock('../src/features/onboarding/components/Onboarding', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Pressable, Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    Onboarding: ({ onFinish }: any) =>
      R.createElement(
        Pressable,
        { testID: 'onboarding', onPress: onFinish },
        R.createElement(Text, null, 'onboarding'),
      ),
  };
});

jest.mock('../src/features/stations/hooks/useStations', () => ({
  useStations: () => ({ data: mockStations }),
}));

jest.mock('../src/features/cart/store/cartStore', () => ({
  useCartStore: () => ({ selectStation: mockSelectStation, selectFuel: mockSelectFuel }),
}));

jest.mock('../src/features/stations/components/FuelCard', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Pressable, Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    // Renders the fuel's name and its position, so both identity and ordering can be
    // asserted without depending on how a card is drawn.
    FuelCard: ({ fuel, station, index, onPress }: any) =>
      R.createElement(
        Pressable,
        { testID: `fuel-${fuel.id}`, onPress: () => onPress(station, fuel) },
        R.createElement(Text, { testID: `name-${fuel.id}` }, fuel.name),
        R.createElement(Text, { testID: `pos-${fuel.id}` }, String(index)),
      ),
  };
});

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
    ScreenHeader: ({ title }: any) => R.createElement(RNText, { testID: 'header-title' }, title),
  };
});

beforeEach(() => {
  mockStoreAuth = false;
  mockHookAuth = false;
  mockAuthLoading = false;
  mockOnboarded = true;
  mockStations = [];
  mockParams = {};
  mockReplace.mockClear();
  mockPush.mockClear();
  mockLogin.mockClear();
  mockCompleteOnboarding.mockClear();
  mockSelectStation.mockClear();
  mockSelectFuel.mockClear();
});

describe('Landing — who gets what', () => {
  it('shows the sign-in form to a signed-out visitor', () => {
    render(<LandingScreen />);

    expect(screen.getByTestId('phone-auth-form')).toBeTruthy();
    expect(screen.queryByTestId('onboarding')).toBeNull();
  });

  it('shows the first-launch disclosure before anything else', () => {
    mockOnboarded = false;
    render(<LandingScreen />);

    // The disclosure has to come before the sign-up form: Apple 5.1.1 requires the
    // phone number to be disclosed *before* registration.
    expect(screen.getByTestId('onboarding')).toBeTruthy();
    expect(screen.queryByTestId('phone-auth-form')).toBeNull();
  });

  it('records that onboarding was finished', () => {
    mockOnboarded = false;
    render(<LandingScreen />);

    fireEvent.press(screen.getByTestId('onboarding'));

    // Without the flag, every launch would show the disclosure again.
    expect(mockCompleteOnboarding).toHaveBeenCalled();
  });

  it('sends an already-signed-in visitor home', () => {
    mockStoreAuth = true;
    render(<LandingScreen />);

    expect(screen.getByTestId('redirect')).toHaveTextContent('/');
  });

  it('does not bounce a signed-in visitor while the auth query is loading', () => {
    mockHookAuth = true;
    mockAuthLoading = true;
    render(<LandingScreen />);

    // Redirecting on `isAuthenticated` alone would send every signed-in customer to
    // the home screen for the length of the query on every cold start.
    expect(screen.queryByTestId('redirect')).toBeNull();
  });

  it('accepts a session known to the store but not yet the query', () => {
    mockStoreAuth = true;
    render(<LandingScreen />);

    expect(screen.getByTestId('redirect')).toHaveTextContent('/');
  });

  it('accepts a session the query knows about before the store has caught up', () => {
    mockHookAuth = true;
    render(<LandingScreen />);

    expect(screen.getByTestId('redirect')).toHaveTextContent('/');
  });

  it('signs in and replaces the landing screen on success', () => {
    render(<LandingScreen />);

    fireEvent.press(screen.getByTestId('phone-auth-form'));

    expect(mockLogin).toHaveBeenCalled();
    // `replace`, not `push`: back from home would otherwise return to the sign-in form
    // the user has just completed.
    expect(mockReplace).toHaveBeenCalledWith('/');
  });
});

describe('Payment result — the last frame of a purchase', () => {
  it('hands off to the wallet on arrival', () => {
    render(<PaymentResultScreen />);

    expect(mockReplace).toHaveBeenCalledWith('/my-codes');
  });

  it('renders nothing while it does so', () => {
    // The screen exists only to redirect; a stray "processing" spinner would flash
    // between the payment provider and the wallet.
    const { toJSON } = render(<PaymentResultScreen />);

    expect(toJSON()).toBeNull();
  });

  it('hands off once, not on every render', () => {
    const { rerender } = render(<PaymentResultScreen />);
    rerender(<PaymentResultScreen />);
    rerender(<PaymentResultScreen />);

    // Re-navigating to /my-codes would stack copies of the wallet.
    expect(mockReplace).toHaveBeenCalledTimes(1);
  });
});

describe('Station detail — finding the station it was sent to', () => {
  const fuel = (id: string, name: string) => ({ id, name });

  const station = {
    id: 'st-1',
    name: 'Shell Darnytsia',
    logoText: 'SH',
    fuels: [fuel('f-95', 'A95'), fuel('f-dp', 'ДП')],
  };

  it('renders nothing when the station is not in the list', () => {
    mockStations = [station];
    mockParams = { id: 'st-unknown' };
    const { toJSON } = render(<StationDetailScreen />);

    // A blank station page would read as "this station has no fuel" rather than
    // "this station is not one of yours".
    expect(toJSON()).toBeNull();
  });

  it('renders nothing before the stations have loaded', () => {
    mockStations = undefined;
    mockParams = { id: 'st-1' };
    const { toJSON } = render(<StationDetailScreen />);

    expect(toJSON()).toBeNull();
  });

  it('titles the page with the station mark, falling back to its name', () => {
    mockStations = [station];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    expect(screen.getByTestId('header-title')).toHaveTextContent('SH');
  });

  it('falls back to the full name when there is no mark', () => {
    mockStations = [{ ...station, logoText: '' }];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    expect(screen.getByTestId('header-title')).toHaveTextContent('Shell Darnytsia');
  });

  it('renders the fuels of the station it was sent to, not another', () => {
    mockStations = [
      station,
      { id: 'st-2', name: 'OKKO', logoText: 'OK', fuels: [fuel('f-other', 'Дизель')] },
    ];
    mockParams = { id: 'st-2' };
    render(<StationDetailScreen />);

    expect(screen.getByTestId('fuel-f-other')).toBeTruthy();
    expect(screen.queryByTestId('fuel-f-95')).toBeNull();
  });
});

describe('Station detail — fuel ordering', () => {
  const fuel = (id: string, name: string) => ({ id, name });
  const withFuels = (fuels: ReturnType<typeof fuel>[]) => ({
    id: 'st-1',
    name: 'Shell',
    logoText: 'SH',
    fuels,
  });

  it('puts diesel first, because it is what a driver reaches for', () => {
    mockStations = [withFuels([fuel('a95', 'A95'), fuel('dp', 'ДП'), fuel('lpg', 'ГАЗ')])];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    expect(screen.getByTestId('pos-dp')).toHaveTextContent('0');
    expect(screen.getByTestId('pos-a95')).toHaveTextContent('1');
    expect(screen.getByTestId('pos-lpg')).toHaveTextContent('2');
  });

  it('puts AdBlue last of all', () => {
    mockStations = [withFuels([fuel('adblue', 'ADBLUE'), fuel('dp', 'ДП'), fuel('a95', 'A95')])];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    expect(screen.getByTestId('pos-adblue')).toHaveTextContent('2');
  });

  it('recognises LPG under its latin name too', () => {
    // Input order chosen so the assertion can actually distinguish the two cases:
    // unrecognised, LPG and A95 both land on priority 2 and a stable sort keeps them
    // in the order given, which is the *reverse* of what recognition produces.
    mockStations = [withFuels([fuel('dp', 'ДП'), fuel('en', 'LPG'), fuel('a95', 'A95')])];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    // Drivers search for "LPG"; treating only the Cyrillic spelling as gas would put
    // it alongside the petrols instead of after them.
    expect(screen.getByTestId('pos-dp')).toHaveTextContent('0');
    expect(screen.getByTestId('pos-a95')).toHaveTextContent('1');
    expect(screen.getByTestId('pos-en')).toHaveTextContent('2');
  });

  it('copes with a station that lists no fuels at all', () => {
    mockStations = [{ id: 'st-1', name: 'Shell', logoText: 'SH' }];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    expect(screen.getByTestId('header-title')).toHaveTextContent('SH');
    expect(screen.queryByTestId(/^fuel-/)).toBeNull();
  });

  it('does not reorder the station payload it was given', () => {
    const fuels = [fuel('lpg', 'ГАЗ'), fuel('dp', 'ДП')];
    const source = withFuels(fuels);
    mockStations = [source];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    expect(source.fuels.map((f) => f.id)).toEqual(['lpg', 'dp']);
    // ...while the screen shows them in business order.
    expect(screen.getByTestId('pos-dp')).toHaveTextContent('0');
  });
});

describe('Station detail — choosing a fuel', () => {
  const fuel = (id: string, name: string) => ({ id, name });
  const station = { id: 'st-1', name: 'Shell', logoText: 'SH', fuels: [fuel('f-95', 'A95')] };

  it('puts the station and the fuel in the cart before moving on', async () => {
    mockStations = [station];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    // The hand-off to the package screen is on a short delay, so the presses have to
    // be wrapped: without `act`, the resulting state updates fail the test outright.
    await act(async () => {
      fireEvent.press(screen.getByTestId('fuel-f-95'));
    });

    expect(mockSelectStation).toHaveBeenCalledWith(expect.objectContaining({ id: 'st-1' }));
    expect(mockSelectFuel).toHaveBeenCalledWith(expect.objectContaining({ id: 'f-95' }));
  });

  it('navigates to the packages after the delay', async () => {
    mockStations = [station];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    await act(async () => {
      fireEvent.press(screen.getByTestId('fuel-f-95'));
      await new Promise((r) => setTimeout(r, 150));
    });

    expect(mockPush).toHaveBeenCalledWith('/packages');
  });

  it('does not navigate before the cart has been filled', async () => {
    mockStations = [station];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    // The package screen reads both from the cart store; arriving early would show it
    // with nothing to sell.
    await act(async () => {
      fireEvent.press(screen.getByTestId('fuel-f-95'));
      await new Promise((r) => setTimeout(r, 150));
    });

    const storeBeforeNav = mockSelectStation.mock.invocationCallOrder[0];
    const navAfter = mockPush.mock.invocationCallOrder[0];
    expect(storeBeforeNav).toBeLessThan(navAfter);
  });

  it('carries the fuel that was pressed, not the first one', async () => {
    const two = {
      ...station,
      fuels: [fuel('f-a', 'A95'), fuel('f-b', 'ДП')],
    };
    mockStations = [two];
    mockParams = { id: 'st-1' };
    render(<StationDetailScreen />);

    await act(async () => {
      fireEvent.press(screen.getByTestId('fuel-f-b'));
      await new Promise((r) => setTimeout(r, 150));
    });

    expect(mockSelectFuel).toHaveBeenCalledWith(expect.objectContaining({ id: 'f-b' }));
  });
});
