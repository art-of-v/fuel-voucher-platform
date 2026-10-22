import {
  parseVersion,
  isVersionBelow,
  getCurrentAppVersion,
  fetchAppVersion,
} from './versionCheck';

// `versionCheck` pulls in `BASE_URL` from the config module and asks the pinning module to
// initialize before its direct fetch. Neither is under test here.
jest.mock('../api/apiConfig', () => ({ BASE_URL: 'https://test.local' }));
jest.mock('../security/sslPinning', () => ({
  ensureSslPinningInitialized: jest.fn().mockResolvedValue(undefined),
}));

// Read lazily via a getter: Jest hoists `jest.mock` above this file's statements, so a plain
// `const` captured in the factory would be read before it is assigned. Deferring the read to
// `getCurrentAppVersion()` call time lets each test set the value it needs.
jest.mock('expo-constants', () => ({
  __esModule: true,
  default: {
    get expoConfig() {
      return (globalThis as any).__EXPO_CONFIG__;
    },
  },
}));

beforeEach(() => {
  (globalThis as any).__EXPO_CONFIG__ = { version: '1.1.0' };
});

afterEach(() => {
  delete (globalThis as any).__EXPO_CONFIG__;
});

describe('isVersionBelow', () => {
  it('says a build at the floor is not below it', () => {
    expect(isVersionBelow('1.0.1', '1.0.1')).toBe(false);
  });

  it('says a newer build is not below the floor', () => {
    expect(isVersionBelow('1.1.0', '1.0.1')).toBe(false);
    expect(isVersionBelow('2.0.0', '1.9.9')).toBe(false);
  });

  it('says an older build is below the floor', () => {
    expect(isVersionBelow('1.0.0', '1.0.1')).toBe(true);
    expect(isVersionBelow('1.0.9', '1.1.0')).toBe(true);
    expect(isVersionBelow('0.9.9', '1.0.0')).toBe(true);
  });

  /**
   * The regression this change is about.
   *
   * `Constants.expoConfig` is not reliably populated in a standalone App Store build, so
   * `getCurrentAppVersion` can genuinely return nothing. That used to become the literal '1.0.0' —
   * *below* the floor the API serves — so the app declared itself outdated and showed a full-screen
   * update wall to testers who were on 1.1.0, a build that was never out of date. The wall then
   * offered a store build that would fail the same check on arrival.
   *
   * An unread version must never be the one that blocks. Blocking on a value that was never read is
   * not caution, it is refusing to serve.
   */
  it('does NOT block when the build could not read its own version', () => {
    expect(isVersionBelow(null, '1.0.1')).toBe(false);
    expect(isVersionBelow(null, '9.9.9')).toBe(false);
  });

  it('compares numerically, not as text', () => {
    // String comparison gets these backwards: '1.10.0' < '1.9.0', because '1' < '9'.
    expect(isVersionBelow('1.10.0', '1.9.0')).toBe(false);
    expect(isVersionBelow('1.9.0', '1.10.0')).toBe(true);
  });

  it('treats a missing component as zero rather than crashing', () => {
    expect(isVersionBelow('1.1', '1.1.0')).toBe(false);
    expect(isVersionBelow('1', '1.0.1')).toBe(true);
  });
});

describe('getCurrentAppVersion', () => {
  it('reports the version the build declares', () => {
    expect(getCurrentAppVersion()).toBe('1.1.0');
  });

  /**
   * The other half of the bug: an unreadable version must surface as null, never as a guessed
   * literal. Any guess we invent is a version we did not verify, and a guess below the floor
   * (which '1.0.0' was, against a '1.0.1' floor) walls off a current build.
   */
  it('returns null when expoConfig is absent', () => {
    (globalThis as any).__EXPO_CONFIG__ = null;
    expect(getCurrentAppVersion()).toBeNull();
  });

  it('returns null when expoConfig carries no version', () => {
    (globalThis as any).__EXPO_CONFIG__ = { extra: {} };
    expect(getCurrentAppVersion()).toBeNull();
  });
});

describe('fetchAppVersion', () => {
  const realFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = realFetch;
  });

  it('returns the served floor', async () => {
    globalThis.fetch = jest.fn(async () => ({
      ok: true,
      json: async () => ({
        minimumVersion: '1.0.1',
        iosStoreUrl: 'ios',
        androidStoreUrl: 'android',
      }),
    })) as unknown as typeof fetch;
    const info = await fetchAppVersion();
    expect(info?.minimumVersion).toBe('1.0.1');
  });

  /**
   * A failed fetch yields null, and `_layout` only walls the user when a floor was actually read.
   * So the outage path already fails open - which is exactly why the version floor could not be
   * the reason testers were walled off, and why the unreadable-version fallback was the culprit.
   */
  it('returns null on a non-ok response so the caller does not wall the user', async () => {
    globalThis.fetch = jest.fn(async () => ({
      ok: false,
      json: async () => ({}),
    })) as unknown as typeof fetch;
    expect(await fetchAppVersion()).toBeNull();
  });

  it('returns null when the network throws', async () => {
    globalThis.fetch = jest.fn(async () => {
      throw new Error('offline');
    }) as unknown as typeof fetch;
    expect(await fetchAppVersion()).toBeNull();
  });
});

describe('parseVersion', () => {
  it('splits and coerces', () => {
    expect(parseVersion('1.2.3')).toEqual([1, 2, 3]);
    expect(parseVersion('1.1.0')).toEqual([1, 1, 0]);
  });
});
