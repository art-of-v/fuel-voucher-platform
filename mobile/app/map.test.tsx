import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { MARKER_POOL_SIZE, MAX_MOUNTED_MARKERS } from '../src/features/stations/lib/viewport';
import { MAP_TAP_SETTLE_MS } from '../src/features/stations/lib/selection';
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

/** Every gesture object the sheet handed to `GestureDetector`, in render order. */
const mockGestures: unknown[] = [];

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

let mockNodes: Record<string, unknown>[] = [];

/** The corners the mocked map reports: central Kyiv, 0.1° tall and 0.2° wide. */
const KYIV_BOUNDARIES = {
  northEast: { latitude: 50.5, longitude: 30.6 },
  southWest: { latitude: 50.4, longitude: 30.4 },
};

let mockBoundaries = KYIV_BOUNDARIES;

jest.mock('../src/features/stations/hooks/useStationNodes', () => ({
  useStationNodes: () => ({ data: mockNodes, isLoading: false, error: null }),
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
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- jest.mock factories are hoisted above imports
  const React2 = require('react');
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- jest.mock factories are hoisted above imports
  const { View, Text } = require('react-native');

  /**
   * The screen drives the map imperatively (`mapRef.current?.animateToRegion`,
   * `getMapBoundaries`, `animateCamera`), so the mock has to expose those methods on its
   * ref. Without them the render dies inside a mount effect, several frames from the
   * missing method.
   *
   * The object is shared rather than rebuilt per render so a test can assert on the calls
   * the screen made; `__mapApi` is where those spies are reachable from the suite.
   */
  const mapApi = {
    animateToRegion: jest.fn(),
    animateCamera: jest.fn(),
    fitToCoordinates: jest.fn(),
    fitToSuppliedMarkers: jest.fn(),
    getCamera: jest.fn(() => ({ center: { latitude: 0, longitude: 0 }, zoom: 1 })),
    // Kept in a variable: the screen asks the map what it is showing before it picks its pins, and
    // a test that opens the map somewhere other than Kyiv says so here.
    getMapBoundaries: jest.fn(() => mockBoundaries),
    setMapPadding: jest.fn(),
    pointForCoordinate: jest.fn(() => ({ x: 0, y: 0 })),
    coordinateForPoint: jest.fn(() => ({ latitude: 0, longitude: 0 })),
  };

  const MapView = React2.forwardRef((props: Record<string, any>, ref: unknown) => {
    React2.useImperativeHandle(ref, () => mapApi, []);
    return (
      <View testID="map" onMapReady={props.onMapReady} onPress={props.onPress}>
        {props.children}
      </View>
    );
  });
  MapView.displayName = 'MapView';

  return {
    __esModule: true,
    default: MapView,
    MapView,
    UrlTile: (props: Record<string, any>) => <Text testID="tile">{String(props.urlTemplate)}</Text>,
    Marker: (props: Record<string, any>) => <View testID="marker" {...props} />,
    Circle: () => null,
    Polyline: () => null,
    __mapApi: mapApi,
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
  const React2 = require('react');
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const { View, Text, ScrollView } = require('react-native');
  return {
    __esModule: true,
    default: { View, Text, ScrollView, call: () => {} },
    View,
    Text,
    ScrollView,
    createAnimatedComponent: (c: unknown) => c,
    // The real hook hands back the *same* object on every render — components key their memos
    // on that identity — so a mock returning a fresh one would look like a stale-closure bug.
    useSharedValue: (initial: unknown) => {
      const ref = React2.useRef({ value: initial });
      return ref.current;
    },
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

  /**
   * A stand-in for a gesture builder: every method returns the same object, so a chain like
   * `Gesture.Pan().onStart(…).onUpdate(…).onEnd(…)` works and the *identity* of the result is
   * what `GestureDetector` is handed — which is the thing the sheet test below measures.
   */
  function mockChainGesture() {
    const gesture: Record<string, unknown> = {};
    for (const method of [
      'onBegin',
      'onStart',
      'onUpdate',
      'onEnd',
      'onFinalize',
      'enabled',
      'minDistance',
      'hitSlop',
      'maxPointers',
    ]) {
      gesture[method] = () => gesture;
    }
    return gesture;
  }

  return {
    GestureHandlerRootView: RNView,
    GestureDetector: ({ gesture, children }: { gesture?: unknown; children?: unknown }) => {
      mockGestures.push(gesture);
      return <RNView>{children}</RNView>;
    },
    PanGestureHandler: RNView,
    TapGestureHandler: RNView,
    ScrollView: RNView,
    FlatList: RNView,
    Gesture: {
      Tap: mockChainGesture,
      Pan: mockChainGesture,
      Native: mockChainGesture,
      LongPress: mockChainGesture,
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
 * after refetching), so it is rendered inside a real brand rather than with
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
 * always passes the store's value (defaulting to `obsidian`), which would make the
 * argument win and leave the test on the default palette.
 */
let mockTheme = 'obsidian';

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
    mockTheme = 'obsidian';
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
      // `obsidian` is the default and is a dark theme (`isDark: true` in themes.ts). A
      // dark theme with light tiles inverts the map against the rest of the app at
      // night, which is worse than no overlay at all.
      renderInTheme('obsidian');
      expect(screen.getByTestId('tile')).toHaveTextContent(/dark_all/);
    });

    it('serves light tiles for a light theme', () => {
      renderInTheme('white');
      expect(screen.getByTestId('tile')).toHaveTextContent(/voyager/);
    });

    it('mounts the overlay after the markers, so a late key appends instead of re-indexing', async () => {
      // The array the interop layer replays mounts into is the map's own list of children, and a
      // child that appears after mount is only harmless at the end of it. `resolveCartoApiKey` can
      // answer late in a native build — it reads `Constants.expoConfig`, which is not there for the
      // first renders — so this overlay really does appear once. In the first position that re-indexes
      // all 120 markers, which is the crash the pool exists to avoid, arriving through a component the
      // pool does not control.
      mockNodes = Array.from({ length: 5 }, (_, i) => ({
        id: `n-${i}`,
        brandSlug: 'klo',
        name: `KLO ${i}`,
        address: 'вулиця Тестова, 1',
        lat: 50.45,
        lng: 30.52,
      }));
      renderScreen();
      // The map is asked what it shows before the pool is built, so the answer arrives a
      // microtask after the event.
      await act(async () => {
        fireEvent(screen.getByTestId('map'), 'mapReady');
      });
      mockNodes = [];

      // eslint-disable-next-line @typescript-eslint/no-require-imports -- the mocked exports are the element types to compare against
      const { UrlTile } = require('react-native-maps');
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      const { StationMarker } = require('../src/features/stations/components/StationMarker');
      const children = React.Children.toArray(screen.getByTestId('map').props.children);
      const types = children.map((child) => (child as { type: unknown }).type);

      expect(types.lastIndexOf(StationMarker)).toBeGreaterThanOrEqual(0);
      expect(types.indexOf(UrlTile)).toBe(types.length - 1);
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

  describe('panning with the ranking sheet open', () => {
    /** `count` АЗК on an even lattice, so pulling the map back really does fill the region. */
    function lattice(count: number) {
      const side = Math.ceil(Math.sqrt(count));
      return Array.from({ length: count }, (_, i) => ({
        id: `n-${i}`,
        brandId: 'b-klo',
        brandSlug: 'klo',
        name: `KLO ${i}`,
        address: 'вулиця Тестова, 1',
        lat: 48.2 + Math.floor(i / side) * (3.4 / side),
        lng: 29.5 + (i % side) * (3.4 / side),
      }));
    }

    const WIDE: Record<string, number> = {
      latitude: 50,
      longitude: 31,
      latitudeDelta: 4,
      longitudeDelta: 4,
    };

    /**
     * How many of the mounted markers are showing a pin. The mock spreads the marker's props onto a
     * view, so `opacity` — which the pool sets to 0 for a slot with no station — is what says it.
     */
    const pinsShown = () =>
      screen.getAllByTestId('marker').filter((pin) => pin.props.opacity === 1).length;

    afterEach(() => {
      mockNodes = [];
    });

    it('mounts the pool once the map has rendered, already carrying its pins', async () => {
      // Two facts, both from the interop layer this build ships. A `Marker` is a legacy view manager
      // hosted by a Fabric interop component, and inserting or removing one replays a child-index
      // change into the map's own array of subviews — when that array and the shadow tree disagree
      // the app dies in Objective-C, twice over (`object cannot be nil`, `index beyond bounds`), with
      // no JS error to read. So the pool arrives once, already carrying the region's pins, and is never
      // touched again. (Which view MapKit draws for each of them is settled by the marker's `image`, not
      // by this schedule — see `StationMarker`.)
      mockNodes = lattice(1000);
      renderScreen();

      const map = screen.getByTestId('map');
      expect(screen.queryAllByTestId('marker')).toHaveLength(0);

      await act(async () => {
        fireEvent(map, 'mapReady');
      });

      // The first thing mounted is the whole pool, with the region's АЗК already in it.
      expect(screen.getAllByTestId('marker')).toHaveLength(MARKER_POOL_SIZE);
      expect(pinsShown()).toBeGreaterThan(0);
      expect(pinsShown()).toBeLessThan(20);

      // Every slot carries the image, not only the ones with a pin. The pool never adds a marker again,
      // so a slot that was born without it is Apple's pin for life — including the day it is handed an
      // АЗК, which is exactly the pan that would otherwise put a red drop on the map.
      const images = screen
        .getAllByTestId('marker')
        .map((pin) => String((pin.props as any).image?.uri));
      expect(new Set(images).size).toBe(1);
      expect(images[0]).toMatch(/^data:image\/png;base64,/);

      act(() => {
        fireEvent(map, 'regionChangeComplete', WIDE);
      });

      // A whole country on screen: the pins thin, the markers do not change.
      expect(screen.getAllByTestId('marker')).toHaveLength(MARKER_POOL_SIZE);
      expect(pinsShown()).toBeGreaterThan(0);
      expect(pinsShown()).toBeLessThanOrEqual(MAX_MOUNTED_MARKERS);
    });

    it('keeps the ranking sheet standing through a map pan', async () => {
      // `GestureDetector` keys its native handler on the gesture object, so a gesture built
      // during render is disposed and re-attached on every re-render — and a map pan
      // re-renders the screen the sheet sits in. RNGH documents the object as stable for
      // exactly that reason, and the sheet is the only thing on this screen that holds one.
      mockNodes = lattice(200);
      renderScreen();

      // The map is taken before the sheet opens: the sheet is `accessibilityViewIsModal`, so
      // once it is up the map is outside what queries can see — the same thing VoiceOver does.
      const map = screen.getByTestId('map');
      await act(async () => {
        fireEvent(map, 'mapReady');
      });
      fireEvent.press(screen.getByRole('button', { name: 'map.networkRanking' }));
      mockGestures.length = 0;

      act(() => {
        fireEvent(map, 'regionChangeComplete', WIDE);
      });
      act(() => {
        fireEvent(map, 'regionChangeComplete', { ...WIDE, latitude: 49.5 });
      });

      expect(screen.getByText('map.networkRanking')).toBeTruthy();
      expect(mockGestures.length).toBeGreaterThanOrEqual(2);
      expect(new Set(mockGestures).size).toBe(1);
    });
  });
});

/**
 * What a tap on a pin has to do, from the person's side.
 *
 * The two failures pinned here were both "the tap did something, just not what it looked
 * like": tapping a neighbouring АЗК closed the open card and opened nothing (the map's own
 * press was allowed to clear the selection before MapKit reported the new one), and the card
 * opened over the pin, so a tap that landed read the same as a tap that missed.
 */
describe('MapScreen - tapping a pin', () => {
  const STATION = {
    brandId: 'b-klo',
    brandSlug: 'klo',
    address: 'вулиця Тестова, 1',
  };

  const A = { ...STATION, id: 'a', name: 'KLO A', lat: 50.45, lng: 30.52 };
  const B = { ...STATION, id: 'b', name: 'KLO B', lat: 50.452, lng: 30.53 };

  /** The markers currently showing a pin, in pool order. */
  const pins = () => screen.getAllByTestId('marker').filter((pin) => pin.props.opacity === 1);

  /** The pin whose accessibility label names `name`; the pool's slot order is not the input order. */
  const pinFor = (name: string) =>
    pins().find((pin) => String(pin.props.accessibilityLabel ?? '').startsWith(name));

  beforeEach(() => {
    jest.clearAllMocks();
  });

  afterEach(() => {
    mockNodes = [];
    jest.useRealTimers();
  });

  async function openStation(name: string) {
    renderScreen();
    // Taken before the card opens: it is `accessibilityViewIsModal`, so once it is up the map
    // is outside what queries can see - the same thing VoiceOver does.
    const map = screen.getByTestId('map');
    await act(async () => {
      fireEvent(map, 'mapReady');
    });
    const pin = pinFor(name);
    expect(pin).toBeTruthy();
    await act(async () => {
      fireEvent.press(pin!);
    });
    return { map, pin: pin! };
  }

  it('swaps the card to the neighbouring station in the same tap', async () => {
    mockNodes = [A, B];
    renderScreen();
    await act(async () => {
      fireEvent(screen.getByTestId('map'), 'mapReady');
    });
    const first = pinFor('KLO A');
    const second = pinFor('KLO B');
    expect(first && second).toBeTruthy();

    await act(async () => {
      fireEvent.press(first!);
    });
    expect(screen.getByText('KLO A')).toBeTruthy();

    await act(async () => {
      fireEvent.press(second!);
    });

    // The new station replaces the old one in one state change: no frame in which the
    // previous card has closed and nothing has opened yet.
    expect(screen.getByText('KLO B')).toBeTruthy();
    expect(screen.queryByText('KLO A')).toBeNull();
  });

  it('closes the card on a tap on empty map, after the settle window', async () => {
    jest.useFakeTimers();
    mockNodes = [A];
    const { map, pin } = await openStation('KLO A');
    expect(screen.getByText('KLO A')).toBeTruthy();

    // The tap that leaves a pin reaches the screen twice: MapKit deselects the pin on the touch
    // itself (`didDeselectAnnotationView:`), and the map reports the press once its double-tap
    // recogniser gives up. Google Maps reports them in the same order (`MapView.java:357-359`).
    act(() => {
      fireEvent(pin, 'deselect');
    });
    fireEvent(map, 'press');
    // The press is still held: it can arrive before the deselection it belongs to.
    expect(screen.getByText('KLO A')).toBeTruthy();

    act(() => {
      jest.advanceTimersByTime(MAP_TAP_SETTLE_MS);
    });
    expect(screen.queryByText('KLO A')).toBeNull();
  });

  it('does not close a card the same tap opened, however late the map reports the tap', async () => {
    // The marker reports its tap at touch-up; the map reports the press only after its double-tap
    // recogniser gives up, and on a fresh launch the JS thread delays that report past any fixed
    // window. Timed against one, every one of the first taps after a launch opened the card and
    // had it dismissed again; the answer that does not drift is the map's own selection.
    jest.useFakeTimers();
    mockNodes = [A];
    const { map } = await openStation('KLO A');

    act(() => {
      jest.advanceTimersByTime(5000);
    });
    act(() => {
      fireEvent(map, 'press');
    });
    act(() => {
      jest.advanceTimersByTime(MAP_TAP_SETTLE_MS);
    });

    expect(screen.getByText('KLO A')).toBeTruthy();
  });

  it('lifts the map when the pin would be behind the card', async () => {
    // Bounds are 50.4-50.5 (a 0.1 degree span), so 50.44 sits at 60% of the height - under a
    // card that owns the bottom of the screen.
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- the mock's spies live on the module it returns
    const { __mapApi } = require('react-native-maps');
    mockNodes = [{ ...A, lat: 50.44 }];

    await openStation('KLO A');

    expect(__mapApi.animateCamera).toHaveBeenCalledTimes(1);
    const [camera] = __mapApi.animateCamera.mock.calls[0];
    expect(camera.center.latitude).toBeLessThan(50.44);
    expect(camera.center.longitude).toBeCloseTo(30.52, 5);
  });

  it('leaves the map alone when the pin is already in view', async () => {
    // 50.47 is 30% of the height - nothing is covering it.
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- the mock's spies live on the module it returns
    const { __mapApi } = require('react-native-maps');
    mockNodes = [{ ...A, lat: 50.47 }];

    await openStation('KLO A');

    expect(__mapApi.animateCamera).not.toHaveBeenCalled();
  });
});

/**
 * Which pins the map loads, from the person's side.
 *
 * The screen opens on Kyiv and centres on the user as soon as a position arrives - which, on a
 * phone whose position is ready before the map has drawn, MapKit applies without reporting a
 * region: the native module drops every region change until it has started rendering. A pool
 * chosen for the region the map was *opened* on is then a pool of АЗК nobody can see, and the map
 * stays empty until the first pan finally reports a region - the "the pins never load" report.
 */
describe('MapScreen - the pins the map picks', () => {
  const STATION = { brandId: 'b-klo', brandSlug: 'klo', address: 'вулиця Тестова, 1' };
  const KYIV_PIN = { ...STATION, id: 'kyiv', name: 'KLO KYIV', lat: 50.45, lng: 30.52 };
  const DNIPRO_PIN = { ...STATION, id: 'dnipro', name: 'KLO DNIPRO', lat: 48.4647, lng: 35.0462 };

  const DNIPRO_BOUNDARIES = {
    northEast: { latitude: 48.6, longitude: 35.2 },
    southWest: { latitude: 48.3, longitude: 34.9 },
  };

  /** The accessible names of the pins currently showing a station. */
  const shownNames = () =>
    screen
      .getAllByTestId('marker')
      .filter((pin) => pin.props.opacity === 1)
      .map((pin) => String(pin.props.accessibilityLabel ?? ''));

  afterEach(() => {
    mockBoundaries = KYIV_BOUNDARIES;
    mockNodes = [];
  });

  it('mounts the pins of the view the map reports, not the one it was opened on', async () => {
    mockBoundaries = DNIPRO_BOUNDARIES;
    mockNodes = [KYIV_PIN, DNIPRO_PIN];
    renderScreen();

    await act(async () => {
      fireEvent(screen.getByTestId('map'), 'mapReady');
    });

    const names = shownNames();
    expect(names.some((name) => name.startsWith('KLO DNIPRO'))).toBe(true);
    expect(names.some((name) => name.startsWith('KLO KYIV'))).toBe(false);
  });

  it('falls back to the region it was opened on when the map cannot say what it shows', async () => {
    // A map with no size yet reports a collapsed box. Mounting pins for it would leave the map
    // empty for good; the region it was opened on is the answer that still puts pins on screen.
    mockBoundaries = {
      northEast: { latitude: 0, longitude: 0 },
      southWest: { latitude: 0, longitude: 0 },
    };
    mockNodes = [KYIV_PIN];
    renderScreen();

    await act(async () => {
      fireEvent(screen.getByTestId('map'), 'mapReady');
    });

    expect(shownNames().some((name) => name.startsWith('KLO KYIV'))).toBe(true);
  });
});
