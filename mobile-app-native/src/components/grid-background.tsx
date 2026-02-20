import React from 'react';
import { View, StyleSheet, Dimensions, Image as RNImage } from 'react-native';
import Svg, { Defs, Mask, Image as SvgImage, Rect, RadialGradient, Stop } from 'react-native-svg';
import { tokens } from '../lib/design-tokens';

const { width, height } = Dimensions.get('window');

interface GridBackgroundProps {
    color?: string;
}

export function GridBackground({
    color = tokens.colors.primary
}: GridBackgroundProps) {
    const GRID_SIZE = 40;
    const horizontalLines = Math.ceil(height / GRID_SIZE);
    const verticalLines = Math.ceil(width / GRID_SIZE);

    return (
        <View style={styles.container}>
            {/* NEON GLOW BACKGROUND LAYER */}
            <View style={StyleSheet.absoluteFill}>
                <NeonBackdrop />
            </View>

            {/* GRID LAYER */}
            <View style={styles.gridLayer}>
                {Array.from({ length: horizontalLines }).map((_, i) => (
                    <View
                        key={`h-${i}`}
                        style={[styles.lineHorizontal, { top: i * GRID_SIZE }]}
                    />
                ))}
                {Array.from({ length: verticalLines }).map((_, i) => (
                    <View
                        key={`v-${i}`}
                        style={[styles.lineVertical, { left: i * GRID_SIZE }]}
                    />
                ))}
            </View>

            {/* Watermark Configuration: Dual Lions with SVG Masking */}
            <View style={styles.watermarkContainer}>
                {/* Large Center Lion */}
                <View style={[styles.watermarkCenter, { opacity: 0.22 }]}>
                    <LionMasked />
                </View>

                {/* Bottom Right Corner Lion */}
                <View style={[styles.watermarkCorner, { opacity: 0.12 }]}>
                    <LionMasked />
                </View>
            </View>
        </View>
    );
}

// Background Glow Component using Radial Gradient to match the screenshot's atmosphere
const NeonBackdrop = () => (
    <Svg height="100%" width="100%">
        <Defs>
            {/* Top Glow - Focused Ray Effect */}
            <RadialGradient
                id="topGlow"
                cx="50%"
                cy="-5%"
                rx="60%"
                ry="40%"
                fx="50%"
                fy="-5%"
                gradientUnits="userSpaceOnUse"
            >
                <Stop offset="0%" stopColor="#00FF80" stopOpacity={0.03} />
                <Stop offset="100%" stopColor="#00FF80" stopOpacity={0} />
            </RadialGradient>

            {/* Bottom Glow - Subtle Accent */}
            <RadialGradient
                id="bottomGlow"
                cx="50%"
                cy="105%"
                rx="70%"
                ry="30%"
                fx="50%"
                fy="105%"
                gradientUnits="userSpaceOnUse"
            >
                <Stop offset="0%" stopColor="#00FF80" stopOpacity={0.05} />
                <Stop offset="60%" stopColor="#00FF80" stopOpacity={0} />
            </RadialGradient>
        </Defs>
        {/* Main Background Color - Pure Black */}
        <Rect x="0" y="0" width="100%" height="100%" fill="#000000" />
        {/* Layered Lighting Effects */}
        <Rect x="0" y="0" width="100%" height="100%" fill="url(#topGlow)" />
        <Rect x="0" y="0" width="100%" height="100%" fill="url(#bottomGlow)" />
    </Svg>
);

// Helper Component for Masked Lion
const LionMasked = () => {
    // Resolve asset source to get the URI for the SVG Image href
    const lionSource = RNImage.resolveAssetSource(require('../../assets/original_lion_watermark.png'));

    return (
        <Svg height="100%" width="100%" viewBox="0 0 1000 1000">
            <Defs>
                <Mask id="lionMask" x="0" y="0" width="100%" height="100%">
                    <SvgImage
                        href={lionSource.uri}
                        x="0"
                        y="0"
                        width="100%"
                        height="100%"
                        preserveAspectRatio="xMidYMid slice"
                    />
                </Mask>
            </Defs>
            {/* Brighter neon-mint for better visibility on dark bg */}
            <Rect
                x="0"
                y="0"
                width="100%"
                height="100%"
                fill="#00FFA2"
                mask="url(#lionMask)"
            />
        </Svg>
    );
};

const styles = StyleSheet.create({
    container: {
        ...StyleSheet.absoluteFillObject,
        backgroundColor: '#000000',
    },
    gridLayer: {
        ...StyleSheet.absoluteFillObject,
    },
    lineHorizontal: {
        position: 'absolute',
        left: 0,
        right: 0,
        height: 1,
        backgroundColor: 'rgba(0, 255, 128, 0.03)',
    },
    lineVertical: {
        position: 'absolute',
        top: 0,
        bottom: 0,
        width: 1,
        backgroundColor: 'rgba(0, 255, 128, 0.03)',
    },
    watermarkContainer: {
        ...StyleSheet.absoluteFillObject,
        overflow: 'hidden',
    },
    watermarkCenter: {
        position: 'absolute',
        top: '18%',
        alignSelf: 'center',
        width: width * 0.9,
        height: width * 0.9,
    },
    watermarkCorner: {
        position: 'absolute',
        bottom: -60,
        right: -40,
        width: width * 0.75,
        height: width * 0.75,
        transform: [{ rotate: '-10deg' }]
    },
});
