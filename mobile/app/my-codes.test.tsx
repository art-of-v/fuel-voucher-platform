import React from 'react';
import { Alert } from 'react-native';
import { render, screen, fireEvent, act } from '@testing-library/react-native';

import MyCodesScreen from './my-codes';
import { shareOrderReceipt } from '../src/features/vouchers/api/getOrderReceipt';

/**
 * What this suite covers, and why not "it renders".
 *
 * `my-codes.tsx` is an 899-line screen whose arithmetic already lives in
 * `lib/display.ts` and `renewal/eligibility.ts` with their own tests. What is untested
 * here is the screen's own decisions, and three of them are money-adjacent:
 *
 * - **a paid order must not be deletable.** `PENDING_FULFILLMENT` means the money is
 *   gone; allowing a delete would remove the customer's record of it.
 * - **the delete happens only after confirmation**, and a failure surfaces instead of
 *   leaving a row that looks deleted.
 * - **the pay button does not open a URL that is not there.**
 *
 * Plus the deep-link behaviour, which regresses silently: tapping an "order fulfilled"
 * push expands that order once, and a manual collapse afterwards has to stick rather
 * than being re-expanded by the same parameter on the next render.
 */

const mockUseMyCodes = jest.fn();
const mockDeleteOrder = jest.fn();
const mockSetSelectedVoucher = jest.fn();
const mockOpenURL = jest.fn();
/** Handle on the module-mocked expo-router, so tests can assert navigation. */
const mockPush = jest.fn();
const alertSpy = jest.spyOn(Alert, 'alert');

/** Set per test; the screen reads it through useLocalSearchParams. */
let mockSearchParams: { orderId?: string } = {};

jest.mock('expo-linking', () => ({
  openURL: (...args: unknown[]) => mockOpenURL(...args),
}));

// `expo-router` exports more than the screen calls directly — `GridPageLayout` reaches
// for the navigation hooks itself — so the mock answers for the whole surface.
// `jest.requireActual` is deliberately not used: the real router pulls in expo's
// native side, which has no meaning under Jest.
jest.mock('expo-router', () => ({
  Redirect: () => null,
  Stack: { Screen: () => null },
  Tabs: { Screen: () => null },
  // Indirect on purpose: this factory runs when `expo-router` is first required, which is
  // during the import of the screen above and therefore BEFORE the `const mockPush` initialiser
  // below has run. Reading it eagerly here yields undefined; calling through a closure defers
  // the lookup to press time.
  router: {
    push: (...a: unknown[]) => mockPush(...a),
    replace: jest.fn(),
    back: jest.fn(),
    navigate: jest.fn(),
  },
  useLocalSearchParams: () => mockSearchParams,
  useGlobalSearchParams: () => mockSearchParams,
  usePathname: () => '/my-codes',
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
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/features/vouchers/hooks/useMyCodes', () => ({
  // Forward the screen's options: the spent-fuel filter IS an option, and a factory that
  // swallowed it would make the chip's wiring untestable.
  useMyCodes: (options?: unknown) => mockUseMyCodes(options),
}));

jest.mock('../src/features/vouchers/renewal/api/renewal', () => ({
  getRenewalConfig: () => Promise.resolve({ enabled: false, thresholdDays: 30, tiers: [] }),
}));

// The receipt touches the real file system and the share sheet, neither of which exists under Jest.
jest.mock('../src/features/vouchers/api/getOrderReceipt', () => ({
  shareOrderReceipt: jest.fn().mockResolvedValue(undefined),
}));

const shareReceiptMock = shareOrderReceipt as jest.MockedFunction<typeof shareOrderReceipt>;

// A module mock must return an object carrying the named exports the screen imports.
// Returning a bare function here once made `VoucherDetailModal` undefined and React
// reported it three screens away as an invalid element type.
// Renders nothing; the tests assert on the voucher the screen HANDS the modal (via
// `setSelectedVoucher`) rather than on the modal's own output, because the mocked hook returns a
// static `selectedVoucher` and would never flip to visible.
jest.mock('../src/features/vouchers/components/VoucherDetailModal', () => ({
  VoucherDetailModal: () => null,
}));

// Decorative, and it reaches the theme through the `@/` alias rather than a relative
// path, which the Jest transform does not resolve the way the bundler does.
jest.mock('../src/components/glow-text', () => ({
  GlowText: () => null,
}));

// The cards are component-tested on their own; here they only need to expose the
// handlers the screen passes them.
jest.mock('../src/features/vouchers/components/OrderCard', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const { Pressable, Text } = require('react-native');
  return {
    OrderCard: (props: Record<string, any>) => (
      <>
        <Text testID={'order-' + props.order.id}>{String(props.order.status)}</Text>
        <Text testID={'expanded-' + props.order.id}>{String(props.isExpanded)}</Text>
        <Pressable
          testID={'delete-' + props.order.id}
          accessibilityRole="button"
          // The real OrderCard invokes `onDelete(order)`; forwarding the press event
          // instead handed the handler undefined and it failed on `order.status`.
          onPress={() => props.onDelete(props.order)}
        />
        <Pressable
          testID={'pay-' + props.order.id}
          accessibilityRole="button"
          onPress={() => props.onPay(props.order)}
        />
        {/* Optional call, like the real card: the screen hands onReceipt only to the
            customer's own order lists, and this mock renders for every list the screen has. */}
        <Pressable
          testID={'receipt-' + props.order.id}
          accessibilityRole="button"
          onPress={() => props.onReceipt?.(props.order)}
        />
        <Pressable
          testID={'toggle-' + props.order.id}
          accessibilityRole="button"
          onPress={() => props.onToggle(props.order.id)}
        />
        {(props.order.vouchers || []).map((voucher: { id: string }) => (
          <Pressable
            key={voucher.id}
            testID={'voucher-' + voucher.id}
            accessibilityRole="button"
            onPress={() => props.onVoucherPress?.(voucher)}
          />
        ))}
      </>
    ),
  };
});

jest.mock('../src/features/vouchers/components', () => ({
  VoucherCard: () => null,
  WalletSummaryBar: () => null,
  VoucherListControlsBar: () => null,
}));

jest.mock('../src/features/company/components', () => ({
  WorkerFuelHeader: () => null,
  WorkerUsageSection: () => null,
}));

// The worker usage report is server data (#150). Mocked like useMyCodes: the hook reaches the API
// client, which pulls in native modules that do not exist under Jest.
jest.mock('../src/features/company/hooks/useWorkerUsage', () => ({
  useWorkerUsage: () => ({ report: null, isLoading: false, isError: false, error: null }),
}));

const order = (id: string, status: string, extra: Record<string, unknown> = {}) => ({
  id,
  status,
  brand: 'okko',
  fuelType: 'A95',
  amount: 10,
  totalAmount: 500,
  createdAt: '2026-01-01T00:00:00Z',
  items: [],
  ...extra,
});

const baseData = {
  isAuthenticated: true,
  authLoading: false,
  user: { id: 'u1' },
  vouchers: [],
  orders: [],
  loading: false,
  error: null,
  selectedVoucher: null,
  setSelectedVoucher: mockSetSelectedVoucher,
  isCompanyContext: false,
  isWorkerContext: false,
  currentCompany: null,
  pendingOrders: [],
  fulfilledOrders: [],
  spentOrderCount: 0,
  issuanceOrders: [],
  looseIssuanceVouchers: [],
  loadData: jest.fn(),
  toggleUsed: jest.fn(),
  deleteOrder: mockDeleteOrder,
};

/** `params` becomes the screen's search params, i.e. the deep link it arrived with. */
function renderScreen(overrides: Record<string, unknown> = {}, params: { orderId?: string } = {}) {
  mockSearchParams = params;
  mockUseMyCodes.mockReturnValue({ ...baseData, ...overrides });
  return render(<MyCodesScreen />);
}

/** Answers the most recent Alert by invoking the button with the given label. */
function answerAlert(label: string) {
  // Index arithmetic rather than .at(-1): the tsconfig lib target predates it.
  const call = alertSpy.mock.calls[alertSpy.mock.calls.length - 1];
  if (!call) throw new Error('Alert.alert was never called');
  const buttons = call[2] as { text: string; onPress?: () => void }[] | undefined;
  const button = buttons?.find((b) => b.text === label);
  if (!button) {
    throw new Error(
      'no button labelled ' + label + '; offered: ' + (buttons ?? []).map((b) => b.text).join(', '),
    );
  }
  button.onPress?.();
}

describe('MyCodesScreen', () => {
  // Regression for planning #180: tapping a voucher on an order card must open the detail with the
  // History the sync response carried. Preferring the /api/vouchers/my copy here is what made the
  // History button dead on device — that DTO has no `history` field at all.
  describe('opening a voucher from an order card', () => {
    const history = [
      { type: 'Purchase', date: '2026-01-01T00:00:00Z', liters: 10, amount: 500 },
      {
        type: 'Renewal',
        date: '2026-02-01T00:00:00Z',
        liters: 10,
        amount: 300,
        validFrom: '2026-01-08',
        validTo: '2026-02-08',
        termCode: '1m',
      },
    ];

    it('merges the order-nested History with the fields only /api/vouchers/my carries', () => {
      renderScreen({
        fulfilledOrders: [
          order('buy', 'FULFILLED', {
            vouchers: [{ id: 'v1', status: 'Assigned', history, originOrderId: 'buy' }],
          }),
        ],
        // The same voucher as /api/vouchers/my sees it: no `history`, but the worker extras.
        vouchers: [{ id: 'v1', status: 'Assigned', workerFirstName: 'Petro' }],
      });

      fireEvent.press(screen.getByTestId('voucher-v1'));

      // What the modal receives is the merge of both views of the same voucher.
      const opened = mockSetSelectedVoucher.mock.calls[0][0] as Record<string, unknown>;
      expect(opened.id).toBe('v1');
      // The timeline the customer is owed: 1 purchase + 1 renewal.
      expect(opened.history).toEqual(history);
      expect(opened.originOrderId).toBe('buy');
      // ...without losing the worker name that only the /api/vouchers/my DTO carries.
      expect(opened.workerFirstName).toBe('Petro');
    });

    it('opens the order-nested voucher even when /api/vouchers/my knows nothing about it', () => {
      renderScreen({
        fulfilledOrders: [
          order('buy', 'FULFILLED', {
            vouchers: [{ id: 'v1', status: 'Assigned', history, originOrderId: 'buy' }],
          }),
        ],
        vouchers: [],
      });

      fireEvent.press(screen.getByTestId('voucher-v1'));

      const opened = mockSetSelectedVoucher.mock.calls[0][0] as Record<string, unknown>;
      expect(opened.history).toEqual(history);
    });
  });

  describe('deleting an order', () => {
    /**
     * Note on reachability: `onDelete` is wired only for `pendingOrders` and
     * `renewalOrders`, and `OrderCard` additionally gates its swipe actions behind
     * `needsPayment`. So a fulfilled order cannot be deleted through the UI at all —
     * two independent layers, both correct.
     *
     * That makes the screen's own `PENDING_FULFILLMENT` guard unreachable from
     * production, which is exactly why it is worth pinning: it is the layer that still
     * holds if either of the other two ever changes. The tests below reach it by
     * handing the screen a paid order inside `pendingOrders` — the one input that
     * would slip past the grouping.
     */
    it('refuses a paid order even when it arrives in the pending list', () => {
      // The customer has paid; deleting removes their only record of it.
      renderScreen({ pendingOrders: [order('paid-1', 'PENDING_FULFILLMENT')] });

      fireEvent.press(screen.getByTestId('delete-paid-1'));

      expect(alertSpy).toHaveBeenCalledWith('codes.cannotDeletePaidOrder');
      // Not merely "no delete dialog" — the guard must not even offer one.
      expect(alertSpy.mock.calls[alertSpy.mock.calls.length - 1][2]).toBeUndefined();
      expect(mockDeleteOrder).not.toHaveBeenCalled();
    });

    it('asks for confirmation on an unpaid order and deletes only afterwards', async () => {
      renderScreen({ pendingOrders: [order('unpaid-1', 'PENDING_PAYMENT')] });

      fireEvent.press(screen.getByTestId('delete-unpaid-1'));

      expect(alertSpy).toHaveBeenCalledWith(
        'codes.deleteOrder',
        'codes.deleteOrderConfirm',
        expect.anything(),
      );
      expect(mockDeleteOrder).not.toHaveBeenCalled();

      answerAlert('common.delete');
      await Promise.resolve();

      expect(mockDeleteOrder).toHaveBeenCalledWith('unpaid-1');
    });

    it('leaves the order alone if the confirmation is cancelled', async () => {
      renderScreen({ pendingOrders: [order('unpaid-1', 'PENDING_PAYMENT')] });

      fireEvent.press(screen.getByTestId('delete-unpaid-1'));
      answerAlert('common.cancel');
      await Promise.resolve();

      expect(mockDeleteOrder).not.toHaveBeenCalled();
    });

    it('tells the user when the delete fails', async () => {
      // Otherwise the row looks deleted while nothing happened.
      mockDeleteOrder.mockRejectedValueOnce(new Error('network'));
      renderScreen({ pendingOrders: [order('unpaid-1', 'PENDING_PAYMENT')] });

      fireEvent.press(screen.getByTestId('delete-unpaid-1'));
      answerAlert('common.delete');
      await Promise.resolve();
      await Promise.resolve();

      expect(alertSpy).toHaveBeenCalledWith('common.error', 'codes.deleteFailed');
    });
  });

  describe('paying for an order', () => {
    beforeEach(() => {
      mockPush.mockReset();
      mockOpenURL.mockReset();
    });

    it('routes to the in-app payment screen when the order has one', () => {
      renderScreen({
        pendingOrders: [
          order('pay-1', 'PENDING_PAYMENT', { monobankPaymentUrl: 'https://mono.test/x' }),
        ],
      });

      fireEvent.press(screen.getByTestId('pay-pay-1'));

      // The page renders inside the app now, so this is a route assertion, not an openURL one.
      expect(mockPush).toHaveBeenCalledWith(
        expect.stringContaining('url=https%3A%2F%2Fmono.test%2Fx'),
      );
      expect(mockOpenURL).not.toHaveBeenCalled();
    });

    it('opens nothing when the order has no payment URL', () => {
      // Opening `undefined` would throw or navigate somewhere meaningless.
      renderScreen({ pendingOrders: [order('pay-2', 'PENDING_PAYMENT')] });

      fireEvent.press(screen.getByTestId('pay-pay-2'));

      expect(mockPush).not.toHaveBeenCalled();
      expect(mockOpenURL).not.toHaveBeenCalled();
    });

    it('refuses to reopen payment for an order that is no longer awaiting it', () => {
      // Defence in depth, not the fix. OrderCard already withholds the swipe pay action for a
      // non-PENDING_PAYMENT order (canSwipe at OrderCard.tsx:96 gates renderRightActions), so a
      // cancelled order cannot be re-paid from the list today.
      //
      // The guard earns its place because `canSwipe` is UI-level: it can be regressed by a card
      // redesign, and handlePay is the single choke point where "this order is no longer payable"
      // must hold. It also documents, next to the assertion, that the real exposure is elsewhere
      // - a Monobank payment page left open in a browser stays payable for the invoice's whole
      // validity window, which the app cannot close from here.
      // Reuses the module-level spy rather than installing a second one. A local
      // `jest.spyOn(Alert, 'alert')` + `mockRestore()` pair silently detaches the shared spy for
      // every test that runs after this one, so their alert assertions pass against a function
      // nothing calls any more - the assertions look green and assert nothing.
      alertSpy.mockImplementation(() => {});

      renderScreen({
        pendingOrders: [
          order('pay-3', 'CANCELLED' as never, { monobankPaymentUrl: 'https://mono.test/stale' }),
        ],
      });

      fireEvent.press(screen.getByTestId('pay-pay-3'));

      expect(alertSpy).toHaveBeenCalledWith('codes.orderNotPayable');
      expect(mockPush).not.toHaveBeenCalled();
      expect(mockOpenURL).not.toHaveBeenCalled();

      // Restore the behaviour, not the spy: clearMocks already clears the recorded calls before
      // each test, and mockRestore would unhook the spy itself.
      alertSpy.mockImplementation(() => undefined);
    });
  });

  describe('the deep link that expands an order', () => {
    it('expands the order named by the parameter on arrival', () => {
      renderScreen(
        { pendingOrders: [order('deep-1', 'PENDING_FULFILLMENT')] },
        { orderId: 'deep-1' },
      );

      expect(screen.getByTestId('expanded-deep-1')).toHaveTextContent(/true/);
    });

    it('expands whichever order the parameter names, including after switching away and back', () => {
      // Recorded behaviour, and it is narrower than the comment above the effect
      // claims ("Consumed once per distinct orderId - a manual collapse afterwards
      // stays collapsed"). `consumedFocusRef` holds only the *most recent* id, so
      // A -> B -> A re-expands A: the check `consumed !== focusOrderId` is true again
      // once the ref has moved on to B.
      //
      // Asserting what the code does rather than what the comment says, so the gap is
      // visible in the suite. Whether the comment or the behaviour should change is a
      // product call — a collapse the user made silently reverting is a small bug, but
      // "the order you tapped from a push opens expanded" is arguably the intent.
      const orders = [order('a-1', 'PENDING_FULFILLMENT'), order('b-1', 'PENDING_FULFILLMENT')];

      const { rerender } = renderScreen({ pendingOrders: orders }, { orderId: 'a-1' });
      expect(screen.getByTestId('expanded-a-1')).toHaveTextContent(/true/);

      fireEvent.press(screen.getByTestId('toggle-a-1'));
      expect(screen.getByTestId('expanded-a-1')).toHaveTextContent(/false/);

      mockSearchParams = { orderId: 'b-1' };
      mockUseMyCodes.mockReturnValue({ ...baseData, pendingOrders: orders });
      rerender(<MyCodesScreen />);
      expect(screen.getByTestId('expanded-b-1')).toHaveTextContent(/true/);

      mockSearchParams = { orderId: 'a-1' };
      mockUseMyCodes.mockReturnValue({ ...baseData, pendingOrders: orders });
      rerender(<MyCodesScreen />);

      expect(screen.getByTestId('expanded-a-1')).toHaveTextContent(/true/);
    });

    it('expands nothing without the parameter', () => {
      renderScreen({ pendingOrders: [order('plain-1', 'PENDING_FULFILLMENT')] });

      expect(screen.getByTestId('expanded-plain-1')).toHaveTextContent(/false/);
    });
  });

  describe('toggling an order by hand', () => {
    it('expands and collapses', () => {
      renderScreen({ pendingOrders: [order('t-1', 'PENDING_FULFILLMENT')] });

      fireEvent.press(screen.getByTestId('toggle-t-1'));
      expect(screen.getByTestId('expanded-t-1')).toHaveTextContent(/true/);

      fireEvent.press(screen.getByTestId('toggle-t-1'));
      expect(screen.getByTestId('expanded-t-1')).toHaveTextContent(/false/);
    });
  });

  describe('the spent-fuel filter', () => {
    it('is absent when no order is fully spent', () => {
      renderScreen({ fulfilledOrders: [order('live', 'FULFILLED')] });

      expect(screen.queryByTestId('spent-orders-toggle')).toBeNull();
    });

    it('asks the data layer for spent vouchers once it is switched on', () => {
      // One switch covers both levels: the hook hands back the fully spent orders AND the spent
      // vouchers inside every order it already shows.
      renderScreen({ fulfilledOrders: [order('live', 'FULFILLED')], spentOrderCount: 3 });

      fireEvent.press(screen.getByTestId('spent-orders-toggle'));

      const calls = mockUseMyCodes.mock.calls;
      expect(calls[calls.length - 1][0]).toEqual({ includeSpent: true });
    });

    it('is offered when every order is spent, so the history is not lost', () => {
      // Nothing live means the empty state, and the empty state is exactly where a toggle hidden
      // behind "not empty" would strand the customer's receipts.
      renderScreen({ fulfilledOrders: [], spentOrderCount: 2 });

      expect(screen.getByTestId('spent-orders-toggle')).toBeTruthy();
    });
  });

  describe('the signed-out gate', () => {
    it('renders no wallet for an unauthenticated user', () => {
      renderScreen({
        isAuthenticated: false,
        authLoading: false,
        pendingOrders: [order('x', 'PENDING_FULFILLMENT')],
      });

      expect(screen.queryByTestId('order-x')).toBeNull();
    });
  });
});

describe('receipt download (#119)', () => {
  beforeEach(() => {
    shareReceiptMock.mockClear();
    shareReceiptMock.mockResolvedValue(undefined);
  });

  it('downloads the receipt for the pressed order', async () => {
    renderScreen({ fulfilledOrders: [order('receipted-1', 'FULFILLED')] });

    await act(async () => {
      fireEvent.press(screen.getByTestId('receipt-receipted-1'));
    });

    expect(shareReceiptMock).toHaveBeenCalledWith('receipted-1');
  });

  // A share sheet that never opens looks identical to a button that does nothing, so the failure
  // has to say something - and say why.
  it('alerts with the reason when the download fails', async () => {
    shareReceiptMock.mockRejectedValue(new Error('Order not found'));
    renderScreen({ fulfilledOrders: [order('receipted-1', 'FULFILLED')] });

    // A real macrotask tick after the press. The handler awaits the download before it
    // decides to alert, and neither a bare expect nor waitFor observed the rejection settle here -
    // this flush does.
    await act(async () => {
      fireEvent.press(screen.getByTestId('receipt-receipted-1'));
    });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(alertSpy).toHaveBeenCalledWith(
      expect.any(String),
      expect.stringContaining('Order not found'),
    );
  });

  // #161 H4: the owner's wallet in a company context is receipts and history, and nothing else.
  // The hub owns who holds what and what is left, so the two screens cannot both list the fuel.
  describe('the owner company context', () => {
    const company = { id: 'le1', name: 'ACME LLC' };
    const receipt = order('co-buy', 'FULFILLED', { legalEntityId: 'le1' });

    it('shows the company its receipts', () => {
      renderScreen({ isCompanyContext: true, currentCompany: company, fulfilledOrders: [receipt] });

      expect(screen.getByTestId('receipt-co-buy')).toBeTruthy();
    });

    it('keeps the link to the hub, which is where the stock lives', () => {
      renderScreen({ isCompanyContext: true, currentCompany: company, fulfilledOrders: [receipt] });

      expect(screen.getByText('company.managementTitle')).toBeTruthy();
    });

    it('is not empty just because its receipts are gone', () => {
      // A company whose fuel has all been used up has nothing left to show in the wallet, and
      // saying so is honest - the hub is one tap away for the operational view.
      renderScreen({ isCompanyContext: true, currentCompany: company, fulfilledOrders: [] });

      expect(screen.queryByTestId('receipt-co-buy')).toBeNull();
    });

    it('still offers the way into the hub when the wallet is empty', () => {
      // The regression this guards: the hub link used to sit inside the "not empty" branch, so a
      // company whose receipts were all spent up lost its only route to the operational view.
      renderScreen({ isCompanyContext: true, currentCompany: company, fulfilledOrders: [] });

      expect(screen.getByText('company.managementTitle')).toBeTruthy();
    });
  });
});
