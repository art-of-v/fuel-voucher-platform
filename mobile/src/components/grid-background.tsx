import React from 'react';
import { View, StyleSheet, Dimensions } from 'react-native';
import Svg, { Defs, Rect, RadialGradient, Stop } from 'react-native-svg';
import { useDesignTokens } from '../core/hooks/useTheme';

const { width, height } = Dimensions.get('window');

interface GridBackgroundProps {
    color?: string;
}

/**
 * @deprecated A decorative backdrop: a neon radial glow plus a 40px grid.
 *
 * The design direction for the redesign rules out decorative grids and glows —
 * they compete with content rather than clarifying hierarchy, and this one sits
 * behind screens about money and signed contracts. `core/ui/PageLayout` therefore
 * has no default background; a background is opt-in per screen.
 *
 * It is still live because the deprecated `components/page-layout` shim keeps it
 * as its default, plus four explicit call sites (`checkout`, `landing`,
 * `my-codes`, `report`). Removing it changes the appearance of the checkout and
 * the wallet, which is Phase 3 — so it is deprecated in place, not deleted.
 */
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

            {/*
              GRID LAYER

              The two line colours below are deliberately raw alphas and not tokens.
              The nearest semantic role is `borderSubtle`, but that is an opaque
              hairline meant to be *seen* (`#EDECE7` on the light themes); at grid
              density it would turn a faint texture into graph paper. A ~3% wash is
              the value this decoration needs, and there is no token for "barely
              perceptible texture" because nothing else in the design system wants
              one. See the deprecation note on this component: the grid itself is on
              its way out.
            */}
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
