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
      json: async () => ({ title: 'Bad Request', status: 400, detail: 'Phone number is required' }),
    });

    await expect(sendVerificationCode('')).rejects.toThrow('Phone number is required');
    await expect(sendVerificationCode('')).rejects.not.toMatchObject({ status: 429 });
  });

  it('passes on the lifetime the server published for the code', async () => {
    mockApiRequest.mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ success: true, expiresInSeconds: 300 }),
    });

    await expect(sendVerificationCode('+380991110001')).resolves.toEqual({ expiresInSeconds: 300 });
  });

  it('resolves without a lifetime when the answer does not publish one', async () => {
    // The code has been sent either way; a body that cannot be read (or predates the
    // deadline) must not turn a successful send into a failure.
    mockApiRequest.mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });

    await expect(sendVerificationCode('+380991110001')).resolves.toEqual({});
  });

  it('ignores a lifetime that is not a positive number', async () => {
    mockApiRequest.mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ expiresInSeconds: -1 }),
    });

    await expect(sendVerificationCode('+380991110001')).resolves.toEqual({});
  });
});
