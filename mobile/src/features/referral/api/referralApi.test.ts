import { createReferralCode, redeemReferralCode, ReferralApiError } from './referralApi';
import { apiFetch } from '../../../core/api/apiClient';

jest.mock('../../../core/api/apiClient', () => ({
  apiFetch: jest.fn(),
}));

const apiFetchMock = apiFetch as jest.Mock;

/**
 * The referral endpoints, from the client's side.
 *
 * The backend (`Features/Referral/ReferralController.cs`) answers a rejected
 * referral with a **bare string** — `BadRequest("Code is required")`,
 * `Conflict(ex.Message)` — and a `429` with a `Retry-After` header. A client
 * that assumed a JSON error body would throw away the only information it
 * needs, on exactly the responses a customer is waiting to read.
 */
function response(status: number, body: string, headers: Record<string, string> = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: async () => body,
    json: async () => JSON.parse(body),
    headers: { get: (name: string) => headers[name] ?? null },
  } as any;
}

describe('createReferralCode', () => {
  beforeEach(() => {
    apiFetchMock.mockReset();
  });

  it('sends the code the way the server stores it', async () => {
    apiFetchMock.mockResolvedValue(
      response(200, JSON.stringify({ userId: 'u1', referralCode: 'ABC-123' })),
    );

    const result = await createReferralCode('  abc-123  ');

    expect(result).toEqual({ userId: 'u1', referralCode: 'ABC-123' });
    const [endpoint, options] = apiFetchMock.mock.calls[0];
    expect(endpoint).toBe('/api/referral/create');
    expect(options.method).toBe('POST');
    expect(JSON.parse(options.body)).toEqual({ code: 'ABC-123' });
  });

  it('reads the plain-text conflict the server sends', async () => {
    // The server's own words, verbatim: a 409 with this body is a code another
    // customer already holds.
    apiFetchMock.mockResolvedValue(response(409, 'Referral code already taken'));

    await expect(createReferralCode('ABC')).rejects.toMatchObject({
      code: 'already_taken',
      status: 409,
    });
  });

  it('reads a JSON error body too, in case the server ever grows one', async () => {
    apiFetchMock.mockResolvedValue(
      response(
        409,
        JSON.stringify({ title: 'Conflict', status: 409, detail: 'Referral code already set' }),
      ),
    );

    await expect(createReferralCode('ABC')).rejects.toMatchObject({ code: 'already_set' });
  });

  it('never puts a body it does not understand in front of the customer', async () => {
    // A proxy's HTML error page is not a referral failure message.
    apiFetchMock.mockResolvedValue(response(502, '<html><body>Bad Gateway</body></html>'));

    const error = await createReferralCode('ABC').catch((e) => e);

    expect(error).toBeInstanceOf(ReferralApiError);
    expect(error.code).toBe('unknown');
    expect(String(error.message)).not.toContain('Bad Gateway');
  });

  it('refuses a code longer than the column without asking the server', async () => {
    // The server has no check for this: the request binds and the save fails on
    // the column, which is a 500 and one of the rate limiter's five writes.
    await expect(createReferralCode('A'.repeat(51))).rejects.toMatchObject({ code: 'too_long' });
    expect(apiFetchMock).not.toHaveBeenCalled();
  });

  it('refuses a blank code without asking the server', async () => {
    await expect(createReferralCode('   ')).rejects.toMatchObject({ code: 'required' });
    expect(apiFetchMock).not.toHaveBeenCalled();
  });
});

describe('redeemReferralCode', () => {
  beforeEach(() => {
    apiFetchMock.mockReset();
  });

  it('posts the code and returns who referred the customer', async () => {
    apiFetchMock.mockResolvedValue(
      response(200, JSON.stringify({ userId: 'u2', redeemedCode: 'FRIEND10', referrerId: 'u1' })),
    );

    const result = await redeemReferralCode('friend10');

    expect(result).toEqual({ userId: 'u2', redeemedCode: 'FRIEND10', referrerId: 'u1' });
    const [endpoint, options] = apiFetchMock.mock.calls[0];
    expect(endpoint).toBe('/api/referral/redeem');
    expect(JSON.parse(options.body)).toEqual({ code: 'FRIEND10' });
  });

  it('recognises the self-referral refusal', async () => {
    apiFetchMock.mockResolvedValue(response(409, 'Cannot redeem your own referral code'));

    await expect(redeemReferralCode('MINE')).rejects.toMatchObject({ code: 'self_redeem' });
  });

  it('recognises an unknown code', async () => {
    apiFetchMock.mockResolvedValue(response(409, 'Referral code not found'));

    await expect(redeemReferralCode('NOPE')).rejects.toMatchObject({ code: 'not_found' });
  });

  it('recognises a second redemption', async () => {
    apiFetchMock.mockResolvedValue(response(409, 'Referral code already redeemed'));

    await expect(redeemReferralCode('FRIEND10')).rejects.toMatchObject({
      code: 'already_redeemed',
    });
  });

  it('carries the rate limiter’s Retry-After, so the customer can be told how long', async () => {
    // Both endpoints share one budget of five writes an hour, so a customer who
    // keeps trying gets a 429 — and "try again later" is not an answer.
    apiFetchMock.mockResolvedValue(response(429, '', { 'Retry-After': '120' }));

    await expect(redeemReferralCode('FRIEND10')).rejects.toMatchObject({
      code: 'rate_limited',
      status: 429,
      retryAfterSeconds: 120,
    });
  });

  it('still answers rate limiting when the limiter sent no Retry-After', async () => {
    apiFetchMock.mockResolvedValue(response(429, ''));

    const error = await redeemReferralCode('FRIEND10').catch((e) => e);

    expect(error.code).toBe('rate_limited');
    expect(error.retryAfterSeconds).toBeUndefined();
  });
});
