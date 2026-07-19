import React, { useRef } from 'react';
import { Animated, Pressable, PressableProps, ViewStyle } from 'react-native';
import { Haptics } from '../utils/haptics';

interface PressableScaleProps extends PressableProps {
  scaleTo?: number;
  hapticFeedback?: boolean;
  hapticStyle?: 'light' | 'medium' | 'heavy';
  style?: ViewStyle;
  children: React.ReactNode;
}

export function PressableScale({
  scaleTo = 0.985,
  hapticFeedback = true,
  hapticStyle = 'light',
  style,
  children,
  onPressIn,
  onPressOut,
  ...rest
}: PressableScaleProps) {
  const scaleAnim = useRef(new Animated.Value(1)).current;

  const handlePressIn = (e: any) => {
    if (hapticFeedback) {
      const styleMap = { light: Haptics.ImpactFeedbackStyle.Light, medium: Haptics.ImpactFeedbackStyle.Medium, heavy: Haptics.ImpactFeedbackStyle.Heavy };
      Haptics.impactAsync(styleMap[hapticStyle]);
    }
    Animated.spring(scaleAnim, {
      toValue: scaleTo,
      useNativeDriver: true,
      friction: 10,
      tension: 50,
    }).start();
    onPressIn?.(e);
  };

  const handlePressOut = (e: any) => {
    Animated.spring(scaleAnim, {
      toValue: 1,
      useNativeDriver: true,
      friction: 3,
      tension: 100,
    }).start();
    onPressOut?.(e);
  };

  return (
    <Pressable
      onPressIn={handlePressIn}
      onPressOut={handlePressOut}
      {...rest}
    >
      <Animated.View style={[{ transform: [{ scale: scaleAnim }] }, style]}>
        {children}
      </Animated.View>
    </Pressable>
  );
}
