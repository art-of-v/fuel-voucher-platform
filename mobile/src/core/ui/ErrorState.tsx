import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { CloudOff, RotateCcw, TriangleAlert } from 'lucide-react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { useI18n } from '../i18n';
import { Button } from './Button';
import { Text } from './Text';

export interface ErrorStateProps {
  /** What failed, in the user's terms. Not the exception class. */
  title?: string;
  /** What the user can do about it. */
  description?: string;
  /** Retry handler. Omit only when retrying genuinely cannot help. */
  onRetry?: () => void;
  /** A way out that is not a retry — go back, contact support. */
  secondaryAction?: { label: string; onPress: () => void };
  /** `offline` swaps the icon and copy for a connectivity failure. */
  variant?: 'error' | 'offline';
  /** Fill the available space and centre. */
  fullScreen?: boolean;
  /**
   * Technical detail. Rendered **only** in development builds — a stack trace or
   * a signed-request diagnostic is not user-facing copy.
   */
  detail?: string;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * Shown when a screen or section could not load.
 *
 * Consolidates the per-screen retry affordances: a bare `Text` "retry" word with
 * `padding: 12` in `/report`, an `Alert.alert` in `/contracts`, and nothing at all
 * on the voucher QR — the one place in the product where a failed load has a
 * user standing at a pump.
 *
 * The retry is a real `Button`, not a text link, because retrying after a failure
 * is the most likely thing the user wants and should be the easiest thing to hit.
 */
export function ErrorState({
  title,
  description,
  onRetry,
  secondaryAction,
  variant = 'error',
  fullScreen = false,
  detail,
  style,
  testID,
}: ErrorStateProps) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);
  const offline = variant === 'offline';

  const Icon = offline ? CloudOff : TriangleAlert;
  const resolvedTitle = title ?? t(offline ? 'state.offlineTitle' : 'state.errorTitle');
  const resolvedDescription =
    description ?? t(offline ? 'state.offlineDescription' : 'state.errorDescription');

  return (
    <View
      accessibilityRole="alert"
      testID={testID}
      style={[
        {
          alignItems: 'center',
          justifyContent: 'center',
          gap: tokens.spacing.md,
          paddingVertical: tokens.spacing['4xl'],
          paddingHorizontal: tokens.spacing.containerPadding,
          ...(fullScreen ? { flex: 1 } : null),
        },
        style,
      ]}
    >
      <Icon size={40} color={tokens.colors.status.danger.base} />

      <Text role="heading" center>
        {resolvedTitle}
      </Text>

      {resolvedDescription ? (
        <Text role="body" tone="muted" center style={{ maxWidth: 320 }}>
          {resolvedDescription}
        </Text>
      ) : null}

      {/* Diagnostics never reach production users. */}
      {__DEV__ && detail ? (
        <Text role="caption" tone="muted" center style={{ maxWidth: 320 }}>
          {detail}
        </Text>
      ) : null}

      {onRetry ? (
        <Button
          label={t('common.retry')}
          onPress={onRetry}
          variant="secondary"
          size="md"
          fullWidth={false}
          icon={<RotateCcw />}
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
