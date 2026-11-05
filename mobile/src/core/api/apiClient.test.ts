import { apiFetch, apiRequest } from './apiClient';
import { SecurityService } from './securityService';
import { TokenStorage } from './tokenStorage';
import {
  clearAppleAttestationKey,
  getAttestationHeaders,
  isAttestationProtected,
} from '../security/appAttestation';

jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { version: '1.2.0', extra: { apiUrl: 'https://api.test' } } },
}));

jest.mock('../security/sslPinning', () => ({
  ensureSslPinningInitialized: jest.fn().mockResolvedValue(undefined),
}));

jest.mock('../security/appAttestation', () => ({
  clearAppleAttestationKey: jest.fn().mockResolvedValue(undefined),
  getAttestationHeaders: jest.fn().mockResolvedValue(null),
  isAttestationProtected: jest.fn().mockReturnValue(false),
}));

jest.mock('./securityService', () => ({
  SecurityService: {
    getDeviceId: jest.fn().mockResolvedValue('device-1'),
    hasKeys: jest.fn().mockResolvedValue(false),
    signPayload: jest.fn(),
  },
}));

jest.mock('./tokenStorage', () => ({
  TokenStorage: {
    getAccessToken: jest.fn().mockResolvedValue(null),
    getRefreshToken: jest.fn().mockResolvedValue(null),
    saveTokens: jest.fn(),
    clearTokens: jest.fn(),
  },
}));

/**
 * The refresh token travels in the response body only for a client that says it is native: the
 * browser gets it in the HttpOnly cookie and never in the body, because a script on the origin can
 * read a response body and cannot read an HttpOnly cookie. React Native has no cookie jar, so the
 * body is the only place the token can live here - and if the app stopped saying so, the backend
 * would hand it nothing and every session would break.
 */
describe('apiClient', () => {
  const fetchMock = jest.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ accessToken: 'a', expiresIn: 900 }),
    });
    (global as unknown as { fetch: unknown }).fetch = fetchMock;
  });

  it('says it is the native client on every request', async () => {
    await apiRequest('POST', '/api/auth/verify', { phoneNumber: '+380991110001' });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers['X-Client-Platform']).toBe('mobile');
  });

  it('says it on the refresh too, so the rotated token comes back in the body', async () => {
    await apiRequest('POST', '/api/auth/refresh', { refreshToken: 'r' });

    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers['X-Client-Platform']).toBe('mobile');
  });

  it('tells the server which OS and app version are on the other end', async () => {
    await apiRequest('GET', '/api/brands');

    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers['X-Client-OS']).toBe('ios');
    expect(init.headers['X-App-Version']).toBe('1.2.0');
  });

  it('attaches the proof to a protected request only', async () => {
    (isAttestationProtected as jest.Mock).mockReturnValueOnce(true);
    (getAttestationHeaders as jest.Mock).mockResolvedValueOnce({
      'X-Attestation-Provider': 'apple',
      'X-Attestation-Challenge': 'challenge',
      'X-Attestation-Proof': 'proof',
      'X-Attestation-Key-Id': 'key',
    });

    await apiRequest('POST', '/api/purchases', { amount: 1 });

    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers['X-Attestation-Provider']).toBe('apple');
    expect(init.headers['X-Attestation-Key-Id']).toBe('key');
    expect(getAttestationHeaders).toHaveBeenCalledWith(
      'POST',
      '/api/purchases',
      JSON.stringify({ amount: 1 }),
    );
  });

  it('re-signs after replacing a retired Apple key, because the first nonce was consumed', async () => {
    (isAttestationProtected as jest.Mock).mockReturnValueOnce(true);
    (SecurityService.hasKeys as jest.Mock).mockResolvedValueOnce(true).mockResolvedValueOnce(true);
    (SecurityService.signPayload as jest.Mock)
      .mockResolvedValueOnce('signature-1')
      .mockResolvedValueOnce('signature-2');
    (getAttestationHeaders as jest.Mock)
      .mockResolvedValueOnce({
        'X-Attestation-Provider': 'apple',
        'X-Attestation-Challenge': 'challenge-1',
        'X-Attestation-Proof': 'proof-1',
        'X-Attestation-Key-Id': 'key-1',
      })
      .mockResolvedValueOnce({
        'X-Attestation-Provider': 'apple',
        'X-Attestation-Challenge': 'challenge-2',
        'X-Attestation-Proof': 'proof-2',
        'X-Attestation-Key-Id': 'key-2',
      });
    fetchMock
      .mockResolvedValueOnce({
        ok: false,
        status: 401,
        headers: {
          get: (name: string) =>
            name.toLowerCase() === 'x-attestation-error' ? 'ATTESTATION_KEY_UNKNOWN' : null,
        },
        json: async () => ({ code: 'ATTESTATION_KEY_UNKNOWN' }),
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({}),
      });

    await apiRequest('POST', '/api/purchases', { amount: 1 });

    expect(clearAppleAttestationKey).toHaveBeenCalledTimes(1);
    expect(SecurityService.signPayload).toHaveBeenCalledTimes(2);
    expect(fetchMock).toHaveBeenCalledTimes(2);

    const [, retry] = fetchMock.mock.calls[1];
    const firstPayload = (SecurityService.signPayload as jest.Mock).mock.calls[0][0];
    const retryPayload = (SecurityService.signPayload as jest.Mock).mock.calls[1][0];
    expect(retry.headers['x-signature']).toBe('signature-2');
    expect(retry.headers['X-Attestation-Key-Id']).toBe('key-2');
    expect(retryPayload).not.toBe(firstPayload);
  });

  /**
   * The retry inside `fetchWithRetry` is a second request, not a replay of the first one.
   *
   * The signature middleware stores its replay nonce BEFORE it runs the endpoint, so an attempt
   * that came back 500 has already burned the timestamp it carried. Retrying with the same headers
   * - which is what this client used to do - was refused as a replay of itself, and on 2026-11-05
   * that turned the bank's refusal into "Request already processed" on 14 customers' screens,
   * blaming them for a request the acquirer never accepted (planning #873).
   */
  it('signs the retry of a 500 from scratch, because the failed attempt already burned its nonce', async () => {
    // Reset, so this test owns both signing calls no matter what an earlier one queued.
    (SecurityService.hasKeys as jest.Mock)
      .mockReset()
      .mockResolvedValueOnce(true)
      .mockResolvedValueOnce(true);
    (SecurityService.signPayload as jest.Mock)
      .mockReset()
      .mockResolvedValueOnce('signature-1')
      .mockResolvedValueOnce('signature-2');

    // One header object is filled in again per attempt, so the recorded call read afterwards shows
    // the LAST signature twice. This copies them as they stood when the attempt went out.
    const sent: Record<string, string>[] = [];
    fetchMock.mockImplementation(
      async (_url: string, init: { headers: Record<string, string> }) => {
        sent.push({ ...init.headers });
        return sent.length === 1
          ? { ok: false, status: 500, headers: { get: () => null }, json: async () => ({}) }
          : {
              ok: true,
              status: 200,
              headers: { get: () => null },
              json: async () => ({ orderIds: [] }),
            };
      },
    );

    const response = await apiRequest('POST', '/api/purchases', { amount: 1 });

    expect(response.status).toBe(200);
    expect(sent).toHaveLength(2);
    expect(sent[0]['x-signature']).toBe('signature-1');
    expect(sent[1]['x-signature']).toBe('signature-2');
    // The nonce is keyed by (device id, timestamp), so an identical timestamp is a replay no
    // matter what signature rides next to it.
    expect(Number(sent[1]['x-timestamp'])).toBeGreaterThan(Number(sent[0]['x-timestamp']));
  });

  it('signs the retry of a request that never arrived, too', async () => {
    // A dropped connection may still have reached the server, so the same rule applies: the next
    // attempt arrives with proof of its own rather than the first attempt's.
    (SecurityService.hasKeys as jest.Mock)
      .mockReset()
      .mockResolvedValueOnce(true)
      .mockResolvedValueOnce(true);
    (SecurityService.signPayload as jest.Mock)
      .mockReset()
      .mockResolvedValueOnce('signature-1')
      .mockResolvedValueOnce('signature-2');

    const sent: Record<string, string>[] = [];
    fetchMock
      .mockImplementationOnce(async (_url: string, init: { headers: Record<string, string> }) => {
        sent.push({ ...init.headers });
        throw new Error('network down');
      })
      .mockImplementationOnce(async (_url: string, init: { headers: Record<string, string> }) => {
        sent.push({ ...init.headers });
        return { ok: true, status: 200, headers: { get: () => null }, json: async () => ({}) };
      });

    const response = await apiRequest('POST', '/api/purchases', { amount: 1 });

    expect(response.status).toBe(200);
    expect(sent).toHaveLength(2);
    expect(sent[0]['x-signature']).toBe('signature-1');
    expect(sent[1]['x-signature']).toBe('signature-2');
  });

  it('does not refresh or clear the session when the server rejects attestation', async () => {
    (isAttestationProtected as jest.Mock).mockReturnValueOnce(true);
    (getAttestationHeaders as jest.Mock).mockResolvedValueOnce({
      'X-Attestation-Provider': 'apple',
      'X-Attestation-Challenge': 'challenge',
      'X-Attestation-Proof': 'proof',
      'X-Attestation-Key-Id': 'key',
    });
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      headers: { get: () => 'ATTESTATION_REQUIRED' },
      json: async () => ({ code: 'ATTESTATION_REQUIRED' }),
    });

    await apiRequest('POST', '/api/purchases', { amount: 1 });

    // One protected call, no refresh call: an attestation refusal is not an expired session.
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

/**
 * The session the app keeps may die only when the SERVER says so.
 *
 * A 401 on a request that carried the app's own token is an expired token, and the app renews
 * it. A 401 on a request that carried no session token - a public endpoint, or a sign-in step -
 * says nothing about the session: a wrong verification code answers 401 too, and clearing the
 * tokens there signed the customer out while their session was still alive.
 */
describe('apiClient session lifetime', () => {
  const fetchMock = jest.fn();

  const unauthorized = { ok: false, status: 401, json: async () => ({}) };
  const ok = (body: unknown = {}) => ({ ok: true, status: 200, json: async () => body });

  beforeEach(() => {
    fetchMock.mockReset();
    (global as unknown as { fetch: unknown }).fetch = fetchMock;
    (TokenStorage.getAccessToken as jest.Mock).mockReset().mockResolvedValue(null);
    (TokenStorage.getRefreshToken as jest.Mock).mockReset().mockResolvedValue(null);
    (TokenStorage.saveTokens as jest.Mock).mockReset().mockResolvedValue(undefined);
    (TokenStorage.clearTokens as jest.Mock).mockReset().mockResolvedValue(undefined);
  });

  it('renews an expired access token and retries the request with the fresh one', async () => {
    (TokenStorage.getAccessToken as jest.Mock)
      .mockResolvedValueOnce('expired-token')
      .mockResolvedValueOnce('fresh-token');
    (TokenStorage.getRefreshToken as jest.Mock).mockResolvedValue('refresh-token');
    fetchMock
      .mockResolvedValueOnce(unauthorized)
      .mockResolvedValueOnce(ok({ accessToken: 'fresh-token', refreshToken: 'rotated-token' }))
      .mockResolvedValueOnce(ok({ id: 'user-1' }));

    const response = await apiFetch('/api/auth/user/me');

    expect(response.status).toBe(200);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(String(fetchMock.mock.calls[1][0])).toContain('/api/auth/refresh');
    const [, retry] = fetchMock.mock.calls[2];
    expect(retry.headers['Authorization']).toBe('Bearer fresh-token');
    expect(TokenStorage.saveTokens).toHaveBeenCalledWith('fresh-token', 'rotated-token');
    expect(TokenStorage.clearTokens).not.toHaveBeenCalled();
  });

  it('ends the session when the request that carried it is rejected again', async () => {
    (TokenStorage.getAccessToken as jest.Mock)
      .mockResolvedValueOnce('expired-token')
      .mockResolvedValueOnce('fresh-token');
    (TokenStorage.getRefreshToken as jest.Mock).mockResolvedValue('refresh-token');
    fetchMock
      .mockResolvedValueOnce(unauthorized)
      .mockResolvedValueOnce(ok({ accessToken: 'fresh-token', refreshToken: 'rotated-token' }))
      .mockResolvedValueOnce(unauthorized);

    const response = await apiFetch('/api/auth/user/me');

    expect(response.status).toBe(401);
    expect(TokenStorage.clearTokens).toHaveBeenCalledTimes(1);
  });

  it('does not end the session because a public endpoint answered 401', async () => {
    (TokenStorage.getAccessToken as jest.Mock).mockResolvedValue('stored-token');
    (TokenStorage.getRefreshToken as jest.Mock).mockResolvedValue('refresh-token');
    fetchMock
      .mockResolvedValueOnce(unauthorized)
      .mockResolvedValueOnce(ok({ accessToken: 'fresh-token', refreshToken: 'rotated-token' }))
      .mockResolvedValueOnce(unauthorized);

    const response = await apiFetch('/api/brands');

    expect(response.status).toBe(401);
    expect(TokenStorage.clearTokens).not.toHaveBeenCalled();
  });

  it('leaves the session alone when the request carries a token of its own', async () => {
    (TokenStorage.getRefreshToken as jest.Mock).mockResolvedValue('refresh-token');
    fetchMock.mockResolvedValueOnce(unauthorized);

    const response = await apiFetch('/api/auth/device/register', {
      headers: { Authorization: 'Bearer step-token' },
    });

    expect(response.status).toBe(401);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(TokenStorage.saveTokens).not.toHaveBeenCalled();
    expect(TokenStorage.clearTokens).not.toHaveBeenCalled();
  });

  it('ends the session when the refresh token itself is rejected', async () => {
    (TokenStorage.getAccessToken as jest.Mock).mockResolvedValue('expired-token');
    (TokenStorage.getRefreshToken as jest.Mock).mockResolvedValue('dead-refresh-token');
    fetchMock.mockResolvedValueOnce(unauthorized).mockResolvedValueOnce(unauthorized);

    const response = await apiRequest('GET', '/api/auth/user/me');

    expect(response.status).toBe(401);
    expect(TokenStorage.clearTokens).toHaveBeenCalledTimes(1);
  });
});
