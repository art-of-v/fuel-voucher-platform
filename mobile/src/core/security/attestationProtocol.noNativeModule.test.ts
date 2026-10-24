import { computeProofRequestHash, computeRequestHash } from './attestationProtocol';

jest.mock('expo-crypto', () => {
  throw new Error("Cannot find native module 'ExpoCrypto'");
});

/**
 * The bundle is imported above and the mock above throws when it is resolved: if this file could
 * be loaded at all, the crypto module is not imported at module scope. That is the property an OTA
 * bundle needs to run on a binary built before `expo-crypto` existed.
 */
describe('attestationProtocol without the crypto native module', () => {
  it('has no hash to bind a proof to, and says so', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

    await expect(computeRequestHash('POST', '/api/auth/verify', '{}')).resolves.toBeNull();
    await expect(computeProofRequestHash('challenge', 'request-hash')).resolves.toBeNull();
    expect(warn).toHaveBeenCalled();

    warn.mockRestore();
  });
});
