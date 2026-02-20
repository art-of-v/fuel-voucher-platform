import React from 'react';
import { View, StyleSheet, Dimensions, Image as RNImage } from 'react-native';
import Svg, { Defs, Mask, Image as SvgImage, Rect } from 'react-native-svg';
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
                <View style={[styles.watermarkCenter, { opacity: 0.15 }]}>
                    <LionMasked />
                </View>

                {/* Bottom Right Corner Lion */}
                <View style={[styles.watermarkCorner, { opacity: 0.08 }]}>
                    <LionMasked />
                </View>
            </View>
        </View>
    );
}

// Helper Component for Masked Lion
const LionMasked = () => {
    // Resolve asset source to get the URI for the SVG Image href
    const lionSource = RNImage.resolveAssetSource(require('../../assets/original_lion_watermark.png'));

    return (
        <Svg height="100%" width="100%" viewBox="0 0 1000 1000">
            <Defs>
                <Mask id="lionMask" x="0" y="0" width="100%" height="100%">
                    {/* The image itself acts as the mask. 
                        Lighter pixels (neon lines) = Opaque.
                        Darker pixels (black bg) = Transparent. */}
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
            {/* The fill color that will take the shape of the lion */}
            <Rect
                x="0"
                y="0"
                width="100%"
                height="100%"
                fill="#FFFFFF"
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
        backgroundColor: 'rgba(0, 255, 106, 0.03)',
    },
    lineVertical: {
        position: 'absolute',
        top: 0,
        bottom: 0,
        width: 1,
        backgroundColor: 'rgba(0, 255, 106, 0.03)',
    },
    watermarkContainer: {
        ...StyleSheet.absoluteFillObject,
        overflow: 'hidden',
    },
    watermarkCenter: {
        position: 'absolute',
        top: '15%',
        alignSelf: 'center',
        width: width * 0.9,
        height: width * 0.9,
    },
    watermarkCorner: {
        position: 'absolute',
        bottom: -50,
        right: -50,
        width: width * 0.7,
        height: width * 0.7,
        transform: [{ rotate: '-15deg' }]
    },
});
