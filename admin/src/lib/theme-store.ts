import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { DEFAULT_THEME, themes, type ThemeType } from './themes';

/**
 * Reflects the active theme onto <html data-theme="…">. index.css restyles every
 * token-bound utility from the `:root[data-theme="…"]` override blocks, so this
 * one attribute is the whole switch.
 *
 * Forge themes additionally get `<html data-forge>`. Their machined treatment —
 * square corners, Rajdhani, hard 3D bevels — is ~25 rules that all four share
 * verbatim and differ from the other themes only in hue, so they key off a
 * single flag instead of repeating a four-way selector list 25 times.
 */
function applyTheme(theme: ThemeType) {
    if (typeof document === 'undefined') return;
    const root = document.documentElement;
    root.dataset.theme = theme;
    if (themes[theme].forge) {
        root.dataset.forge = '';
    } else {
        delete root.dataset.forge;
    }
}

/**
 * The stored theme is a bare string from localStorage, so it can hold an id
 * that no longer exists (a theme renamed or removed, or hand-edited state).
 * Writing that through would leave <html> with an attribute no CSS block
 * matches, silently falling back to the default palette — so clamp instead.
 */
function resolveTheme(theme: unknown): ThemeType {
    return typeof theme === 'string' && theme in themes ? (theme as ThemeType) : DEFAULT_THEME;
}

interface ThemeStore {
    theme: ThemeType;
    setTheme: (theme: ThemeType) => void;
}

/** zustand persist merge hook, kept pure so it stays safe during `create()`. */
function mergePersistedTheme(
    persisted: unknown,
    current: ThemeStore,
): ThemeStore {
    const stored = (persisted as { theme?: unknown } | null)?.theme;
    return { ...current, theme: resolveTheme(stored) };
}

export const useTheme = create<ThemeStore>()(
    persist(
        (set) => ({
            theme: DEFAULT_THEME,
            setTheme: (theme) => {
                const next = resolveTheme(theme);
                applyTheme(next);
                set({ theme: next });
            },
        }),
        {
            name: 'admin-lemberg-theme', // Unique storage key for admin (mirrors admin-lemberg-language)
            merge: mergePersistedTheme,
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
