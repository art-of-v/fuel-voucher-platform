import { apiRequest } from '../../../core/api/apiClient';
import { verifyTwoFactor } from './verifyTwoFactor';

jest.mock('../../../core/api/apiClient', () => ({
  apiRequest: jest.fn(),
}));

const mockApiRequest = apiRequest as jest.Mock;

describe('verifyTwoFactor', () => {
  it('tags a 429 with status so the UI can show a rate-limit message', async () => {
    mockApiRequest.mockResolvedValue({
      ok: false,
      status: 429,
      json: async () => ({}),
    });

    await expect(verifyTwoFactor('challenge', '123456')).rejects.toMatchObject({
      status: 429,
    });
  });

  it('throws a plain error (no 429 status) for other failures', async () => {
    mockApiRequest.mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => ({ message: 'Invalid or expired verification code' }),
    });

    await expect(verifyTwoFactor('challenge', '000000')).rejects.toThrow(
      'Invalid or expired verification code',
    );
    await expect(verifyTwoFactor('challenge', '000000')).rejects.not.toMatchObject({
      status: 429,
    });
  });

  it('returns the session the challenge was exchanged for', async () => {
    const session = {
      accessToken: 'access',
      refreshToken: 'refresh',
      deviceRegistrationNonce: 'nonce',
    };
    mockApiRequest.mockResolvedValue({ ok: true, status: 200, json: async () => session });

    await expect(verifyTwoFactor('challenge', '123456')).resolves.toEqual(session);
  });
});
