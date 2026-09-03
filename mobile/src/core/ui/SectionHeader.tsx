import React from 'react';
import { Pressable, StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Text } from './Text';

export interface SectionHeaderProps {
  title: string;
  /** A count, a total, or a one-line explanation of the group. */
  subtitle?: string;
  /** Right-aligned action — "See all", "Edit", a sort control. */
  action?: { label: string; onPress: () => void };
  /** Leading icon. Use sparingly; the title should carry the meaning. */
  icon?: React.ReactElement<{ size?: number; color?: string }>;
  style?: StyleProp<ViewStyle>;
}

/**
 * Groups content within a screen.
 *
 * Replaces the audit's "three arrangements of icon/rule/label within one screen"
 * on `/my-codes` and the `fontSize: 12, letterSpacing: 4` treatment on `/report`.
 * There is exactly one arrangement: title on the left, optional action on the
 * right, no decorative rule.
 *
 * The rule was removed deliberately — the group's own `Card` borders already
 * bound it, so a rule above them was a third boundary for the same edge.
 */
export function SectionHeader({ title, subtitle, action, icon, style }: SectionHeaderProps) {
  const tokens = useDesignTokens();

  return (
    <View
      accessibilityRole="header"
      style={[
        {
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: tokens.spacing.md,
          paddingBottom: tokens.spacing.md,
        },
        style,
      ]}
    >
      <View style={{ flex: 1, flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.sm }}>
        {icon ? React.cloneElement(icon, { size: 16, color: tokens.colors.text.muted }) : null}
        <View style={{ flex: 1, gap: 2 }}>
          <Text role="sectionTitle" tone="muted" numberOfLines={1}>
            {title}
          </Text>
          {subtitle ? (
            <Text role="secondary" tone="muted" numberOfLines={2}>
              {subtitle}
            </Text>
          ) : null}
        </View>
      </View>

      {action ? (
        <Pressable
          onPress={action.onPress}
          hitSlop={tokens.touchTarget.slopFor(24)}
          accessibilityRole="button"
          accessibilityLabel={action.label}
        >
          {({ pressed }) => (
            <Text role="buttonSmall" tone="accent" style={{ opacity: pressed ? 0.6 : 1 }}>
              {action.label}
            </Text>
          )}
        </Pressable>
      ) : null}
    </View>
  );
}
