import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react-native';

import CheckoutScreen from './checkout';
import type { CartItem } from '../src/features/cart/types';

/**
 * The payment screen, which had no tests at all.
 *
 * This is the highest-risk screen left uncovered: it builds a real Monobank invoice
 * from the basket, opens the payment page, and only then empties the basket. A
 * regression here is not a visual glitch — it either charges the wrong amount,
 * loses a purchase, or clears a basket that was never paid for.
 *
 * The behaviours that matter are narrow and specific:
 *
 * - the per-line price the customer agreed to is the one sent in the invoice
 * - the purchase lands in the *active* context, never one chosen here, and a worker
 *   context cannot buy at all
 * - the basket is emptied only after the payment page opens
 * - a failed or URL-less invoice leaves the basket intact and says why
 *
 * `formatMoney` is mocked to a fixed suffix so assertions read as plain numbers, and
 * `GridPageLayout` is a pass-through that still renders `fixedFooter` — the pay button
 * lives there, and a stub that dropped it would silently test an empty screen.
 */

const mockClearCart = jest.fn();
const mockCreateInvoice = jest.fn();
const mockOpenURL = jest.fn();
const mockPush = jest.fn();
const mockLogin = jest.fn();

let mockCart: CartItem[] = [];
let mockDiscountedTotal = 0;
let mockStoreAuth = true;
let mockHookAuth = true;
let mockAuthLoading = false;
let mockContext: { kind: 'personal' | 'owner' | 'worker'; company?: { id: string; name: string } } =
  {
    kind: 'personal',
  };

jest.mock('expo-router', () => {
  // `jest.mock` factories are hoisted above the imports, so JSX here cannot close
  // over `Text` — it is pulled from the real module inside the factory instead.
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
  // Keys, not translations: the button label is built as `${t('packages.payTitle')} …`,
  // so a real Ukrainian string would make every money assertion depend on the copy.
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/core/state/appStore', () => ({
  useStore: () => ({ isAuthenticated: mockStoreAuth, login: mockLogin }),
}));

jest.mock('../src/features/cart/store/cartStore', () => ({
  useCartStore: () => ({
    cart: mockCart,
    getDiscountedTotal: () => mockDiscountedTotal,
    clearCart: mockClearCart,
  }),
}));

jest.mock('../src/features/auth/hooks/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: mockHookAuth, isLoading: mockAuthLoading }),
}));

jest.mock('../src/features/vouchers/api/purchases', () => ({
  createBulkMonobankInvoice: (...a: unknown[]) => mockCreateInvoice(...a),
  // Keep the real mapping: the screen branches on it, and a stub returning undefined would make the
  // test below pass for the wrong reason.
  purchaseErrorKey: (code?: string) => (code === 'below_cost' ? 'purchase.error.belowCost' : null),
}));

jest.mock('../src/features/company/hooks/useAccountContext', () => ({
  useAccountContext: () => mockContext,
}));

jest.mock('../src/features/auth/components/PhoneAuthForm', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    PhoneAuthForm: () => R.createElement(RNText, { testID: 'phone-auth-form' }, 'auth'),
  };
});

jest.mock('../src/core/hooks/useTheme', () => {
  // Real tokens, resolved inside the factory: the screen reads `surface.soft` and
  // dozens of colours off them, and a fake object would just make assertions noisier.
  const { getTokens } = jest.requireActual<typeof import('../src/core/design/tokens')>(
    '../src/core/design/tokens',
  );
  return { useDesignTokens: () => getTokens() };
});

jest.mock('../src/core/ui', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { View } = jest.requireActual<typeof import('react-native')>('react-native');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    GridBackground: () => null,
    ScreenHeader: ({ title }: { title?: string }) => R.createElement(RNText, null, title),
    GridPageLayout: ({ children, header, fixedFooter }: any) =>
      R.createElement(
        View,
        { testID: 'grid-layout' },
        header,
        children,
        // The pay button is passed in as a prop, not as a child. A stub that dropped
        // it would leave this suite testing a screen with nothing to press.
        fixedFooter,
      ),
  };
});

jest.mock('../src/core/utils/currency', () => ({
  formatMoney: (n: number) => `${n} UAH`,
}));

jest.mock('expo-linking', () => ({ openURL: (...a: unknown[]) => mockOpenURL(...a) }));

jest.mock('lucide-react-native', () => {
  const Icon = () => null;
  return { __esModule: true, User: Icon, Building2: Icon, Zap: Icon };
});

/** A basket line. Only the fields the screen actually reads are filled in. */
function makeItem(over: Partial<CartItem> = {}): CartItem {
  return {
    id: 'line-1',
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
    station: {
      id: 'st-1',
      name: 'Shell Darnytsia',
      color: '#f00',
      logoText: 'SH',
      sortOrder: 1,
    },
    fuel: { id: 'ft-1', name: 'A95', stationId: 'st-1', basePrice: 40, discountPrice: 38 },
    quantity: 2,
    ...over,
  } as CartItem;
}

/** Clears the basket so each test starts from one known invoice. */
function basket(over: Partial<CartItem> = {}) {
  mockCart = [makeItem(over)];
  mockDiscountedTotal = 4000;
}

describe('Checkout — what gets sent to the payment provider', () => {
  beforeEach(() => {
    mockCart = [];
    mockDiscountedTotal = 0;
    mockStoreAuth = true;
    mockHookAuth = true;
    mockAuthLoading = false;
    mockContext = { kind: 'personal' };
    mockClearCart.mockClear();
    mockCreateInvoice.mockReset();
    mockOpenURL.mockReset();
    mockPush.mockReset();
    mockLogin.mockClear();
    global.alert = jest.fn();
    mockCreateInvoice.mockResolvedValue({
      orderIds: ['o-1'],
      invoiceId: 'inv-1',
      pageUrl: 'https://monobank.com.ua/pay/inv-1',
    });
  });

  it('prices a line at the term price the customer agreed to', () => {
    basket({ termCode: '1w', termLinePrice: 1500, quantity: 2 });
    render(<CheckoutScreen />);

    // 1500 was what the package card quoted, already for the nominal. Charging the
    // undiscounted package price instead would be a silent price rise at payment.
    expect(screen.getByText('3000 UAH')).toBeTruthy();
  });

  it('falls back to the package price when no term was chosen', () => {
    basket({ quantity: 3 });
    render(<CheckoutScreen />);

    expect(screen.getByText('6000 UAH')).toBeTruthy();
  });

  it('sends the agreed line price in the invoice', async () => {
    basket({ termCode: '1w', termLinePrice: 1500, quantity: 2 });
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    await waitFor(() => expect(mockCreateInvoice).toHaveBeenCalled());
    expect(mockCreateInvoice.mock.calls[0][0]).toEqual([
      expect.objectContaining({
        packageId: 'pkg-1',
        stationId: 'st-1',
        stationName: 'Shell Darnytsia',
        fuelType: 'ft-1',
        fuelName: 'A95',
        liters: 50,
        quantity: 2,
        price: 3000,
        termCode: '1w',
      }),
    ]);
  });

  it('leaves the term off the invoice when there is none', async () => {
    basket({ termCode: undefined });
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    await waitFor(() => expect(mockCreateInvoice).toHaveBeenCalled());
    const item = mockCreateInvoice.mock.calls[0][0][0];
    // `undefined` means the voucher's full remaining term, which the server prices
    // itself; sending an empty string would ask for a term that does not exist.
    expect('termCode' in item).toBe(true);
    expect(item.termCode).toBeUndefined();
  });

  it('buys into the active company', async () => {
    mockContext = { kind: 'owner', company: { id: 'le-1', name: 'TOV Romashka' } };
    basket();
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    await waitFor(() => expect(mockCreateInvoice).toHaveBeenCalled());
    expect(mockCreateInvoice.mock.calls[0][1]).toBe('le-1');
    expect(screen.getByText('TOV Romashka')).toBeTruthy();
  });

  it('buys personally when no company context is active', async () => {
    mockContext = { kind: 'personal' };
    basket();
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    await waitFor(() => expect(mockCreateInvoice).toHaveBeenCalled());
    // No company means no `legalEntityId`, rather than a null the server would read
    // as "some company".
    expect(mockCreateInvoice.mock.calls[0][1]).toBeUndefined();
  });

  // The customer is standing at the payment screen when this fires. Rendering the server's English
  // sentence there showed them our margin decision ("priced below supplier cost") — true, and nothing
  // they can act on — instead of what they can do.
  it('shows the customer their own wording when the rejection carries a known code', async () => {
    mockContext = { kind: 'personal' };
    basket();
    mockCreateInvoice.mockRejectedValue(
      Object.assign(new Error('This fuel is currently unavailable for purchase.'), {
        code: 'below_cost',
      }),
    );
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    await waitFor(() => expect(global.alert).toHaveBeenCalledWith('purchase.error.belowCost'));
  });

  it('falls back to the server message for a rejection with no code', async () => {
    mockContext = { kind: 'personal' };
    basket();
    mockCreateInvoice.mockRejectedValue(new Error('Some other failure'));
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    // Unchanged behaviour for anything we have no wording for — the server's message is still
    // better than a generic one.
    await waitFor(() => expect(global.alert).toHaveBeenCalledWith('Some other failure'));
  });

  it("refuses to let a worker spend the employer's money", () => {
    // The basket tab is hidden in a worker context; this is the backstop. A worker
    // landing here must not be able to start a purchase at all.
    mockContext = { kind: 'worker' };
    basket();
    render(<CheckoutScreen />);

    expect(screen.getByTestId('redirect')).toHaveTextContent('/my-codes');
    expect(screen.queryByText(/packages\.payTitle/)).toBeNull();
  });
});

describe('Checkout — the basket and the payment page', () => {
  beforeEach(() => {
    mockCart = [];
    mockDiscountedTotal = 0;
    mockStoreAuth = true;
    mockHookAuth = true;
    mockAuthLoading = false;
    mockContext = { kind: 'personal' };
    mockClearCart.mockClear();
    mockCreateInvoice.mockReset();
    mockOpenURL.mockReset();
    mockPush.mockReset();
    mockLogin.mockClear();
    global.alert = jest.fn();
    mockCreateInvoice.mockResolvedValue({
      orderIds: ['o-1'],
      invoiceId: 'inv-1',
      pageUrl: 'https://monobank.com.ua/pay/inv-1',
    });
  });

  it('quotes the discounted total on the pay button', () => {
    basket();
    mockDiscountedTotal = 3200;
    render(<CheckoutScreen />);

    // The summary lines are undiscounted; the button is what the customer agrees to
    // pay, so it has to be the total after the promo.
    expect(screen.getByText('packages.payTitle 3200 UAH')).toBeTruthy();
  });

  it('routes to the in-app payment screen, then empties the basket', async () => {
    basket();
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    // The payment page is rendered inside the app now, so the assertion is on the route and
    // not on Linking.openURL - handing the customer to a browser was the thing being fixed.
    await waitFor(() =>
      expect(mockPush).toHaveBeenCalledWith(
        expect.stringMatching(/^\/pay\?.*url=https%3A%2F%2Fmonobank\.com\.ua%2Fpay%2Finv-1/),
      ),
    );
    expect(mockClearCart).toHaveBeenCalled();
    expect(mockOpenURL).not.toHaveBeenCalled();
  });

  it('keeps the basket when the payment route cannot be opened', async () => {
    // The ordering is the whole point: the basket may only be emptied *after* the payment
    // screen is entered. Emptying first means a phone that cannot get there loses an order
    // that was never paid for, with no way back to it.
    basket();
    mockPush.mockImplementation(() => {
      throw new Error('No route to /pay');
    });
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    await waitFor(() => expect(global.alert).toHaveBeenCalledWith('No route to /pay'));
    expect(mockClearCart).not.toHaveBeenCalled();
  });

  it('keeps the basket and explains itself when no payment URL comes back', async () => {
    // Paying for nothing is the worst outcome here: emptying the basket on a failed
    // invoice loses the customer's order with no way back.
    basket();
    mockCreateInvoice.mockResolvedValue({ orderIds: [], invoiceId: 'inv-2', pageUrl: '' });
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    await waitFor(() => expect(global.alert).toHaveBeenCalledWith('No payment URL received'));
    expect(mockClearCart).not.toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();
    expect(mockOpenURL).not.toHaveBeenCalled();
  });

  it('keeps the basket when the invoice request fails', async () => {
    basket();
    mockCreateInvoice.mockRejectedValue(new Error('Provider unavailable'));
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    await waitFor(() => expect(global.alert).toHaveBeenCalledWith('Provider unavailable'));
    expect(mockClearCart).not.toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();
  });

  it('re-enables the pay button after a failure so the user can retry', async () => {
    basket();
    mockCreateInvoice.mockRejectedValue(new Error('Provider unavailable'));
    render(<CheckoutScreen />);

    fireEvent.press(screen.getByText(/packages\.payTitle/));

    // Without the reset in the catch, a single failure would strand the customer on
    // a permanently disabled button.
    await waitFor(() => expect(screen.getByText(/packages\.payTitle/)).toBeTruthy());
    expect(global.alert).toHaveBeenCalled();
  });

  it('offers no pay button with an empty basket', () => {
    mockCart = [];
    render(<CheckoutScreen />);

    expect(screen.queryByText(/packages\.payTitle/)).toBeNull();
  });

  it('states each line, with the term labelled and in the short form', () => {
    basket({ termCode: '1w', termLinePrice: 1500 });
    render(<CheckoutScreen />);

    expect(screen.getByText('SH')).toBeTruthy();
    expect(screen.getByText('A95 x 2')).toBeTruthy();
    // "Validity period: 1 wk" - a labelled short term, not a bare long-form word (#174).
    expect(screen.getByText('term.label: term.short.1w')).toBeTruthy();
    expect(screen.queryByText('term.1w')).toBeNull();
  });
});

describe('Checkout — getting to the screen at all', () => {
  beforeEach(() => {
    mockCart = [];
    mockStoreAuth = false;
    mockHookAuth = false;
    mockAuthLoading = false;
    mockContext = { kind: 'personal' };
    mockClearCart.mockClear();
    mockCreateInvoice.mockReset();
    mockOpenURL.mockReset();
    mockPush.mockReset();
    mockLogin.mockClear();
    global.alert = jest.fn();
  });

  it('asks a signed-out visitor to authenticate first', () => {
    render(<CheckoutScreen />);

    expect(screen.getByTestId('phone-auth-form')).toBeTruthy();
    expect(screen.queryByText(/packages\.payTitle/)).toBeNull();
  });

  it('lets the screen through while the auth query is still loading', () => {
    // Bouncing to the login form during the query would flash it at every signed-in
    // customer on a cold start.
    mockAuthLoading = true;
    basket();
    render(<CheckoutScreen />);

    expect(screen.queryByTestId('phone-auth-form')).toBeNull();
    expect(screen.getByText(/packages\.payTitle/)).toBeTruthy();
  });

  it('accepts a session known to the store but not yet to the query', () => {
    mockStoreAuth = true;
    mockHookAuth = false;
    basket();
    render(<CheckoutScreen />);

    expect(screen.queryByTestId('phone-auth-form')).toBeNull();
    expect(screen.getByText(/packages\.payTitle/)).toBeTruthy();
  });

  it('accepts a session the query knows about before the store has caught up', () => {
    // The mirror of the case above. `storeAuth || hookAuth` exists precisely because
    // the two disagree in both directions during a cold start, and either one alone
    // would flash the login form at a signed-in customer.
    mockStoreAuth = false;
    mockHookAuth = true;
    basket();
    render(<CheckoutScreen />);

    expect(screen.queryByTestId('phone-auth-form')).toBeNull();
    expect(screen.getByText(/packages\.payTitle/)).toBeTruthy();
  });
});
