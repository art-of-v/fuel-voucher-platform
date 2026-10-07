import { TokenStorage } from './tokenStorage';

/**
 * Both tokens live under one SecureStore key on purpose (#26). Writing them as two
 * entries was not atomic: a kill between the two writes left a NEW access token
 * beside an OLD refresh token, that stale pair tripped the backend's refresh-reuse
 * detection, and the user was logged out.
 *
 * So the rules that matter here are the ones that keep the pair together: it is read
 * and written as one value, a half-written pair is never assembled from a mix of the
 * new key and the legacy ones, and a corrupt value reads as "signed out" rather than
 * throwing on a screen nobody is looking at.
 */

// An in-memory SecureStore, so the tests exercise the real key names and the real
// migration sequence rather than a stubbed happy path.
const mockStore = new Map<string, string>();

jest.mock('expo-secure-store', () => ({
  AFTER_FIRST_UNLOCK: 'AFTER_FIRST_UNLOCK',
  getItemAsync: jest.fn(async (key: string) => mockStore.get(key) ?? null),
  setItemAsync: jest.fn(async (key: string, value: string) => {
    mockStore.set(key, value);
  }),
  deleteItemAsync: jest.fn(async (key: string) => {
    mockStore.delete(key);
  }),
}));

const TOKENS_KEY = 'auth_tokens';
const LEGACY_ACCESS = 'auth_access_token';
const LEGACY_REFRESH = 'auth_refresh_token';

beforeEach(() => mockStore.clear());

describe('saving and reading the pair', () => {
  it('round-trips both tokens', async () => {
    await TokenStorage.saveTokens('access-1', 'refresh-1');

    expect(await TokenStorage.getAccessToken()).toBe('access-1');
    expect(await TokenStorage.getRefreshToken()).toBe('refresh-1');
  });

  it('writes the pair as one value, never as two keys', async () => {
    await TokenStorage.saveTokens('access-1', 'refresh-1');

    // Two keys is the exact failure #26 describes.
    expect(mockStore.get(TOKENS_KEY)).toBe(
      JSON.stringify({ accessToken: 'access-1', refreshToken: 'refresh-1' }),
    );
    expect(mockStore.has(LEGACY_ACCESS)).toBe(false);
    expect(mockStore.has(LEGACY_REFRESH)).toBe(false);
  });

  it('reports signed out when nothing is stored', async () => {
    expect(await TokenStorage.getAccessToken()).toBeNull();
    expect(await TokenStorage.getRefreshToken()).toBeNull();
  });

  it('rejects an empty or non-string token instead of storing a broken pair', async () => {
    // Storing one half of a pair is what #26 was about, so a half-pair must not be
    // savable in the first place.
    await expect(TokenStorage.saveTokens('', 'refresh-1')).rejects.toThrow(/accessToken/);
    await expect(TokenStorage.saveTokens('access-1', '')).rejects.toThrow(/refreshToken/);
    await expect(TokenStorage.saveTokens(undefined as never, 'refresh-1')).rejects.toThrow(
      /accessToken/,
    );
    expect(mockStore.size).toBe(0);
  });
});

describe('a stored value that is not a usable pair', () => {
  it('reads a corrupt value as signed out rather than crashing', async () => {
    mockStore.set(TOKENS_KEY, '{not json');

    // Throwing here would take down whatever screen happened to mount first.
    expect(await TokenStorage.getAccessToken()).toBeNull();
  });

  it('reads a value missing one half as signed out', async () => {
    mockStore.set(TOKENS_KEY, JSON.stringify({ accessToken: 'access-1' }));

    // Reading the refresh token as undefined would replay an empty one against the API.
    expect(await TokenStorage.getAccessToken()).toBeNull();
    expect(await TokenStorage.getRefreshToken()).toBeNull();
  });

  it('reads empty strings as signed out', async () => {
    mockStore.set(TOKENS_KEY, JSON.stringify({ accessToken: '', refreshToken: 'refresh-1' }));

    expect(await TokenStorage.getAccessToken()).toBeNull();
  });
});

describe('migration from the pre-#26 split keys', () => {
  it('folds a legacy pair into the single key and drops the originals', async () => {
    mockStore.set(LEGACY_ACCESS, 'old-access');
    mockStore.set(LEGACY_REFRESH, 'old-refresh');

    expect(await TokenStorage.getAccessToken()).toBe('old-access');
    expect(await TokenStorage.getRefreshToken()).toBe('old-refresh');
    // Otherwise the next read could assemble a pair out of two different eras.
    expect(mockStore.has(LEGACY_ACCESS)).toBe(false);
    expect(mockStore.has(LEGACY_REFRESH)).toBe(false);
    expect(mockStore.get(TOKENS_KEY)).toBe(
      JSON.stringify({ accessToken: 'old-access', refreshToken: 'old-refresh' }),
    );
  });

  it('prefers the current key over a legacy pair left behind', async () => {
    mockStore.set(
      TOKENS_KEY,
      JSON.stringify({ accessToken: 'new-access', refreshToken: 'new-refresh' }),
    );
    mockStore.set(LEGACY_ACCESS, 'old-access');
    mockStore.set(LEGACY_REFRESH, 'old-refresh');

    // Mixing a new access token with an old refresh token is precisely what trips
    // the backend's reuse detection.
    expect(await TokenStorage.getAccessToken()).toBe('new-access');
    expect(await TokenStorage.getRefreshToken()).toBe('new-refresh');
  });

  it('ignores a half-present legacy pair', async () => {
    mockStore.set(LEGACY_ACCESS, 'old-access');

    // One half is not a session. Treating it as one would replay an empty refresh.
    expect(await TokenStorage.getAccessToken()).toBeNull();
    expect(mockStore.has(TOKENS_KEY)).toBe(false);
  });
});

describe('clearing', () => {
  it('removes the pair', async () => {
    await TokenStorage.saveTokens('access-1', 'refresh-1');

    await TokenStorage.clearTokens();

    expect(await TokenStorage.getAccessToken()).toBeNull();
  });

  it('removes a legacy pair too, so it cannot resurface', async () => {
    mockStore.set(LEGACY_ACCESS, 'old-access');
    mockStore.set(LEGACY_REFRESH, 'old-refresh');

    await TokenStorage.clearTokens();

    expect(mockStore.has(LEGACY_ACCESS)).toBe(false);
    expect(mockStore.has(LEGACY_REFRESH)).toBe(false);
  });
});
