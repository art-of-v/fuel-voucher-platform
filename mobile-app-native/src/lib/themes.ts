export type ThemeType = 'lemberg' | 'white' | 'blue';

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
        border: 'rgba(0, 255, 106, 0.15)',
        borderLight: 'rgba(255, 255, 255, 0.05)',
        error: '#FF4B4B',
        text: {
            primary: '#FFFFFF',
            secondary: 'rgba(255, 255, 255, 0.7)',
            muted: 'rgba(255, 255, 255, 0.45)',
            dim: 'rgba(255, 255, 255, 0.25)',
            neon: '#16FF00',
        },
        isDark: true,
    },
    white: {
        background: '#FFFFFF',
        primary: '#059669', // Emerald 600
        primaryDim: 'rgba(5, 150, 105, 0.08)',
        primaryGlow: 'rgba(5, 150, 105, 0.15)',
        accent: '#2563EB', // Blue 600
        card: '#F9FAFB',
        border: 'rgba(0, 0, 0, 0.08)',
        borderLight: 'rgba(0, 0, 0, 0.04)',
        error: '#DC2626',
        text: {
            primary: '#111827', // Dark Gray 900
            secondary: '#374151', // Dark Gray 700 - Better contrast
            muted: '#4B5563', // Medium Gray 600 - Readable
            dim: '#6B7280', // Gray 500 - Visible but subtle
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
        border: 'rgba(255, 255, 255, 0.1)',
        borderLight: 'rgba(255, 255, 255, 0.05)',
        error: '#EF4444',
        text: {
            primary: '#F8FAFC',
            secondary: '#CBD5E1',
            muted: '#94A3B8', // Increased from 64748B
            dim: '#64748B', // Increased from 475569
            neon: '#60A5FA',
        },
        isDark: true,
    }
};

export const themeOptions: { id: ThemeType; label: string; color: string }[] = [
    { id: 'lemberg', label: 'profile.themeLemberg', color: '#00FF6A' },
    { id: 'white', label: 'profile.themeWhite', color: '#059669' },
    { id: 'blue', label: 'profile.themeBlue', color: '#3B82F6' },
];
