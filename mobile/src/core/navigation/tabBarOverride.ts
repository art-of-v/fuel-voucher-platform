import { create } from 'zustand';

/**
 * Lets a screen ask the tab bar to step aside while it owns the bottom of the
 * screen, and tells the layout system that the bar's clearance is free.
 *
 * The map needs this and cannot get it any other way. Its leaderboard sheet and
 * the station panel are drawn *inside* the scene, and the tab bar is a sibling of
 * the scene inside the navigator. React Native only compares `zIndex` between
 * siblings, so a sheet cannot lift itself past a bar that sits above the whole
 * scene — the bar's icons paint straight over the list.
 *
 * Both halves have to be live: the tab bar subscribes to `hidden` and unmounts,
 * and `useContentInsets` drops the bar's height from the screen's bottom padding
 * so the sheet can reach the home indicator instead of floating above a strip of
 * empty map. This file used to declare the state and never write or read it —
 * `setHidden` was a permanent no-op and nothing consumed `hidden` — so the bar
 * stayed painted over the map's sheet.
 */
interface TabBarOverride {
  /** True while some screen is holding the bottom of the screen. */
  hidden: boolean;
  setHidden: (hidden: boolean) => void;
}

export const useTabBarOverride = create<TabBarOverride>((set) => ({
  hidden: false,
  setHidden: (hidden) => set({ hidden }),
}));
