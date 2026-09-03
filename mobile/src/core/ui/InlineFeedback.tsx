import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { CircleAlert, CircleCheck, Info, TriangleAlert, X } from 'lucide-react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { useI18n } from '../i18n';
import { IconButton } from './IconButton';
import { Text } from './Text';

export type FeedbackKind = 'success' | 'warning' | 'danger' | 'info';

export interface InlineFeedbackProps {
  kind: FeedbackKind;
  /** The message. Required — feedback with no words is decoration. */
  message: string;
  /** An optional heading when the message needs more than one line. */
  title?: string;
  /** A single action the feedback offers — "Retry", "Undo", "Review". */
  action?: { label: string; onPress: () => void };
  /** Show a dismiss control. Only for feedback the user can safely ignore. */
  onDismiss?: () => void;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

const ICONS: Record<FeedbackKind, React.ComponentType<{ size?: number; color?: string }>> = {
  success: CircleCheck,
  warning: TriangleAlert,
  danger: CircleAlert,
  info: Info,
};

/**
 * Feedback that stays on screen, anchored next to what it is about.
 *
 * **This is the default feedback mechanism in this system**, not the toast. Use
 * `InlineFeedback` when the information is:
 *   - about a specific thing the user can see (a field, a card, a section), or
 *   - something they may need to re-read, or
 *   - a condition that persists until they act.
 *
 * Use `Toast` only for transient confirmation of something already visibly done.
 * Use `ConfirmDialog` only when you must block until the user decides.
 *
 * Before Phase 2 the product had none of these: every outcome, success and
 * failure alike, arrived as `Alert.alert` — a blocking OS dialog that covers the
 * thing it is describing, cannot be styled, cannot be re-read once dismissed, and
 * cannot carry an action other than "OK".
 */
export function InlineFeedback({
  kind,
  message,
  title,
  action,
  onDismiss,
  style,
  testID,
}: InlineFeedbackProps) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);
  const role = tokens.colors.status[kind];
  const Icon = ICONS[kind];

  return (
    <View
      // `alert` is announced immediately; success and info are read in turn via
      // the live region. React Native has no `status` role, so the live region is
      // what carries politeness for the non-urgent kinds.
      accessibilityRole={kind === 'danger' || kind === 'warning' ? 'alert' : undefined}
      accessibilityLiveRegion={kind === 'danger' || kind === 'warning' ? 'assertive' : 'polite'}
      accessibilityLabel={title ? `${title}. ${message}` : message}
      testID={testID}
      style={[
        {
          flexDirection: 'row',
          alignItems: 'flex-start',
          gap: tokens.spacing.md,
          padding: tokens.spacing.md,
          borderRadius: tokens.radius.md,
          backgroundColor: role.subtle,
          borderWidth: 1,
          borderColor: role.border,
        },
        style,
      ]}
    >
      <Icon size={18} color={role.base} />

      <View style={{ flex: 1, gap: tokens.spacing.xs }}>
        {title ? (
          <Text role="bodyStrong" tone="inherit" style={{ color: role.base }}>
            {title}
          </Text>
        ) : null}
        <Text role="secondary" tone="primary">
          {message}
        </Text>

        {action ? (
          <Text
            role="buttonSmall"
            tone="inherit"
            onPress={action.onPress}
            accessibilityRole="button"
            style={{ color: role.base, paddingTop: tokens.spacing.xs }}
          >
            {action.label}
          </Text>
        ) : null}
      </View>

      {onDismiss ? (
        <IconButton
          icon={<X />}
          size="sm"
          onPress={onDismiss}
          accessibilityLabel={t('common.dismiss')}
        />
      ) : null}
    </View>
  );
}
