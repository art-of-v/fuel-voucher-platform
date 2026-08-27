import React from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextStyle, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Haptics } from '../utils/haptics';

interface ButtonProps {
  title: string;
  onPress: () => void;
  /** primary = filled brand surface, secondary = tinted surface with border */
  variant?: 'primary' | 'secondary';
  /** override the theme primary (e.g. brand color) */
  color?: string;
  height?: number;
  loading?: boolean;
  disabled?: boolean;
  /** pre-rendered icon shown before the title (caller picks the color) */
  icon?: React.ReactNode;
  style?: ViewStyle;
  textStyle?: TextStyle;
  hapticStyle?: 'light' | 'medium' | 'heavy';
}

/**
 * Shared CTA button. Soft radius, tinted press state, haptic on press —
 * replaces the ad-hoc Pressable buttons scattered across screens.
 */
export function Button({
  title,
  onPress,
  variant = 'primary',
  color,
  height = 56,
  loading = false,
  disabled = false,
  icon,
  style,
  textStyle,
  hapticStyle = 'medium',
}: ButtonProps) {
  const tokens = useDesignTokens();
  const bg = color || tokens.colors.primary || '#064E3B';
  const filledFg = tokens.colors.isDark ? '#FFF' : '#000';
  const isBlocked = disabled || loading;

  const fg = variant === 'primary' ? filledFg : tokens.colors.primary;

  return (
    <Pressable
      onPress={() => {
        const styleMap = {
          light: Haptics.ImpactFeedbackStyle.Light,
          medium: Haptics.ImpactFeedbackStyle.Medium,
          heavy: Haptics.ImpactFeedbackStyle.Heavy,
        };
        Haptics.impactAsync(styleMap[hapticStyle]);
        onPress();
      }}
      disabled={isBlocked}
      style={({ pressed }) => [
        styles.base,
        {
          height,
          // Soft themes get the modern radius; legacy themes keep the sharp edge.
          borderRadius: tokens.surface.soft ? tokens.surface.button : 4,
          backgroundColor: variant === 'primary' ? bg : tokens.colors.primaryDim,
        },
        variant === 'secondary' && {
          borderWidth: 1,
          borderColor: `${bg}44`,
        },
        pressed && !isBlocked && styles.pressed,
        isBlocked && styles.blocked,
        style,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={fg} />
      ) : (
        <>
          {icon}
          <Text
            allowFontScaling={false}
            style={[styles.label, { color: fg }, textStyle]}
          >
            {title}
          </Text>
        </>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    width: '100%',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
  },
  label: {
    fontFamily: 'Inter-Black',
    fontSize: 16,
    letterSpacing: 1,
    textTransform: 'uppercase',
  },
  pressed: {
    opacity: 0.85,
    transform: [{ scale: 0.98 }],
  },
  blocked: {
    opacity: 0.5,
  },
});
