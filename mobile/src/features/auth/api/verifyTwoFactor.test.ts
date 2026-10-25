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

  it('tags a refused code with its status, so the UI can say the code is wrong', async () => {
    // The API answers in the one RFC 9457 envelope (see ApiError.cs): the sentence is in
    // `detail`, not in a `message` field. The status is what the UI needs to tell a
    // refused code (401) from a server failure (5xx).
    mockApiRequest.mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => ({ title: 'Unauthorized', detail: 'Invalid or expired verification code' }),
    });

    await expect(verifyTwoFactor('challenge', '000000')).rejects.toMatchObject({
      status: 401,
    });
    await expect(verifyTwoFactor('challenge', '000000')).rejects.toThrow(
      'Invalid or expired verification code',
    );
  });

  it('keeps the status of a server failure, so it is not shown as a wrong code', async () => {
    mockApiRequest.mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({
        title: 'Internal Server Error',
        detail: 'An unexpected error occurred',
      }),
    });

    await expect(verifyTwoFactor('challenge', '000000')).rejects.toMatchObject({
      status: 500,
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
