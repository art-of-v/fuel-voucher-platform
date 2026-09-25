import * as SecureStore from 'expo-secure-store';

// Single key holding both tokens as one JSON value. Writing the access and refresh tokens as
// two separate SecureStore entries was not atomic: if the app was killed or suspended between
// the two writes, storage was left with a NEW access token beside the OLD refresh token. That
// stale refresh token is later replayed against /api/auth/refresh, trips the backend's reuse
// detection, and logs the user out (#26). One key means the pair is always written and read
// together, so a partial write can never split them.
const TOKENS_KEY = 'auth_tokens';

// Pre-#26 keys. Read once for migration so an already-signed-in user is not logged out by the
// upgrade, then cleared as soon as the pair is rewritten under TOKENS_KEY.
const LEGACY_ACCESS_TOKEN_KEY = 'auth_access_token';
const LEGACY_REFRESH_TOKEN_KEY = 'auth_refresh_token';

// AFTER_FIRST_UNLOCK: readable and writable once the device has been unlocked at least once
// since boot, including while it is later locked. The default (WHEN_UNLOCKED) fails a write
// that happens while the device is locked and leaves the pair stale. Tokens stay encrypted at
// rest and are unreadable before the first unlock after a reboot.
const OPTIONS = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK };

interface StoredTokens {
  accessToken: string;
  refreshToken: string;
}

async function readTokens(): Promise<StoredTokens | null> {
  const raw = await SecureStore.getItemAsync(TOKENS_KEY, OPTIONS);
  if (raw) {
    try {
      const parsed = JSON.parse(raw) as Partial<StoredTokens>;
      if (parsed?.accessToken && parsed?.refreshToken) {
        return { accessToken: parsed.accessToken, refreshToken: parsed.refreshToken };
      }
    } catch {
      // Corrupt value - treat as no tokens rather than crash; the user re-authenticates.
    }
    return null;
  }

  // Migrate a pre-#26 split pair, if one is present, into the single atomic key.
  const [accessToken, refreshToken] = await Promise.all([
    SecureStore.getItemAsync(LEGACY_ACCESS_TOKEN_KEY),
    SecureStore.getItemAsync(LEGACY_REFRESH_TOKEN_KEY),
  ]);
  if (accessToken && refreshToken) {
    await SecureStore.setItemAsync(TOKENS_KEY, JSON.stringify({ accessToken, refreshToken }), OPTIONS);
    await Promise.all([
      SecureStore.deleteItemAsync(LEGACY_ACCESS_TOKEN_KEY),
      SecureStore.deleteItemAsync(LEGACY_REFRESH_TOKEN_KEY),
    ]);
    return { accessToken, refreshToken };
  }
  return null;
}

export const TokenStorage = {
  async saveTokens(accessToken: string, refreshToken: string) {
    if (!accessToken || typeof accessToken !== 'string') {
      throw new Error('Invalid accessToken: requires a non-empty string');
    }
    if (!refreshToken || typeof refreshToken !== 'string') {
      throw new Error('Invalid refreshToken: requires a non-empty string');
    }
    await SecureStore.setItemAsync(TOKENS_KEY, JSON.stringify({ accessToken, refreshToken }), OPTIONS);
  },

  async getAccessToken(): Promise<string | null> {
    return (await readTokens())?.accessToken ?? null;
  },

  async getRefreshToken(): Promise<string | null> {
    return (await readTokens())?.refreshToken ?? null;
  },

  async clearTokens() {
    await SecureStore.deleteItemAsync(TOKENS_KEY, OPTIONS);
    // Also clear any pre-#26 keys so a stale legacy pair can never resurface.
    await Promise.all([
      SecureStore.deleteItemAsync(LEGACY_ACCESS_TOKEN_KEY),
      SecureStore.deleteItemAsync(LEGACY_REFRESH_TOKEN_KEY),
    ]);
  },
};
