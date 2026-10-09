import { describe, it, expect } from 'vitest';
import { resolveAllowBelowCost } from './belowCostOptIn';

describe('resolveAllowBelowCost', () => {
  // Regression: the editor showed `false` for a fuel that was opted in, because it read only the
  // draft and the draft is undefined until the operator touches something. The operator saw a
  // permission that was actually on as off.
  it('reads the stored value when nothing has been edited yet', () => {
    expect(resolveAllowBelowCost(undefined, true)).toBe(true);
    expect(resolveAllowBelowCost(undefined, false)).toBe(false);
  });

  it('prefers what the operator just did', () => {
    expect(resolveAllowBelowCost(true, false)).toBe(true);
    expect(resolveAllowBelowCost(false, true)).toBe(false);
  });

  // Un-ticking has to be able to turn a standing permission off, so an explicit false must not
  // lose to a stored true. `||` would silently invert this case.
  it('lets an explicit untick beat a stored opt-in', () => {
    expect(resolveAllowBelowCost(false, true)).toBe(false);
  });

  it('is off when nothing is known', () => {
    expect(resolveAllowBelowCost(undefined, undefined)).toBe(false);
  });
});