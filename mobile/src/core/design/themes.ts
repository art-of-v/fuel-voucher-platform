import { getStatusPalette, StatusPalette } from './palette';

export type ThemeType = 'lemberg' | 'white' | 'blue' | 'obsidian' | 'nova' | 'glass' | 'sorbet' | 'blade';

/**
 * A theme is a **colour** variation only.
 *
 * Phase 2 decision: themes no longer carry shape. The `soft` boolean and the
 * per-theme `surface` radius overrides were removed, because they produced two
 * competing shape languages (sharp `radius: 2` HUD vs. rounded/pill) that could
 * appear one tap apart — `/contracts` at radius 2 next to `/invitations` at 12.
 * Shape now comes from a single scale in `tokens.ts` and is identical on all
 * eight themes. See `docs/design/DESIGN_SYSTEM.md` § Visual language.
 *
 * Fields are grouped by semantic role. The `@deprecated` fields are retained so
 * that screens not yet migrated keep compiling; they are aliases over the
 * semantic roles and must not be used in new code.
 */
export interface ThemeColors {
  // ── Canvas & surfaces ────────────────────────────────────────────────────
  /** App canvas. Nothing sits behind this. */
  background: string;
  /** Default raised surface: cards, list rows, tiles. */
  surface: string;
  /** Surfaces that float above `surface`: sheets, dialogs, menus, toasts. */
  surfaceElevated: string;
  /** Recessed surface: input wells, inset code/QR areas, track backgrounds. */
  surfaceSunken: string;

  // ── Text ─────────────────────────────────────────────────────────────────
  text: {
    /** Primary reading colour. Titles, values, body. */
    primary: string;
    /** Supporting copy that is still meant to be read. */
    secondary: string;
    /** Labels and metadata — the quietest text that is still legible. */
    muted: string;
    /** Text/icons on a `disabled` surface. */
    disabled: string;
    /**
     * @deprecated Too low-contrast to be a reading colour; it was used for
     * decision-critical text (fuel names). Use `muted`.
     */
    dim: string;
    /** @deprecated Brand-glow text colour. Use `primary` action colour. */
    neon: string;
  };

  // ── Lines ────────────────────────────────────────────────────────────────
  /** Default hairline: card outlines, dividers, field borders. Neutral. */
  border: string;
  /** Quieter divider inside an already-bordered container. */
  borderSubtle: string;
  /** Emphasised outline: selected, hovered, or high-contrast separation. */
  borderStrong: string;
  /** Brand-tinted outline. Only for selected/active brand affordances. */
  borderAccent: string;

  // ── Actions ──────────────────────────────────────────────────────────────
  /** The single primary action colour. */
  primary: string;
  /** Primary under press. */
  primaryPressed: string;
  /** Low-emphasis tinted container derived from `primary`. */
  primarySubtle: string;
  /**
   * Text/icon colour on a `primary` fill. Derived from the *luminance of
   * `primary`*, not from `isDark` — the previous `isDark ? '#FFF' : '#000'`
   * rule produced white-on-`#00FF6A` (≈2:1) on the default theme.
   */
  onPrimary: string;
  /** Focus ring / keyboard-focus indicator. */
  focus: string;
  /** Fill for a disabled control. */
  disabled: string;

  // ── Meta ─────────────────────────────────────────────────────────────────
  isDark: boolean;
  /** Optional per-theme corrections to the shared status palette. */
  status?: Partial<StatusPalette>;

  // ── Deprecated aliases (kept so unmigrated screens compile) ──────────────
  /** @deprecated Use `primarySubtle`. */
  primaryDim: string;
  /** @deprecated Glow effects are not part of the design language. */
  primaryGlow: string;
  /** @deprecated A second brand colour with no semantic role. Use `primary`. */
  accent: string;
  /** @deprecated Use `surface`. */
  card: string;
  /** @deprecated Use `borderSubtle`. */
  borderLight: string;
  /** @deprecated Use the `danger` status role. */
  error: string;
}

export const themes: Record<ThemeType, ThemeColors> = {
  lemberg: {
    background: '#000000',
    surface: '#0C0C0C',
    surfaceElevated: '#161616',
    surfaceSunken: '#050505',
    text: {
      primary: '#FFFFFF',
      secondary: 'rgba(255, 255, 255, 0.72)',
      muted: 'rgba(255, 255, 255, 0.56)',
      disabled: 'rgba(255, 255, 255, 0.32)',
      dim: 'rgba(255, 255, 255, 0.4)',
      neon: '#16FF00',
    },
    border: 'rgba(255, 255, 255, 0.14)',
    borderSubtle: 'rgba(255, 255, 255, 0.08)',
    borderStrong: 'rgba(255, 255, 255, 0.26)',
    borderAccent: 'rgba(0, 255, 106, 0.45)',
    primary: '#00E85F',
    primaryPressed: '#00C351',
    primarySubtle: 'rgba(0, 232, 95, 0.14)',
    onPrimary: '#FFFFFF',
    focus: '#00E85F',
    disabled: 'rgba(255, 255, 255, 0.10)',
    isDark: true,
    primaryDim: 'rgba(0, 255, 106, 0.1)',
    primaryGlow: 'rgba(0, 255, 106, 0.4)',
    accent: '#00FFFF',
    card: '#0C0C0C',
    borderLight: 'rgba(255, 255, 255, 0.08)',
    error: '#FF5D5D',
  },
  white: {
    background: '#FAF9F6',
    surface: '#FFFFFF',
    surfaceElevated: '#FFFFFF',
    surfaceSunken: '#F1F0EB',
    text: {
      primary: '#17181A',
      secondary: '#4B5563',
      muted: '#6B7280',
      disabled: '#A9AEB6',
      dim: '#9CA3AF',
      neon: '#059669',
    },
    border: '#E2E1DC',
    borderSubtle: '#EDECE7',
    borderStrong: '#C9C7C0',
    borderAccent: 'rgba(6, 78, 59, 0.38)',
    primary: '#065F46',
    primaryPressed: '#044633',
    primarySubtle: 'rgba(6, 95, 70, 0.10)',
    onPrimary: '#FFFFFF',
    focus: '#065F46',
    disabled: '#EBEAE5',
    isDark: false,
    primaryDim: 'rgba(6, 78, 59, 0.05)',
    primaryGlow: 'rgba(6, 78, 59, 0.12)',
    accent: '#B45309',
    card: '#FFFFFF',
    borderLight: '#EDECE7',
    error: '#B91C1C',
  },
  blue: {
    background: '#0E1626',
    surface: '#1B2739',
    surfaceElevated: '#243449',
    surfaceSunken: '#0A1120',
    text: {
      primary: '#F8FAFC',
      secondary: '#C3CEDD',
      muted: '#94A3B8',
      disabled: '#5C6B80',
      dim: '#64748B',
      neon: '#60A5FA',
    },
    border: 'rgba(255, 255, 255, 0.14)',
    borderSubtle: 'rgba(255, 255, 255, 0.08)',
    borderStrong: 'rgba(255, 255, 255, 0.28)',
    borderAccent: 'rgba(96, 165, 250, 0.50)',
    primary: '#3B82F6',
    primaryPressed: '#2A6BD4',
    primarySubtle: 'rgba(59, 130, 246, 0.16)',
    onPrimary: '#FFFFFF',
    focus: '#60A5FA',
    disabled: 'rgba(255, 255, 255, 0.10)',
    isDark: true,
    primaryDim: 'rgba(59, 130, 246, 0.1)',
    primaryGlow: 'rgba(59, 130, 246, 0.3)',
    accent: '#06B6D4',
    card: '#1B2739',
    borderLight: 'rgba(255, 255, 255, 0.08)',
    error: '#FF5D5D',
  },
  obsidian: {
    background: '#020203',
    surface: '#0D0D10',
    surfaceElevated: '#17171C',
    surfaceSunken: '#000000',
    text: {
      primary: '#F8FAFC',
      secondary: 'rgba(248, 250, 252, 0.72)',
      muted: 'rgba(248, 250, 252, 0.55)',
      disabled: 'rgba(248, 250, 252, 0.30)',
      dim: 'rgba(248, 250, 252, 0.35)',
      neon: '#A78BFA',
    },
    border: 'rgba(255, 255, 255, 0.12)',
    borderSubtle: 'rgba(255, 255, 255, 0.07)',
    borderStrong: 'rgba(255, 255, 255, 0.24)',
    borderAccent: 'rgba(139, 92, 246, 0.48)',
    primary: '#8B5CF6',
    primaryPressed: '#7443E0',
    primarySubtle: 'rgba(139, 92, 246, 0.16)',
    onPrimary: '#FFFFFF',
    focus: '#A78BFA',
    disabled: 'rgba(255, 255, 255, 0.10)',
    isDark: true,
    primaryDim: 'rgba(139, 92, 246, 0.1)',
    primaryGlow: 'rgba(139, 92, 246, 0.4)',
    accent: '#F472B6',
    card: '#0D0D10',
    borderLight: 'rgba(255, 255, 255, 0.07)',
    error: '#FF5D5D',
  },
  nova: {
    background: '#060607',
    surface: '#121216',
    surfaceElevated: '#1B1B21',
    surfaceSunken: '#0A0A0C',
    text: {
      primary: '#FFFFFF',
      secondary: 'rgba(255, 255, 255, 0.72)',
      muted: 'rgba(255, 255, 255, 0.55)',
      disabled: 'rgba(255, 255, 255, 0.30)',
      dim: 'rgba(255, 255, 255, 0.38)',
      neon: '#00D68F',
    },
    border: 'rgba(255, 255, 255, 0.14)',
    borderSubtle: 'rgba(255, 255, 255, 0.08)',
    borderStrong: 'rgba(255, 255, 255, 0.26)',
    borderAccent: 'rgba(0, 214, 143, 0.45)',
    primary: '#00D68F',
    primaryPressed: '#00B378',
    primarySubtle: 'rgba(0, 214, 143, 0.14)',
    onPrimary: '#00231A',
    focus: '#00D68F',
    disabled: 'rgba(255, 255, 255, 0.10)',
    isDark: true,
    primaryDim: 'rgba(0, 214, 143, 0.08)',
    primaryGlow: 'rgba(0, 214, 143, 0.25)',
    accent: '#38BDF8',
    card: '#121216',
    borderLight: 'rgba(255, 255, 255, 0.08)',
    error: '#FF5D5D',
  },
  glass: {
    background: '#04070D',
    // Translucent frosted surfaces — this theme's identity is transparency,
    // which is colour, not shape, so it survives the shape unification.
    surface: 'rgba(255, 255, 255, 0.07)',
    surfaceElevated: 'rgba(255, 255, 255, 0.13)',
    surfaceSunken: 'rgba(0, 0, 0, 0.28)',
    text: {
      primary: '#F0F9FF',
      secondary: 'rgba(240, 249, 255, 0.74)',
      muted: 'rgba(240, 249, 255, 0.56)',
      disabled: 'rgba(240, 249, 255, 0.32)',
      dim: 'rgba(240, 249, 255, 0.38)',
      neon: '#67E8F9',
    },
    border: 'rgba(255, 255, 255, 0.20)',
    borderSubtle: 'rgba(255, 255, 255, 0.12)',
    borderStrong: 'rgba(255, 255, 255, 0.34)',
    borderAccent: 'rgba(103, 232, 249, 0.48)',
    primary: '#67E8F9',
    primaryPressed: '#3FCBDE',
    primarySubtle: 'rgba(103, 232, 249, 0.14)',
    onPrimary: '#04252B',
    focus: '#67E8F9',
    disabled: 'rgba(255, 255, 255, 0.10)',
    isDark: true,
    primaryDim: 'rgba(103, 232, 249, 0.08)',
    primaryGlow: 'rgba(103, 232, 249, 0.3)',
    accent: '#A78BFA',
    card: 'rgba(255, 255, 255, 0.07)',
    borderLight: 'rgba(255, 255, 255, 0.12)',
    error: '#FF5D5D',
  },
  sorbet: {
    background: '#FFF8F0',
    surface: '#FFFFFF',
    surfaceElevated: '#FFFFFF',
    surfaceSunken: '#FDF1E6',
    text: {
      primary: '#2B1D14',
      secondary: '#5A4839',
      muted: '#7A6A5C',
      disabled: '#B4A697',
      dim: '#A99A8C',
      neon: '#FF6B4A',
    },
    border: '#F0DFCE',
    borderSubtle: '#F7EBDF',
    borderStrong: '#DCC3AC',
    borderAccent: 'rgba(214, 74, 42, 0.38)',
    primary: '#D64A2A',
    primaryPressed: '#B93B1F',
    primarySubtle: 'rgba(214, 74, 42, 0.10)',
    onPrimary: '#FFFFFF',
    focus: '#D64A2A',
    disabled: '#F5EAE0',
    isDark: false,
    primaryDim: 'rgba(255, 107, 74, 0.08)',
    primaryGlow: 'rgba(255, 107, 74, 0.2)',
    accent: '#FFB020',
    card: '#FFFFFF',
    borderLight: '#F7EBDF',
    error: '#B91C1C',
  },
  blade: {
    background: '#F7F7F5',
    surface: '#FFFFFF',
    surfaceElevated: '#FFFFFF',
    surfaceSunken: '#EFEFEC',
    text: {
      primary: '#18181B',
      secondary: '#3F3F46',
      muted: '#71717A',
      disabled: '#B0B0B6',
      dim: '#A1A1AA',
      neon: '#E11D48',
    },
    border: '#E4E4E7',
    borderSubtle: '#EFEFF1',
    borderStrong: '#C8C8CD',
    borderAccent: 'rgba(24, 24, 27, 0.32)',
    primary: '#18181B',
    primaryPressed: '#000000',
    primarySubtle: 'rgba(24, 24, 27, 0.07)',
    onPrimary: '#FFFFFF',
    focus: '#18181B',
    disabled: '#EAEAEC',
    isDark: false,
    primaryDim: 'rgba(24, 24, 27, 0.05)',
    primaryGlow: 'rgba(24, 24, 27, 0.12)',
    accent: '#E11D48',
    card: '#FFFFFF',
    borderLight: '#EFEFF1',
    error: '#B91C1C',
  },
};

/** Resolved status roles for a theme. See `palette.ts`. */
export function getThemeStatus(themeType: ThemeType): StatusPalette {
  const theme = themes[themeType] ?? themes.lemberg;
  return getStatusPalette(theme.isDark, theme.status);
}

export const themeOptions: { id: ThemeType; label: string; color: string }[] = [
  { id: 'lemberg', label: 'profile.themeLemberg', color: '#00E85F' },
  { id: 'white', label: 'profile.themeWhite', color: '#065F46' },
  { id: 'blue', label: 'profile.themeBlue', color: '#3B82F6' },
  { id: 'obsidian', label: 'profile.themeObsidian', color: '#8B5CF6' },
  { id: 'nova', label: 'profile.themeNova', color: '#00D68F' },
  { id: 'glass', label: 'profile.themeGlass', color: '#67E8F9' },
  { id: 'sorbet', label: 'profile.themeSorbet', color: '#D64A2A' },
  { id: 'blade', label: 'profile.themeBlade', color: '#18181B' },
];
