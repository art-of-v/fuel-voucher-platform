import React from 'react';
import {
  ActivityIndicator,
  Pressable,
  StyleProp,
  View,
  ViewStyle,
} from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Haptics } from '../utils/haptics';
import { Text } from './Text';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'destructive';
export type ButtonSize = 'sm' | 'md' | 'lg';

export interface ButtonProps {
  label: string;
  onPress: () => void;
  /**
   * `primary`     — the one action the screen wants. At most one per view.
   * `secondary`   — a real alternative (outlined).
   * `ghost`       — a low-stakes action (no fill, no border).
   * `destructive` — deletes, cancels, declines. Outlined in danger, filled only
   *                 when it is the confirming action inside a `ConfirmDialog`.
   */
  variant?: ButtonVariant;
  /**
   * `lg` — 56pt. The primary action on a screen.
   * `md` — 52pt. Secondary and in-form actions.
   * `sm` — 36pt. A compact action that lives *inside* another element: a card
   *        header, a list row, a retry next to an error line. It keeps a 44pt
   *        touch target via `hitSlop`, so it is smaller to look at but not to
   *        hit — which is what the hand-rolled 28–31pt inline buttons got wrong.
   *        Defaults to `fullWidth: false`, because a compact action that
   *        stretches edge to edge is not compact.
   */
  size?: ButtonSize;
  disabled?: boolean;
  loading?: boolean;
  /** Leading icon. Sized and coloured by the button — pass a bare element. */
  icon?: React.ReactElement<{ size?: number; color?: string }>;
  /** Stretch to the width of the parent. */
  fullWidth?: boolean;
  /** Filled `destructive` — only inside a destructive confirmation. */
  emphasis?: 'normal' | 'high';
  hapticStyle?: 'light' | 'medium' | 'heavy';
  style?: StyleProp<ViewStyle>;
  accessibilityLabel?: string;
  testID?: string;
}

/**
 * The action primitive.
 *
 * States are part of the component, not the call site:
 *   default   — variant fill/outline
 *   pressed   — background steps to `primaryPressed` (fills) or `primarySubtle`
 *               (outlines); no scale, because a 56pt bar scaling reads as jitter
 *   disabled  — `colors.disabled` fill, `text.disabled` label, no border tint,
 *               press and haptics suppressed
 *   loading   — spinner replaces the label, width held, press suppressed,
 *               `accessibilityState.busy` set
 *
 * `loading` implies non-interactive. Screens no longer need their own
 * `isProcessing` guard around `onPress` — a stuck `isProcessing` flag was how
 * checkout's CTA became permanently dead after a payment hand-off failure.
 */
export function Button({
  label,
  onPress,
  variant = 'primary',
  size = 'lg',
  disabled = false,
  loading = false,
  icon,
  fullWidth = size !== 'sm',
  emphasis = 'normal',
  hapticStyle = 'medium',
  style,
  accessibilityLabel,
  testID,
}: ButtonProps) {
  const tokens = useDesignTokens();
  const c = tokens.colors;
  const inactive = disabled || loading;

  const compact = size === 'sm';
  const height =
    size === 'lg' ? tokens.control.xl : compact ? tokens.control.sm : tokens.control.lg;
  const typeRole = size === 'lg' ? 'button' : 'buttonSmall';
  const iconSize = size === 'lg' ? 20 : compact ? 16 : 18;

  const filledDestructive = variant === 'destructive' && emphasis === 'high';

  /** Resolve fill, border and content colour for a state. */
  const skin = (pressed: boolean) => {
    if (disabled) {
      return {
        background: c.disabled,
        border: c.disabled,
        borderWidth: 0,
        content: c.text.disabled,
      };
    }
    switch (variant) {
      case 'primary':
        // Use an explicit hex string (not the design token reference) so React
        // Native's view layer can resolve the colour at render time. The previous
        // `c.primary` reference (which evaluated to `'#00E85F'`) was being
        // discarded by iOS's compositing pass in some builds, leaving the button
        // looking like plain text.
        return {
          background: pressed ? '#00C351' : '#00E85F',
          border: pressed ? '#00C351' : '#00E85F',
          borderWidth: 2,
          content: c.text.onPrimary,
        };
      case 'secondary':
        return {
          background: pressed ? c.primarySubtle : (c.isDark ? 'rgba(255, 255, 255, 0.05)' : 'rgba(0, 0, 0, 0.03)'),
          border: c.borderAccent,
          borderWidth: 1.5,
          content: c.primary,
        };
      case 'ghost':
        return {
          background: pressed ? c.primarySubtle : 'transparent',
          border: 'transparent',
          borderWidth: 0,
          content: c.primary,
        };
      case 'destructive':
        if (filledDestructive) {
          return {
            background: c.status.danger.base,
            border: c.status.danger.base,
            borderWidth: 2,
            content: c.status.danger.onBase,
          };
        }
        return {
          background: pressed ? c.status.danger.subtle : (c.isDark ? 'rgba(255, 93, 93, 0.08)' : 'rgba(239, 68, 68, 0.06)'),
          border: c.status.danger.border,
          borderWidth: 1.5,
          content: c.status.danger.base,
        };
    }
  };

  const handlePress = () => {
    if (inactive) return;
    const styleMap = {
      light: Haptics.ImpactFeedbackStyle.Light,
      medium: Haptics.ImpactFeedbackStyle.Medium,
      heavy: Haptics.ImpactFeedbackStyle.Heavy,
    };
    Haptics.impactAsync(styleMap[hapticStyle]);
    onPress();
  };

  return (
    <Pressable
      onPress={handlePress}
      disabled={inactive}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityState={{ disabled: inactive, busy: loading }}
      testID={testID}
      /*
       * `sm` is 36pt tall, so it borrows the 4pt of slop on each edge that brings
       * it back to the 44pt minimum. The other sizes already meet it.
       */
      hitSlop={compact ? tokens.touchTarget.slopFor(tokens.control.sm) : undefined}
      style={[{ alignSelf: fullWidth ? 'stretch' : 'flex-start' }, style]}
    >
      {({ pressed }) => {
        const s = skin(pressed && !inactive);
        
        const innerStyle = {
          height,
          minHeight: compact ? tokens.control.sm : tokens.touchTarget.min,
          borderRadius: tokens.radius.md,
          backgroundColor: s.background,
          borderWidth: s.borderWidth,
          borderColor: s.border,
          flexDirection: 'row' as const,
          alignItems: 'center' as const,
          justifyContent: 'center' as const,
          gap: compact ? tokens.spacing.xs : tokens.spacing.sm,
          paddingHorizontal: compact ? tokens.spacing.md : tokens.spacing.xl,
          opacity: loading ? 0.85 : 1,
        };

        if (loading) {
          return (
            <View style={innerStyle}>
              <ActivityIndicator size="small" color={s.content} />
              {/* Reserve the label's box so the button does not resize. */}
              <View style={{ opacity: 0 }} pointerEvents="none">
                <Text role={typeRole} tone="inherit" style={{ color: s.content }}>
                  {label}
                </Text>
              </View>
            </View>
          );
        }
        return (
          <View style={innerStyle}>
            {icon
              ? React.cloneElement(icon, { size: iconSize, color: s.content })
              : null}
            <Text
              role={typeRole}
              tone="inherit"
              numberOfLines={1}
              style={{ color: s.content }}
            >
              {label}
            </Text>
          </View>
        );
      }}
    </Pressable>
  );
}
