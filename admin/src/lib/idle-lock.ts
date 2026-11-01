/**
 * The panel locks itself after a stretch with no human input.
 *
 * <p>
 * The access token lives fifteen minutes and the SPA refreshes it on demand, and the refresh
 * cookie slides for fourteen days. That is right for a phone in a pocket and wrong for a dashboard
 * on a desk: a tab left open on an unattended laptop stays a live staff session for as long as the
 * browser is open, and the person who walks past it gets the whole panel. Nothing measured time
 * since the last action until this file existed.
 * </p>
 *
 * <p>
 * The lock does not merely hide the screen: the caller signs the session out, which revokes the
 * refresh token server-side. A reload therefore cannot resurrect it, and neither can a second tab
 * - it shares the same cookie.
 * </p>
 */

/** Minutes without input before the panel locks. Fifteen is also the access token's lifetime. */
export const IDLE_LOCK_MINUTES = 15;
export const IDLE_LOCK_MS = IDLE_LOCK_MINUTES * 60_000;

/**
 * How often the elapsed time is compared against the limit. A timer per input event would be
 * reset just as often and would keep waking the page in an idle tab; one slow poll costs nothing
 * and is exact to within half a minute, which is well inside the margin.
 */
const CHECK_INTERVAL_MS = 30_000;

/**
 * Input the operator can produce without touching the page's own controls. `mousemove` is left out
 * on purpose: a nudged desk or a cursor drifting over the window is not a person using the panel.
 */
export const ACTIVITY_EVENTS = [
  "pointerdown",
  "keydown",
  "wheel",
  "touchstart",
] as const;

export interface IdleLockOptions {
  /** What to do once the limit is passed. Called exactly once. */
  onLock: () => void;
  /** Silence, in milliseconds, after which the panel locks. */
  timeoutMs?: number;
  /** The clock, injectable so the test does not wait fifteen minutes. */
  now?: () => number;
  /** Where activity is listened for. Defaults to the window. */
  target?: EventTarget;
}

export interface IdleLock {
  /** Compare the elapsed time now, without waiting for the poll. */
  check: () => void;
  /** Remove every listener and timer. Safe to call more than once. */
  stop: () => void;
}

export function startIdleLock({
  onLock,
  timeoutMs = IDLE_LOCK_MS,
  now = () => Date.now(),
  target = typeof window === "undefined" ? undefined : window,
}: IdleLockOptions): IdleLock {
  let lastActivity = now();
  let locked = false;
  let stopped = false;
  let interval: ReturnType<typeof setInterval> | undefined;

  function stop() {
    stopped = true;
    if (interval !== undefined) {
      clearInterval(interval);
      interval = undefined;
    }
    if (!target) return;
    for (const event of ACTIVITY_EVENTS) {
      target.removeEventListener(event, touch);
    }
    target.removeEventListener("visibilitychange", check);
  }

  function touch() {
    lastActivity = now();
  }

  function check() {
    if (locked || stopped) return;
    if (now() - lastActivity < timeoutMs) return;
    locked = true;
    stop();
    onLock();
  }

  if (target) {
    for (const event of ACTIVITY_EVENTS) {
      target.addEventListener(event, touch, { passive: true });
    }
    // Returning to a tab that sat in the background is the common case: the poll may have been
    // throttled while it was hidden, so make the verdict as soon as it is visible again.
    target.addEventListener("visibilitychange", check);
  }
  interval = setInterval(check, CHECK_INTERVAL_MS);

  return { check, stop };
}
