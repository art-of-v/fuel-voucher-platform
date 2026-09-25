// Admin color themes.
//
// Parity port of the mobile app's theme catalogue. Keep in sync with
// mobile/src/core/design/themes.ts — the `ThemeType` union, the per-theme
// `isDark` flag, and the swatch colours mirror that file. Admin is a separate
// Vite package and cannot import from mobile/, so the values are duplicated.
//
// The actual colour values live as CSS custom properties in index.css
// (`:root` + `:root[data-theme="…"]`). This module only carries the metadata
// the UI needs: which themes to offer, their swatch, and whether each is dark
// (used to drive the sonner <Toaster> and any future light/dark-aware chrome).

export type ThemeType = 'lemberg' | 'white' | 'blue' | 'obsidian' | 'nova' | 'glass' | 'sorbet' | 'blade';

/** isDark for every theme in the catalogue (dark: 5, light: 3), matching mobile. */
export const themes: Record<ThemeType, { isDark: boolean }> = {
    lemberg: { isDark: true },
    white: { isDark: false },
    blue: { isDark: true },
    obsidian: { isDark: true },
    nova: { isDark: true },
    glass: { isDark: true },
    sorbet: { isDark: false },
    blade: { isDark: false },
};

/**
 * Themes offered in the switcher. Phase 1 ships the 5 dark themes only; the
 * 3 light themes (white, sorbet, blade) are deferred to Phase 2, which
 * de-hardcodes the glass/aurora chrome and migrates raw bg-white/text-black
 * utilities to semantic tokens. `swatch` mirrors the mobile themeOptions colour.
 */
export const themeOptions: { id: ThemeType; label: string; swatch: string }[] = [
    { id: 'lemberg', label: 'Lemberg', swatch: '#00E85F' },
    { id: 'blue', label: 'Blue', swatch: '#3B82F6' },
    { id: 'obsidian', label: 'Obsidian', swatch: '#8B5CF6' },
    { id: 'nova', label: 'Nova', swatch: '#00D68F' },
    { id: 'glass', label: 'Glass', swatch: '#67E8F9' },
];
