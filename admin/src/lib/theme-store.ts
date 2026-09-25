import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { ThemeType } from './themes';

/**
 * Reflects the active theme onto <html data-theme="…">. index.css restyles every
 * token-bound utility from the `:root[data-theme="…"]` override blocks, so this
 * one attribute is the whole switch.
 */
function applyTheme(theme: ThemeType) {
    if (typeof document !== 'undefined') {
        document.documentElement.dataset.theme = theme;
    }
}

interface ThemeStore {
    theme: ThemeType;
    setTheme: (theme: ThemeType) => void;
}

export const useTheme = create<ThemeStore>()(
    persist(
        (set) => ({
            theme: 'lemberg', // Default matches the :root palette
            setTheme: (theme) => {
                applyTheme(theme);
                set({ theme });
            },
        }),
        {
            name: 'admin-lemberg-theme', // Unique storage key for admin (mirrors admin-lemberg-language)
            onRehydrateStorage: () => (state) => {
                if (state) applyTheme(state.theme);
            },
        }
    )
);

// Apply on module load so the attribute is set before React's first paint
// (zustand persist hydrates from localStorage synchronously), avoiding a flash
// of the default theme when a non-default theme is stored.
applyTheme(useTheme.getState().theme);
