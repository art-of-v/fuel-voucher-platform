// Admin color themes.
//
// Parity port of the mobile app's theme catalogue. Keep in sync with
// mobile/src/core/design/themes.ts — the `ThemeType` union, the per-theme
// `isDark` flag, and the swatch colours mirror that file. Admin is a separate
// Vite package and cannot import from mobile/, so the values are duplicated.
// The forge themes are the exception: they have no mobile counterpart.
//
// The actual colour values live as CSS custom properties in index.css
// (`:root` + `:root[data-theme="…"]`). This module only carries the metadata
// the UI needs: which themes to offer, their swatch, whether each is dark (used
// to drive the sonner <Toaster>), and whether it is a forge theme.

export type ThemeType =
    | 'lemberg'
    | 'white'
    | 'blue'
    | 'obsidian'
    | 'nova'
    | 'glass'
    | 'sorbet'
    | 'blade'
    | 'mercury'
    | 'lemberg-forge'
    | 'blue-forge'
    | 'obsidian-forge';

/** The theme whose palette lives on bare `:root` (every other theme overrides it). */
export const DEFAULT_THEME: ThemeType = 'lemberg';

export interface ThemeMeta {
    isDark: boolean;
    /**
     * Forge themes share one machined treatment — square corners, Rajdhani, hard
     * 3D bevels — and differ only in which hue they carry. index.css keys that
     * whole treatment off `<html data-forge>`, so this flag is what turns it on;
     * adding a fifth forge theme is a palette block plus one line here.
     */
    forge: boolean;
}

/** isDark for every theme in the catalogue (dark: 9, light: 3), matching mobile. */
export const themes: Record<ThemeType, ThemeMeta> = {
    lemberg: { isDark: true, forge: false },
    white: { isDark: false, forge: false },
    blue: { isDark: true, forge: false },
    obsidian: { isDark: true, forge: false },
    nova: { isDark: true, forge: false },
    glass: { isDark: true, forge: false },
    sorbet: { isDark: false, forge: false },
    blade: { isDark: false, forge: false },

    mercury: { isDark: true, forge: true },
    'lemberg-forge': { isDark: true, forge: true },
    'blue-forge': { isDark: true, forge: true },
    'obsidian-forge': { isDark: true, forge: true },
};

/**
 * Themes offered in the switcher — all 12 (9 dark + 3 light). `swatch` mirrors
 * the mobile themeOptions colour. The actual palettes + light/dark chrome live
 * in index.css; `themes[id].isDark` drives color-scheme + the sonner Toaster.
 *
 * The four forge themes are grouped at the end rather than interleaved with the
 * originals: they read as a family, and "Lemberg Forge" sitting directly under
 * "Lemberg" is the only way to tell at a glance which is which.
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
    { id: 'mercury', label: 'Mercury', swatch: '#CBD4DE' },
    { id: 'lemberg-forge', label: 'Lemberg Forge', swatch: '#22D873' },
    { id: 'blue-forge', label: 'Blue Forge', swatch: '#4B8EF7' },
    { id: 'obsidian-forge', label: 'Obsidian Forge', swatch: '#9061F8' },
];
