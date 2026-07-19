import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { ThemeType } from '../design/themes';

interface AppStore {
  theme: ThemeType;
  setTheme: (theme: ThemeType) => void;

  isAuthenticated: boolean;
  isAppUnlocked: boolean;

  login: () => void;
  logout: () => void;
  unlockApp: () => void;
  lockApp: () => void;
}

export const useStore = create<AppStore>()(
  persist(
    (set) => ({
      theme: 'lemberg',
      setTheme: (theme) => set({ theme }),

      isAuthenticated: false,
      isAppUnlocked: false,

      login: () => set({ isAuthenticated: true }),
      logout: () => set({ isAuthenticated: false, isAppUnlocked: false }),
      unlockApp: () => set({ isAppUnlocked: true }),
      lockApp: () => set({ isAppUnlocked: false }),
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
