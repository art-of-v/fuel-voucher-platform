import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import NotificationsScreen from './notifications';
import { Haptics } from '../src/core/utils/haptics';

/**
 * The notifications list.
 *
 * An unread row can always be tapped — to clear it. An order-fulfilled row can
 * also be tapped to open that order in the wallet (the id is recovered from the
 * message, see utils/orderRef), read or not. A read row that names no order
 * carries no target, so a press handler on it would be a dead tap. The suite
 * pins which rows are actionable and where a tap goes.
 */

const mockMarkAsRead = jest.fn();
const mockMarkAllAsRead = jest.fn();
const mockRefetch = jest.fn();
const mockImpact = jest.fn();
const mockPush = jest.fn();

let mockState: ReturnType<typeof baseState>;
let mockStoreAuth = true;
let mockHookAuth = true;
let mockAuthLoading = false;

function baseState() {
  return {
    notifications: [] as any[],
    unreadCount: 0,
    isLoading: false,
    isError: false,
    isRefetching: false,
    refetch: mockRefetch,
    markAsRead: mockMarkAsRead,
    markAllAsRead: mockMarkAllAsRead,
    isMarkingAll: false,
  };
}

jest.mock('expo-router', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    useFocusEffect: () => {},
    useRouter: () => ({ push: mockPush }),
    Redirect: ({ href }: { href: string }) => R.createElement(Text, { testID: 'redirect' }, href),
  };
});

jest.mock('../src/features/notifications/hooks/useNotifications', () => ({
  useNotifications: () => mockState,
}));

jest.mock('../src/features/notifications/utils/formatNotificationTime', () => ({
  formatNotificationTime: (iso: string) => `at(${iso})`,
}));

jest.mock('../src/features/auth/hooks/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: mockHookAuth, isLoading: mockAuthLoading }),
}));

jest.mock('../src/core/state/appStore', () => ({
  useStore: (selector?: (s: any) => unknown) => {
    const state = { isAuthenticated: mockStoreAuth };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/core/utils/haptics', () => {
  const actual = jest.requireActual<typeof import('../src/core/utils/haptics')>(
    '../src/core/utils/haptics',
  );
  return {
    __esModule: true,
    ...actual,
    Haptics: { ...actual.Haptics, impactAsync: (...a: unknown[]) => mockImpact(...a) },
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
  const {
    Pressable,
    Text: RNText,
    View,
  } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    PageLayout: ({ children, header }: any) =>
      R.createElement(View, { testID: 'page-layout' }, header, children),
    ScreenHeader: ({ title, actions }: any) =>
      R.createElement(
        View,
        { testID: 'header' },
        R.createElement(RNText, null, title),
        actions ?? null,
      ),
    Card: ({ children }: any) => R.createElement(View, null, children),
    EmptyState: ({ title }: any) => R.createElement(RNText, { testID: 'empty-state' }, title),
    LoadingState: () => R.createElement(RNText, { testID: 'loading' }, 'loading'),
    ErrorState: ({ onRetry }: any) =>
      R.createElement(RNText, { testID: 'error-state', onPress: onRetry }, 'error'),
    Text: ({ children }: any) => R.createElement(RNText, null, children),
    Button: ({ label, onPress, loading }: any) =>
      R.createElement(
        Pressable,
        { testID: 'mark-all', onPress },
        R.createElement(RNText, null, loading ? 'LOADING' : label),
      ),
    ListItem: ({ title, subtitle, trailing, onPress }: any) =>
      R.createElement(
        Pressable,
        { testID: `row-${title}`, onPress, accessibilityState: { disabled: !onPress } },
        R.createElement(RNText, null, title),
        R.createElement(RNText, null, subtitle),
        trailing,
      ),
  };
});

jest.mock('lucide-react-native', () => {
  const Icon = () => null;
  return { __esModule: true, Bell: Icon };
});

const note = (id: string, title: string, isRead: boolean) => ({
  id,
  title,
  message: `${title} body`,
  isRead,
  createdAt: `2026-01-0${id}`,
});

// A real order-fulfilled notification: the full order id rides inside the copy.
const ORDER_ID = 'ad9cf45f-1541-405c-95a9-41e2b2f9da72';
const orderNote = (id: string, isRead: boolean) => ({
  id,
  title: 'Замовлення виконано',
  message: `Ваше замовлення #${ORDER_ID} виконано. Ваучери призначені та готові до використання.`,
  isRead,
  createdAt: `2026-01-0${id}`,
});

beforeEach(() => {
  mockState = baseState();
  mockStoreAuth = true;
  mockHookAuth = true;
  mockAuthLoading = false;
  mockMarkAsRead.mockClear();
  mockMarkAllAsRead.mockClear();
  mockRefetch.mockClear();
  mockImpact.mockClear();
  mockPush.mockClear();
});

describe('Notifications - getting in', () => {
  it('sends a signed-out visitor to the landing screen', () => {
    mockStoreAuth = false;
    mockHookAuth = false;
    render(<NotificationsScreen />);
    expect(screen.getByTestId('redirect')).toHaveTextContent('/landing');
  });

  it('does not bounce a signed-in user while the auth query is loading', () => {
    mockStoreAuth = false;
    mockHookAuth = false;
    mockAuthLoading = true;
    mockState.isLoading = true;
    render(<NotificationsScreen />);
    expect(screen.queryByTestId('redirect')).toBeNull();
  });
});

describe('Notifications - the states', () => {
  it('shows a loading state first', () => {
    mockState.isLoading = true;
    render(<NotificationsScreen />);
    expect(screen.getByTestId('loading')).toBeTruthy();
  });

  it('offers a retry on failure', () => {
    mockState.isError = true;
    render(<NotificationsScreen />);
    fireEvent.press(screen.getByTestId('error-state'));
    expect(mockRefetch).toHaveBeenCalled();
  });

  it('says so when there is nothing', () => {
    mockState.notifications = [];
    render(<NotificationsScreen />);
    expect(screen.getByTestId('empty-state')).toBeTruthy();
  });
});

describe('Notifications - which rows do something', () => {
  it('clears an unread row when it is tapped', () => {
    mockState.notifications = [note('1', 'Order ready', false)];
    mockState.unreadCount = 1;
    render(<NotificationsScreen />);

    fireEvent.press(screen.getByTestId('row-Order ready'));

    expect(mockMarkAsRead).toHaveBeenCalledWith('1');
    expect(mockImpact).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Light);
  });

  it('shows every notification, read and unread', () => {
    mockState.notifications = [note('1', 'First', false), note('2', 'Second', true)];
    mockState.unreadCount = 1;
    render(<NotificationsScreen />);

    expect(screen.getByTestId('row-First')).toBeTruthy();
    expect(screen.getByTestId('row-Second')).toBeTruthy();
  });

  it('marks an unread row with a dot and a read one without', () => {
    mockState.notifications = [note('1', 'Fresh', false), note('2', 'Seen', true)];
    mockState.unreadCount = 1;
    render(<NotificationsScreen />);

    expect(screen.getAllByLabelText('notifications.unread')).toHaveLength(1);
  });
});

describe('Notifications - reading a row', () => {
  const rowOf = () => screen.getByTestId(`row-${orderNote('1', true).title}`);

  it('reveals the message on a tap instead of navigating away', () => {
    mockState.notifications = [orderNote('1', true)];
    render(<NotificationsScreen />);

    fireEvent.press(rowOf());

    // A tap that deep-linked meant the message could never actually be read.
    expect(mockPush).not.toHaveBeenCalled();
    expect(screen.getByText(new RegExp(ORDER_ID))).toBeTruthy();
  });

  it('clears the row as it expands, and collapses on a second tap', () => {
    mockState.notifications = [orderNote('1', false)];
    mockState.unreadCount = 1;
    render(<NotificationsScreen />);

    fireEvent.press(rowOf());
    expect(mockMarkAsRead).toHaveBeenCalledWith('1');
    expect(screen.getByText(new RegExp(ORDER_ID))).toBeTruthy();

    fireEvent.press(rowOf());
    expect(screen.queryByText(new RegExp(ORDER_ID))).toBeNull();
  });

  it('opens the order from inside the expanded row', () => {
    mockState.notifications = [orderNote('1', true)];
    render(<NotificationsScreen />);

    fireEvent.press(rowOf());
    fireEvent.press(screen.getByText('notifications.openOrder'));

    expect(mockPush).toHaveBeenCalledWith({
      pathname: '/my-codes',
      params: { orderId: ORDER_ID },
    });
  });

  it('keeps the collapsed row free of the id fragment', () => {
    mockState.notifications = [orderNote('1', true)];
    render(<NotificationsScreen />);

    // Dropping only the guid would leave a bare `#` mid-sentence.
    expect(screen.queryByText(/#/)).toBeNull();
    expect(screen.queryByText(new RegExp(ORDER_ID))).toBeNull();
    expect(screen.queryByText(/\.f9da72/)).toBeNull();
  });

  it('a read row expands too - there is no inert state any more', () => {
    mockState.notifications = [orderNote('1', true)];
    render(<NotificationsScreen />);

    fireEvent.press(rowOf());

    expect(screen.getByText(new RegExp(ORDER_ID))).toBeTruthy();
    // Reading a read notification must not re-mark it.
    expect(mockMarkAsRead).not.toHaveBeenCalled();
  });
});

describe('Notifications - either auth source is enough', () => {
  it('accepts a session known to the store but not yet the query', () => {
    mockStoreAuth = true;
    mockHookAuth = false;
    mockState.notifications = [note('1', 'First', true)];
    render(<NotificationsScreen />);
    expect(screen.queryByTestId('redirect')).toBeNull();
  });

  it('accepts a session the query knows before the store catches up', () => {
    mockStoreAuth = false;
    mockHookAuth = true;
    mockState.notifications = [note('1', 'First', true)];
    render(<NotificationsScreen />);
    expect(screen.queryByTestId('redirect')).toBeNull();
  });
});

describe('Notifications - mark all read', () => {
  it('offers the control only while something is unread', () => {
    mockState.notifications = [note('1', 'First', true)];
    mockState.unreadCount = 0;
    render(<NotificationsScreen />);
    expect(screen.queryByTestId('mark-all')).toBeNull();
  });

  it('marks everything read on request', () => {
    mockState.notifications = [note('1', 'First', false)];
    mockState.unreadCount = 2;
    render(<NotificationsScreen />);

    fireEvent.press(screen.getByTestId('mark-all'));
    expect(mockMarkAllAsRead).toHaveBeenCalled();
    expect(mockImpact).toHaveBeenCalled();
  });

  it('shows the control working while the request is in flight', () => {
    mockState.notifications = [note('1', 'First', false)];
    mockState.unreadCount = 2;
    mockState.isMarkingAll = true;
    render(<NotificationsScreen />);
    expect(screen.getByText('LOADING')).toBeTruthy();
  });
});
