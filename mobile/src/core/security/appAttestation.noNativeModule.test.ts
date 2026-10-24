import { getAttestationHeaders } from './appAttestation';

jest.mock('expo-crypto', () => {
  const { createHash: create } = jest.requireActual<typeof import('crypto')>('crypto');
  return {
    CryptoDigestAlgorithm: { SHA256: 'SHA-256' },
    CryptoEncoding: { BASE64: 'base64' },
    digestStringAsync: async (_algorithm: string, data: string) =>
      create('sha256').update(data).digest('base64'),
  };
});

jest.mock('@expo/app-integrity', () => {
  throw new Error("Cannot find native module 'ExpoAppIntegrity'");
});

jest.mock('../api/securityService', () => ({
  SecurityService: { getDeviceId: async () => 'device-id' },
}));

/**
 * A binary built before `@expo/app-integrity` existed cannot resolve it, and the import throws
 * while the bundle is being evaluated. The attestation has to degrade to "no proof" instead of
 * taking the app down - the server decides in report-only mode whether that is fatal.
 */
describe('appAttestation without the app-integrity native module', () => {
  it('sends nothing instead of crashing the app', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
    const fetchMock = jest.fn();
    global.fetch = fetchMock as unknown as typeof fetch;

    await expect(getAttestationHeaders('POST', '/api/auth/verify', '{}')).resolves.toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();

    warn.mockRestore();
  });
});
