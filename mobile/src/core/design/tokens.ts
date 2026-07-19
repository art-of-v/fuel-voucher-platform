import { StyleSheet } from 'react-native';
import { themes, ThemeType } from './themes';

export type DesignTokens = ReturnType<typeof getTokens>;

export const baseTokens = {
  spacing: {
    containerPadding: 24,
    cardGap: 16,
    sectionGap: 40,
    hairline: StyleSheet.hairlineWidth,
  },
  typography: {
    allowFontScaling: false,
    fonts: {
      heading: 'Rajdhani-Bold',
      headingSemi: 'Rajdhani-SemiBold',
      headingReg: 'Rajdhani',
      body: 'Inter',
      bodyBold: 'Inter-Bold',
      bodyBlack: 'Inter-Black',
    },
    sizes: {
      h1: 42,
      h2: 28,
      h3: 22,
      body: 16,
      caption: 12,
      tiny: 10,
    },
    lineHeights: {
      h1: 44,
      h2: 30,
      h3: 24,
      body: 22,
      caption: 16,
    },
    letterSpacing: {
      tight: -0.5,
      tighter: -1.2,
      widest: 6,
      protocol: 8,
    },
  },
  effects: {
    blurIntensity: 80,
    radius: {
      xs: 2,
      sm: 4,
      md: 8,
      lg: 12,
      xl: 20,
    },
  },
};

export const BRAND_COLORS: Record<string, string> = {
  okko: '#16FF00',
  wog: '#008B45',
  upg: '#00C853',
  klo: '#FFCE00',
};

export function getTokens(themeType: ThemeType = 'lemberg') {
  const themeColors = themes[themeType] || themes.lemberg;

  return {
    ...baseTokens,
    colors: {
      ...themeColors,
      text: {
        ...themeColors.text,
        brand: { ...BRAND_COLORS },
      },
    },
    glows: {
      primary: {
        low: {
          shadowColor: themeColors.primary,
          shadowOffset: { width: 0, height: 0 },
          shadowOpacity: 0.6,
          shadowRadius: 5,
        },
        medium: {
          shadowColor: themeColors.primary,
          shadowOffset: { width: 0, height: 0 },
          shadowOpacity: 0.8,
          shadowRadius: 12,
        },
        high: {
          shadowColor: themeColors.primary,
          shadowOffset: { width: 0, height: 0 },
          shadowOpacity: 1,
          shadowRadius: 20,
        },
      },
      text: {
        high: [5, 15, 30],
      },
    },
  };
}

export const defaultTokens = getTokens('lemberg');
