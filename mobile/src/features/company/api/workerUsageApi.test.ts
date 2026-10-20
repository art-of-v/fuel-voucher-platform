import { getWorkerUsage } from './workerUsageApi';
import { apiFetch } from '../../../core/api/apiClient';

jest.mock('../../../core/api/apiClient', () => ({
  apiFetch: jest.fn(),
}));

const apiFetchMock = apiFetch as jest.Mock;

/**
 * A page of the report. The header carries the server's own count, which is what ends the
 * loop when the last page happens to be exactly full.
 */
function page(ids: string[], totalCount?: number, litersReceived = 0) {
  return {
    ok: true,
    status: 200,
    json: async () => ({
      legalEntityId: 'company-1',
      totals: {
        count: totalCount ?? ids.length,
        litersReceived,
        litersUsed: 0,
        litersRemaining: 0,
        countUsed: 0,
        countRemaining: 0,
      },
      items: ids.map((id) => ({ id, brand: 'okko', liters: 10 })),
    }),
    headers: {
      get: (name: string) =>
        name === 'X-Total-Count' && totalCount !== undefined ? String(totalCount) : null,
    },
  } as any;
}

describe('getWorkerUsage', () => {
  beforeEach(() => {
    apiFetchMock.mockReset();
  });

  it('asks for the pages in order and joins them', async () => {
    apiFetchMock
      .mockResolvedValueOnce(page(['v1', 'v2'], 3))
      .mockResolvedValueOnce(page(['v3'], 3));

    const report = await getWorkerUsage('company-1');

    expect(report.items.map((i) => i.id)).toEqual(['v1', 'v2', 'v3']);
    expect(apiFetchMock).toHaveBeenCalledTimes(2);
    expect(apiFetchMock.mock.calls[0][0]).toContain('page=1');
    expect(apiFetchMock.mock.calls[1][0]).toContain('page=2');
  });

  /**
   * The regression this is for: a worker who has been handed a few hundred vouchers is a
   * worker whose report grew without the screen changing. The server pages the list; the app
   * asks for the pages one at a time.
   */
  it('never asks for a page that is bigger than the server is willing to answer', async () => {
    apiFetchMock.mockResolvedValue(page(['v1'], 1));

    await getWorkerUsage('company-1');

    expect(apiFetchMock.mock.calls[0][0]).toContain('pageSize=50');
  });

  it('stops on a short page, even when the server sends no count', async () => {
    apiFetchMock.mockResolvedValue(page(['v1']));

    const report = await getWorkerUsage('company-1');

    expect(report.items).toHaveLength(1);
    expect(apiFetchMock).toHaveBeenCalledTimes(1);
  });

  it('keeps the totals the server sent, which describe every voucher and not the page', async () => {
    // The totals are the server's, over EVERY voucher the worker holds. A page of one of three
    // must not change them.
    apiFetchMock
      .mockResolvedValueOnce(page(['v1'], 3, 35))
      .mockResolvedValueOnce(page(['v2'], 3, 35))
      .mockResolvedValueOnce(page(['v3'], 3, 35));

    const report = await getWorkerUsage('company-1');

    expect(report.items).toHaveLength(3);
    expect(report.totals.litersReceived).toBe(35);
    expect(report.totals.count).toBe(3);
  });

  it('fails loudly when the server does', async () => {
    apiFetchMock.mockResolvedValue({ ok: false, status: 500, json: async () => ({}) } as any);

    await expect(getWorkerUsage('company-1')).rejects.toThrow(/HTTP 500/);
  });
});
