import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import MapScreen from './map';

/**
 * What this suite covers.
 *
 * `map.tsx` is the largest screen left (1,343 lines), but its arithmetic already lives
 * in `features/stations/lib/` — radar, ranking and the search filter have 258 lines of
 * tests beside 286 lines of code. What is untested here is the screen's own decisions,
 * and three of them fail visibly:
 *
 * - **no CARTO key means no raster overlay at all.** CARTO's public CDN watermarks
 *   unauthenticated tiles with "API KEY REQUIRED". Rendering an overlay without a key
 *   therefore paints that watermark across the whole map — which is exactly why the
 *   fallback is to the native basemap rather than to a free-looking URL.
 * - **the tile style follows the theme.** A dark theme must not get light tiles, or the
 *   map inverts against the rest of the app at night.
 * - **an unknown fuel id still gets a readable label.** `FUEL_LABELS` is a fixed table
 *   and the API can add a fuel the app has never heard of; the fallback is uppercased
 *   rather than blank, so a new grade shows up instead of rendering nothing.
 */

const mockRequest = jest.fn();
const mockResolveCartoApiKey = jest.fn();

jest.mock('expo-router', () => ({
  Redirect: ({ href }: { href: string }) => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const { Text } = require('react-native');
    return <Text testID="redirect">{href}</Text>;
  },
  Stack: { Screen: () => null },
  Tabs: { Screen: () => null },
  router: { push: jest.fn(), replace: jest.fn(), back: jest.fn(), navigate: jest.fn() },
  useLocalSearchParams: () => ({}),
  useGlobalSearchParams: () => ({}),
  usePathname: () => '/map',
  useSegments: () => [],
  useRootNavigationState: () => ({
    key: 'root',
    index: 0,
    routes: [],
    stale: false,
    type: 'stack',
  }),
  useNavigation: () => ({ setOptions: jest.fn() }),
  useNavigationState: () => ({ index: 0 }),
  useIsFocused: () => true,
  useFocusEffect: () => {},
  Link: () => null,
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = { t: (key: string) => key, language: 'uk', setLanguage: jest.fn() };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/features/stations/hooks/useUserLocation', () => ({
  useUserLocation: () => ({
    location: { lat: 50.45, lng: 30.52 },
    status: 'granted',
    request: mockRequest,
  }),
}));

jest.mock('../src/features/stations/lib/basemap', () => ({
  resolveCartoApiKey: () => mockResolveCartoApiKey(),
}));

jest.mock('../src/features/stations/hooks/useStationNodes', () => ({
  useStationNodes: () => ({ data: [], isLoading: false, error: null }),
}));

/**
 * Packages drive the fuel selector: `availableFuels(packages)` derives the chips.
 * Written to a variable so a test can offer a fuel the label table has never heard of.
 *
 * Labels are asserted with `\\u` escapes rather than literal Cyrillic: the test file
 * passes through shells that mangle non-ASCII on output, and a mangled expectation
 * would fail for a reason that has nothing to do with the code.
 */
let mockPackages: unknown[] = [];

jest.mock('../src/features/stations/hooks/useAllPackages', () => ({
  useAllPackages: () => ({ data: mockPackages, isLoading: false }),
}));

jest.mock('../src/features/stations/components/NavigatorPickerSheet', () => ({
  NavigatorPickerSheet: () => null,
}));

// `react-native-maps` is mocked rather than real: the suite asserts on which tile
// overlay the screen chose, not on rendering thousands of pins.
jest.mock('react-native-maps', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const React2 = require('react');
  const { View, Text } = require('react-native');

  /**
   * The screen drives the map imperatively (`mapRef.current?.animateToRegion`), so the
   * mock has to expose those methods on its ref. Without them the render dies inside a
   * mount effect, several frames from the missing method.
   */
  const MapView = React2.forwardRef((props: Record<string, any>, ref: unknown) => {
    React2.useImperativeHandle(ref, () => ({
      animateToRegion: jest.fn(),
      animateCamera: jest.fn(),
      fitToCoordinates: jest.fn(),
      fitToSuppliedMarkers: jest.fn(),
      getCamera: jest.fn(() => ({ center: { latitude: 0, longitude: 0 }, zoom: 1 })),
      setMapPadding: jest.fn(),
      pointForCoordinate: jest.fn(() => ({ x: 0, y: 0 })),
      coordinateForPoint: jest.fn(() => ({ latitude: 0, longitude: 0 })),
    }));
    return <View testID="map">{props.children}</View>;
  });
  MapView.displayName = 'MapView';

  return {
    __esModule: true,
    default: MapView,
    MapView,
    UrlTile: (props: Record<string, any>) => <Text testID="tile">{String(props.urlTemplate)}</Text>,
    Marker: (props: Record<string, any>) => <View testID="marker" {...props} />,
    Callout: (props: Record<string, any>) => <View testID="callout" {...props} />,
    Circle: () => null,
    Polyline: () => null,
  };
});

// `BottomSheet` (reached through `core/ui`) pulls in gesture-handler, which loads
// reanimated, whose native worklets module throws under Jest. Both are mocked here for
// this file; the first screen test that needs them should move these into
// `jest.setup.js` alongside the other shared stubs.
//
// The reanimated mock is hand-written rather than `react-native-reanimated/mock`,
// because that file itself initialises the worklets module and throws the same error.
jest.mock('react-native-reanimated', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const { View, Text, ScrollView } = require('react-native');
  return {
    __esModule: true,
    default: { View, Text, ScrollView, call: () => {} },
    View,
    Text,
    ScrollView,
    createAnimatedComponent: (c: unknown) => c,
    useSharedValue: (v: unknown) => ({ value: v }),
    useDerivedValue: (fn: () => unknown) => ({ value: fn() }),
    useAnimatedStyle: (fn: () => unknown) => fn(),
    useAnimatedRef: () => ({ current: null }),
    useAnimatedGestureHandler: () => ({}),
    withSpring: (v: unknown) => v,
    withTiming: (v: unknown) => v,
    withDelay: (_d: unknown, v: unknown) => v,
    withRepeat: (v: unknown) => v,
    runOnJS: (fn: unknown) => fn,
    runOnUI: (fn: unknown) => fn,
    interpolate: () => 0,
    Easing: { linear: (v: number) => v, ease: (v: number) => v, inOut: (v: number) => v },
    FadeIn: {},
    FadeOut: {},
    Layout: {},
  };
});

jest.mock('react-native-worklets', () => ({
  __esModule: true,
  runOnJS: (fn: unknown) => fn,
  runOnUI: (fn: unknown) => fn,
  createWorkletRuntime: () => ({}),
  isSharedValue: () => false,
  makeShareableCloneRecursive: (v: unknown) => v,
}));

jest.mock('react-native-gesture-handler', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const { View: RNView } = require('react-native');
  return {
    GestureHandlerRootView: RNView,
    GestureDetector: ({ children }: { children?: unknown }) => <RNView>{children}</RNView>,
    PanGestureHandler: RNView,
    TapGestureHandler: RNView,
    ScrollView: RNView,
    FlatList: RNView,
    Gesture: {
      Tap: () => ({ onBegin: () => ({}), onEnd: () => ({}), enabled: () => ({}) }),
      Pan: () => ({
        onBegin: () => ({}),
        onUpdate: () => ({}),
        onEnd: () => ({}),
        enabled: () => ({}),
      }),
      Native: () => ({ onBegin: () => ({}), onEnd: () => ({}), enabled: () => ({}) }),
    },
    State: {},
    Directions: {},
    ScrollViewProps: {},
  };
});

jest.mock('expo-blur', () => ({
  BlurView: (props: Record<string, any>) => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const { View: RNView } = require('react-native');
    return <RNView {...props} />;
  },
}));

/**
 * The screen reads `useQueryClient()` directly (it calls `queryClient.invalidateQueries`
 * after refetching), so it is rendered inside a real provider rather than with
 * react-query mocked — a mock here would hide the invalidation calls this suite is
 * positioned to check later.
 */
/**
 * The tile style is chosen from `tokens.colors.isDark`, which comes from the theme in
 * the app store. Only `getTokens` is wrapped — it is the single function that turns a
 * theme into tokens, so overriding it switches the whole palette coherently, where
 * mocking the token object would have to invent a hundred paths.
 *
 * It ignores the theme it is handed and uses `mockTheme`, because `useDesignTokens`
 * always passes the store's value (defaulting to `lemberg`), which would make the
 * argument win and leave the test on the default palette.
 */
let mockTheme = 'lemberg';

jest.mock('../src/core/design/tokens', () => {
  const actual = jest.requireActual('../src/core/design/tokens');
  return {
    ...actual,
    getTokens: () => actual.getTokens(mockTheme),
  };
});

function renderInTheme(theme: string) {
  mockTheme = theme;
  try {
    return renderScreen();
  } finally {
    mockTheme = 'lemberg';
  }
}

function renderScreen() {
  mockResolveCartoApiKey.mockReturnValue('test-key');
  mockRequest.mockReset();
  return render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <MapScreen />
    </QueryClientProvider>,
  );
}

function renderWithoutKey(key: string | null) {
  mockResolveCartoApiKey.mockReturnValue(key);
  mockRequest.mockReset();
  return render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <MapScreen />
    </QueryClientProvider>,
  );
}

describe('MapScreen', () => {
  describe('the raster basemap overlay', () => {
    it('renders no tile overlay when no CARTO key is configured', () => {
      // The failure this prevents: CARTO watermarks unauthenticated tiles with
      // "API KEY REQUIRED", so an overlay without a key paints that across the map.
      // Falling back to the native basemap is a plain but clean map.
      renderWithoutKey(null);

      expect(screen.queryByTestId('tile')).toBeNull();
      expect(screen.getByTestId('map')).toBeTruthy();
    });

    it('renders no tile overlay when the key is an empty string', () => {
      // A blank env var is the common way to end up here, and it is falsy — worth
      // pinning so someone does not "fix" it into a truthiness check that misses it.
      renderWithoutKey('');

      expect(screen.queryByTestId('tile')).toBeNull();
    });

    it('requests a tile overlay when a key is present', () => {
      renderScreen();
      expect(screen.getByTestId('tile')).toBeTruthy();
    });

    it('passes the key through in the tile URL', () => {
      renderScreen();
      expect(screen.getByTestId('tile')).toHaveTextContent(/key=test-key/);
    });

    it('serves dark tiles for a dark theme', () => {
      // `lemberg` is the default and is a dark theme (`isDark: true` in themes.ts). A
      // dark theme with light tiles inverts the map against the rest of the app at
      // night, which is worse than no overlay at all.
      renderInTheme('lemberg');
      expect(screen.getByTestId('tile')).toHaveTextContent(/dark_all/);
    });

    it('serves light tiles for a light theme', () => {
      renderInTheme('white');
      expect(screen.getByTestId('tile')).toHaveTextContent(/voyager/);
    });
  });

  describe('locating the user', () => {
    it('requests a location fix when locate is used', () => {
      renderScreen();

      const locate = screen.getByLabelText(/map.locate/i);
      act(() => {
        fireEvent.press(locate);
      });

      expect(mockRequest).toHaveBeenCalled();
    });

    it('does not request a fix merely by rendering', () => {
      // Asking for location on mount would prompt a permission dialog the moment the
      // tab opens, before the user has indicated they want to be located.
      renderScreen();
      expect(mockRequest).not.toHaveBeenCalled();
    });
  });

  describe('the fuel selector', () => {
    /** Renders the selector for the given fuel names and returns the chip labels. */
    function chipsFor(fuelNames: string[]): string[] {
      mockPackages = fuelNames.map((fuelName, i) => ({
        id: 'pkg-' + i,
        fuelName,
        price: 4000 + i,
        isActive: true,
      }));
      renderScreen();
      return screen
        .getAllByText(/.*/)
        .map((n) => String(n.props.children))
        .filter((s) => s.length > 0 && s.length < 24);
    }

    it('labels a fuel the app knows, in Ukrainian', () => {
      const labels = chipsFor(['А-95']);
      // U+0410 is Cyrillic capital A. Asserted as a prefix so the dash the table uses
      // does not have to be duplicated here.
      expect(labels.some((l) => l.startsWith('\u0410'))).toBe(true);
    });

    it('shows a readable uppercase label for a fuel the app has never seen', () => {
      // FUEL_LABELS is a fixed table and the API is not, so a new grade must still
      // render something. The fallback uppercases the canonical id rather than leaving
      // the chip blank, which would look like a broken selector.
      const labels = chipsFor(['RacingFuel']);
      expect(labels.some((l) => l === 'RACINGFUEL' || l === 'RACING FUEL')).toBe(true);
    });

    it('never renders an empty chip for an unknown fuel', () => {
      const labels = chipsFor(['RacingFuel', 'AnotherNewGrade']);
      expect(labels.filter((l) => l.trim() === '')).toHaveLength(0);
    });
  });
});
