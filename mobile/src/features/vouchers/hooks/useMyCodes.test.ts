import { renderHook, act, waitFor } from '@testing-library/react-native';
import { Alert } from 'react-native';
import { useMyCodes } from './useMyCodes';
import { getMyVouchers, getMyOrders, deleteMyOrder } from '../api/getVouchers';
import { markVoucherAsUsed, restoreVoucher, VoucherActionError } from '../api/updateVoucher';
import { reportError } from '../../../core/observability/sentry';
import type { Voucher, Order, Company } from '../../../core/types/api';
import type { MyCompanyMembershipDto } from '../../company/types';

// Mutable state the mocks read. Must be `mock`-prefixed so Jest allows referencing
// them from the hoisted jest.mock factories below.
let mockAuthState: { isAuthenticated: boolean; isLoading: boolean; user: { id: string } | null };
let mockStoreAuth: boolean;
let mockStoreCurrentLegalEntityId: string | null;
let mockCompanies: Company[];
let mockMemberships: MyCompanyMembershipDto[];

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
// controlled slice (auth flag + active account context, both read by the hook).
jest.mock('../../../core/state/appStore', () => ({
  useStore: (selector: (s: { isAuthenticated: boolean; currentLegalEntityId: string | null }) => unknown) =>
    selector({ isAuthenticated: mockStoreAuth, currentLegalEntityId: mockStoreCurrentLegalEntityId }),
}));

// useLegalEntities drags in legalEntityApi → apiClient → securityService →
// react-native-device-info (a NativeEventEmitter that can't initialise under Jest).
// The hook only needs the caller's owned companies to resolve the active context,
// so feed it a controlled list; the pure context/stock libs stay real.
jest.mock('../../company/hooks/useLegalEntities', () => ({
  useLegalEntities: () => ({ companies: mockCompanies }),
}));

// Same native-module chain, for the companies the user works for (epic #103 S5).
// `useAccountContext` stays real so the resolution under test is the real one.
jest.mock('../../company/hooks/useMemberships', () => ({
  useMemberships: () => ({ memberships: mockMemberships }),
}));

// Sentry boundary — the real module pulls in @sentry/react-native. We only assert
// WHICH failures get forwarded, so a bare jest.fn is enough.
jest.mock('../../../core/observability/sentry', () => ({
  reportError: jest.fn(),
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
  mockStoreCurrentLegalEntityId = null;
  mockCompanies = [];
  mockMemberships = [];
  asMock(getMyVouchers).mockResolvedValue([]);
  asMock(getMyOrders).mockResolvedValue([]);
  asMock(markVoucherAsUsed).mockResolvedValue({ success: true });
  asMock(restoreVoucher).mockResolvedValue({ success: true });
  asMock(deleteMyOrder).mockResolvedValue(undefined);
  asMock(reportError).mockClear();
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

  it('segregates renewal orders and keeps their voucher in the available list', async () => {
    const renewed = makeVoucher({ id: 'v-renewed' });
    asMock(getMyVouchers).mockResolvedValue([renewed]);
    asMock(getMyOrders).mockResolvedValue([
      makeOrder({ id: 'buy', status: 'FULFILLED' }),
      makeOrder({ id: 'renew', status: 'FULFILLED', isRenewal: true, vouchers: [renewed] }),
    ]);

    const { result } = renderHook(() => useMyCodes());
    await waitFor(() => expect(result.current.loading).toBe(false));

    // A renewal order goes to its own section, not mixed into fulfilled/pending purchases.
    expect(result.current.renewalOrders.map((o) => o.id)).toEqual(['renew']);
    expect(result.current.fulfilledOrders.map((o) => o.id)).toEqual(['buy']);
    expect(result.current.pendingOrders).toHaveLength(0);
    // The renewal "fulfilled" this voucher, but it must stay in the available list
    // (with its new expiry) rather than nesting under the renewal receipt.
    expect(result.current.unassignedVouchers.map((v) => v.id)).toEqual(['v-renewed']);
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

  it('surfaces a load failure as localized copy (never a raw message) and reports it', async () => {
    asMock(getMyVouchers).mockRejectedValue(new Error('network down'));

    const { result } = renderHook(() => useMyCodes());
    await waitFor(() => expect(result.current.loading).toBe(false));

    // The raw cause ("network down") must never reach the error state — localized key only.
    expect(result.current.error).toBe('codes.failedToLoad');
    // No status → treated as server/network → forwarded to Sentry.
    expect(reportError).toHaveBeenCalledTimes(1);
  });

  it('does not forward a client (4xx) load failure to Sentry', async () => {
    asMock(getMyVouchers).mockRejectedValue(Object.assign(new Error('bad request'), { status: 400 }));

    const { result } = renderHook(() => useMyCodes());
    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.error).toBe('codes.failedToLoad');
    expect(reportError).not.toHaveBeenCalled();
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

  // Multi-company epic #103, S2: the wallet is scoped to the active account
  // context. The pure filter/group logic has its own exhaustive suite in
  // stock.test.ts — these cover the hook wiring (context resolution + no leak).
  describe('account context scoping (S2)', () => {
    const company = { id: 'company-1', name: 'ACME', edrpou: '12345678' };

    it('personal context exposes only personal vouchers and no company stock', async () => {
      mockStoreCurrentLegalEntityId = null;
      mockCompanies = [company];
      asMock(getMyVouchers).mockResolvedValue([
        makeVoucher({ id: 'personal', legalEntityId: null }),
        makeVoucher({ id: 'company', legalEntityId: 'company-1' }),
      ]);

      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.isCompanyContext).toBe(false);
      expect(result.current.currentCompany).toBeNull();
      expect(result.current.vouchers.map((v) => v.id)).toEqual(['personal']);
    });

    it('company context scopes to that company and splits pool vs per-worker stock', async () => {
      mockStoreCurrentLegalEntityId = 'company-1';
      mockCompanies = [company];
      asMock(getMyVouchers).mockResolvedValue([
        makeVoucher({ id: 'personal', legalEntityId: null }),
        makeVoucher({ id: 'pool', legalEntityId: 'company-1', workerUserId: null, amount: 20 }),
        makeVoucher({
          id: 'worker-v',
          legalEntityId: 'company-1',
          workerUserId: 'w-1',
          workerFirstName: 'Іван',
          amount: 30,
        }),
      ]);

      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.isCompanyContext).toBe(true);
      expect(result.current.currentCompany?.id).toBe('company-1');
      // The personal voucher must not leak into the company context.
      expect(result.current.vouchers.map((v) => v.id).sort()).toEqual(['pool', 'worker-v']);
      expect(result.current.companyStock.pool.map((v) => v.id)).toEqual(['pool']);
      expect(result.current.companyStock.workers).toHaveLength(1);
      expect(result.current.companyStock.workers[0].workerUserId).toBe('w-1');
      expect(result.current.companyStock.workers[0].liters).toBe(30);
    });

    it('falls back to personal for a stale/foreign context id (no cross-account leak)', async () => {
      mockStoreCurrentLegalEntityId = 'company-GONE';
      mockCompanies = [company];
      asMock(getMyVouchers).mockResolvedValue([
        makeVoucher({ id: 'personal', legalEntityId: null }),
        makeVoucher({ id: 'company', legalEntityId: 'company-1' }),
      ]);

      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.isCompanyContext).toBe(false);
      expect(result.current.vouchers.map((v) => v.id)).toEqual(['personal']);
    });
  });

  // Multi-company epic #103, S5: a person who works for a company gets a context for
  // it. The regression this pins: `GET /api/vouchers/my` already returns the fuel
  // issued to a worker, but the wallet scoped by an owner-only company list dropped it —
  // the worker could not see or redeem their own fuel.
  describe('worker context (S5)', () => {
    const membership: MyCompanyMembershipDto = {
      memberId: 'm-1',
      legalEntityId: 'company-1',
      name: 'ACME',
      edrpou: '12345678',
      ownerUserId: 'owner-1',
      isOwner: false,
      joinedAtUtc: '2026-01-01T00:00:00Z',
    };

    beforeEach(() => {
      mockAuthState = { isAuthenticated: true, isLoading: false, user: { id: 'user-1' } };
      mockStoreCurrentLegalEntityId = 'company-1';
      mockCompanies = [];
      mockMemberships = [membership];
    });

    it('shows the worker the fuel issued to them, and nothing else from that company', async () => {
      asMock(getMyVouchers).mockResolvedValue([
        makeVoucher({ id: 'personal', legalEntityId: null }),
        makeVoucher({ id: 'mine', legalEntityId: 'company-1', workerUserId: 'user-1' }),
        makeVoucher({ id: 'pool', legalEntityId: 'company-1', workerUserId: null }),
        makeVoucher({ id: 'theirs', legalEntityId: 'company-1', workerUserId: 'w-2' }),
      ]);

      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.isWorkerContext).toBe(true);
      expect(result.current.isCompanyContext).toBe(true);
      expect(result.current.currentCompany?.name).toBe('ACME');
      expect(result.current.vouchers.map((v) => v.id)).toEqual(['mine']);
      // The employer's stock view is not the worker's.
      expect(result.current.companyStock.pool).toEqual([]);
      expect(result.current.companyStock.workers).toEqual([]);
    });

    it('never shows the employer\'s orders to a worker', async () => {
      asMock(getMyOrders).mockResolvedValue([
        makeOrder({ id: 'employer-buy', status: 'FULFILLED', legalEntityId: 'company-1' }),
      ]);

      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.orders).toEqual([]);
      expect(result.current.fulfilledOrders).toEqual([]);
    });

    it('lets the worker redeem the fuel issued to them', async () => {
      asMock(getMyVouchers).mockResolvedValue([
        makeVoucher({ id: 'mine', legalEntityId: 'company-1', workerUserId: 'user-1' }),
      ]);

      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      await act(async () => {
        await result.current.toggleUsed(
          makeVoucher({ id: 'mine', legalEntityId: 'company-1', workerUserId: 'user-1' }),
        );
      });

      expect(markVoucherAsUsed).toHaveBeenCalledWith('mine');
      expect(Alert.alert).not.toHaveBeenCalled();
    });

    it('falls back to personal once the membership is gone (fired worker)', async () => {
      mockMemberships = [];
      asMock(getMyVouchers).mockResolvedValue([
        makeVoucher({ id: 'personal', legalEntityId: null }),
        makeVoucher({ id: 'issued', legalEntityId: 'company-1', workerUserId: 'user-1' }),
      ]);

      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.isWorkerContext).toBe(false);
      expect(result.current.vouchers.map((v) => v.id)).toEqual(['personal']);
    });

    it('owner rights win when the user both owns and works for the company', async () => {
      mockCompanies = [{ id: 'company-1', name: 'ACME', edrpou: '12345678' }];
      mockMemberships = [{ ...membership, isOwner: true }];
      asMock(getMyVouchers).mockResolvedValue([
        makeVoucher({ id: 'pool', legalEntityId: 'company-1', workerUserId: null }),
      ]);

      const { result } = renderHook(() => useMyCodes());
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.isWorkerContext).toBe(false);
      expect(result.current.vouchers.map((v) => v.id)).toEqual(['pool']);
      expect(result.current.companyStock.pool.map((v) => v.id)).toEqual(['pool']);
    });
  });
});
