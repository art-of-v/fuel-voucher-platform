import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ThemeType } from '../design/themes';

interface AppStore {
  theme: ThemeType;
  setTheme: (theme: ThemeType) => void;

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

  login: () => void;
  logout: () => void;
  unlockApp: () => void;
  lockApp: () => void;
  completeOnboarding: () => void;
  setCurrentContext: (legalEntityId: string | null) => void;
}

export const useStore = create<AppStore>()(
  persist(
    (set) => ({
      theme: 'lemberg',
      setTheme: (theme) => set({ theme }),

      isAuthenticated: false,
      isAppUnlocked: false,
      hasCompletedOnboarding: false,
      currentLegalEntityId: null,

      login: () => set({ isAuthenticated: true }),
      logout: () =>
        set({ isAuthenticated: false, isAppUnlocked: false, currentLegalEntityId: null }),
      unlockApp: () => set({ isAppUnlocked: true }),
      lockApp: () => set({ isAppUnlocked: false }),
      completeOnboarding: () => set({ hasCompletedOnboarding: true }),
      setCurrentContext: (legalEntityId) => set({ currentLegalEntityId: legalEntityId }),
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
