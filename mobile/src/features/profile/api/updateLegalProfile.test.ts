import { updateLegalProfile } from './updateLegalProfile';
import { apiFetch } from '../../../core/api/apiClient';
import { LegalEntityApiError } from '../../company/api/legalEntityApi';

jest.mock('../../../core/api/apiClient', () => ({
  apiFetch: jest.fn(),
}));

const apiFetchMock = apiFetch as jest.Mock;

function ok() {
  return { ok: true, status: 200, json: async () => ({ id: 'e1' }) } as any;
}

function fail(status: number, body: unknown = {}) {
  return {
    ok: false,
    status,
    json: async () => body,
  } as any;
}

/**
 * Regression cover for the status being thrown away. `new Error('Failed to update legal
 * profile')` carried no status, so the caller read `err.status ?? 500`, treated a 409 caused
 * by the user's own keystroke as a server fault, reported it to Sentry, and showed one
 * "Error" toast for every outcome.
 */
describe('updateLegalProfile', () => {
  beforeEach(() => {
    apiFetchMock.mockReset();
  });

  it('returns the profile on success', async () => {
    apiFetchMock.mockResolvedValue(ok());

    await expect(updateLegalProfile({ name: 'Acme' })).resolves.toEqual({ id: 'e1' });
  });

  it.each([400, 409, 500])('carries status %i on the thrown error', async (status) => {
    apiFetchMock.mockResolvedValue(fail(status));

    const error = await updateLegalProfile({ edrpou: '38528147' }).catch((e) => e);

    expect(error).toBeInstanceOf(LegalEntityApiError);
    expect(error.status).toBe(status);
  });

  it('surfaces the server sentence rather than a fixed string', async () => {
    apiFetchMock.mockResolvedValue(
      fail(409, { title: 'Conflict', status: 409, detail: 'EDRPOU already registered' }),
    );

    const error = await updateLegalProfile({ edrpou: '38528147' }).catch((e) => e);

    expect(error.message).toBe('EDRPOU already registered');
  });

  it('still throws a typed error when the body is not JSON', async () => {
    apiFetchMock.mockResolvedValue({
      ok: false,
      status: 502,
      json: async () => {
        throw new Error('not json');
      },
    } as any);

    const error = await updateLegalProfile({ name: 'Acme' }).catch((e) => e);

    // A 5xx with an unreadable body must not degrade into an untyped Error, or it goes
    // back to reading as `status ?? 500` and loses the distinction again.
    expect(error).toBeInstanceOf(LegalEntityApiError);
    expect(error.status).toBe(502);
  });
});
