import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { ChevronRight } from 'lucide-react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { PressableScale } from './PressableScale';
import { Text } from './Text';

export interface ListItemProps {
  title: string;
  /** Supporting line beneath the title. */
  subtitle?: string;
  /** Leading element — an icon, an avatar, a brand dot. */
  leading?: React.ReactNode;
  /** Trailing element — a value, a switch, a badge. Replaces the chevron. */
  trailing?: React.ReactNode;
  onPress?: () => void;
  /** Show a chevron. Implied when `onPress` is set and `trailing` is not. */
  showChevron?: boolean;
  selected?: boolean;
  disabled?: boolean;
  /** Renders the title and chevron in the danger role. */
  destructive?: boolean;
  /** Hairline below the row. Omit on the last row of a group. */
  divider?: boolean;
  style?: StyleProp<ViewStyle>;
  accessibilityLabel?: string;
  testID?: string;
}

/**
 * The row primitive: settings rows, menu entries, worker rosters, any
 * label/value or label/navigate pair.
 *
 * Replaces the hand-rolled `Pressable` + `View` + two `Text`s + `ChevronRight`
 * pattern repeated across `/profile`, `/company`, `/contracts` and
 * `/invitations`, each with its own height, padding and chevron size — and, in
 * `/profile`, no chevron at all on rows that navigate.
 *
 * Height is `control.lg` (52) minimum, so every row clears the touch minimum
 * regardless of how little text it holds.
 */
export function ListItem({
  title,
  subtitle,
  leading,
  trailing,
  onPress,
  showChevron,
  selected = false,
  disabled = false,
  destructive = false,
  divider = false,
  style,
  accessibilityLabel,
  testID,
}: ListItemProps) {
  const tokens = useDesignTokens();
  const c = tokens.colors;
  const chevron = showChevron ?? (!!onPress && !trailing);

  const titleTone = disabled ? 'disabled' : destructive ? 'danger' : 'primary';

  const body = (
    <View
      style={{
        flexDirection: 'row',
        alignItems: 'center',
        gap: tokens.spacing.md,
        minHeight: tokens.control.lg,
        paddingVertical: tokens.spacing.md,
        paddingHorizontal: tokens.spacing.lg,
      }}
    >
      {leading ? <View style={{ width: 24, alignItems: 'center' }}>{leading}</View> : null}

      <View style={{ flex: 1, gap: 2 }}>
        <Text role="bodyStrong" tone={titleTone} numberOfLines={2}>
          {title}
        </Text>
        {subtitle ? (
          <Text role="secondary" tone={disabled ? 'disabled' : 'muted'} numberOfLines={2}>
            {subtitle}
          </Text>
        ) : null}
      </View>

      {trailing}
      {chevron ? (
        <ChevronRight
          size={20}
          color={disabled ? c.text.disabled : destructive ? c.status.danger.base : c.text.muted}
        />
      ) : null}
    </View>
  );

  const container: ViewStyle = {
    backgroundColor: selected ? c.primarySubtle : 'transparent',
    borderBottomWidth: divider ? 1 : 0,
    borderBottomColor: c.borderSubtle,
    opacity: disabled ? 0.55 : 1,
  };

  if (!onPress) {
    return (
      <View style={[container, style]} testID={testID}>
        {body}
      </View>
    );
  }

  return (
    <PressableScale
      onPress={disabled ? undefined : onPress}
      disabled={disabled}
      scaleTo={1}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? title}
      accessibilityState={{ disabled, selected }}
      testID={testID}
      style={[container, style]}
      pressedStyle={{ backgroundColor: destructive ? c.status.danger.subtle : c.primarySubtle }}
    >
      {body}
    </PressableScale>
  );
}
