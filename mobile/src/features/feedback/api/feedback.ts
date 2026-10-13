import { Linking } from 'react-native';

import { apiFetch } from '../../../core/api/apiClient';

/**
 * Client for the in-app feedback channel (planning `FEEDBACK_BOT.md`).
 *
 *  - GET /api/feedback/deep-link - a signed `https://t.me/<bot>?start=<payload>` link.
 *
 * The app asks the server for the URL rather than building it, and that is the whole point: the
 * payload binds this Telegram chat to this FuelFlow account, and the key that signs it cannot
 * ship inside the binary. A key in a shipped app is a public key, which would let anyone start
 * a chat as any account.
 *
 * A 404 is the feature being off — not an error. `FeedbackBot:Enabled` in app_settings is the
 * switch an admin flips to take the tab out of the app without a release, so the tab hides
 * itself on a miss rather than showing a button that opens nothing.
 */
export interface FeedbackDeepLink {
  url: string;
}

/**
 * @returns the deep link, or null when the channel is switched off.
 * @throws only on a real failure (offline, 5xx). A 404 resolves to null on purpose.
 */
export async function getFeedbackDeepLink(): Promise<FeedbackDeepLink | null> {
  const response = await apiFetch('/api/feedback/deep-link');

  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`Feedback deep link failed (${response.status})`);

  const data = await response.json();
  if (typeof data?.url !== 'string' || data.url.length === 0) return null;

  return { url: data.url };
}

/**
 * Opens the bot chat.
 *
 * @returns whether the device could actually open it. Telegram is not installed on every phone,
 * so this reports false rather than throwing: the screen shows the web-form fallback instead of
 * leaving a button that does nothing.
 */
export async function openTelegramChat(url: string): Promise<boolean> {
  try {
    await Linking.openURL(url);
    return true;
  } catch {
    return false;
  }
}
