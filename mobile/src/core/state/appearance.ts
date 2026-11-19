import { BASE_URL } from '../api/apiConfig';
import { themes, ThemeType } from '../design/themes';
import { ensureSslPinningInitialized } from '../security/sslPinning';
import { useStore } from './appStore';

/**
 * The installation's default theme, as the server has it.
 *
 * <p>
 * A manager sets it once in the admin panel (Settings → App appearance) and every phone that has
 * not chosen a theme of its own opens in it. The app asks for it at launch, because the alternative
 * — assume obsidian, then swap — is a visible flash of the wrong palette on the first screen a
 * worker sees.
 * </p>
 *
 * <p>
 * It is a plain `fetch`, NOT `apiFetch`, and that is deliberate. This call has nothing to do with
 * the session: the endpoint is anonymous, the splash needs it before anyone signs in, and a 401
 * from anything on the way (a proxy, a stale bearer) must not be read as a dead session —
 * `apiFetch` treats a 401 as one and clears the tokens, which would turn a colour into a logout.
 * It follows `fetchAppVersion`, the other launch-time read that bypasses the session client: pin
 * the certificate chain first, then send one request.
 * </p>
 *
 * <p>
 * Best-effort by design: this is a colour. An unreachable server, a timeout, a proxy returning
 * HTML, or an id this build has never heard of all leave the app on the theme it already had
 * rather than delaying the first screen or painting nothing. Nothing about sign-in, wallets or
 * vouchers depends on it.
 * </p>
 */

interface AppearanceResponse {
  defaultTheme?: unknown;
}

/**
 * Whether a value from the server is a theme this build can actually paint.
 *
 * <p>
 * The server validates against the same catalogue (see `AppThemes` in the backend), so a mismatch
 * means a phone running an older build than the theme the admin picked. Ignoring it is the only
 * safe answer: an id this build does not have would resolve to `undefined` colours.
 * </p>
 */
export function isKnownTheme(value: unknown): value is ThemeType {
  return typeof value === 'string' && Object.prototype.hasOwnProperty.call(themes, value);
}

/**
 * Reads the installation's default theme and applies it if the worker has not chosen one.
 *
 * @returns the theme that was applied, or null when nothing changed. Returned for the tests and
 * for a caller that wants to log it; the store is the authority either way.
 */
export async function syncAppearanceTheme(): Promise<ThemeType | null> {
  try {
    await ensureSslPinningInitialized(BASE_URL);

    const response = await fetch(`${BASE_URL}/api/app/appearance`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (!response.ok) {
      return null;
    }

    const body = (await response.json()) as AppearanceResponse;
    if (!isKnownTheme(body?.defaultTheme)) {
      return null;
    }

    const { theme, themePinned, applyServerTheme } = useStore.getState();
    if (themePinned) {
      return null;
    }

    applyServerTheme(body.defaultTheme);

    return theme === body.defaultTheme ? null : body.defaultTheme;
  } catch {
    // Offline, DNS, a captive portal: the app keeps the theme it already had.
    return null;
  }
}
