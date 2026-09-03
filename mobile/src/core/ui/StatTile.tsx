import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Card } from './Card';
import { Text } from './Text';

export interface StatTileProps {
  /** What the number means. Sentence case, not an abbreviation. */
  label: string;
  /** The value. Pass a `Price` element for money. */
  value: React.ReactNode;
  /** Context for the value — a period, a comparison, a unit. */
  caption?: string;
  icon?: React.ReactElement<{ size?: number; color?: string }>;
  onPress?: () => void;
  /** Status accent on the leading edge, for a value that reports a state. */
  accent?: 'success' | 'warning' | 'danger' | 'info' | 'primary';
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * A single labelled figure.
 *
 * Replaces five separate stat-tile designs (`/report` summary cards,
 * `/company` roster stats, `/profile` counters, the `/my-codes` `SummaryBar`,
 * and the inline tiles on `/index`), which between them used `width: '48%'` with
 * a competing `gap`, radii of 2 and 12, and labels at 8px with 1.5 letter
 * spacing.
 *
 * Layout note: the tile is `flex: 1` and expects to sit in a `flexDirection:
 * 'row'` with `gap`. It does **not** set a percentage width — `width: '48%'`
 * plus `gap: 10` over-constrains the row and was the cause of the ragged report
 * grid.
 *
 * Label sits **above** the value. The value is the reason the tile exists, so it
 * gets the last word and the larger type; a label underneath makes the eye
 * travel back up to interpret what it just read.
 */
export function StatTile({
  label,
  value,
  caption,
  icon,
  onPress,
  accent,
  style,
  testID,
}: StatTileProps) {
  const tokens = useDesignTokens();

  return (
    <Card
      onPress={onPress}
      accent={accent}
      padding="md"
      style={[{ flex: 1, minWidth: 140 }, style]}
      accessibilityLabel={label}
      testID={testID}
    >
      <View style={{ gap: tokens.spacing.sm }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.xs }}>
          {icon ? React.cloneElement(icon, { size: 14, color: tokens.colors.text.muted }) : null}
          <Text role="label" tone="muted" numberOfLines={1} style={{ flex: 1 }}>
            {label}
          </Text>
        </View>

        {typeof value === 'string' || typeof value === 'number' ? (
          <Text role="numeric" tone="primary" numberOfLines={1}>
            {String(value)}
          </Text>
        ) : (
          value
        )}

        {caption ? (
          <Text role="caption" tone="muted" numberOfLines={2}>
            {caption}
          </Text>
        ) : null}
      </View>
    </Card>
  );
}
