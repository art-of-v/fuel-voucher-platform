import { getNotifications } from './notificationsApi';
import { apiFetch } from '../../../core/api/apiClient';

jest.mock('../../../core/api/apiClient', () => ({
  apiFetch: jest.fn(),
}));

const apiFetchMock = apiFetch as jest.Mock;

/**
 * The row's type and its target are the contract: the copy is prose, and a client that has to
 * understand the prose to route a notification is a client that breaks when the prose is
 * rewritten. So this suite pins what the app reads off the wire.
 */
function response(body: unknown, ok = true, status = 200) {
  return { ok, status, json: async () => body } as any;
}

describe('getNotifications', () => {
  beforeEach(() => {
    apiFetchMock.mockReset();
  });

  it('reads the type and the target off the row', async () => {
    apiFetchMock.mockResolvedValue(
      response([
        {
          id: 'n1',
          title: 'Вам видано паливо',
          message: 'QA company name видав вам 3 талони.',
          isRead: false,
          createdAt: '2026-10-21T10:00:00Z',
          type: 'voucher_issued',
          legalEntityId: 'company-1',
          orderId: null,
        },
      ]),
    );

    const [notification] = await getNotifications();

    expect(notification.type).toBe('voucher_issued');
    expect(notification.legalEntityId).toBe('company-1');
    expect(notification.orderId).toBeNull();
  });

  /**
   * A row written before the type existed has none. It is still a row the customer reads, and it
   * is a row with nothing to open - so it reads as a plain row rather than as an error.
   */
  it('reads a row without a type as a plain row', async () => {
    apiFetchMock.mockResolvedValue(
      response([
        {
          id: 'n1',
          title: 'Замовлення виконано',
          message: 'Ваше замовлення виконано.',
          isRead: true,
          createdAt: '2026-10-21T10:00:00Z',
        },
      ]),
    );

    const [notification] = await getNotifications();

    expect(notification.type).toBe('generic');
    expect(notification.legalEntityId).toBeNull();
    expect(notification.orderId).toBeNull();
  });

  /**
   * A type this build does not know reads as a plain row: the customer still reads it, this
   * build simply cannot route it.
   */
  it('reads a type it does not know as a plain row', async () => {
    apiFetchMock.mockResolvedValue(
      response([
        {
          id: 'n1',
          title: 'Щось нове',
          message: '...',
          isRead: false,
          createdAt: '2026-10-21T10:00:00Z',
          type: 'something_new',
        },
      ]),
    );

    const [notification] = await getNotifications();

    expect(notification.type).toBe('generic');
  });

  it('treats a 401 as an empty list rather than an error', async () => {
    apiFetchMock.mockResolvedValue(response(null, false, 401));

    await expect(getNotifications()).resolves.toEqual([]);
  });
});
