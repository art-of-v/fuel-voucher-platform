import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Button } from './Button';
import { Text } from './Text';

export interface EmptyStateProps {
  /** What is not here. A noun phrase, not "No data". */
  title: string;
  /** Why it is empty and what would fill it. One or two sentences. */
  description?: string;
  icon?: React.ReactElement<{ size?: number; color?: string }>;
  /** The action that resolves the emptiness. At most one primary. */
  action?: { label: string; onPress: () => void };
  /** A secondary route out — "Learn more", "Contact support". */
  secondaryAction?: { label: string; onPress: () => void };
  /** Compact variant for an empty section inside a populated screen. */
  compact?: boolean;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * Shown when a list or section has no content.
 *
 * An empty state is a **designed state**, not the absence of one. The audit found
 * `/index` testing emptiness against the *unfiltered* list, so filtering to zero
 * results produced a blank screen with no explanation and no way back — and
 * `/my-codes`, `/company` and `/invitations` each hand-rolling a different
 * icon-plus-two-lines arrangement.
 *
 * The icon is deliberately muted, not brand-coloured: nothing has gone right or
 * wrong here, so the state should not attract attention to itself.
 */
export function EmptyState({
  title,
  description,
  icon,
  action,
  secondaryAction,
  compact = false,
  style,
  testID,
}: EmptyStateProps) {
  const tokens = useDesignTokens();

  return (
    <View
      testID={testID}
      style={[
        {
          alignItems: 'center',
          justifyContent: 'center',
          gap: tokens.spacing.md,
          paddingVertical: compact ? tokens.spacing['3xl'] : tokens.spacing['5xl'],
          paddingHorizontal: tokens.spacing.containerPadding,
        },
        style,
      ]}
    >
      {icon
        ? React.cloneElement(icon, {
            size: compact ? 32 : 48,
            color: tokens.colors.text.disabled,
          })
        : null}

      <Text role={compact ? 'bodyStrong' : 'heading'} center>
        {title}
      </Text>

      {description ? (
        <Text role="body" tone="muted" center style={{ maxWidth: 320 }}>
          {description}
        </Text>
      ) : null}

      {action ? (
        <Button
          label={action.label}
          onPress={action.onPress}
          variant="primary"
          size={compact ? 'md' : 'lg'}
          fullWidth={false}
          style={{ marginTop: tokens.spacing.sm }}
        />
      ) : null}

      {secondaryAction ? (
        <Button
          label={secondaryAction.label}
          onPress={secondaryAction.onPress}
          variant="ghost"
          size="md"
          fullWidth={false}
        />
      ) : null}
    </View>
  );
}
