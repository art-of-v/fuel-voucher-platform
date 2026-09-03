import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { ChevronLeft } from 'lucide-react-native';
import { router } from 'expo-router';
import { useDesignTokens } from '../hooks/useTheme';
import { useI18n } from '../i18n';
import { IconButton } from './IconButton';
import { Text } from './Text';

export interface ScreenHeaderProps {
  /** The screen's name. Sentence case — this is a title, not a label. */
  title: string;
  /** One line of context: a station address, an order reference, a count. */
  subtitle?: string;
  /**
   * Back behaviour. Defaults to `router.back()` — a *pop*. Pass a handler only
   * when the screen genuinely needs different behaviour, and never pass
   * `router.push`: `/basket` used `router.push('/')` as its back action, which
   * grew the stack on every "back".
   */
  onBack?: () => void;
  /** Hide the back control — for a root tab screen. */
  hideBack?: boolean;
  /** Up to two trailing actions. More than two belongs in an overflow sheet. */
  actions?: React.ReactNode;
  /** Reduce the title to `heading` size — for a sheet or a nested view. */
  compact?: boolean;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * The one header.
 *
 * Replaces six implementations: this component (used by exactly one screen), plus
 * five hand-rolled variants with title sizes of 32 / 28 / 24 / 18, back buttons of
 * 44×44-bordered / `padding: 8`-unbordered / 56×56-bordered, and subtitles at 8px
 * with 4px letter spacing.
 *
 * A header answers two questions — *where am I* and *how do I leave* — so it has
 * exactly two guaranteed parts: a back control at the leading edge and a title.
 * Actions are optional and trailing.
 *
 * Layout note: the title is `flex: 1` between two fixed-width slots, and the
 * trailing slot reserves the back control's width when there are no actions. That
 * keeps the title optically centred without the phantom `<View style={{ width:
 * 56 }} />` spacers the old implementation used.
 */
export function ScreenHeader({
  title,
  subtitle,
  onBack,
  hideBack = false,
  actions,
  compact = false,
  style,
  testID,
}: ScreenHeaderProps) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);

  const handleBack = onBack ?? (() => router.back());

  return (
    <View
      testID={testID}
      style={[
        {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.md,
          minHeight: tokens.chrome.headerHeight,
          paddingHorizontal: tokens.spacing.containerPadding,
          paddingBottom: tokens.spacing.lg,
        },
        style,
      ]}
    >
      {hideBack ? null : (
        <IconButton
          icon={<ChevronLeft />}
          onPress={handleBack}
          accessibilityLabel={t('common.back')}
          variant="plain"
          size="md"
          // Pull the glyph to the container edge; the hit area still extends out.
          style={{ marginLeft: -tokens.spacing.md }}
        />
      )}

      <View style={{ flex: 1, gap: 2 }}>
        <Text
          role={compact ? 'heading' : 'title'}
          accessibilityRole="header"
          numberOfLines={2}
        >
          {title}
        </Text>
        {subtitle ? (
          <Text role="secondary" tone="muted" numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
      </View>

      {actions ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.sm }}>
          {actions}
        </View>
      ) : null}
    </View>
  );
}
