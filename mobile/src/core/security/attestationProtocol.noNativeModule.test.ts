import { computeProofRequestHash, computeRequestHash } from './attestationProtocol';

// Counted inside the mock factory: reaching this `require` is what Metro turns into a fatal
// error on a binary without `ExpoCrypto`, so the loader must not get here at all.
const mockCryptoRequires = { count: 0 };

jest.mock('expo-crypto', () => {
  mockCryptoRequires.count += 1;
  throw new Error("Cannot find native module 'ExpoCrypto'");
});

/**
 * The bundle is imported above and the mock above throws when it is resolved: if this file could
 * be loaded at all, the crypto module is not imported at module scope. That is the property an OTA
 * bundle needs to run on a binary built before `expo-crypto` existed - and the registry probe is
 * what keeps the require itself from happening, since Metro reports a throw from it as fatal and
 * no `try/catch` around it can see the error.
 */
describe('attestationProtocol without the crypto native module', () => {
  beforeEach(() => {
    mockCryptoRequires.count = 0;
  });

  it('does not even require the package, and has no hash to bind a proof to', async () => {
    await expect(computeRequestHash('POST', '/api/auth/verify', '{}')).resolves.toBeNull();
    await expect(computeProofRequestHash('challenge', 'request-hash')).resolves.toBeNull();
    expect(mockCryptoRequires.count).toBe(0);
    expect(console.warn).toHaveBeenCalled();
  });
});
