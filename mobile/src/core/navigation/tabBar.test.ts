import { isTabBarVisible } from './tabBar';

/**
 * This is a pure list lookup, so the tests are about the routes rather than the function:
 * which screens keep the bar, which lose it, and - the reason this file exists - that the
 * payment screen loses it like the rest of the purchase funnel.
 *
 * `useIsTabBarVisible` is the hook the bar and the layout actually call; it is this
 * route rule plus whatever a screen asked for via the override. The route rule is the part
 * that silently regresses, because adding a screen costs nothing and nothing fails when the
 * list is forgotten.
 */
describe('isTabBarVisible', () => {
  it.each(['/', '/my-codes', '/profile', '/map', '/renew'])('keeps the bar on %s', (route) => {
    expect(isTabBarVisible(route)).toBe(true);
  });

  it.each(['/landing', '/packages', '/checkout', '/pay', '/payment-result'])(
    'hides the bar on %s',
    (route) => {
      expect(isTabBarVisible(route)).toBe(false);
    },
  );

  it('hides the bar on the payment route with its query string', () => {
    // `/pay` is always navigated to with `?url=` and `?appUrl=` carrying the Monobank URLs.
    // The route rule matches on the pathname, but a prefix list that only matched a bare
    // `/pay` would quietly stop hiding the bar the moment a URL was attached.
    expect(isTabBarVisible('/pay?url=https%3A%2F%2Fpay.monobank.ua%2Fframe%2Finv-1')).toBe(false);
  });

  it('matches by prefix, so a sibling route sharing a prefix would also hide the bar', () => {
    // Documents the existing semantics rather than asserting they are desirable: this is a
    // plain `startsWith` list, so `/paylater` hides the bar exactly like `/pay` does, and the
    // same holds for `/packages` / `/checkout`. Nothing routes there today, and tightening the
    // matcher to segment boundaries would change behaviour for every entry at once - which is
    // a navigation decision, not something to smuggle in with a one-line addition.
    expect(isTabBarVisible('/paylater')).toBe(false);
  });
});
