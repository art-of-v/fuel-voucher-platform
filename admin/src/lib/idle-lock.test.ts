import { afterEach, describe, expect, it, vi } from "vitest";
import { ACTIVITY_EVENTS, IDLE_LOCK_MS, startIdleLock } from "./idle-lock";

/**
 * The idle lock is the client half of F-15. The server half is the refresh token's absolute
 * ceiling; this one decides when the browser stops being a live staff session at all.
 */
describe("startIdleLock", () => {
  const start = Date.parse("2026-11-01T10:00:00Z");
  let clock = start;

  const setup = (timeoutMs = IDLE_LOCK_MS) => {
    clock = start;
    const target = new EventTarget();
    const onLock = vi.fn();
    const lock = startIdleLock({
      onLock,
      timeoutMs,
      now: () => clock,
      target,
    });
    return { target, onLock, lock };
  };

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("locks after the limit and says so exactly once", () => {
    const { onLock, lock } = setup();

    clock = start + IDLE_LOCK_MS - 1;
    lock.check();
    expect(onLock).not.toHaveBeenCalled();

    clock = start + IDLE_LOCK_MS;
    lock.check();
    lock.check();
    expect(onLock).toHaveBeenCalledTimes(1);
  });

  it("counts real silence, not time since the page opened", () => {
    const { target, onLock, lock } = setup();

    clock = start + IDLE_LOCK_MS - 1_000;
    target.dispatchEvent(new Event("keydown"));
    clock += 30_000;
    lock.check();

    expect(onLock).not.toHaveBeenCalled();
  });

  it("treats a click, a wheel and a touch as activity", () => {
    for (const event of ["pointerdown", "wheel", "touchstart"]) {
      const { target, onLock, lock } = setup();
      clock = start + IDLE_LOCK_MS - 1_000;
      target.dispatchEvent(new Event(event));
      clock += 30_000;
      lock.check();
      expect(onLock, event).not.toHaveBeenCalled();
    }
  });

  it("does not treat a drifting cursor as activity", () => {
    const { target, onLock, lock } = setup();

    clock = start + IDLE_LOCK_MS - 1_000;
    target.dispatchEvent(new Event("mousemove"));
    clock += 30_000;
    lock.check();

    expect(onLock).toHaveBeenCalledTimes(1);
    expect(ACTIVITY_EVENTS).not.toContain("mousemove");
  });

  it("re-checks when the tab becomes visible again", () => {
    // A background tab's timers are throttled, so the poll alone can miss the limit by minutes.
    const { target, onLock } = setup();

    clock = start + IDLE_LOCK_MS + 60_000;
    target.dispatchEvent(new Event("visibilitychange"));

    expect(onLock).toHaveBeenCalledTimes(1);
  });

  it("stops listening once stopped", () => {
    const { target, onLock, lock } = setup();

    lock.stop();
    clock = start + IDLE_LOCK_MS * 2;
    target.dispatchEvent(new Event("keydown"));
    lock.check();

    expect(onLock).not.toHaveBeenCalled();
  });
});
