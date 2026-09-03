import React, { useRef, useState } from 'react';
import { Animated, Pressable, PressableProps, StyleProp, ViewStyle } from 'react-native';
import { Haptics } from '../utils/haptics';
import { motion } from '../design/layout';

export interface PressableScaleProps extends Omit<PressableProps, 'style'> {
  /** Scale reached at full press. Defaults to `motion.pressScale` (0.985). */
  scaleTo?: number;
  hapticFeedback?: boolean;
  hapticStyle?: 'light' | 'medium' | 'heavy';
  style?: StyleProp<ViewStyle>;
  /** Applied additively while pressed — use for a tint or border change. */
  pressedStyle?: StyleProp<ViewStyle>;
  children: React.ReactNode;
}

/**
 * The single press primitive for surfaces (cards, rows, tiles).
 *
 * Retained from before Phase 2 — it was already correct, it just was not used:
 * it was imported by `FuelCard` and never rendered, while screens hand-rolled
 * `Animated.timing` tilts of `3deg` on one screen and `8deg` on the next.
 *
 * Press feedback in this system is **scale plus an optional tint**. No rotation,
 * no translation, no shadow change. One physics for one gesture.
 *
 * Controls with a fixed height (`Button`, `IconButton`) use opacity/colour
 * instead of scale, because scaling a 56pt bar is visible as a layout wobble.
 */
export function PressableScale({
  scaleTo = motion.pressScale,
  hapticFeedback = true,
  hapticStyle = 'light',
  style,
  pressedStyle,
  children,
  onPressIn,
  onPressOut,
  disabled,
  ...rest
}: PressableScaleProps) {
  const scaleAnim = useRef(new Animated.Value(1)).current;
  const [pressed, setPressed] = useState(false);

  const handlePressIn = (e: any) => {
    onPressIn?.(e);
    if (disabled) return;
    setPressed(true);
    if (hapticFeedback) {
      const styleMap = {
        light: Haptics.ImpactFeedbackStyle.Light,
        medium: Haptics.ImpactFeedbackStyle.Medium,
        heavy: Haptics.ImpactFeedbackStyle.Heavy,
      };
      Haptics.impactAsync(styleMap[hapticStyle]);
    }
    Animated.spring(scaleAnim, {
      toValue: scaleTo,
      useNativeDriver: true,
      friction: motion.spring.friction,
      tension: motion.spring.tension,
    }).start();
  };

  const handlePressOut = (e: any) => {
    onPressOut?.(e);
    setPressed(false);
    Animated.spring(scaleAnim, {
      toValue: 1,
      useNativeDriver: true,
      friction: motion.spring.friction,
      tension: motion.spring.tension,
    }).start();
  };

  return (
    <Pressable
      onPressIn={handlePressIn}
      onPressOut={handlePressOut}
      disabled={disabled}
      {...rest}
    >
      <Animated.View
        style={[{ transform: [{ scale: scaleAnim }] }, style, pressed && pressedStyle]}
      >
        {children}
      </Animated.View>
    </Pressable>
  );
}
