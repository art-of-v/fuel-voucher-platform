import { StyleSheet, Platform } from 'react-native';

export const tokens = {
    colors: {
        background: '#000000', // Absolute Black for maximum contrast
        primary: '#00FF6A', // Original Lemberg Minty Neon Green
        primaryDim: 'rgba(0, 255, 106, 0.1)',
        primaryGlow: 'rgba(0, 255, 106, 0.4)',
        accent: '#00FFFF',
        card: '#0A0A0A', // Darker cards for depth
        border: 'rgba(0, 255, 106, 0.15)', // Neon-tinted borders
        borderLight: 'rgba(255, 255, 255, 0.05)',
        text: {
            primary: '#FFFFFF',
            secondary: 'rgba(255, 255, 255, 0.7)',
            muted: 'rgba(255, 255, 255, 0.45)',
            dim: 'rgba(255, 255, 255, 0.25)',
            neon: '#16FF00',
            brand: {
                okko: '#16FF00', // Real OKKO Neon Green (Yellowish)
                wog: '#008B45', // Real WOG Dark Green (Forest)
                upg: '#00C853', // Racing Green (UPG Black/Green aesthetic)
                klo: '#FFCE00', // Real KLO Yellow (High Contrast)
            }
        }
    },
    spacing: {
        containerPadding: 24,
        cardGap: 16, // Tighter rhythm
        sectionGap: 40,
        hairline: StyleSheet.hairlineWidth,
    },
    typography: {
        fontConfig: {
            allowFontScaling: false,
        },
        fonts: {
            heading: 'Rajdhani-Bold',
            headingSemi: 'Rajdhani-SemiBold',
            headingReg: 'Rajdhani',
            body: 'Inter',
            bodyBold: 'Inter-Bold',
            bodyBlack: 'Inter-Black',
        },
        sizes: {
            h1: 42, // Larger, more aggressive headers
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
            protocol: 8, // More technical spacing
        }
    },
    glows: {
        // Multi-layer deterministic glow physics
        primary: {
            low: {
                shadowColor: '#00FF6A',
                shadowOffset: { width: 0, height: 0 },
                shadowOpacity: 0.6,
                shadowRadius: 5,
            },
            medium: {
                shadowColor: '#00FF6A',
                shadowOffset: { width: 0, height: 0 },
                shadowOpacity: 0.8,
                shadowRadius: 12,
            },
            high: {
                shadowColor: '#00FF6A',
                shadowOffset: { width: 0, height: 0 },
                shadowOpacity: 1,
                shadowRadius: 20,
            }
        },
        text: {
            high: [5, 15, 30], // Radii for layered text shadows
        }
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
    }
};
