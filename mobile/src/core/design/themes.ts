export type ThemeType = 'lemberg' | 'white' | 'blue' | 'obsidian';

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
  },
};

export const themeOptions: { id: ThemeType; label: string; color: string }[] = [
  { id: 'lemberg', label: 'profile.themeLemberg', color: '#00FF6A' },
  { id: 'white', label: 'profile.themeWhite', color: '#064E3B' },
  { id: 'blue', label: 'profile.themeBlue', color: '#3B82F6' },
  { id: 'obsidian', label: 'profile.themeObsidian', color: '#8B5CF6' },
];
