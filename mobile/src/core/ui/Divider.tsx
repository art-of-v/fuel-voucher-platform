import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';

export interface DividerProps {
  /** Inset the rule from both edges by the standard container padding. */
  inset?: boolean;
  /** `subtle` for rules inside an already-bordered container. */
  tone?: 'default' | 'subtle';
  /** Vertical rule instead of horizontal. */
  vertical?: boolean;
  style?: StyleProp<ViewStyle>;
}

/**
 * A hairline rule.
 *
 * Small, but it earns its place: the audit found separators implemented as
 * `borderBottomWidth: 1` on the row above, `height: 1` `View`s, `borderTopWidth`
 * on the element below, and one `height: StyleSheet.hairlineWidth` — four
 * mechanisms producing four different weights within one screen.
 */
export function Divider({ inset = false, tone = 'default', vertical = false, style }: DividerProps) {
  const tokens = useDesignTokens();
  const color = tone === 'subtle' ? tokens.colors.borderSubtle : tokens.colors.border;

  return (
    <View
      accessibilityRole="none"
      style={[
        vertical
          ? { width: 1, alignSelf: 'stretch', backgroundColor: color }
          : {
              height: 1,
              backgroundColor: color,
              marginHorizontal: inset ? tokens.spacing.containerPadding : 0,
            },
        style,
      ]}
    />
  );
}
