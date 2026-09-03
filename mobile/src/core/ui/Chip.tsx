import React from 'react';
import { Pressable, StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Haptics } from '../utils/haptics';
import { Text } from './Text';

export interface ChipProps {
  label: string;
  onPress: () => void;
  /** Selected chips carry a brand fill; unselected carry a hairline border. */
  selected?: boolean;
  disabled?: boolean;
  icon?: React.ReactElement<{ size?: number; color?: string }>;
  /** A count or secondary value shown after the label. */
  count?: number;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * A tappable filter or toggle: brand filters on the station list, period filters
 * on the report, fuel-type toggles.
 *
 * Replaces three chip implementations with radii 8, 12 and 999 and three
 * different selected treatments (border colour change, background fill, and a
 * glow). Here, selection is a **fill** change, because a chip's only job is to
 * report whether it is on.
 *
 * Visually 36pt tall — below the 44pt minimum, so it always carries `hitSlop`.
 * Chips are the one control allowed to be under 44pt, because they appear in
 * horizontal rows where 44pt would consume a third of the viewport height.
 */
export function Chip({
  label,
  onPress,
  selected = false,
  disabled = false,
  icon,
  count,
  style,
  testID,
}: ChipProps) {
  const tokens = useDesignTokens();
  const c = tokens.colors;

  const content = disabled
    ? c.text.disabled
    : selected
      ? c.text.onPrimary
      : c.text.secondary;

  return (
    <Pressable
      onPress={() => {
        if (disabled) return;
        Haptics.selectionAsync();
        onPress();
      }}
      disabled={disabled}
      hitSlop={tokens.touchTarget.slopFor(36)}
      accessibilityRole="button"
      accessibilityState={{ selected, disabled }}
      accessibilityLabel={count != null ? `${label}, ${count}` : label}
      testID={testID}
      style={({ pressed }) => [
        {
          height: 36,
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.sm,
          paddingHorizontal: tokens.spacing.md,
          borderRadius: tokens.radius.md,
          backgroundColor: disabled
            ? c.disabled
            : selected
              ? pressed
                ? c.primaryPressed
                : c.primary
              : pressed
                ? c.primarySubtle
                : 'transparent',
          borderWidth: selected ? 0 : 1,
          borderColor: c.border,
        },
        style,
      ]}
    >
      {icon ? React.cloneElement(icon, { size: 16, color: content }) : null}
      <Text role="buttonSmall" tone="inherit" numberOfLines={1} style={{ color: content }}>
        {label}
      </Text>
      {count != null ? (
        <View
          style={{
            minWidth: 18,
            paddingHorizontal: 5,
            height: 18,
            borderRadius: tokens.radius.sm,
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: selected ? c.text.onPrimary : c.surfaceSunken,
          }}
        >
          <Text
            role="caption"
            tone="inherit"
            style={{ color: selected ? c.primary : c.text.muted }}
          >
            {String(count)}
          </Text>
        </View>
      ) : null}
    </Pressable>
  );
}
