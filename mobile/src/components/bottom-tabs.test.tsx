import React from 'react';
import { StyleSheet } from 'react-native';
import { render, screen, fireEvent, waitFor } from '@testing-library/react-native';

import { getTokens } from '../core/design/tokens';
import { Haptics } from '../core/utils/haptics';
import { BottomTabs } from './bottom-tabs';

/**
 * The shape of a rendered node that these tests touch. Typed structurally on
 * purpose: `react-test-renderer` ships no declarations and `@types/` for it is
 * not installed, and this file is inside the `tsc --noEmit` include set.
 */
type Node = { props: Record<string, any>; parent: Node | null };

/**
 * The tab bar is the app's most-used control and the one a thumb is least precise
 * with, so "did my finger land?" has to be answered on touch-down. These tests
 * pin the three things that answer it — the haptic timing, the pressed fill, and
 * a target with no dead space around it — so none of them can quietly regress
 * into "nothing happens when you miss".
 *
 * Everything below reaches the bar through its accessibility surface
 * (`getByRole('tab')`) rather than through the icon tree, so the assertions are
 * about what a user can hit, not about how it is drawn.
 */

let mockPathname = '/';
let mockIsAuthenticated = true;
let mockHookAuth = true;
let mockContextKind: 'personal' | 'owner' | 'worker' = 'personal';

jest.mock('expo-router', () => {
  const { cloneElement } = jest.requireActual<typeof import('react')>('react');
  return {
    __esModule: true,
    usePathname: () => mockPathname,
    // Stands in for expo-router's `asChild` path, which renders through Radix's
    // `Slot`: the child's own props win, and the router injects only `onPress`
    // for navigation. Reproducing that matters here, because the press contract
    // below only holds if the wrapper passes the child's `onPressIn`,
    // `android_ripple` and function `style` straight through.
    Link: ({ children, ...rest }: any) => cloneElement(children, { ...rest, onPress: () => {} }),
  };
});

// The icon glyphs carry no tab-bar behaviour; `accessibilityLabel` is the handle
// these tests use, so rendering them as null keeps the query surface honest.
jest.mock('lucide-react-native', () => {
  const Icon = () => null;
  return {
    __esModule: true,
    Home: Icon,
    MapPin: Icon,
    ShoppingCart: Icon,
    QrCode: Icon,
    User: Icon,
  };
});

jest.mock('react-native-safe-area-context', () => ({
  useSafeAreaInsets: () => ({ top: 0, bottom: 34, left: 0, right: 0 }),
}));

// `useStore` is called twice: directly for auth, and inside `useTheme` for the
// theme name. One state object answers both selectors.
jest.mock('../core/state/appStore', () => ({
  useStore: (selector: (s: any) => unknown) =>
    selector({ isAuthenticated: mockIsAuthenticated, theme: 'lemberg' }),
}));

jest.mock('../features/auth/hooks/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: mockHookAuth }),
}));

jest.mock('../features/cart/store/cartStore', () => ({
  useCartStore: (selector: (s: any) => unknown) => selector({ getCartItemCount: () => 0 }),
}));

jest.mock('../features/notifications/hooks/useNotifications', () => ({
  useUnreadNotificationCount: () => 0,
}));

jest.mock('../core/i18n', () => ({
  useI18n: (selector: (s: any) => unknown) => selector({ t: (key: string) => key }),
}));

// Pulled in for the worker-context tab rule (epic #103 S5). The real hook reaches
// for `react-native-device-info`, which needs a native module and cannot load
// under Jest, so only the `kind` the bar actually branches on is supplied.
jest.mock('../features/company/hooks/useAccountContext', () => ({
  useAccountContext: () => ({ kind: mockContextKind }),
}));

// The `core/ui` barrel re-exports `ErrorBoundary`, which reaches for Sentry. The
// SDK resolves to ESM under Jest and needs a native module anyway, so it is
// stubbed rather than made transformable; nothing under test depends on it.
jest.mock('@sentry/react-native', () => ({
  init: jest.fn(),
  captureException: jest.fn(),
  captureMessage: jest.fn(),
  addBreadcrumb: jest.fn(),
  setUser: jest.fn(),
  setTag: jest.fn(),
  setContext: jest.fn(),
  withScope: (cb: (scope: unknown) => unknown) => cb({ setTag: jest.fn() }),
}));

const tokens = getTokens('lemberg');

/** The `i18n` mock echoes keys back, so a tab's accessible name is its key. */
const TAB_NAMES = ['nav.stations', 'map.title', 'nav.basket', 'nav.codes', 'nav.profile'] as const;

type TabName = (typeof TAB_NAMES)[number];

function tab(name: TabName) {
  return screen.getByRole('tab', { name });
}

function flat(el: Node): Record<string, any> {
  return StyleSheet.flatten(el.props.style) ?? {};
}

/**
 * `Pressable` consumes `android_ripple` and folds it into the host `View` before
 * it ever reaches the tree, so the only place to read it is the `Pressable`
 * element itself — one level up from the host view the queries return.
 */
function pressableOf(name: TabName): Record<string, any> {
  let node: Node | null = tab(name);
  while (node && !('android_ripple' in node.props)) {
    node = node.parent;
  }
  return (node?.props ?? {}) as Record<string, any>;
}

/** A gesture event shaped enough for `Pressability`, which pools and persists it. */
function touch() {
  const nativeEvent = {
    touches: [] as unknown[],
    changedTouches: [] as unknown[],
    identifier: 1,
    pageX: 20,
    pageY: 10,
    locationX: 20,
    locationY: 10,
    target: 1,
    timestamp: 0,
  };
  return {
    ...nativeEvent,
    nativeEvent,
    currentTarget: 1,
    persist: () => {},
  };
}

/**
 * Touch-down and touch-up, driven through the responder props `Pressable`
 * actually listens on. `fireEvent(el, 'pressIn')` would find the `onPressIn`
 * prop passed in from above and call it directly, skipping `Pressability`
 * entirely — so the `pressed` flag behind the style function would never flip,
 * and the press itself would be untestable. `responderGrant` is the same path a
 * real finger takes.
 */
const touchDown = (name: TabName) => fireEvent(tab(name), 'responderGrant', touch());
const touchUp = (name: TabName) => fireEvent(tab(name), 'responderRelease', touch());

beforeEach(() => {
  mockPathname = '/';
  mockIsAuthenticated = true;
  mockHookAuth = true;
  mockContextKind = 'personal';
  jest.spyOn(Haptics, 'impactAsync').mockResolvedValue(undefined as any);
});

describe('BottomTabs — press feedback', () => {
  it('confirms the touch the moment a tab is pressed, not when the finger lifts', () => {
    render(<BottomTabs />);

    expect(Haptics.impactAsync).not.toHaveBeenCalled();

    touchDown('nav.profile');

    expect(Haptics.impactAsync).toHaveBeenCalledTimes(1);
  });

  it('uses a Medium impact, because Light is a tick too short to feel under a moving thumb', () => {
    render(<BottomTabs />);

    touchDown('nav.profile');

    expect(Haptics.impactAsync).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Medium);
  });

  it('does not buzz again on release', () => {
    render(<BottomTabs />);

    touchDown('nav.profile');
    touchUp('nav.profile');

    expect(Haptics.impactAsync).toHaveBeenCalledTimes(1);
  });

  it('fills the tab while it is held and clears it again on release', async () => {
    render(<BottomTabs />);

    expect(flat(tab('map.title')).backgroundColor).toBeUndefined();

    touchDown('map.title');
    expect(flat(tab('map.title')).backgroundColor).toBe(tokens.colors.primarySubtle);

    touchUp('map.title');
    // `Pressability` holds `onPressOut` back by `minPressDuration` (130ms) so a
    // quick tap still registers as a press, so the fill clears a beat after the
    // finger is up.
    await waitFor(() => expect(flat(tab('map.title')).backgroundColor).toBeUndefined());
  });

  it('rounds the pressed fill into a pill, so the target boundary is visible', () => {
    render(<BottomTabs />);

    touchDown('map.title');

    expect(flat(tab('map.title')).borderRadius).toBe(tokens.radius.full);
  });

  it('confirms a press on every tab, not only the selected one', () => {
    render(<BottomTabs />);

    for (const name of TAB_NAMES) {
      touchDown(name);
      expect(flat(tab(name)).backgroundColor).toBe(tokens.colors.primarySubtle);
      touchUp(name);
    }
  });

  it('gives Android a ripple bounded to the tab, instead of no feedback at all', () => {
    render(<BottomTabs />);

    const ripple = pressableOf('nav.codes').android_ripple;

    expect(ripple).toBeDefined();
    expect(ripple.borderless).toBe(false);
    expect(ripple.color).toBe(tokens.colors.primarySubtle);
  });
});

describe('BottomTabs — target size', () => {
  it('stretches every tab to fill its share of the row', () => {
    render(<BottomTabs />);

    for (const name of TAB_NAMES) {
      expect(flat(tab(name)).flex).toBe(1);
    }
  });

  it('leaves no unowned space along the row for a thumb to land in', () => {
    render(<BottomTabs />);

    const bar = flat(screen.getByTestId('bottom-tab-bar'));

    // `space-around` plus outer padding is what produced the ~8.6pt dead gutter
    // on each side of every tab; neither may come back.
    expect(bar.justifyContent).not.toBe('space-around');
    expect(bar.paddingHorizontal).toBeUndefined();
  });

  it('floors each tab at the 44pt minimum, so a narrow screen cannot shrink it below', () => {
    render(<BottomTabs />);

    for (const name of TAB_NAMES) {
      expect(flat(tab(name)).minWidth).toBe(tokens.touchTarget.min);
    }
    expect(tokens.touchTarget.min).toBeGreaterThanOrEqual(44);
  });

  it('does not overlap neighbouring targets with hitSlop, which would steal the wrong tab', () => {
    render(<BottomTabs />);

    // Overlapping `hitSlop` makes RN resolve the winner by z-order rather than by
    // proximity, turning a near miss into pressing the wrong tab. `flex: 1`
    // leaves no dead space for slop to usefully cover.
    for (const name of TAB_NAMES) {
      expect(tab(name).props.hitSlop).toBeUndefined();
    }
  });
});

describe('BottomTabs — rendering', () => {
  it('offers one tab per destination', () => {
    render(<BottomTabs />);

    expect(screen.getAllByRole('tab')).toHaveLength(TAB_NAMES.length);
    for (const name of TAB_NAMES) {
      expect(screen.getByRole('tab', { name })).toBeTruthy();
    }
  });

  it('marks only the current destination as selected', () => {
    mockPathname = '/map';
    render(<BottomTabs />);

    expect(tab('map.title').props.accessibilityState.selected).toBe(true);
    expect(tab('nav.profile').props.accessibilityState.selected).toBe(false);
  });

  it('drops the basket tab in a worker context (epic #103 S5)', () => {
    mockContextKind = 'worker';
    render(<BottomTabs />);

    expect(screen.queryByRole('tab', { name: 'nav.basket' })).toBeNull();
  });

  it('keeps every remaining tab full-width when a worker context removes one', () => {
    mockContextKind = 'worker';
    render(<BottomTabs />);

    // The share-out has to survive a changing tab count, or dropping the basket
    // tab would silently reintroduce dead space into the row.
    const remaining: TabName[] = ['nav.stations', 'map.title', 'nav.codes', 'nav.profile'];
    for (const name of remaining) {
      expect(flat(tab(name)).flex).toBe(1);
      expect(flat(tab(name)).minWidth).toBe(tokens.touchTarget.min);
    }
  });

  it('renders nothing on a route that hides the bar', () => {
    mockPathname = '/checkout';
    render(<BottomTabs />);

    expect(screen.queryByTestId('bottom-tab-bar')).toBeNull();
  });

  it('renders nothing when signed out, whichever source the session is read from', () => {
    // `BottomTabs` treats the store and the auth hook as interchangeable, so
    // "signed out" has to mean both of them are false.
    mockIsAuthenticated = false;
    mockHookAuth = false;
    render(<BottomTabs />);

    expect(screen.queryByTestId('bottom-tab-bar')).toBeNull();
  });
});
