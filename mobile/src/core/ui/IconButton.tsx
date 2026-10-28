import React from 'react';
import { Insets, Pressable, StyleProp, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Haptics } from '../utils/haptics';

export type IconButtonVariant = 'plain' | 'outlined' | 'filled' | 'danger';
export type IconButtonSize = 'sm' | 'md' | 'lg';

export interface IconButtonProps {
  icon: React.ReactElement<{ size?: number; color?: string }>;
  onPress: () => void;
  /**
   * Required. An icon-only control with no accessible name is invisible to a
   * screen reader — the audit found the contract-signing action was a bare
   * 40×40 circle with a `PenTool` glyph and nothing else.
   */
  accessibilityLabel: string;
  variant?: IconButtonVariant;
  size?: IconButtonSize;
  disabled?: boolean;
  /** Circular instead of the standard `radius.md` square. */
  round?: boolean;
  hapticStyle?: 'light' | 'medium' | 'heavy';
  /**
   * Extra reach beyond the box. Only pass it when the space around the control
   * belongs to nobody else — two adjacent targets with overlapping slop resolve
   * by z-order, so the nearer one steals the press.
   */
  hitSlop?: Insets | number;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * `md` is 48, not the 44 minimum: at exactly 44 the header's back and basket
 * controls were missed on the first tap often enough to be reported as broken,
 * because the eye aims at the 22pt glyph and a few points off its edge is
 * outside the target. `sm` is 36 visually and borrows 4pt of slop on every edge
 * to reach 44.
 */
const BOX: Record<IconButtonSize, number> = { sm: 36, md: 48, lg: 52 };
const GLYPH: Record<IconButtonSize, number> = { sm: 18, md: 22, lg: 24 };

/**
 * A single-icon action: back, close, share, expand, overflow.
 *
 * No icon action in the product is smaller than the 44pt minimum: `sm` reaches
 * it through `hitSlop`, the larger variants are 48 and 52 on their own.
 */
export function IconButton({
  icon,
  onPress,
  accessibilityLabel,
  variant = 'plain',
  size = 'md',
  disabled = false,
  round = false,
  hapticStyle = 'light',
  hitSlop,
  style,
  testID,
}: IconButtonProps) {
  const tokens = useDesignTokens();
  const c = tokens.colors;
  const box = BOX[size];

  const skin = (pressed: boolean) => {
    if (disabled) {
      return { background: 'transparent', border: 'transparent', content: c.text.disabled };
    }
    switch (variant) {
      case 'plain':
        return {
          background: pressed ? c.primarySubtle : 'transparent',
          border: 'transparent',
          content: c.text.primary,
        };
      case 'outlined':
        return {
          background: pressed ? c.primarySubtle : 'transparent',
          border: c.border,
          content: c.text.primary,
        };
      case 'filled':
        return {
          background: pressed ? c.primaryPressed : c.primary,
          border: 'transparent',
          content: c.text.onPrimary,
        };
      case 'danger':
        return {
          background: pressed ? c.status.danger.subtle : 'transparent',
          border: c.status.danger.border,
          content: c.status.danger.base,
        };
    }
  };

  return (
    <Pressable
      onPress={() => {
        if (disabled) return;
        const map = {
          light: Haptics.ImpactFeedbackStyle.Light,
          medium: Haptics.ImpactFeedbackStyle.Medium,
          heavy: Haptics.ImpactFeedbackStyle.Heavy,
        };
        Haptics.impactAsync(map[hapticStyle]);
        onPress();
      }}
      disabled={disabled}
      hitSlop={hitSlop ?? tokens.touchTarget.slopFor(box)}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ disabled }}
      testID={testID}
      style={({ pressed }) => {
        const s = skin(pressed && !disabled);
        return [
          {
            width: box,
            height: box,
            borderRadius: round ? tokens.radius.full : tokens.radius.md,
            alignItems: 'center' as const,
            justifyContent: 'center' as const,
            backgroundColor: s.background,
            borderWidth: s.border === 'transparent' ? 0 : 1,
            borderColor: s.border,
          },
          style,
        ];
      }}
    >
      {({ pressed }) =>
        React.cloneElement(icon, {
          size: GLYPH[size],
          color: skin(pressed && !disabled).content,
        })
      }
    </Pressable>
  );
}
