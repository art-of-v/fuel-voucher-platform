import React from 'react';
import { ActivityIndicator, StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Text } from './Text';

export interface LoadingStateProps {
  /**
   * What is loading. Optional — omit for a sub-second fetch, where a label
   * appears and vanishes faster than it can be read.
   */
  message?: string;
  /** Fill the available space and centre. Use for a first load. */
  fullScreen?: boolean;
  /** `inline` sits in a row of content without claiming vertical space. */
  variant?: 'block' | 'inline';
  size?: 'small' | 'large';
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * A spinner with an optional label.
 *
 * Replaces the old `LoadingIndicator` (which lived in `core/ui/` and was never
 * imported by anything — since deleted) and the seven hand-rolled centred
 * `ActivityIndicator` blocks in `/report`, `/my-codes`, `/contracts`, `/company`,
 * `/invitations` and `/profile` (which had two).
 *
 * Six of those seven shared a structural bug worth naming, because it is easy to
 * reintroduce: they `return`ed a bare centred `View` **instead of** the screen's
 * `PageLayout`, so for the duration of the load the screen had no header, no
 * background and no safe-area insets — then visibly re-assembled itself when the
 * data arrived. Render this **inside** the layout, never in place of it:
 *
 *     if (isLoading) {
 *       return (
 *         <PageLayout header={Header} disableScroll>
 *           <LoadingState fullScreen />
 *         </PageLayout>
 *       );
 *     }
 *
 * `disableScroll` matters: `fullScreen` is `flex: 1`, which needs a fixed-height
 * parent to centre itself in rather than a scroll container.
 *
 * When to use which:
 * - `fullScreen` — only for a screen's **first** load, when there is genuinely
 *   nothing to show yet.
 * - `variant="inline"` — for a refresh of content that is already on screen.
 *
 * Do **not** use `fullScreen` for a filter change. `/report` set `loading = true`
 * on every period tap, which wiped the whole screen to a centred spinner and
 * destroyed the user's scroll position and sense of place. Keep the stale content
 * visible and show inline progress instead.
 */
export function LoadingState({
  message,
  fullScreen = false,
  variant = 'block',
  size = 'large',
  style,
  testID,
}: LoadingStateProps) {
  const tokens = useDesignTokens();

  const layout: ViewStyle =
    variant === 'inline'
      ? {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.sm,
        }
      : {
          alignItems: 'center',
          justifyContent: 'center',
          gap: tokens.spacing.md,
          paddingVertical: tokens.spacing['4xl'],
          ...(fullScreen ? { flex: 1 } : null),
        };

  return (
    <View
      accessibilityRole="progressbar"
      accessibilityLabel={message}
      accessibilityState={{ busy: true }}
      testID={testID}
      style={[layout, style]}
    >
      <ActivityIndicator
        size={variant === 'inline' ? 'small' : size}
        color={tokens.colors.primary}
      />
      {message ? (
        <Text role={variant === 'inline' ? 'secondary' : 'body'} tone="muted" center>
          {message}
        </Text>
      ) : null}
    </View>
  );
}
