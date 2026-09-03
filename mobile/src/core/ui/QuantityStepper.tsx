import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { Minus, Plus, Trash2 } from 'lucide-react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { IconButton } from './IconButton';
import { Text } from './Text';

export interface QuantityStepperProps {
  value: number;
  onChange: (value: number) => void;
  min?: number;
  max?: number;
  step?: number;
  /**
   * Called when the user decrements at `min`. When provided, the decrement
   * control becomes a **remove** affordance (a bin icon in the danger role) at
   * `min` instead of silently disappearing the line.
   *
   * This is the fix for the audit's highest-severity interaction defect: "−" at
   * quantity 1 deleted the cart line with no confirmation and no undo, using the
   * same glyph that had meant "one fewer" a moment earlier. A control must not
   * change what it does without changing how it looks.
   */
  onRemove?: () => void;
  /** Unit suffix rendered after the number — `л`, `шт`. */
  unit?: string;
  disabled?: boolean;
  size?: 'sm' | 'md';
  style?: StyleProp<ViewStyle>;
  accessibilityLabel?: string;
  testID?: string;
}

/**
 * Increment/decrement a bounded quantity.
 *
 * Replaces two implementations: the cart's `−`/`+` pair (which deleted the line
 * at 1) and `PackageCard`'s stepper (a different size, different radius, and its
 * own bounds logic).
 *
 * Both controls are real 44pt targets. At the bounds the disabled control stays
 * in place — it does not vanish, because a control that disappears makes the
 * layout jump under the user's thumb.
 */
export function QuantityStepper({
  value,
  onChange,
  min = 1,
  max = 99,
  step = 1,
  onRemove,
  unit,
  disabled = false,
  size = 'md',
  style,
  accessibilityLabel,
  testID,
}: QuantityStepperProps) {
  const tokens = useDesignTokens();
  const atMin = value <= min;
  const atMax = value >= max;
  const removeMode = atMin && !!onRemove;

  const btnSize = size === 'sm' ? 'sm' : 'md';

  return (
    <View
      accessible
      accessibilityLabel={accessibilityLabel}
      accessibilityValue={{ text: `${value}${unit ? ` ${unit}` : ''}` }}
      style={[
        {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.sm,
          alignSelf: 'flex-start',
        },
        style,
      ]}
      testID={testID}
    >
      <IconButton
        icon={removeMode ? <Trash2 /> : <Minus />}
        variant={removeMode ? 'danger' : 'outlined'}
        size={btnSize}
        disabled={disabled || (atMin && !onRemove)}
        accessibilityLabel={removeMode ? 'remove' : 'decrease'}
        onPress={() => {
          if (removeMode) {
            onRemove?.();
            return;
          }
          onChange(Math.max(min, value - step));
        }}
      />

      <View style={{ minWidth: 48, alignItems: 'center' }}>
        <Text role="numeric" tone={disabled ? 'disabled' : 'primary'}>
          {String(value)}
        </Text>
        {unit ? (
          <Text role="caption" tone="muted">
            {unit}
          </Text>
        ) : null}
      </View>

      <IconButton
        icon={<Plus />}
        variant="outlined"
        size={btnSize}
        disabled={disabled || atMax}
        accessibilityLabel="increase"
        onPress={() => onChange(Math.min(max, value + step))}
      />
    </View>
  );
}
