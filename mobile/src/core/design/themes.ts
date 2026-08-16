export type ThemeType = 'lemberg' | 'white' | 'blue' | 'obsidian' | 'nova' | 'glass' | 'sorbet' | 'blade';

export interface ThemeColors {
  background: string;
  primary: string;
  primaryDim: string;
  primaryGlow: string;
  accent: string;
  card: string;
  border: string;
  borderLight: string;
  error: string;
  text: {
    primary: string;
    secondary: string;
    muted: string;
    dim: string;
    neon: string;
  };
  isDark: boolean;
  /**
   * Soft surface language: rounded cards/tiles, pill badges, thin accent
   * edges, quiet pressed states. Legacy themes keep the sharp HUD edges.
   */
  soft: boolean;
  /**
   * Per-theme shape overrides for the soft surface language (radii and
   * accent edge width). Omitted fields fall back to the soft defaults in
   * tokens.ts. Ignored by legacy (sharp) themes.
   */
  surface?: {
    card?: number;
    button?: number;
    field?: number;
    icon?: number;
    accentWidth?: number;
  };
}

export const themes: Record<ThemeType, ThemeColors> = {
  lemberg: {
    background: '#000000',
    primary: '#00FF6A',
    primaryDim: 'rgba(0, 255, 106, 0.1)',
    primaryGlow: 'rgba(0, 255, 106, 0.4)',
    accent: '#00FFFF',
    card: '#0A0A0A',
    border: 'rgba(0, 255, 106, 0.4)',
    borderLight: 'rgba(255, 255, 255, 0.15)',
    error: '#FF4B4B',
    text: {
      primary: '#FFFFFF',
      secondary: 'rgba(255, 255, 255, 0.9)',
      muted: 'rgba(255, 255, 255, 0.65)',
      dim: 'rgba(255, 255, 255, 0.4)',
      neon: '#16FF00',
    },
    isDark: true,
    soft: false,
  },
  white: {
    background: '#FAF9F6',
    primary: '#064E3B',
    primaryDim: 'rgba(6, 78, 59, 0.05)',
    primaryGlow: 'rgba(6, 78, 59, 0.12)',
    accent: '#B45309',
    card: '#FFFFFF',
    border: '#D1D5DB',
    borderLight: '#E5E7EB',
    error: '#B91C1C',
    text: {
      primary: '#1A1A1A',
      secondary: '#4B5563',
      muted: '#6B7280',
      dim: '#9CA3AF',
      neon: '#059669',
    },
    isDark: false,
    soft: false,
  },
  blue: {
    background: '#0F172A',
    primary: '#3B82F6',
    primaryDim: 'rgba(59, 130, 246, 0.1)',
    primaryGlow: 'rgba(59, 130, 246, 0.3)',
    accent: '#06B6D4',
    card: '#1E293B',
    border: 'rgba(255, 255, 255, 0.2)',
    borderLight: 'rgba(255, 255, 255, 0.1)',
    error: '#EF4444',
    text: {
      primary: '#F8FAFC',
      secondary: '#CBD5E1',
      muted: '#94A3B8',
      dim: '#64748B',
      neon: '#60A5FA',
    },
    isDark: true,
    soft: false,
  },
  obsidian: {
    background: '#020202',
    primary: '#8B5CF6',
    primaryDim: 'rgba(139, 92, 246, 0.1)',
    primaryGlow: 'rgba(139, 92, 246, 0.4)',
    accent: '#F472B6',
    card: '#0A0A0B',
    border: 'rgba(139, 92, 246, 0.3)',
    borderLight: 'rgba(255, 255, 255, 0.08)',
    error: '#EF4444',
    text: {
      primary: '#F8FAFC',
      secondary: 'rgba(248, 250, 252, 0.85)',
      muted: 'rgba(248, 250, 252, 0.6)',
      dim: 'rgba(248, 250, 252, 0.35)',
      neon: '#A78BFA',
    },
    isDark: true,
    soft: false,
  },
  nova: {
    background: '#050505',
    primary: '#00D68F',
    primaryDim: 'rgba(0, 214, 143, 0.08)',
    primaryGlow: 'rgba(0, 214, 143, 0.25)',
    accent: '#38BDF8',
    card: '#101014',
    border: 'rgba(255, 255, 255, 0.16)',
    borderLight: 'rgba(255, 255, 255, 0.08)',
    error: '#FF4B4B',
    text: {
      primary: '#FFFFFF',
      secondary: 'rgba(255, 255, 255, 0.9)',
      muted: 'rgba(255, 255, 255, 0.6)',
      dim: 'rgba(255, 255, 255, 0.38)',
      neon: '#00D68F',
    },
    isDark: true,
    soft: true,
  },
  glass: {
    background: '#04070D',
    primary: '#67E8F9',
    primaryDim: 'rgba(103, 232, 249, 0.08)',
    primaryGlow: 'rgba(103, 232, 249, 0.3)',
    accent: '#A78BFA',
    // Translucent frosted surface — the liquid-glass look.
    card: 'rgba(255, 255, 255, 0.07)',
    border: 'rgba(255, 255, 255, 0.28)',
    borderLight: 'rgba(255, 255, 255, 0.16)',
    error: '#F87171',
    text: {
      primary: '#F0F9FF',
      secondary: 'rgba(240, 249, 255, 0.9)',
      muted: 'rgba(240, 249, 255, 0.6)',
      dim: 'rgba(240, 249, 255, 0.38)',
      neon: '#67E8F9',
    },
    isDark: true,
    soft: true,
    surface: { card: 24 },
  },
  sorbet: {
    background: '#FFF8F0',
    primary: '#FF6B4A',
    primaryDim: 'rgba(255, 107, 74, 0.08)',
    primaryGlow: 'rgba(255, 107, 74, 0.2)',
    accent: '#FFB020',
    card: '#FFFFFF',
    border: '#F3D9C4',
    borderLight: '#F1E3D3',
    error: '#DC2626',
    text: {
      primary: '#2B1D14',
      secondary: '#4A3A2E',
      muted: '#7A6A5C',
      dim: '#A99A8C',
      neon: '#FF6B4A',
    },
    isDark: false,
    soft: true,
    // Extra-round tiles and full-pill CTAs.
    surface: { card: 28, button: 999 },
  },
  blade: {
    background: '#F7F7F5',
    primary: '#18181B',
    primaryDim: 'rgba(24, 24, 27, 0.05)',
    primaryGlow: 'rgba(24, 24, 27, 0.12)',
    accent: '#E11D48',
    card: '#FFFFFF',
    border: '#D4D4D8',
    borderLight: '#E4E4E7',
    error: '#DC2626',
    text: {
      primary: '#18181B',
      secondary: '#3F3F46',
      muted: '#71717A',
      dim: '#A1A1AA',
      neon: '#E11D48',
    },
    isDark: false,
    soft: true,
    // Editorial: deliberately tight radii and a hairline accent edge.
    surface: { card: 8, button: 6, field: 6, icon: 6, accentWidth: 2 },
  },
};

export const themeOptions: { id: ThemeType; label: string; color: string }[] = [
  { id: 'lemberg', label: 'profile.themeLemberg', color: '#00FF6A' },
  { id: 'white', label: 'profile.themeWhite', color: '#064E3B' },
  { id: 'blue', label: 'profile.themeBlue', color: '#3B82F6' },
  { id: 'obsidian', label: 'profile.themeObsidian', color: '#8B5CF6' },
  { id: 'nova', label: 'profile.themeNova', color: '#00D68F' },
  { id: 'glass', label: 'profile.themeGlass', color: '#67E8F9' },
  { id: 'sorbet', label: 'profile.themeSorbet', color: '#FF6B4A' },
  { id: 'blade', label: 'profile.themeBlade', color: '#18181B' },
];
