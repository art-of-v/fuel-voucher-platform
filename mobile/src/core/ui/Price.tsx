import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { formatMoney, splitMoney } from '../utils/currency';
import { Text } from './Text';
import type { TextRole } from '../design/typography';

export type PriceSize = 'sm' | 'md' | 'lg';

export interface PriceProps {
  /** The amount in hryvnia. */
  amount: number;
  /**
   * `sm` — inline in a row or sentence (`numericSmall`)
   * `md` — a card's price (`numeric`)
   * `lg` — the total the screen is about (`numericLarge`)
   */
  size?: PriceSize;
  /**
   * An amount this one replaces, rendered struck through beside it. Use for a
   * discount; do not fake one.
   */
  original?: number;
  /** Semantic colour. Defaults to `primary` text — a price is not an accent. */
  tone?: 'primary' | 'secondary' | 'muted' | 'accent' | 'success' | 'danger';
  /** Force a leading sign — for deltas and credits. */
  signed?: boolean;
  /** Fraction digits. Defaults to 2. Only set 0 for non-payable values. */
  decimals?: number;
  /** Right-align within the parent (numeric columns). */
  align?: 'left' | 'right';
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

const ROLE: Record<PriceSize, TextRole> = {
  sm: 'numericSmall',
  md: 'numeric',
  lg: 'numericLarge',
};

/** Symbol size, relative to the value, per size step. */
const SYMBOL_ROLE: Record<PriceSize, TextRole> = {
  sm: 'numericSmall',
  md: 'bodyStrong',
  lg: 'numeric',
};

/**
 * Renders a monetary amount.
 *
 * Every price in the product goes through this component, which is the only way
 * to guarantee that the cart total, the package price, the report row and the
 * receipt all read the same. Before Phase 2 they used four different formats and
 * one of them leaked raw float error into the UI.
 *
 * The `₴` symbol is typeset one step down from the digits, so the number is what
 * the eye lands on — the currency is constant across the whole product and does
 * not need to compete.
 */
export function Price({
  amount,
  size = 'md',
  original,
  tone = 'primary',
  signed = false,
  decimals = 2,
  align = 'left',
  style,
  testID,
}: PriceProps) {
  const tokens = useDesignTokens();
  const { value, symbol } = splitMoney(amount, { decimals, signed });

  return (
    <View
      testID={testID}
      accessible
      accessibilityLabel={
        original != null
          ? `${formatMoney(amount, { decimals })}, was ${formatMoney(original, { decimals })}`
          : formatMoney(amount, { decimals })
      }
      style={[
        {
          flexDirection: 'row',
          alignItems: 'baseline',
          gap: tokens.spacing.xs,
          justifyContent: align === 'right' ? 'flex-end' : 'flex-start',
        },
        style,
      ]}
    >
      {original != null && original !== amount ? (
        <Text
          role={size === 'lg' ? 'numeric' : 'numericSmall'}
          tone="muted"
          style={{ textDecorationLine: 'line-through' }}
        >
          {formatMoney(original, { decimals, hideSymbol: true })}
        </Text>
      ) : null}

      <Text role={ROLE[size]} tone={tone}>
        {value}
      </Text>
      <Text role={SYMBOL_ROLE[size]} tone={tone === 'primary' ? 'muted' : tone}>
        {symbol}
      </Text>
    </View>
  );
}
