import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import PackagesScreen from './packages';

/**
 * The package picker, which had no tests.
 *
 * This is the catalog a worker is allowed to browse but not always buy from
 * (multi-company epic #103 S5): in a company they only work for, the list, prices and
 * radar stay, and only the purchase is withheld. The suite pins that distinction,
 * because getting it wrong either lets a worker spend the employer's money or hides a
 * catalog they are entitled to see.
 *
 * It also pins the add-to-cart contract: the quoted term price travels with the line,
 * so the basket shows what the customer agreed to rather than the undiscounted price,
 * and the screen renders nothing at all without a chosen station and fuel.
 */

const mockAddToCart = jest.fn();
const mockPush = jest.fn();
const mockInvalidate = jest.fn();
const mockCanBuy = jest.fn();

let mockSelectedStation: any = { id: 'st-1', name: 'Shell' };
let mockSelectedFuel: any = { id: 'f-1', name: 'A95' };
let mockCartCount = 0;
let mockPackages: any[] | undefined = [];
let mockPkgLoading = false;
let mockPkgError: unknown = null;

jest.mock('expo-router', () => ({
  __esModule: true,
  useRouter: () => ({ push: mockPush, replace: jest.fn(), back: jest.fn() }),
  useFocusEffect: () => {},
}));

jest.mock('../src/features/cart/store/cartStore', () => ({
  useCartStore: (selector?: (s: any) => unknown) => {
    const state = {
      selectedStation: mockSelectedStation,
      selectedFuel: mockSelectedFuel,
      addToCart: mockAddToCart,
      getCartItemCount: () => mockCartCount,
    };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/features/stations/hooks/usePackages', () => ({
  usePackages: () => ({ data: mockPackages, isLoading: mockPkgLoading, error: mockPkgError }),
}));

jest.mock('@tanstack/react-query', () => ({
  __esModule: true,
  useQueryClient: () => ({ invalidateQueries: mockInvalidate }),
}));

jest.mock('../src/features/company/hooks/useAccountContext', () => ({
  useAccountContext: () => ({ kind: 'personal' }),
}));

jest.mock('../src/features/company/lib/context', () => ({
  canBuyInContext: (...a: unknown[]) => mockCanBuy(...a),
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
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
    GridPageLayout: ({ children, header }: any) =>
      R.createElement(View, { testID: 'grid-layout' }, header, children),
    ScreenHeader: ({ title, subtitle, actions }: any) =>
      R.createElement(
        View,
        { testID: 'header' },
        R.createElement(RNText, null, title),
        R.createElement(RNText, null, subtitle),
        actions ?? null,
      ),
    LoadingState: () => R.createElement(RNText, { testID: 'loading' }, 'loading'),
    ErrorState: ({ detail, onRetry }: any) =>
      R.createElement(RNText, { testID: 'error-state', onPress: onRetry }, detail ?? 'error'),
    EmptyState: ({ title }: any) => R.createElement(RNText, { testID: 'empty-state' }, title),
    IconButton: ({ onPress, accessibilityLabel }: any) =>
      R.createElement(
        Pressable,
        { testID: 'cart-button', onPress, accessibilityLabel },
        R.createElement(RNText, null, 'cart'),
      ),
  };
});

jest.mock('../src/features/stations/components/PackageCard', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Pressable, Text } = jest.requireActual<typeof import('react-native')>('react-native');
  // Exposes the add button, its canPurchase flag, and a term-carrying add so the
  // line contract can be asserted.
  return {
    __esModule: true,
    PackageCard: ({ pkg, canPurchase, onAdd }: any) =>
      R.createElement(
        Pressable,
        {
          testID: `add-${pkg.id}`,
          accessibilityState: { disabled: !canPurchase },
          onPress: () => onAdd({ termCode: '1w', termLinePrice: 1500 }),
        },
        R.createElement(Text, null, pkg.name),
      ),
  };
});

jest.mock('lucide-react-native', () => {
  const Icon = () => null;
  return { __esModule: true, ShoppingCart: Icon, Package: Icon, ShoppingBag: Icon };
});

const pkg = (id: string, name: string) => ({ id, name, price: 2000, liters: 50 });

beforeEach(() => {
  mockSelectedStation = { id: 'st-1', name: 'Shell' };
  mockSelectedFuel = { id: 'f-1', name: 'A95' };
  mockCartCount = 0;
  mockPackages = [pkg('p-1', 'Starter'), pkg('p-2', 'Pro')];
  mockPkgLoading = false;
  mockPkgError = null;
  mockAddToCart.mockClear();
  mockPush.mockClear();
  mockInvalidate.mockClear();
  mockCanBuy.mockReset();
  mockCanBuy.mockReturnValue(true);
});

describe('Packages - nothing to show', () => {
  it('renders nothing without a chosen station', () => {
    mockSelectedStation = null;
    const { toJSON } = render(<PackagesScreen />);
    expect(toJSON()).toBeNull();
  });

  it('renders nothing without a chosen fuel', () => {
    mockSelectedFuel = null;
    const { toJSON } = render(<PackagesScreen />);
    expect(toJSON()).toBeNull();
  });

  it('shows a loading state while packages load', () => {
    mockPkgLoading = true;
    render(<PackagesScreen />);
    expect(screen.getByTestId('loading')).toBeTruthy();
  });

  it('offers a retry that refetches on error', () => {
    mockPkgError = new Error('down');
    mockPackages = undefined;
    render(<PackagesScreen />);

    fireEvent.press(screen.getByTestId('error-state'));
    expect(mockInvalidate).toHaveBeenCalledWith({
      queryKey: ['packages', 'st-1', 'A95'],
    });
  });

  it('says so when the fuel has no packages', () => {
    mockPackages = [];
    render(<PackagesScreen />);
    expect(screen.getByTestId('empty-state')).toBeTruthy();
  });
});

describe('Packages - adding to the cart', () => {
  it('adds the package with the term price the customer agreed to', () => {
    render(<PackagesScreen />);

    fireEvent.press(screen.getByTestId('add-p-1'));

    // The quoted term price travels with the line, so the basket and payment screen
    // show what was agreed, not the undiscounted package price.
    expect(mockAddToCart).toHaveBeenCalledWith(
      expect.objectContaining({
        package: expect.objectContaining({ id: 'p-1' }),
        station: expect.objectContaining({ id: 'st-1' }),
        fuel: expect.objectContaining({ id: 'f-1' }),
        quantity: 1,
        termCode: '1w',
        termLinePrice: 1500,
      }),
    );
  });

  it('adds the package that was pressed', () => {
    render(<PackagesScreen />);
    fireEvent.press(screen.getByTestId('add-p-2'));
    expect(mockAddToCart).toHaveBeenCalledWith(
      expect.objectContaining({ package: expect.objectContaining({ id: 'p-2' }) }),
    );
  });

  it('opens the basket from the cart button', () => {
    render(<PackagesScreen />);
    fireEvent.press(screen.getByTestId('cart-button'));
    expect(mockPush).toHaveBeenCalledWith('/basket');
  });

  it('shows the running cart count', () => {
    mockCartCount = 3;
    render(<PackagesScreen />);
    expect(screen.getByText('3')).toBeTruthy();
  });

  it('shows no badge on an empty cart', () => {
    mockCartCount = 0;
    render(<PackagesScreen />);
    // A badge reading '0' is a count of nothing dressed up as a notification.
    expect(screen.queryByText('0')).toBeNull();
  });
});

describe('Packages - a worker who may only browse', () => {
  beforeEach(() => {
    mockCanBuy.mockReturnValue(false);
  });

  it('still shows the catalog', () => {
    render(<PackagesScreen />);
    expect(screen.getByTestId('add-p-1')).toBeTruthy();
    expect(screen.getByTestId('add-p-2')).toBeTruthy();
  });

  it('says why the purchase controls are gone', () => {
    render(<PackagesScreen />);
    expect(screen.getByText('packages.browseOnly')).toBeTruthy();
  });

  it('offers no cart button', () => {
    render(<PackagesScreen />);
    expect(screen.queryByTestId('cart-button')).toBeNull();
  });

  it('refuses to add to the cart even if an add fires', () => {
    render(<PackagesScreen />);

    fireEvent.press(screen.getByTestId('add-p-1'));

    // The basket tab is hidden and /basket + /checkout refuse this context; this is
    // the backstop that stops a worker spending the employer's money.
    expect(mockAddToCart).not.toHaveBeenCalled();
  });

  it('marks the cards as not purchasable', () => {
    render(<PackagesScreen />);
    expect(screen.getByTestId('add-p-1').props.accessibilityState.disabled).toBe(true);
  });
});
