import React from 'react';
import { Alert } from 'react-native';
import { render, screen, fireEvent } from '@testing-library/react-native';

import MyCodesScreen from './my-codes';

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
  router: { push: jest.fn(), replace: jest.fn(), back: jest.fn(), navigate: jest.fn() },
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
  useMyCodes: () => mockUseMyCodes(),
}));

jest.mock('../src/features/vouchers/renewal/api/renewal', () => ({
  getRenewalConfig: () => Promise.resolve({ enabled: false, minDaysBeforeExpiry: 30 }),
}));

// A module mock must return an object carrying the named exports the screen imports.
// Returning a bare function here once made `VoucherDetailModal` undefined and React
// reported it three screens away as an invalid element type.
jest.mock('../src/components/VoucherDetailModal', () => ({
  VoucherDetailModal: () => null,
}));

// Decorative, and it reaches the theme through the `@/` alias rather than a relative
// path, which the Jest transform does not resolve the way the bundler does.
jest.mock('../src/components/glow-text', () => ({
  GlowText: () => null,
}));

// The cards are component-tested on their own; here they only need to expose the
// handlers the screen passes them.
jest.mock('../src/components/OrderCard', () => {
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
        <Pressable
          testID={'toggle-' + props.order.id}
          accessibilityRole="button"
          onPress={() => props.onToggle(props.order.id)}
        />
      </>
    ),
  };
});

jest.mock('../src/features/vouchers/components', () => ({
  VoucherCard: () => null,
  WalletSummaryBar: () => null,
}));

jest.mock('../src/features/company/components', () => ({
  CompanyStockHeader: () => null,
  WorkerFuelHeader: () => null,
}));

const order = (id: string, status: string, extra: Record<string, unknown> = {}) => ({
  id,
  status,
  provider: 'okko',
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
  companyStock: { pool: [], poolLiters: 0, workers: [] },
  pendingOrders: [],
  fulfilledOrders: [],
  renewalOrders: [],
  issuanceOrders: [],
  looseIssuanceVouchers: [],
  unassignedVouchers: [],
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
    it('opens the payment URL when the order has one', () => {
      renderScreen({
        pendingOrders: [
          order('pay-1', 'PENDING_PAYMENT', { monobankPaymentUrl: 'https://mono.test/x' }),
        ],
      });

      fireEvent.press(screen.getByTestId('pay-pay-1'));

      expect(mockOpenURL).toHaveBeenCalledWith('https://mono.test/x');
    });

    it('opens nothing when the order has no payment URL', () => {
      // Opening `undefined` would throw or navigate somewhere meaningless.
      renderScreen({ pendingOrders: [order('pay-2', 'PENDING_PAYMENT')] });

      fireEvent.press(screen.getByTestId('pay-pay-2'));

      expect(mockOpenURL).not.toHaveBeenCalled();
    });
  });

  describe('the deep link that expands an order', () => {
    it('expands the order named by the parameter on arrival', () => {
      renderScreen(
        { fulfilledOrders: [order('deep-1', 'PENDING_FULFILLMENT')] },
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

      const { rerender } = renderScreen({ fulfilledOrders: orders }, { orderId: 'a-1' });
      expect(screen.getByTestId('expanded-a-1')).toHaveTextContent(/true/);

      fireEvent.press(screen.getByTestId('toggle-a-1'));
      expect(screen.getByTestId('expanded-a-1')).toHaveTextContent(/false/);

      mockSearchParams = { orderId: 'b-1' };
      mockUseMyCodes.mockReturnValue({ ...baseData, fulfilledOrders: orders });
      rerender(<MyCodesScreen />);
      expect(screen.getByTestId('expanded-b-1')).toHaveTextContent(/true/);

      mockSearchParams = { orderId: 'a-1' };
      mockUseMyCodes.mockReturnValue({ ...baseData, fulfilledOrders: orders });
      rerender(<MyCodesScreen />);

      expect(screen.getByTestId('expanded-a-1')).toHaveTextContent(/true/);
    });

    it('expands nothing without the parameter', () => {
      renderScreen({ fulfilledOrders: [order('plain-1', 'PENDING_FULFILLMENT')] });

      expect(screen.getByTestId('expanded-plain-1')).toHaveTextContent(/false/);
    });
  });

  describe('toggling an order by hand', () => {
    it('expands and collapses', () => {
      renderScreen({ fulfilledOrders: [order('t-1', 'PENDING_FULFILLMENT')] });

      fireEvent.press(screen.getByTestId('toggle-t-1'));
      expect(screen.getByTestId('expanded-t-1')).toHaveTextContent(/true/);

      fireEvent.press(screen.getByTestId('toggle-t-1'));
      expect(screen.getByTestId('expanded-t-1')).toHaveTextContent(/false/);
    });
  });

  describe('the signed-out gate', () => {
    it('renders no wallet for an unauthenticated user', () => {
      renderScreen({
        isAuthenticated: false,
        authLoading: false,
        fulfilledOrders: [order('x', 'PENDING_FULFILLMENT')],
      });

      expect(screen.queryByTestId('order-x')).toBeNull();
    });
  });
});
