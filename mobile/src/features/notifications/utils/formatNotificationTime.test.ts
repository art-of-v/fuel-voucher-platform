import { formatNotificationTime } from './formatNotificationTime';
import { formatExpirationDate } from '../../../core/utils/formatters';

/**
 * Compact relative time for a notification list: "just now", "5m ago", "3h ago",
 * "2d ago", then an absolute date once it is a week old.
 *
 * This is five buckets in a row, so every boundary is a place the function can be off
 * by one and still look right in review: 59 seconds is not "1m ago", 60 minutes is
 * "1h ago", and 7 days is where it stops counting. Each boundary is pinned on both
 * sides here, because that is where the bugs are.
 */

const t = (key: string, ...params: string[]) =>
  params.length ? key + ':' + params.join(',') : key;

/** An ISO timestamp `seconds` in the past. */
const ago = (seconds: number) => new Date(Date.now() - seconds * 1000).toISOString();

describe('formatNotificationTime - buckets', () => {
  it('calls anything under a minute "just now"', () => {
    expect(formatNotificationTime(ago(0), t)).toBe('notifications.time.justNow');
    expect(formatNotificationTime(ago(59), t)).toBe('notifications.time.justNow');
  });

  it('rolls over to minutes at exactly 60 seconds', () => {
    expect(formatNotificationTime(ago(60), t)).toBe('notifications.time.minutes:1');
    expect(formatNotificationTime(ago(5 * 60), t)).toBe('notifications.time.minutes:5');
  });

  it('holds minutes right up to the hour', () => {
    expect(formatNotificationTime(ago(59 * 60), t)).toBe('notifications.time.minutes:59');
  });

  it('rolls over to hours at exactly 60 minutes', () => {
    expect(formatNotificationTime(ago(60 * 60), t)).toBe('notifications.time.hours:1');
    expect(formatNotificationTime(ago(3 * 3600), t)).toBe('notifications.time.hours:3');
  });

  it('holds hours right up to the day', () => {
    expect(formatNotificationTime(ago(23 * 3600), t)).toBe('notifications.time.hours:23');
  });

  it('rolls over to days at exactly 24 hours', () => {
    expect(formatNotificationTime(ago(24 * 3600), t)).toBe('notifications.time.days:1');
    expect(formatNotificationTime(ago(2 * 86400), t)).toBe('notifications.time.days:2');
  });

  it('holds days right up to the week', () => {
    expect(formatNotificationTime(ago(6 * 86400), t)).toBe('notifications.time.days:6');
  });

  it('stops counting at seven days and shows the date instead', () => {
    const sevenDays = ago(7 * 86400);

    // 7 days is a week: a "7d ago" label is less useful than a date here.
    expect(formatNotificationTime(sevenDays, t)).toBe(formatExpirationDate(sevenDays));
    expect(formatNotificationTime(ago(400 * 86400), t)).not.toMatch(/days/);
  });

  it('reuses the app-wide date format, so there is only one date shape', () => {
    const old = ago(30 * 86400);
    expect(formatNotificationTime(old, t)).toBe(formatExpirationDate(old));
  });
});

describe('formatNotificationTime - bad input', () => {
  it('renders nothing for a timestamp it cannot parse', () => {
    // An empty cell beats "NaNN minutes ago" in a list.
    expect(formatNotificationTime('not-a-date', t)).toBe('');
  });

  it('does not show a negative age for a timestamp in the future', () => {
    const future = new Date(Date.now() + 5 * 60 * 1000).toISOString();

    expect(formatNotificationTime(future, t)).toBe('notifications.time.justNow');
  });
});
