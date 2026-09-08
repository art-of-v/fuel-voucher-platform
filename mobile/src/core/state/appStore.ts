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

  login: () => void;
  logout: () => void;
  unlockApp: () => void;
  lockApp: () => void;
  completeOnboarding: () => void;
}

export const useStore = create<AppStore>()(
  persist(
    (set) => ({
      theme: 'lemberg',
      setTheme: (theme) => set({ theme }),

      isAuthenticated: false,
      isAppUnlocked: false,
      hasCompletedOnboarding: false,

      login: () => set({ isAuthenticated: true }),
      logout: () => set({ isAuthenticated: false, isAppUnlocked: false }),
      unlockApp: () => set({ isAppUnlocked: true }),
      lockApp: () => set({ isAppUnlocked: false }),
      completeOnboarding: () => set({ hasCompletedOnboarding: true }),
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
