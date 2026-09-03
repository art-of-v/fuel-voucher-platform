/**
 * Where the bottom tab bar is shown, and how tall it is.
 *
 * This lives outside both `BottomTabs` and `PageLayout` because both need the
 * answer: the bar needs to know whether to render, and the layout needs to know
 * how much bottom padding to reserve so content is not hidden beneath it.
 *
 * Before Phase 2 only the bar knew. Screens compensated by guessing — the shared
 * layout added `paddingBottom: 150`, and individual screens added a further 84,
 * 100 or 120 on top of it.
 */

/**
 * Routes that hide the tab bar. Unchanged from the previous implementation —
 * Phase 2 does not restructure navigation, it only stops the number being
 * duplicated.
 *
 * Note the known inconsistency, documented rather than fixed here: `/basket` sits
 * between `/packages` and `/checkout` in the purchase funnel and is *not* in this
 * list, so the bar disappears, reappears, and disappears again mid-funnel. That
 * is a navigation decision for Phase 3.
 */
export const TAB_BAR_HIDDEN_PREFIXES = [
  '/station/',
  '/packages',
  '/checkout',
  '/payment-result',
] as const;

/** Whether the tab bar is on screen for a given route. */
export function isTabBarVisible(pathname: string): boolean {
  if (pathname === '/landing') return false;
  return !TAB_BAR_HIDDEN_PREFIXES.some((p) => pathname.startsWith(p));
}
