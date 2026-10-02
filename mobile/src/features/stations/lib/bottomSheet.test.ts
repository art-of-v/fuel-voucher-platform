import { nearestSnapOffset, resolveSheetRelease } from './bottomSheet';

// Offsets ascending: 0 = full (open to the top), 300 = half, 520 = peek (resting, lowest).
const OFFSETS = [0, 300, 520];

describe('nearestSnapOffset', () => {
  it('snaps to the closest offset when the release is slow', () => {
    expect(nearestSnapOffset(40, 0, OFFSETS)).toBe(0);
    expect(nearestSnapOffset(280, 0, OFFSETS)).toBe(300);
    expect(nearestSnapOffset(500, 0, OFFSETS)).toBe(520);
  });

  it('carries past the midpoint when flicked hard', () => {
    // Sitting at full (0) but flung down fast → projection lands near half.
    expect(nearestSnapOffset(40, 2000, OFFSETS)).toBe(300);
    // Sitting at peek (520) but flung up fast → projection lands near half.
    expect(nearestSnapOffset(500, -2000, OFFSETS)).toBe(300);
  });

  it('ignores velocity below the projection threshold', () => {
    expect(nearestSnapOffset(40, 300, OFFSETS)).toBe(0); // 40 + 45 = 85, still nearest 0
  });
});

describe('resolveSheetRelease', () => {
  it('settles on the nearest snap for an ordinary release', () => {
    expect(resolveSheetRelease(280, 0, OFFSETS)).toEqual({ offset: 300, dismiss: false });
  });

  it('dismisses when dragged well past peek', () => {
    expect(resolveSheetRelease(600, 0, OFFSETS)).toEqual({ offset: 520, dismiss: true });
  });

  it('dismisses on a hard downward fling from peek', () => {
    expect(resolveSheetRelease(515, 1200, OFFSETS)).toEqual({ offset: 520, dismiss: true });
  });

  it('does not dismiss on a downward fling away from peek', () => {
    // Fast down-flick from full lands on a snap, never a dismiss.
    const res = resolveSheetRelease(20, 1200, OFFSETS);
    expect(res.dismiss).toBe(false);
    expect(OFFSETS).toContain(res.offset);
  });

  it('snaps upward from peek on an upward fling instead of dismissing', () => {
    expect(resolveSheetRelease(515, -1500, OFFSETS)).toEqual({ offset: 300, dismiss: false });
  });
});
