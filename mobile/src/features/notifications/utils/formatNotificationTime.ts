import { formatExpirationDate } from '../../../core/utils/formatters';

/** The i18n `t` — key plus positional `{0}` string params. */
type Translate = (key: string, ...params: string[]) => string;

/**
 * Compact relative time for a notification's timestamp: "just now", "5m ago",
 * "3h ago", "2d ago", then an absolute `DD.MM.YYYY` date once it is a week or
 * more old. It is feature-local rather than a shared formatter because this
 * bucketing is a notifications-list presentation choice, not a general date
 * utility — the app has exactly one shared date format (`formatExpirationDate`),
 * which this reuses for the fallback so there is still only one date shape.
 */
export function formatNotificationTime(iso: string, t: Translate): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '';

  const diffSec = Math.max(0, Math.floor((Date.now() - then) / 1000));
  if (diffSec < 60) return t('notifications.time.justNow');

  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return t('notifications.time.minutes', String(diffMin));

  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return t('notifications.time.hours', String(diffHr));

  const diffDay = Math.floor(diffHr / 24);
  if (diffDay < 7) return t('notifications.time.days', String(diffDay));

  return formatExpirationDate(iso);
}
