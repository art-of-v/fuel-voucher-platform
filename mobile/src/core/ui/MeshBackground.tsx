import React from 'react';
import { View, StyleSheet } from 'react-native';
import Svg, { Rect, Defs, Pattern, Path, LinearGradient, Stop, RadialGradient } from 'react-native-svg';
import { useDesignTokens } from '../hooks/useTheme';

interface MeshBackgroundProps {
  color?: string;
  intensity?: number;
  variant?: 'honeycomb' | 'hexagon';
}

export function MeshBackground({
  color,
  intensity = 0.08,
  variant = 'hexagon',
}: MeshBackgroundProps) {
  const tokens = useDesignTokens();
  const meshColor = color || tokens.colors.primary;

  const isHexagon = variant === 'hexagon';

  return (
    <View style={StyleSheet.absoluteFill}>
      <Svg height="100%" width="100%">
        <Defs>
          <Pattern
            id="mesh-pattern"
            patternUnits="userSpaceOnUse"
            width={isHexagon ? '34.64' : '20'}
            height={isHexagon ? '30' : '34'}
            viewBox={isHexagon ? '0 0 34.64 30' : '0 0 20 34'}
          >
            {isHexagon ? (
              <Path
                d="M8.66 0 L25.98 0 L34.64 15 L25.98 30 L8.66 30 L0 15 Z"
                fill="transparent"
                stroke={meshColor}
                strokeWidth="0.5"
                opacity={intensity}
              />
            ) : (
              <Path
                d="M10 0 L20 5.8 L20 17.4 L10 23.2 L0 17.4 L0 5.8 Z M10 34 L20 28.2 L20 11.6 L10 17.4 L0 11.6 L0 28.2 Z"
                fill="transparent"
                stroke={meshColor}
                strokeWidth="0.5"
                opacity={intensity}
              />
            )}
          </Pattern>

          <LinearGradient id="rim-light" x1="0" y1="0" x2="1" y2="1">
            <Stop offset="0" stopColor={meshColor} stopOpacity="0.1" />
            <Stop offset="0.5" stopColor="transparent" stopOpacity="0" />
            <Stop offset="1" stopColor={meshColor} stopOpacity="0.05" />
          </LinearGradient>

          <RadialGradient
            id="gloss"
            cx="20%"
            cy="20%"
            r="50%"
          >
            <Stop offset="0" stopColor="#FFF" stopOpacity="0.05" />
            <Stop offset="1" stopColor="transparent" stopOpacity="0" />
          </RadialGradient>
        </Defs>

        <Rect width="100%" height="100%" fill="url(#mesh-pattern)" />
        <Rect width="100%" height="100%" fill="url(#rim-light)" />
        <Rect width="100%" height="100%" fill="url(#gloss)" />
      </Svg>
    </View>
  );
}

