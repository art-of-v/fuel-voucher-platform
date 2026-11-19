import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ThemeType } from '../design/themes';

interface AppStore {
  theme: ThemeType;
  setTheme: (theme: ThemeType) => void;

  /**
   * Whether the theme in this store is the worker's own choice.
   *
   * <p>
   * It exists to answer one question that `theme` alone cannot: "has this person picked a
   * theme, or are they still on whatever the app defaulted to?". Without it the installation's
   * default theme (the admin setting behind `GET /api/app/appearance`) could not be applied
   * without either overwriting a deliberate choice or never applying at all.
   * </p>
   *
   * <p>
   * It is NOT a "user changed something" flag: it is set only by <see cref="setTheme"/>, which
   * is the profile picker, and never by <see cref="applyServerTheme"/>. Persisted, so it survives
   * a relaunch — and an install that predates this field has no value at all, which reads as
   * "never chose" and takes the installation's default. That is the right answer for those
   * installs: before this existed the default was obsidian and their stored theme is either that
   * same obsidian or a choice the server should not have to guess about.
   * </p>
   */
  themePinned: boolean;

  /**
   * Applies a theme that came from the server, unless the worker has chosen one themselves.
   *
   * <p>
   * The admin setting is a DEFAULT, not a directive: the whole point of a per-worker picker is
   * that the worker wins. So this is a no-op once <see cref="themePinned"/> is true.
   * </p>
   */
  applyServerTheme: (theme: ThemeType) => void;

  isAuthenticated: boolean;
  isAppUnlocked: boolean;

  /**
   * First-run onboarding gate (Apple 5.1.1: data collection must be explained
   * before sign-up). Persisted — shown once per install, never for returning
   * users. Lives outside the partialize exclusion list on purpose.
   */
  hasCompletedOnboarding: boolean;

  /**
   * Active account context (multi-company epic #103, S1). `null` = the personal
   * (фіз-особа) root, which is always the default on a fresh open; a company
   * UUID selects that legal entity. Persisted so a chosen context survives a
   * relaunch, but reset to `null` on logout so it never leaks across accounts.
   */
  currentLegalEntityId: string | null;

  /**
   * The phone number the last sign-in used. Persisted on purpose: the sign-in form is
   * where a worker comes back to, and making them type their own number again after every
   * logout is a step that only ever loses people - especially when the code arrives as a
   * push and the form is supposed to be ready for it. It survives the logout (that is the
   * whole point) and it is only ever shown back to the person holding the device.
   */
  lastPhone: string | null;

  login: () => void;
  logout: () => void;
  unlockApp: () => void;
  lockApp: () => void;
  completeOnboarding: () => void;
  setCurrentContext: (legalEntityId: string | null) => void;
  setLastPhone: (phone: string | null) => void;
}

export const useStore = create<AppStore>()(
  persist(
    (set) => ({
      // Fresh-install default. `persist` rehydrates a saved choice over this, so
      // existing installs keep whatever theme they already picked.
      theme: 'obsidian',
      themePinned: false,
      setTheme: (theme) => set({ theme, themePinned: true }),
      applyServerTheme: (theme) =>
        set((state) => (state.themePinned ? state : { ...state, theme })),

      isAuthenticated: false,
      isAppUnlocked: false,
      hasCompletedOnboarding: false,
      currentLegalEntityId: null,
      lastPhone: null,

      login: () => set({ isAuthenticated: true }),
      logout: () =>
        set({ isAuthenticated: false, isAppUnlocked: false, currentLegalEntityId: null }),
      unlockApp: () => set({ isAppUnlocked: true }),
      lockApp: () => set({ isAppUnlocked: false }),
      completeOnboarding: () => set({ hasCompletedOnboarding: true }),
      setCurrentContext: (legalEntityId) => set({ currentLegalEntityId: legalEntityId }),
      setLastPhone: (phone) => set({ lastPhone: phone }),
    }),
    {
      name: 'fuel-app-state',
      storage: createJSONStorage(() => AsyncStorage),
      partialize: (state) => {
        const { isAppUnlocked, isAuthenticated, ...rest } = state as any;
        return rest;
      },
    },
  ),
);
