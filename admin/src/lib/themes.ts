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
 * Themes offered in the switcher — all 8 (5 dark + 3 light). `swatch` mirrors
 * the mobile themeOptions colour. The actual palettes + light/dark chrome live
 * in index.css; `themes[id].isDark` drives color-scheme + the sonner Toaster.
 */
export const themeOptions: { id: ThemeType; label: string; swatch: string }[] = [
    { id: 'lemberg', label: 'Lemberg', swatch: '#00E85F' },
    { id: 'blue', label: 'Blue', swatch: '#3B82F6' },
    { id: 'obsidian', label: 'Obsidian', swatch: '#8B5CF6' },
    { id: 'nova', label: 'Nova', swatch: '#00D68F' },
    { id: 'glass', label: 'Glass', swatch: '#67E8F9' },
    { id: 'white', label: 'White', swatch: '#065F46' },
    { id: 'sorbet', label: 'Sorbet', swatch: '#D64A2A' },
    { id: 'blade', label: 'Blade', swatch: '#18181B' },
];
