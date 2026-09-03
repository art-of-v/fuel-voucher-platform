import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Text } from './Text';

export type BadgeStatus = 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'primary';

export interface BadgeProps {
  label: string;
  /**
   * The semantic state this badge reports. Never pass a colour — the whole point
   * of this component is that "spent", "unpaid" and "blocked" resolve to the
   * same colours everywhere, on all eight themes.
   */
  status?: BadgeStatus;
  /** `subtle` = tinted container (default). `solid` = filled, for emphasis. */
  emphasis?: 'subtle' | 'solid';
  /** Leading dot instead of relying on the container tint alone. */
  dot?: boolean;
  icon?: React.ReactElement<{ size?: number; color?: string }>;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * A non-interactive state label.
 *
 * Replaces `VoucherBadge`'s hardcoded Tailwind colours (`#a855f7` for
 * `gifted_to_worker`, `#3b82f6`, `#22c55e`) at `fontSize: 9` in `Inter-Black`,
 * plus the inline `READY` / `REDEEMED` / `UNPAID` / `REFUNDED` strings that were
 * styled independently in four files.
 *
 * A badge is **not** a button. If it can be tapped, it is a `Chip`.
 */
export function Badge({
  label,
  status = 'neutral',
  emphasis = 'subtle',
  dot = false,
  icon,
  style,
  testID,
}: BadgeProps) {
  const tokens = useDesignTokens();
  const c = tokens.colors;

  const role =
    status === 'primary'
      ? { base: c.primary, onBase: c.text.onPrimary, subtle: c.primarySubtle, border: c.borderAccent }
      : c.status[status];

  const solid = emphasis === 'solid';
  const content = solid ? role.onBase : role.base;

  return (
    <View
      testID={testID}
      style={[
        {
          flexDirection: 'row',
          alignItems: 'center',
          alignSelf: 'flex-start',
          gap: tokens.spacing.xs,
          paddingHorizontal: tokens.spacing.sm,
          paddingVertical: 3,
          borderRadius: tokens.radius.sm,
          backgroundColor: solid ? role.base : role.subtle,
          borderWidth: solid ? 0 : 1,
          borderColor: role.border,
        },
        style,
      ]}
    >
      {dot ? (
        <View
          style={{
            width: 6,
            height: 6,
            borderRadius: tokens.radius.full,
            backgroundColor: content,
          }}
        />
      ) : null}
      {icon ? React.cloneElement(icon, { size: 12, color: content }) : null}
      <Text role="label" tone="inherit" numberOfLines={1} style={{ color: content }}>
        {label}
      </Text>
    </View>
  );
}
