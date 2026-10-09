import React from 'react';
import { Alert } from 'react-native';
import { render, screen, fireEvent, waitFor } from '@testing-library/react-native';

import RenewScreen from './renew';
import { RenewalApiError } from '../src/features/vouchers/renewal/api/renewal';

/**
 * Voucher renewal, which had no tests.
 *
 * Renewal is the screen a manager reaches when fuel is running low, and its whole
 * job is arithmetic under a set of rules that are easy to get subtly wrong:
 *
 * - a term with no stock must not be payable, and must not contribute to the total
 * - an ineligible voucher must stay visible *with its reason* — a batch that mixes
 *   live and stale vouchers has to explain itself — but must never be paid for
 * - only the vouchers that actually have a chosen term go to checkout
 * - a failure at pay time surfaces as a localised alert, and the button comes back
 *
 * The server keeps the authoritative stock gate; this screen is an optimistic
 * preview. What matters here is that the preview never *invents* a price.
 */

const mockQuoteRenewal = jest.fn();
const mockCreateCheckout = jest.fn();
const mockOpenURL = jest.fn();
const mockReplace = jest.fn();
/** Success navigates with push to /pay; replace is only for the still-here paths. */
const mockPush = jest.fn();
let alertSpy: jest.SpyInstance;

let mockParams: { voucherIds?: string } = {};

jest.mock('expo-router', () => ({
  __esModule: true,
  // `router` is a module-level import here, not a hook — renewal replaces the
  // route rather than pushing onto it.
  router: { replace: (...a: unknown[]) => mockReplace(...a), push: (...a: unknown[]) => mockPush(...a), back: jest.fn() },
  useLocalSearchParams: () => mockParams,
}));

jest.mock('expo-linking', () => ({ openURL: (...a: unknown[]) => mockOpenURL(...a) }));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = {
      // Honours the interpolation argument. Several strings here embed one — the
      // term price and the pay button's total — and a stub that dropped it would
      // make every money assertion pass on a label with no number in it.
      t: (key: string, arg?: string) => (arg === undefined ? key : `${key} (${arg})`),
      language: 'uk',
      setLanguage: jest.fn(),
    };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/core/api/apiClient', () => ({
  apiFetch: jest.fn(),
}));

jest.mock('../src/features/vouchers/renewal/api/renewal', () => {
  // The error class and the code→message map are the real ones: the tests assert
  // *which* localised key a failure raises, and a hand-written copy of that switch
  // would pass while the real one was wrong.
  //
  // Reaching them means loading the module, which imports the API client, which
  // pulls in the native keychain and biometrics — hence the apiClient mock above.
  const actual = jest.requireActual<typeof import('../src/features/vouchers/renewal/api/renewal')>(
    '../src/features/vouchers/renewal/api/renewal',
  );
  return {
    ...actual,
    quoteRenewal: (...a: unknown[]) => mockQuoteRenewal(...a),
    createRenewalCheckout: (...a: unknown[]) => mockCreateCheckout(...a),
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
    // Header and footer arrive as props; dropping them would leave a screen with
    // nothing to press and a suite that still passed.
    PageLayout: ({ children, header, footer }: any) =>
      R.createElement(View, { testID: 'page-layout' }, header, children, footer),
    ScreenHeader: ({ title }: any) => R.createElement(RNText, { testID: 'screen-header' }, title),
    Card: ({ children, testID }: any) => R.createElement(View, { testID }, children),
    Divider: () => null,
    LoadingState: () => R.createElement(RNText, { testID: 'loading' }, 'loading'),
    ErrorState: ({ title, onRetry }: any) =>
      R.createElement(
        View,
        null,
        R.createElement(RNText, { testID: 'error-state' }, title),
        R.createElement(
          Pressable,
          { testID: 'retry', onPress: onRetry },
          R.createElement(RNText, null, 'retry'),
        ),
      ),
    EmptyState: ({ title }: any) => R.createElement(RNText, { testID: 'empty-state' }, title),
    Price: ({ children }: any) => R.createElement(RNText, null, children),
    Text: ({ children, testID }: any) => R.createElement(RNText, { testID }, children),
    Button: ({ label, onPress, disabled, loading }: any) =>
      R.createElement(
        Pressable,
        { testID: 'pay-button', onPress, accessibilityState: { disabled: !!disabled } },
        R.createElement(RNText, null, loading ? 'PAYING…' : label),
      ),
    // The term picker, made drivable: each option is pressable, and a disabled one
    // stays disabled — which is the whole point of a term with no stock.
    Select: ({ label, options, onChange }: any) =>
      R.createElement(
        View,
        { testID: `select-${label}` },
        (options ?? []).map((o: any) =>
          R.createElement(
            Pressable,
            {
              key: o.value,
              testID: `option-${o.value}`,
              onPress: o.disabled ? undefined : () => onChange(o.value),
              accessibilityState: { disabled: !!o.disabled },
            },
            R.createElement(RNText, null, `${o.label} ${o.description ?? ''}`),
          ),
        ),
      ),
  };
});

/** One voucher with terms; `outOfStock` marks a term as unavailable. */
function voucher(over: Record<string, any> = {}) {
  return {
    voucherId: 'v-1',
    eligible: true,
    branch: 'extend',
    provider: 'Shell',
    fuelTypeId: 'ft-1',
    fuelName: 'A95',
    liters: 50,
    terms: [
      { term: '1w', priceUah: 1000, available: true, unavailableReason: null },
      { term: '1m', priceUah: 3000, available: true, unavailableReason: null },
    ],
    ...over,
  };
}

function quote(over: Record<string, any> = {}) {
  return { enabled: true, thresholdDays: 14, vouchers: [voucher()], ...over };
}

beforeEach(() => {
  mockParams = { voucherIds: 'v-1' };
  mockQuoteRenewal.mockReset();
  mockCreateCheckout.mockReset();
  mockOpenURL.mockReset();
  mockReplace.mockReset();
  mockPush.mockReset();
  alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
  mockQuoteRenewal.mockResolvedValue(quote());
  mockCreateCheckout.mockResolvedValue({
    orderId: 'o-1',
    monobankInvoiceId: 'inv-1',
    paymentUrl: 'https://monobank.com.ua/pay/inv-1',
    totalUah: 1000,
  });
});

describe('Renew — getting to a quote', () => {
  it('asks for the vouchers named in the link', async () => {
    mockParams = { voucherIds: 'v-1,v-2' };
    render(<RenewScreen />);

    // The comma-separated ids are the whole addressing scheme; spaces and empties
    // are dropped rather than sent as a voucher that does not exist.
    await waitFor(() => expect(mockQuoteRenewal).toHaveBeenCalledWith(['v-1', 'v-2']));
  });

  it('trims stray whitespace and empty entries from the ids', async () => {
    mockParams = { voucherIds: ' v-1 , ,v-2 ' };
    render(<RenewScreen />);

    await waitFor(() => expect(mockQuoteRenewal).toHaveBeenCalledWith(['v-1', 'v-2']));
  });

  it('shows nothing to renew when no vouchers were named', async () => {
    mockParams = {};
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('empty-state')).toBeTruthy());
    // Quoting an empty list would be a pointless request the server must reject.
    expect(mockQuoteRenewal).not.toHaveBeenCalled();
  });

  it('offers a retry when the quote fails', async () => {
    mockQuoteRenewal.mockRejectedValue(new Error('network'));
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('error-state')).toBeTruthy());
    expect(screen.getByText('renew.loadFailed')).toBeTruthy();
  });

  it('quotes again on retry', async () => {
    mockQuoteRenewal.mockRejectedValueOnce(new Error('network'));
    mockQuoteRenewal.mockResolvedValue(quote());
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('error-state')).toBeTruthy());
    fireEvent.press(screen.getByTestId('retry'));

    await waitFor(() => expect(mockQuoteRenewal).toHaveBeenCalledTimes(2));
  });

  it('shows an empty state when renewal is switched off server-side', async () => {
    // The flag can flip between rendering and paying; the screen must not offer a
    // term picker for a feature that is off.
    mockQuoteRenewal.mockResolvedValue(quote({ enabled: false }));
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('empty-state')).toBeTruthy());
    expect(screen.getByText('renew.error.disabled')).toBeTruthy();
    expect(screen.queryByTestId('pay-button')).toBeNull();
  });

  it('shows an empty state when the batch holds no vouchers', async () => {
    mockQuoteRenewal.mockResolvedValue(quote({ vouchers: [] }));
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('empty-state')).toBeTruthy());
  });
});

describe('Renew — the term picker', () => {
  it('offers each term with its price', async () => {
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('option-1w')).toBeTruthy());
    expect(screen.getByText('renew.term.1w renew.priceUah (1000)')).toBeTruthy();
    expect(screen.getByText('renew.term.1m renew.priceUah (3000)')).toBeTruthy();
  });

  it('prices a term with no stock as unavailable and unpressable', async () => {
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [
          voucher({
            terms: [
              { term: '1w', priceUah: 1000, available: false, unavailableReason: 'no_stock' },
              { term: '1m', priceUah: 3000, available: true, unavailableReason: null },
            ],
          }),
        ],
      }),
    );
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('option-1w')).toBeTruthy());
    // Shown as unavailable rather than hidden, so the customer can see the tier
    // exists and why it cannot be bought.
    expect(screen.getByText('renew.term.1w renew.unavailable')).toBeTruthy();
    expect(screen.getByTestId('option-1w').props.accessibilityState.disabled).toBe(true);

    fireEvent.press(screen.getByTestId('option-1w'));
    await waitFor(() => expect(screen.getByTestId('pay-button')).toBeTruthy());
    // Still on the only buyable term: the press on a dead option changed nothing.
    expect(screen.getByText(/renew.pay/)).toHaveTextContent(/\(3000\)/);
  });

  it('defaults to the first available term, not the first listed', async () => {
    // The cheapest-looking tier can be sold out; defaulting to it would quote a
    // price for something that cannot be bought.
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [
          voucher({
            terms: [
              { term: '1w', priceUah: 1000, available: false, unavailableReason: 'no_stock' },
              { term: '1m', priceUah: 3000, available: true, unavailableReason: null },
            ],
          }),
        ],
      }),
    );
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('pay-button')).toBeTruthy());
    expect(screen.getByText(/renew.pay/)).toHaveTextContent(/\(3000\)/);
  });

  it('re-quotes the total when a different term is chosen', async () => {
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText(/renew.pay/)).toHaveTextContent(/\(1000\)/));
    fireEvent.press(screen.getByTestId('option-1m'));

    await waitFor(() => expect(screen.getByText(/renew.pay/)).toHaveTextContent(/\(3000\)/));
  });

  it('sums the chosen terms across several vouchers', async () => {
    mockParams = { voucherIds: 'v-1,v-2' };
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [
          voucher({ voucherId: 'v-1', terms: [{ term: '1w', priceUah: 1000, available: true }] }),
          voucher({ voucherId: 'v-2', terms: [{ term: '1m', priceUah: 3000, available: true }] }),
        ],
      }),
    );
    render(<RenewScreen />);

    // Two vouchers, two defaults, one total — the sum is the customer's agreement.
    await waitFor(() => expect(screen.getByText(/renew.pay/)).toHaveTextContent(/\(4000\)/));
  });
});

describe('Renew — vouchers that cannot be renewed', () => {
  it('keeps an ineligible voucher visible with its reason', async () => {
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [
          voucher(),
          voucher({
            voucherId: 'v-2',
            eligible: false,
            ineligibleReason: 'not_your_voucher',
            terms: [],
          }),
        ],
      }),
    );
    render(<RenewScreen />);

    // A batch mixing live and stale vouchers has to explain itself rather than
    // silently dropping one the customer expected to see.
    await waitFor(() => expect(screen.getByText('renew.ineligible.not_your_voucher')).toBeTruthy());
    // Both vouchers name the same station, so the count is what makes the point:
    // the stale one is on screen, not silently dropped.
    expect(screen.getAllByText('Shell · A95').length).toBe(2);
  });

  it('distinguishes a voucher that is not renewable from one that is not theirs', async () => {
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [
          voucher({
            voucherId: 'v-2',
            eligible: false,
            ineligibleReason: 'not_renewable',
            terms: [],
          }),
        ],
      }),
    );
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText('renew.ineligible.not_renewable')).toBeTruthy());
    expect(screen.queryByText('renew.ineligible.not_your_voucher')).toBeNull();
  });

  it('never prices or charges an ineligible voucher', async () => {
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [
          voucher({ terms: [{ term: '1w', priceUah: 1000, available: true }] }),
          voucher({
            voucherId: 'v-2',
            eligible: false,
            ineligibleReason: 'not_your_voucher',
            branch: 'extend',
            terms: [{ term: '1w', priceUah: 9999, available: true }],
          }),
        ],
      }),
    );
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText(/renew.pay/)).toHaveTextContent(/\(1000\)/));
    fireEvent.press(screen.getByText(/renew.pay/));

    await waitFor(() => expect(mockCreateCheckout).toHaveBeenCalled());
    // 9999 was on the wire in the quote; it must not reach the invoice.
    expect(mockCreateCheckout).toHaveBeenCalledWith([{ voucherId: 'v-1', termCode: '1w' }]);
  });

  it('offers no term picker for an ineligible voucher', async () => {
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [voucher({ eligible: false, ineligibleReason: 'not_renewable', terms: [] })],
      }),
    );
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText('renew.ineligible.not_renewable')).toBeTruthy());
    expect(screen.queryByTestId('option-1w')).toBeNull();
  });
});

describe('Renew — paying', () => {
  it('sends the chosen terms and routes to the in-app payment screen', async () => {
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText(/renew.pay/)).toBeTruthy());
    fireEvent.press(screen.getByText(/renew.pay/));

    await waitFor(() =>
      expect(mockCreateCheckout).toHaveBeenCalledWith([{ voucherId: 'v-1', termCode: '1w' }]),
    );
    // The payment page renders inside the app now, so this is a route assertion rather than an
    // openURL one - handing the customer to a browser was the thing being fixed.
    expect(mockPush).toHaveBeenCalledWith(
      expect.stringContaining('url=https%3A%2F%2Fmonobank.com.ua%2Fpay%2Finv-1'),
    );
    expect(mockOpenURL).not.toHaveBeenCalled();
  });

  it('stays put when no payment URL comes back', async () => {
    // An invoice with no page URL is not payable, and /pay would only render its
    // "missing link" screen - a dead end on a screen the customer already paid nothing for.
    mockCreateCheckout.mockResolvedValue({
      orderId: 'o-1',
      monobankInvoiceId: 'inv-1',
      paymentUrl: '',
      totalUah: 1000,
    });
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText(/renew.pay/)).toBeTruthy());
    fireEvent.press(screen.getByText(/renew.pay/));

    await waitFor(() => expect(mockCreateCheckout).toHaveBeenCalled());
    expect(mockPush).not.toHaveBeenCalled();
    expect(mockOpenURL).not.toHaveBeenCalled();
  });

  it('surfaces a failed renewal as a localised alert', async () => {
    // The server is the authoritative stock gate, so a tier can pass here and be
    // rejected at pay time. The message has to be the localised one for the reason.
    mockCreateCheckout.mockRejectedValue(new RenewalApiError('no stock', 409, 'tier_unavailable'));
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText(/renew.pay/)).toBeTruthy());
    fireEvent.press(screen.getByText(/renew.pay/));

    await waitFor(() => expect(alertSpy).toHaveBeenCalled());
    // The real `renewalErrorKey` maps this code to `renew.error.unavailable`.
    // Asserting the key rather than the alert existing is what proves the *reason*
    // reaches the customer instead of a generic failure.
    expect(alertSpy.mock.calls[0][1]).toBe('renew.error.unavailable');
    expect(mockReplace).not.toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();
  });

  it('falls back to a generic message for a failure with no reason', async () => {
    mockCreateCheckout.mockRejectedValue(new Error('socket hang up'));
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText(/renew.pay/)).toBeTruthy());
    fireEvent.press(screen.getByText(/renew.pay/));

    await waitFor(() => expect(alertSpy).toHaveBeenCalled());
    expect(mockReplace).not.toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();
  });

  it('re-enables the pay button after a failure', async () => {
    mockCreateCheckout.mockRejectedValue(new Error('socket hang up'));
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByText(/renew.pay/)).toBeTruthy());
    fireEvent.press(screen.getByText(/renew.pay/));

    // Without the reset the customer would be stranded on a spinner, with no way
    // to try again once stock came back.
    await waitFor(() => expect(screen.getByText('renew.pay (1000)')).toBeTruthy());
  });

  it('prices a voucher with no buyable term at nothing', async () => {
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [
          voucher({
            terms: [
              { term: '1w', priceUah: 1000, available: false, unavailableReason: 'no_stock' },
            ],
          }),
        ],
      }),
    );
    render(<RenewScreen />);

    // Every term sold out means there is nothing to buy, so nothing to charge. A
    // price here would be invented out of a term that cannot be bought.
    await waitFor(() => expect(screen.getByText(/renew.pay/)).toBeTruthy());
    expect(screen.getByText(/renew.pay/)).toHaveTextContent(/\(0\)/);
  });

  it('does nothing when there is nothing to pay for', async () => {
    mockQuoteRenewal.mockResolvedValue(
      quote({
        vouchers: [
          voucher({
            terms: [
              { term: '1w', priceUah: 1000, available: false, unavailableReason: 'no_stock' },
            ],
          }),
        ],
      }),
    );
    render(<RenewScreen />);

    await waitFor(() => expect(screen.getByTestId('pay-button')).toBeTruthy());
    expect(screen.getByTestId('pay-button').props.accessibilityState.disabled).toBe(true);

    fireEvent.press(screen.getByTestId('pay-button'));
    // Every term sold out must not produce a checkout for a voucher with no term.
    expect(mockCreateCheckout).not.toHaveBeenCalled();
  });
});
