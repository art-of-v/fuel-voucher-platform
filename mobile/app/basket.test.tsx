import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react-native';

import BasketScreen from './basket';
import { Haptics } from '../src/core/utils/haptics';
import { getTokens } from '../src/core/design/tokens';
import type { CartItem } from '../src/features/cart/types';

/**
 * The basket, which had no tests at all.
 *
 * It is the last screen before money, and it owns three things a customer would
 * notice immediately if they broke:
 *
 * - the arithmetic the customer reads: subtotal, the deduction, and the total to pay
 * - the promocode, including that a rejected one is visibly rejected rather than
 *   silently ignored
 * - the two ways out of an empty basket, and the guard that stops a worker buying
 *
 * `CartItemCard` is stubbed on purpose. This screen's job is to *wire* the quantity
 * and remove callbacks to the store; drawing a card is that component's own concern
 * and is where a screen test would otherwise stop being a screen test.
 */

const mockPush = jest.fn();
const mockUpdateQuantity = jest.fn();
const mockRemoveFromCart = jest.fn();
const mockClearCart = jest.fn();
const mockApplyPromocode = jest.fn();
const mockClearPromocode = jest.fn();
const mockImpact = jest.fn();

let mockCart: CartItem[] = [];
let mockPromocode: string | null = null;
let mockDiscount = 0;
let mockTotal = 0;
let mockDiscounted = 0;
let mockIsWorker = false;

jest.mock('expo-router', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    useRouter: () => ({ push: mockPush, replace: jest.fn(), back: jest.fn() }),
    Redirect: ({ href }: { href: string }) => R.createElement(RNText, { testID: 'redirect' }, href),
  };
});

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  // Keys, not translations: several assertions are about which label sits next to
  // which number, and that pairing should not move when the copy is edited.
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/features/cart/store/cartStore', () => ({
  useCartStore: () => ({
    cart: mockCart,
    updateQuantity: mockUpdateQuantity,
    removeFromCart: mockRemoveFromCart,
    clearCart: mockClearCart,
    promocode: mockPromocode,
    discount: mockDiscount,
    applyPromocode: mockApplyPromocode,
    clearPromocode: mockClearPromocode,
    getCartTotal: () => mockTotal,
    getDiscountedTotal: () => mockDiscounted,
  }),
}));

jest.mock('../src/features/company/hooks/useAccountContext', () => ({
  useAccountContext: () => ({ kind: mockIsWorker ? 'worker' : 'personal' }),
}));

jest.mock('../src/core/hooks/useTheme', () => {
  const { getTokens } = jest.requireActual<typeof import('../src/core/design/tokens')>(
    '../src/core/design/tokens',
  );
  return { useDesignTokens: () => getTokens() };
});

jest.mock('../src/core/utils/haptics', () => {
  // The real enum, the spy call. The screen asserts the *style* it asked for, so a
  // hand-written stub would have to restate the enum and could drift from it.
  const actual = jest.requireActual<typeof import('../src/core/utils/haptics')>(
    '../src/core/utils/haptics',
  );
  return {
    __esModule: true,
    ...actual,
    Haptics: { ...actual.Haptics, impactAsync: (...a: unknown[]) => mockImpact(...a) },
  };
});

jest.mock('../src/core/utils/currency', () => ({
  // Deterministic and positional, so "which number is this" is readable at a glance.
  formatMoney: (n: number) => `${n} UAH`,
  formatPercent: (n: number) => `${n}%`,
}));

jest.mock('../src/components/glow-text', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    GlowText: ({ children }: any) => R.createElement(RNText, null, children),
  };
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
    // The footer and the header action both arrive as props; a stub that dropped
    // them would leave this suite testing a screen with nothing to press.
    GridPageLayout: ({ children, header, fixedFooter }: any) =>
      R.createElement(View, { testID: 'grid-layout' }, header, children, fixedFooter),
    ScreenHeader: ({ title, subtitle, actions }: any) =>
      R.createElement(
        View,
        { testID: 'screen-header' },
        R.createElement(RNText, { testID: 'header-title' }, title),
        subtitle ? R.createElement(RNText, { testID: 'header-subtitle' }, subtitle) : null,
        actions ?? null,
      ),
    Button: ({ label, onPress }: any) =>
      R.createElement(
        Pressable,
        { onPress, testID: 'ui-button' },
        R.createElement(RNText, null, label),
      ),
  };
});

jest.mock('../src/features/cart/components/CartItemCard', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const {
    Pressable,
    Text: RNText,
    View,
  } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    // Exposes the two callbacks so the wiring can be asserted directly. Drawing the
    // card is that component's own tested concern.
    CartItemCard: ({ item, onUpdateQuantity, onRemove }: any) =>
      R.createElement(
        View,
        { testID: `card-${item.id}` },
        R.createElement(RNText, null, item.station.logoText),
        R.createElement(
          Pressable,
          {
            testID: `plus-${item.id}`,
            onPress: () => onUpdateQuantity(item.id, item.quantity + 1),
          },
          R.createElement(RNText, null, 'plus'),
        ),
        R.createElement(
          Pressable,
          {
            testID: `minus-${item.id}`,
            onPress: () => onUpdateQuantity(item.id, item.quantity - 1),
          },
          R.createElement(RNText, null, 'minus'),
        ),
        R.createElement(
          Pressable,
          { testID: `remove-${item.id}`, onPress: () => onRemove(item.id) },
          R.createElement(RNText, null, 'remove'),
        ),
      ),
  };
});

jest.mock('lucide-react-native', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  // Rendered as an addressable node rather than null: several controls here are
  // icon-only (clearing a promo is a bare `X`), and a null glyph would make them
  // unreachable by anything but a traversal.
  const icon = (name: string) => () => R.createElement(RNText, { testID: `icon-${name}` });
  return {
    __esModule: true,
    ShoppingCart: icon('ShoppingCart'),
    Tag: icon('Tag'),
    Zap: icon('Zap'),
    Check: icon('Check'),
    X: icon('X'),
  };
});

function makeItem(id = 'line-1', over: Partial<CartItem> = {}): CartItem {
  return {
    id,
    package: {
      id: 'pkg-1',
      stationId: 'st-1',
      fuelTypeId: 'ft-1',
      fuelName: 'A95',
      liters: 50,
      price: 2000,
      originalPrice: 2200,
      provider: 'MONOBANK',
      fuelType: 'A95',
    },
    station: { id: 'st-1', name: 'Shell', color: '#f00', logoText: 'SH', sortOrder: 1 },
    fuel: { id: 'ft-1', name: 'A95', stationId: 'st-1', basePrice: 40, discountPrice: 38 },
    quantity: 1,
    ...over,
  } as CartItem;
}

/** Two lines, a 4000 subtotal and a 500 discount, unless overridden. */
function stocked(over: { total?: number; discounted?: number; discount?: number } = {}) {
  mockCart = [makeItem('line-1'), makeItem('line-2')];
  mockTotal = over.total ?? 4000;
  mockDiscounted = over.discounted ?? 3500;
  mockDiscount = over.discount ?? 10;
}

describe('Basket — the arithmetic the customer reads', () => {
  beforeEach(() => {
    mockCart = [];
    mockPromocode = null;
    mockDiscount = 0;
    mockTotal = 0;
    mockDiscounted = 0;
    mockIsWorker = false;
    mockPush.mockClear();
    mockUpdateQuantity.mockClear();
    mockRemoveFromCart.mockClear();
    mockClearCart.mockClear();
    mockApplyPromocode.mockClear();
    mockClearPromocode.mockClear();
    mockImpact.mockClear();
  });

  it('states the subtotal and the total to pay', () => {
    stocked({ total: 4000, discounted: 3500, discount: 0 });
    render(<BasketScreen />);

    expect(screen.getByText('basket.subtotal')).toBeTruthy();
    expect(screen.getByText('4000 UAH')).toBeTruthy();
    expect(screen.getByText('basket.totalToPay')).toBeTruthy();
    expect(screen.getByText('3500 UAH')).toBeTruthy();
  });

  it('shows no discount line when there is no discount', () => {
    // A discount row with a zero would read as a discount the customer never got.
    stocked({ total: 4000, discounted: 4000, discount: 0 });
    render(<BasketScreen />);

    expect(screen.queryByText('basket.discount (0%)')).toBeNull();
    expect(screen.queryByText('basket.discount (10%)')).toBeNull();
  });

  it('shows the discount as a deduction, not an addition', () => {
    stocked({ total: 4000, discounted: 3500, discount: 10 });
    render(<BasketScreen />);

    expect(screen.getByText('basket.discount (10%)')).toBeTruthy();
    // 4000 - 3500 = 500 off, rendered negative so it reads as money leaving.
    expect(screen.getByText('-500 UAH')).toBeTruthy();
    expect(screen.getByText('3500 UAH')).toBeTruthy();
  });
});

describe('Basket — the promocode', () => {
  beforeEach(() => {
    mockCart = [];
    mockPromocode = null;
    mockDiscount = 0;
    mockTotal = 0;
    mockDiscounted = 0;
    mockIsWorker = false;
    mockPush.mockClear();
    mockClearCart.mockClear();
    mockApplyPromocode.mockClear();
    mockClearPromocode.mockClear();
    mockImpact.mockClear();
  });

  /** The input is the only place the rejection is shown, via its border. */
  function inputBorder(): string | undefined {
    return screen.getByPlaceholderText('basket.enterCode').props.style
      ? Object.assign({}, ...[screen.getByPlaceholderText('basket.enterCode').props.style].flat())
          .borderColor
      : undefined;
  }

  it('does nothing when applied with no code typed', () => {
    stocked();
    render(<BasketScreen />);

    fireEvent.press(screen.getByText('basket.apply'));

    // An empty apply is disabled; calling the store with '' would burn an attempt.
    expect(mockApplyPromocode).not.toHaveBeenCalled();
  });

  it('clears the field after a code is accepted', async () => {
    stocked();
    mockApplyPromocode.mockReturnValue(true);
    render(<BasketScreen />);

    const input = screen.getByPlaceholderText('basket.enterCode');
    fireEvent.changeText(input, 'FUEL10');
    fireEvent.press(screen.getByText('basket.apply'));

    await waitFor(() => expect(mockApplyPromocode).toHaveBeenCalledWith('FUEL10'));
    // Leaving the code on screen invites a second submit of one that already worked.
    expect(screen.getByPlaceholderText('basket.enterCode').props.value).toBe('');
  });

  it('marks the field when a code is rejected', async () => {
    stocked();
    mockApplyPromocode.mockReturnValue(false);
    render(<BasketScreen />);

    const before = inputBorder();
    fireEvent.changeText(screen.getByPlaceholderText('basket.enterCode'), 'NOPE');
    fireEvent.press(screen.getByText('basket.apply'));

    await waitFor(() => expect(inputBorder()).not.toBe(before));
    // The code stays put so it can be corrected rather than retyped from scratch.
    expect(screen.getByPlaceholderText('basket.enterCode').props.value).toBe('NOPE');
  });

  it('clears the rejection as soon as the code is edited', async () => {
    stocked();
    mockApplyPromocode.mockReturnValue(false);
    render(<BasketScreen />);

    fireEvent.changeText(screen.getByPlaceholderText('basket.enterCode'), 'NOPE');
    fireEvent.press(screen.getByText('basket.apply'));
    await waitFor(() => expect(inputBorder()).toBeDefined());

    fireEvent.changeText(screen.getByPlaceholderText('basket.enterCode'), 'NOP');
    await waitFor(() =>
      expect(screen.getByPlaceholderText('basket.enterCode').props.value).toBe('NOP'),
    );
    // A stale red border on a half-typed code is an error the user cannot resolve.
    expect(inputBorder()).not.toBe(getTokens().colors.error);
  });

  it('shows the active code and its discount', () => {
    mockCart = [makeItem()];
    mockTotal = 4000;
    mockDiscounted = 3500;
    mockDiscount = 10;
    mockPromocode = 'FUEL10';
    render(<BasketScreen />);

    expect(screen.getByText('FUEL10')).toBeTruthy();
    // Stated twice on purpose — on the applied code and in the summary — so the
    // figure appearing at least once is the weakest honest claim here.
    expect(screen.getAllByText('basket.discount (10%)').length).toBeGreaterThan(0);
    // With a code applied the entry field is replaced by the applied state.
    expect(screen.queryByPlaceholderText('basket.enterCode')).toBeNull();
  });

  it('clears an applied code on request', () => {
    mockCart = [makeItem()];
    mockTotal = 4000;
    mockDiscounted = 3500;
    mockDiscount = 10;
    mockPromocode = 'FUEL10';
    render(<BasketScreen />);

    // The control is icon-only, so it has no label to query by.
    fireEvent.press(screen.getByTestId('icon-X'));
    expect(mockClearPromocode).toHaveBeenCalled();
  });
});

describe('Basket — the ways out', () => {
  beforeEach(() => {
    mockCart = [];
    mockPromocode = null;
    mockDiscount = 0;
    mockTotal = 0;
    mockDiscounted = 0;
    mockIsWorker = false;
    mockPush.mockClear();
    mockUpdateQuantity.mockClear();
    mockRemoveFromCart.mockClear();
    mockClearCart.mockClear();
    mockApplyPromocode.mockClear();
    mockClearPromocode.mockClear();
    mockImpact.mockClear();
  });

  it('refuses to let a worker reach the basket', () => {
    // The tab is hidden in a worker context; this catches a stale link.
    stocked();
    mockIsWorker = true;
    render(<BasketScreen />);

    expect(screen.getByTestId('redirect')).toHaveTextContent('/my-codes');
    expect(screen.queryByText('basket.checkout')).toBeNull();
  });

  it('offers a way back to the stations when the basket is empty', () => {
    render(<BasketScreen />);

    expect(screen.getByText('basket.empty')).toBeTruthy();
    expect(screen.getByText('basket.browseStations')).toBeTruthy();
    // An empty basket is a dead end unless there is an exit.
    expect(screen.queryByText('basket.checkout')).toBeNull();
  });

  it('sends an empty-basket customer back to browsing', () => {
    render(<BasketScreen />);

    fireEvent.press(screen.getByText('basket.continueShopping'));
    expect(mockPush).toHaveBeenCalledWith('/');
  });

  it('counts the cards in the header', () => {
    stocked();
    render(<BasketScreen />);

    expect(screen.getByTestId('header-subtitle')).toHaveTextContent('2 basket.cards');
  });

  it('empties the basket from the header', () => {
    stocked();
    render(<BasketScreen />);

    fireEvent.press(screen.getByText('basket.remove'));
    expect(mockClearCart).toHaveBeenCalled();
  });

  it('offers no destructive header control on an empty basket', () => {
    render(<BasketScreen />);

    // "Remove all" on an empty basket is a control with nothing behind it.
    expect(screen.queryByText('basket.remove')).toBeNull();
  });

  it('goes to checkout, with a haptic for the thumb', () => {
    stocked();
    render(<BasketScreen />);

    fireEvent.press(screen.getByText('basket.checkout'));

    expect(mockPush).toHaveBeenCalledWith('/checkout');
    expect(mockImpact).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Medium);
  });
});

describe('Basket — wiring the lines to the store', () => {
  beforeEach(() => {
    mockCart = [];
    mockPromocode = null;
    mockDiscount = 0;
    mockTotal = 0;
    mockDiscounted = 0;
    mockIsWorker = false;
    mockPush.mockClear();
    mockUpdateQuantity.mockClear();
    mockRemoveFromCart.mockClear();
    mockClearCart.mockClear();
    mockApplyPromocode.mockClear();
    mockClearPromocode.mockClear();
    mockImpact.mockClear();
  });

  it('renders one card per basket line', () => {
    stocked();
    render(<BasketScreen />);

    expect(screen.getByTestId('card-line-1')).toBeTruthy();
    expect(screen.getByTestId('card-line-2')).toBeTruthy();
  });

  it('sends a quantity change to the store', () => {
    stocked();
    render(<BasketScreen />);

    fireEvent.press(screen.getByTestId('plus-line-1'));
    expect(mockUpdateQuantity).toHaveBeenCalledWith('line-1', 2);
  });

  it('sends a removal to the store', () => {
    stocked();
    render(<BasketScreen />);

    fireEvent.press(screen.getByTestId('remove-line-2'));
    expect(mockRemoveFromCart).toHaveBeenCalledWith('line-2');
  });
});
