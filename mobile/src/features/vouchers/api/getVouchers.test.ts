import { getMyOrders, getMyVouchers } from './getVouchers';
import { apiFetch } from '../../../core/api/apiClient';

jest.mock('../../../core/api/apiClient', () => ({
  apiFetch: jest.fn(),
}));

const apiFetchMock = apiFetch as jest.Mock;

/**
 * A page response. The header carries the server's own count, which is what ends the
 * loop when the last page happens to be exactly full.
 */
function page(ids: string[], totalCount?: number) {
  return {
    ok: true,
    status: 200,
    json: async () => ids.map((id) => ({ id, brand: 'okko', fuelType: 'A-95', liters: 10 })),
    headers: {
      get: (name: string) =>
        name === 'X-Total-Count' && totalCount !== undefined ? String(totalCount) : null,
    },
  } as any;
}

describe('getMyVouchers', () => {
  beforeEach(() => {
    apiFetchMock.mockReset();
  });

  it('asks for the pages in order and joins them', async () => {
    apiFetchMock
      .mockResolvedValueOnce(page(['v1', 'v2'], 3))
      .mockResolvedValueOnce(page(['v3'], 3));

    const vouchers = await getMyVouchers();

    expect(vouchers.map((v) => v.id)).toEqual(['v1', 'v2', 'v3']);
    expect(apiFetchMock).toHaveBeenCalledTimes(2);
    expect(apiFetchMock.mock.calls[0][0]).toContain('page=1');
    expect(apiFetchMock.mock.calls[1][0]).toContain('page=2');
  });

  /**
   * The regression this is for: the list carries the redeemable QR of every voucher in it,
   * and a rendered PNG per row, so one customer with a few hundred vouchers made the API
   * render all of their QR images in a single request. The server pages the list; the app
   * asks for the pages one at a time.
   */
  it('never asks for a page that is bigger than the server is willing to render', async () => {
    apiFetchMock.mockResolvedValue(page(['v1'], 1));

    await getMyVouchers();

    expect(apiFetchMock.mock.calls[0][0]).toContain('pageSize=50');
  });

  it('stops on a short page, even when the server sends no count', async () => {
    apiFetchMock.mockResolvedValue(page(['v1']));

    const vouchers = await getMyVouchers();

    expect(vouchers).toHaveLength(1);
    expect(apiFetchMock).toHaveBeenCalledTimes(1);
  });

  it('stops on the server count when the last page is exactly full', async () => {
    // Fifty of them, and the count says fifty: the second page would be empty, and the
    // count is what says so without asking for it.
    const fifty = Array.from({ length: 50 }, (_, i) => `v${i}`);
    apiFetchMock.mockResolvedValueOnce(page(fifty, 50)).mockResolvedValueOnce(page([], 50));

    const vouchers = await getMyVouchers();

    expect(vouchers).toHaveLength(50);
    expect(apiFetchMock).toHaveBeenCalledTimes(1);
  });

  it('yields an empty list when the caller is signed out', async () => {
    apiFetchMock.mockResolvedValue({ ok: false, status: 401, json: async () => ({}) } as any);

    await expect(getMyVouchers()).resolves.toEqual([]);
  });

  it('throws on a server failure', async () => {
    apiFetchMock.mockResolvedValue({ ok: false, status: 500, json: async () => ({}) } as any);

    await expect(getMyVouchers()).rejects.toThrow('Failed to fetch vouchers');
  });
});

/**
 * The wallet decides what a customer may do with an order, and until app PR #1712 the only thing an
 * unpaid order could be waiting for was its bank. A promotional order looks identical in this
 * response - no invoice, no payment URL - and is waiting for the settlement channel instead, so the
 * one field that separates them has to survive the read or the row offers a payment it cannot honour
 * and answers it with "buy again" (planning #889, item 6).
 */
describe('getMyOrders — which checkout wrote the order', () => {
  beforeEach(() => apiFetchMock.mockReset());

  function ordersBody(orders: Record<string, unknown>[]) {
    return { ok: true, status: 200, json: async () => orders } as any;
  }

  async function one(order: Record<string, unknown>) {
    apiFetchMock.mockResolvedValue(ordersBody([{ id: 'o1', status: 'PendingPayment', ...order }]));
    const [result] = await getMyOrders();
    return result;
  }

  it('keeps the route of an order the promotional button wrote', async () => {
    const order = await one({ settlementRoute: 'Declared' });

    expect(order.status).toBe('PENDING_PAYMENT');
    expect(order.settlementRoute).toBe('Declared');
  });

  it('keeps the route of an order the bank route wrote', async () => {
    const order = await one({ settlementRoute: 'Acquirer', monobankPaymentUrl: 'https://pay' });

    expect(order.settlementRoute).toBe('Acquirer');
  });

  it('leaves the route un-set for a server older than the column', async () => {
    // Absent means the bank, which is what every order was before the second button existed; the
    // wallet must keep offering the payment rather than withhold it on a guess.
    const order = await one({ monobankPaymentUrl: 'https://pay' });

    expect(order.settlementRoute).toBeUndefined();
  });

  it('does not take a route it has not heard of for the promotional one', async () => {
    // Withholding a payment is the destructive half of this field: a value the app does not know must
    // not be allowed to remove the only action that can complete the order.
    const order = await one({
      settlementRoute: 'SomeFutureRoute',
      monobankPaymentUrl: 'https://pay',
    });

    expect(order.settlementRoute).toBeUndefined();
  });
});
