import React, { useEffect, useRef } from 'react';
import { Animated, Text, View, StyleSheet, TextStyle, StyleProp } from 'react-native';
import { useDesignTokens } from '@/core/hooks/useTheme';

interface GlowTextProps {
    children: React.ReactNode;
    style?: StyleProp<TextStyle>;
    color?: string;
    glowColor?: string;
    intensity?: 'none' | 'low' | 'medium' | 'high';
    align?: 'left' | 'center' | 'right';
    animation?: 'none' | 'flicker' | 'pulse';
    animatedValue?: Animated.Value;
}

export function GlowText({
    children,
    style,
    color,
    glowColor,
    intensity = 'medium',
    align = 'center',
    animation = 'none',
    animatedValue
}: GlowTextProps) {
    const tokens = useDesignTokens();
    const internalOpacityAnim = useRef(new Animated.Value(1)).current;
    const opacityAnim = animatedValue || internalOpacityAnim;

    const ACTIVE_COLOR = color || tokens.colors.primary;
    const ACTIVE_GLOW = glowColor || tokens.colors.primary;

    useEffect(() => {
        if (!animatedValue && animation === 'flicker') {
            const flickerAnimation = () => {
                Animated.sequence([
                    Animated.timing(opacityAnim, { toValue: 1, duration: 1500, useNativeDriver: true }),
                    Animated.timing(opacityAnim, { toValue: 0.8, duration: 40, useNativeDriver: true }),
                    Animated.timing(opacityAnim, { toValue: 1, duration: 40, useNativeDriver: true }),
                    Animated.timing(opacityAnim, { toValue: 0.8, duration: 40, useNativeDriver: true }),
                    Animated.timing(opacityAnim, { toValue: 1, duration: 40, useNativeDriver: true }),
                    Animated.timing(opacityAnim, { toValue: 1, duration: 1340, useNativeDriver: true }),
                ]).start(() => flickerAnimation());
            };
            flickerAnimation();
        } else if (!animatedValue && animation === 'pulse') {
            Animated.loop(
                Animated.sequence([
                    Animated.timing(opacityAnim, { toValue: 0.6, duration: 2000, useNativeDriver: true }),
                    Animated.timing(opacityAnim, { toValue: 1, duration: 2000, useNativeDriver: true }),
                ])
            ).start();
        }
    }, [animation, animatedValue]);

    const shadowRads: Record<string, number[]> = {
        none: [],
        low: [5],
        medium: [8, 15],
        high: tokens.glows.text.high
    };

    const currentShadows = shadowRads[intensity] || [];

    return (
        <View style={{ alignItems: align === 'center' ? 'center' : (align === 'left' ? 'flex-start' : 'flex-end'), paddingHorizontal: 10 }}>
            <Animated.View style={{ opacity: opacityAnim }}>
                <View style={styles.container}>
                    {/* Background Glow Layers */}
                    {currentShadows.map((radius: number, idx: number) => (
                        <Text
                            key={idx}
                            allowFontScaling={false}
                            style={[
                                style,
                                styles.absolute,
                                {
                                    color: ACTIVE_GLOW,
                                    textShadowColor: ACTIVE_GLOW,
                                    textShadowOffset: { width: 0, height: 0 },
                                    textShadowRadius: radius,
                                    opacity: (0.8 / currentShadows.length),
                                    textAlign: align
                                }
                            ]}
                        >
                            {children}
                        </Text>
                    ))}

                    {/* Main Sharp Text */}
                    <Text
                        allowFontScaling={false}
                        style={[
                            style,
                            {
                                color: ACTIVE_COLOR,
                                textAlign: align,
                                textShadowColor: intensity === 'none' ? 'transparent' : ACTIVE_GLOW,
                                textShadowRadius: currentShadows.length > 0 ? (currentShadows[0] / 2) : 0,
                                textShadowOffset: { width: 0, height: 0 },
                            }
                        ]}
                    >
                        {children}
                    </Text>
                </View>
            </Animated.View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        position: 'relative',
    },
    absolute: {
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
    }
});
