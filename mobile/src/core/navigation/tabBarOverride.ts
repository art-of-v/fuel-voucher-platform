import { create } from 'zustand';

/**
 * Lets a screen ask the tab bar to step aside while it owns the bottom of the
 * screen.
 *
 * The map needs this and cannot get it any other way. Its leaderboard is a
 * gesture-driven sheet drawn *inside* the scene, and the tab bar is a sibling of
 * the scene inside the navigator. React Native only compares `zIndex` between
 * siblings, so the sheet's own `zIndex: 200` cannot lift it past a bar that sits
 * above the whole scene - the bar's icons paint straight over the list.
 */
interface TabBarOverride {
  /** True while some screen is holding the bottom of the screen. */
  hidden: boolean;
  setHidden: (hidden: boolean) => void;
}

export const useTabBarOverride = create<TabBarOverride>(() => ({
  hidden: false,
  // Overwritten by whichever screen asks for it; the default keeps the store inert
  // so importing it costs nothing on screens that never touch it.
  setHidden: () => {},
}));
