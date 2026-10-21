import { apiRequest } from './apiClient';

jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { extra: { apiUrl: 'https://api.test' } } },
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
});
