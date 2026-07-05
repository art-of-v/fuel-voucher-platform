import React from 'react';
import { View, StyleSheet, Dimensions } from 'react-native';
import Svg, { Defs, Rect, RadialGradient, Stop } from 'react-native-svg';
import { useDesignTokens } from '../lib/design-tokens';

const { width, height } = Dimensions.get('window');

interface GridBackgroundProps {
    color?: string;
}

export function GridBackground({
    color
}: GridBackgroundProps) {
    const tokens = useDesignTokens();
    const GRID_SIZE = 40;
    const horizontalLines = Math.ceil(height / GRID_SIZE);
    const verticalLines = Math.ceil(width / GRID_SIZE);
    const ACTIVE_COLOR = color || tokens.colors.primary;

    return (
        <View style={[styles.container, { backgroundColor: tokens.colors.background }]}>
            {/* NEON GLOW BACKGROUND LAYER */}
            <View style={StyleSheet.absoluteFill}>
                <NeonBackdrop themeTokens={tokens} color={ACTIVE_COLOR} />
            </View>

            {/* GRID LAYER */}
            <View style={styles.gridLayer}>
                {Array.from({ length: horizontalLines }).map((_, i) => (
                    <View
                        key={`h-${i}`}
                        style={[
                            styles.lineHorizontal,
                            {
                                top: i * GRID_SIZE,
                                backgroundColor: tokens.colors.isDark ? `${tokens.colors.primary}08` : 'rgba(0, 0, 0, 0.04)'
                            }
                        ]}
                    />
                ))}
                {Array.from({ length: verticalLines }).map((_, i) => (
                    <View
                        key={`v-${i}`}
                        style={[
                            styles.lineVertical,
                            {
                                left: i * GRID_SIZE,
                                backgroundColor: tokens.colors.isDark ? `${tokens.colors.primary}08` : 'rgba(0, 0, 0, 0.04)'
                            }
                        ]}
                    />
                ))}
            </View>

            {/* Watermark: decorative glow (lion watermark removed) */}
        </View>
    );
}

// Background Glow Component using Radial Gradient
const NeonBackdrop = ({ themeTokens, color }: { themeTokens: any, color: string }) => (
    <Svg height="100%" width="100%">
        <Defs>
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
                <Stop offset="0%" stopColor={color} stopOpacity={themeTokens.colors.isDark ? 0.03 : 0.01} />
                <Stop offset="100%" stopColor={color} stopOpacity={0} />
            </RadialGradient>

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
                <Stop offset="0%" stopColor={color} stopOpacity={themeTokens.colors.isDark ? 0.05 : 0.02} />
                <Stop offset="60%" stopColor={color} stopOpacity={0} />
            </RadialGradient>
        </Defs>
        <Rect x="0" y="0" width="100%" height="100%" fill={themeTokens.colors.background} />
        {/* Layered Lighting Effects */}
        <Rect x="0" y="0" width="100%" height="100%" fill="url(#topGlow)" />
        <Rect x="0" y="0" width="100%" height="100%" fill="url(#bottomGlow)" />
    </Svg>
);

const styles = StyleSheet.create({
    container: {
        ...StyleSheet.absoluteFillObject,
    },
    gridLayer: {
        ...StyleSheet.absoluteFillObject,
    },
    lineHorizontal: {
        position: 'absolute',
        left: 0,
        right: 0,
        height: 1,
    },
    lineVertical: {
        position: 'absolute',
        top: 0,
        bottom: 0,
        width: 1,
    },

});
