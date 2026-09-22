import { apiRequest } from '../../../core/api/apiClient';
import { sendVerificationCode } from './sendCode';

jest.mock('../../../core/api/apiClient', () => ({
  apiRequest: jest.fn(),
}));

const mockApiRequest = apiRequest as jest.Mock;

describe('sendVerificationCode', () => {
  it('tags a 429 with status so the UI can show a rate-limit message', async () => {
    mockApiRequest.mockResolvedValue({
      ok: false,
      status: 429,
      json: async () => ({}),
    });

    await expect(sendVerificationCode('+380991110001')).rejects.toMatchObject({
      status: 429,
    });
  });

  it('throws a plain error (no 429 status) for other failures', async () => {
    mockApiRequest.mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({ message: 'Phone number is required' }),
    });

    await expect(sendVerificationCode('')).rejects.toThrow('Phone number is required');
    await expect(sendVerificationCode('')).rejects.not.toMatchObject({ status: 429 });
  });

  it('resolves on success', async () => {
    mockApiRequest.mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });

    await expect(sendVerificationCode('+380991110001')).resolves.toBeUndefined();
  });
});
