import { renderHook, act, waitFor } from '@testing-library/react-native';
import { Alert } from 'react-native';
import { useMyCodes } from './useMyCodes';
import { getMyVouchers, getMyOrders, deleteMyOrder } from '../api/getVouchers';
import { markVoucherAsUsed, restoreVoucher, VoucherActionError } from '../api/updateVoucher';
import type { Voucher, Order } from '../../../core/types/api';

// Mutable state the mocks read. Must be `mock`-prefixed so Jest allows referencing
// them from the hoisted jest.mock factories below.
let mockAuthState: { isAuthenticated: boolean; isLoading: boolean; user: { id: string } | null };
let mockStoreAuth: boolean;

// Mock the network boundary (everything funnels through apiFetch, which pulls in
// expo-constants / secure-store / device signing — none of which belong in a unit
// test). The screen's data layer is what we're testing, not the transport.
jest.mock('../api/getVouchers', () => ({
  getMyVouchers: jest.fn(),
  getMyOrders: jest.fn(),
  deleteMyOrder: jest.fn(),
}));

// Re-implement VoucherActionError inside the mock so `error instanceof
// VoucherActionError` in the hook resolves against the SAME class the test throws.
// (requireActual would drag the real apiClient graph in; the class is trivial.)
jest.mock('../api/updateVoucher', () => {
  class MockVoucherActionError extends Error {
    status: number;
    code: string;
    constructor(status: number, code: string) {
      super(code);
      this.name = 'VoucherActionError';
      this.status = status;
      this.code = code;
    }
  }
  return {
    VoucherActionError: MockVoucherActionError,
    markVoucherAsUsed: jest.fn(),
    restoreVoucher: jest.fn(),
  };
});

// t() returns the key verbatim so assertions can check WHICH message was chosen
// without coupling to translation copy.
jest.mock('../../../core/i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}));

jest.mock('../../auth/hooks/useAuth', () => ({
  useAuth: () => mockAuthState,
}));

// zustand's useStore is called with a selector; faithfully invoke it against a
// controlled slice.
jest.mock('../../../core/state/appStore', () => ({
  useStore: (selector: (s: { isAuthenticated: boolean }) => unknown) =>
    selector({ isAuthenticated: mockStoreAuth }),
}));

// NOTE: ../../../core/types/api is deliberately NOT mocked — classifyVoucher is the
// real security guard and is pure (no native deps), so the tests exercise it for real.

const asMock = (fn: unknown) => fn as jest.Mock;

function makeVoucher(overrides: Partial<Voucher> = {}): Voucher {
  return {
    id: 'v1',
    provider: 'okko',
    fuelType: 'a95',
    amount: 10,
    status: 'active',
    legalEntityId: null,
    workerUserId: null,
    ...overrides,
  };
}

function makeOrder(overrides: Partial<Order> = {}): Order {
  return {
    id: 'o1',
    provider: 'okko',
    fuelType: 'a95',
    liters: 10,
    quantity: 1,
    price: 500,
    status: 'PENDING_PAYMENT',
    createdAt: '2026-01-01T00:00:00Z',
    fulfilledAt: null,
    vouchers: [],
    lineItems: [],
    ...overrides,
  };
}

beforeEach(() => {
  mockAuthState = { isAuthenticated: true, isLoading: false, user: { id: 'user-1' } };
  mockStoreAuth = false;
  asMock(getMyVouchers).mockResolvedValue([]);
  asMock(getMyOrders).mockResolvedValue([]);
  asMock(markVoucherAsUsed).mockResolvedValue({ success: true });
  asMock(restoreVoucher).mockResolvedValue({ success: true });
  asMock(deleteMyOrder).mockResolvedValue(undefined);
  jest.spyOn(Alert, 'alert').mockImplementation(() => {});
});

describe('useMyCodes', () => {
  it('loads vouchers and orders on mount and splits orders into pending/fulfilled', async () => {
    asMock(getMyVouchers).mockResolvedValue([makeVoucher({ id: 'v1' })]);
    asMock(getMyOrders).mockResolvedValue([
      makeOrder({ id: 'pending', status: 'PENDING_PAYMENT' }),
      makeOrder({ id: 'done', status: 'FULFILLED' }),
    ]);

    const { result } = renderHook(() => useMyCodes());

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(getMyVouchers).toHaveBeenCalledTimes(1);
    expect(result.current.vouchers).toHaveLength(1);
    expect(result.current.pendingOrders.map((o) => o.id)).toEqual(['pending']);
    expect(result.current.fulfilledOrders.map((o) => o.id)).toEqual(['done']);
    // v1 is in no order's voucher list, so it is unassigned.
    expect(result.current.unassignedVouchers.map((v) => v.id)).toEqual(['v1']);
    expect(result.current.error).toBeNull();
  });

  it('excludes vouchers that belong to an order from unassignedVouchers', async () => {
    const assigned = makeVoucher({ id: 'v-assigned' });
    asMock(getMyVouchers).mockResolvedValue([assigned, makeVoucher({ id: 'v-loose' })]);
    asMock(getMyOrders).mockResolvedValue([makeOrder({ id: 'o1', vouchers: [assigned] })]);

    const { result } = renderHook(() => useMyCodes());
    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.unassignedVouchers.map((v) => v.id)).toEqual(['v-loose']);
  });

  it('does not fetch when the user is not authenticated', async () => {
    mockAuthState = { isAuthenticated: false, isLoading: false, user: null };
    mockStoreAuth = false;

    const { result } = renderHook(() => useMyCodes());
    // Flush the mount effect.
    await act(async () => {});

    expect(getMyVouchers).not.toHaveBeenCalled();
    expect(result.current.isAuthenticated).toBe(false);
  });

  it('surfaces a load failure as an error message and stops loading', async () => {
    asMock(getMyVouchers).mockRejectedValue(new Error('network down'));

    const { result } = renderHook(() => useMyCodes());
    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.error).toBe('network down');
  });

  describe('toggleUsed ownership guard', () => {
    it('blocks redemption of a blocked voucher without calling the API', async () => {
      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      await act(async () => {
        await result.current.toggleUsed(makeVoucher({ status: 'blocked' }));
      });

      expect(Alert.alert).toHaveBeenCalledWith('common.error', 'voucher.error.blocked');
      expect(markVoucherAsUsed).not.toHaveBeenCalled();
      expect(restoreVoucher).not.toHaveBeenCalled();
    });

    it("blocks redemption of another worker's gifted voucher", async () => {
      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      await act(async () => {
        await result.current.toggleUsed(
          makeVoucher({ legalEntityId: 'company-1', workerUserId: 'someone-else' }),
        );
      });

      expect(Alert.alert).toHaveBeenCalledWith('common.error', 'voucher.error.workerOnly');
      expect(markVoucherAsUsed).not.toHaveBeenCalled();
    });
  });

  it('maps a VoucherActionError to its localized message on a failed mark-used', async () => {
    asMock(markVoucherAsUsed).mockRejectedValue(new VoucherActionError(403, 'forbidden'));

    const { result } = renderHook(() => useMyCodes());
    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      await result.current.toggleUsed(makeVoucher({ id: 'v1', status: 'active' }));
    });

    expect(markVoucherAsUsed).toHaveBeenCalledWith('v1');
    expect(Alert.alert).toHaveBeenCalledWith('common.error', 'voucher.error.workerOnly');
  });

  it('optimistically drops a deleted order from the list', async () => {
    asMock(getMyOrders).mockResolvedValue([
      makeOrder({ id: 'keep', status: 'PENDING_PAYMENT' }),
      makeOrder({ id: 'drop', status: 'PENDING_PAYMENT' }),
    ]);

    const { result } = renderHook(() => useMyCodes());
    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      await result.current.deleteOrder('drop');
    });

    expect(deleteMyOrder).toHaveBeenCalledWith('drop');
    expect(result.current.orders.map((o) => o.id)).toEqual(['keep']);
  });
});
