import { createHash } from 'crypto';
import { stubExpoNativeModules } from '../../test-utils/nativeModules';
import {
  computeProofRequestHash,
  computeRequestHash,
  isAttestationProtected,
  normalizeAttestationPath,
} from './attestationProtocol';

jest.mock('expo-crypto', () => {
  const { createHash: create } = jest.requireActual<typeof import('crypto')>('crypto');
  return {
    CryptoDigestAlgorithm: { SHA256: 'SHA-256' },
    CryptoEncoding: { BASE64: 'base64' },
    digestStringAsync: async (_algorithm: string, data: string) =>
      create('sha256').update(data).digest('base64'),
  };
});

function base64Url(value: string): string {
  return createHash('sha256')
    .update(value)
    .digest('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

describe('attestationProtocol', () => {
  let restoreRuntime: () => void;

  beforeEach(() => {
    // The binary this suite pretends to run on has the crypto module; without the registry entry
    // the loader would (correctly) skip the require and every hash would be null.
    restoreRuntime = stubExpoNativeModules({ ExpoCrypto: {} });
  });

  afterEach(() => {
    restoreRuntime();
  });

  it('hashes method, exact path and body with newlines', async () => {
    await expect(computeRequestHash('POST', '/api/purchases', '{"amount":1}')).resolves.toBe(
      base64Url('POST\n/api/purchases\n{"amount":1}'),
    );
  });

  it('uppercases the method before hashing', async () => {
    await expect(computeRequestHash('post', '/api/purchases', '')).resolves.toBe(
      base64Url('POST\n/api/purchases\n'),
    );
  });

  it('binds the proof hash to the challenge and the request hash', async () => {
    await expect(computeProofRequestHash('challenge', 'request-hash')).resolves.toBe(
      base64Url('challenge:request-hash'),
    );
  });

  it('normalizes query strings and trailing slashes for matching', () => {
    expect(normalizeAttestationPath('/api/purchases/?x=1')).toBe('/api/purchases');
    expect(normalizeAttestationPath('/api/purchases/')).toBe('/api/purchases');
    expect(normalizeAttestationPath('/')).toBe('/');
  });

  it('protects the auth, money and voucher-action endpoints', () => {
    expect(isAttestationProtected('/api/auth/send-code')).toBe(true);
    expect(isAttestationProtected('/api/auth/verify?x=1')).toBe(true);
    expect(isAttestationProtected('/api/purchases')).toBe(true);
    expect(isAttestationProtected('/api/purchases/')).toBe(true);
    expect(isAttestationProtected('/api/purchases/bulk')).toBe(true);
    expect(isAttestationProtected('/api/purchases/renew')).toBe(true);
    expect(isAttestationProtected('/api/vouchers/abc/mark-used')).toBe(true);
    expect(isAttestationProtected('/api/vouchers/ABC/restore')).toBe(true);
  });

  it('does not protect the read-only previews or unrelated paths', () => {
    expect(isAttestationProtected('/api/purchases/renew/quote')).toBe(false);
    expect(isAttestationProtected('/api/purchases/renew/config')).toBe(false);
    expect(isAttestationProtected('/api/vouchers/abc/mark-used/extra')).toBe(false);
    expect(isAttestationProtected('/api/vouchers/abc/other')).toBe(false);
    expect(isAttestationProtected('/api/brands')).toBe(false);
    expect(isAttestationProtected('/api/devices')).toBe(false);
  });
});
