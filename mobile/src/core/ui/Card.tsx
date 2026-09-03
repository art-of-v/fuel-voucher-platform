import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { PressableScale } from './PressableScale';

export type CardTone = 'default' | 'sunken' | 'elevated';

export interface CardProps {
  children: React.ReactNode;
  /** Makes the card a single tappable surface. */
  onPress?: () => void;
  onLongPress?: () => void;
  /**
   * Selected state: brand-tinted border and a faint brand wash. Selection is a
   * *border* change, not a fill change, so a selected card in a list of five
   * still reads as the same kind of object.
   */
  selected?: boolean;
  disabled?: boolean;
  /**
   * A status accent along the leading edge. Use for objects whose state matters
   * at a glance (an unpaid order, a spent voucher). Replaces the per-component
   * `accentBar` implementations, which ranged from 3px to 5px.
   */
  accent?: 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'primary';
  /** `default` = surface + hairline. `elevated` = shadow, for floating cards. */
  tone?: CardTone;
  /** Padding preset. `none` when the card owns a full-bleed image or list. */
  padding?: 'none' | 'sm' | 'md' | 'lg';
  style?: StyleProp<ViewStyle>;
  accessibilityLabel?: string;
  testID?: string;
}

/**
 * The container primitive.
 *
 * Replaces the per-screen card implementations (`StationCard`, `FuelCard`,
 * `PackageCard`, `OrderCard`, the standalone `VoucherCard` that lived
 * alongside `OrderCard`, and the inline card inside `my-codes.tsx`) whose
 * radii were 2, 4, 8, 12, 20, 22 and 28 and whose differentiation strategies
 * were variously border-only, border+shadow, border+shadow+glow, and a
 * `MeshBackground` gradient.
 *
 * Differentiation here follows one rule (`layout.ts` § elevation): a content
 * surface uses **colour + hairline border, no shadow**. Only `tone="elevated"`
 * adds a shadow, and then it drops the border.
 *
 * States: default · pressed (scale + tint, via `PressableScale`) · selected
 * (accent border + wash) · disabled (reduced opacity, press suppressed).
 */
export function Card({
  children,
  onPress,
  onLongPress,
  selected = false,
  disabled = false,
  accent,
  tone = 'default',
  padding = 'md',
  style,
  accessibilityLabel,
  testID,
}: CardProps) {
  const tokens = useDesignTokens();
  const c = tokens.colors;

  const pad =
    padding === 'none'
      ? 0
      : padding === 'sm'
        ? tokens.spacing.md
        : padding === 'lg'
          ? tokens.spacing.xl
          : tokens.spacing.lg;

  const accentColor = accent
    ? accent === 'primary'
      ? c.primary
      : c.status[accent].base
    : undefined;

  const base: ViewStyle = {
    backgroundColor: tone === 'elevated' ? c.surfaceElevated : tone === 'sunken' ? c.surfaceSunken : c.surface,
    borderRadius: tokens.radius.lg,
    padding: pad,
    // Border OR shadow, never both.
    ...(tone === 'elevated'
      ? tokens.elevation.low
      : { borderWidth: 1, borderColor: selected ? c.borderAccent : c.border }),
    ...(selected ? { backgroundColor: c.primarySubtle } : null),
    ...(accentColor ? { borderLeftWidth: tokens.surface.accentWidth, borderLeftColor: accentColor } : null),
    ...(disabled ? { opacity: 0.5 } : null),
    overflow: 'hidden',
  };

  if (!onPress && !onLongPress) {
    return (
      <View style={[base, style]} testID={testID}>
        {children}
      </View>
    );
  }

  return (
    <PressableScale
      onPress={disabled ? undefined : onPress}
      onLongPress={disabled ? undefined : onLongPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ disabled, selected }}
      testID={testID}
      style={[base, style]}
      pressedStyle={{ backgroundColor: c.primarySubtle }}
    >
      {children}
    </PressableScale>
  );
}
