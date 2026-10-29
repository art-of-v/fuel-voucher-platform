import React from 'react';
import { StyleProp, View, ViewStyle } from 'react-native';
import { ChevronLeft } from 'lucide-react-native';
import { router } from 'expo-router';
import { useDesignTokens } from '../hooks/useTheme';
import { useI18n } from '../i18n';
import { IconButton } from './IconButton';
import { Text } from './Text';

/**
 * How far a header control reaches past the box it draws.
 *
 * The header is the one row where each end has an empty corner, so a thumb that
 * lands beside the 22pt glyph should still count: 48pt of box plus this reach is
 * 72pt across and taller than the row, which is close to twice the area of the box
 * alone. The reach cannot come from `hitSlop` on its own — React Native never
 * delivers a touch past an ancestor's frame — so the slots carry it as a
 * transparent border and take the space back with a matching negative margin: the
 * layout does not move, only the frame a touch may land in.
 *
 * `left: 0` on the leading control is deliberate: iOS starts its swipe-back
 * gesture in a strip along the leading edge and react-native-screens cancels the
 * touch of anything under it, so the leading reach may only go inwards.
 */
export const HEADER_REACH = 12;
export const HEADER_LEADING_HIT_SLOP = {
  top: HEADER_REACH,
  bottom: 16,
  left: 0,
  right: 24,
};
export const HEADER_TRAILING_HIT_SLOP = {
  top: HEADER_REACH,
  bottom: 16,
  left: HEADER_REACH,
  right: HEADER_REACH,
};

/** The frame a header action's touch may land in, as a style a slot must carry. */
export const HEADER_ACTION_FRAME = {
  borderWidth: HEADER_REACH,
  borderColor: 'transparent',
  margin: -HEADER_REACH,
} as const;

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
          // The reach goes right, never left: iOS starts its swipe-back gesture in a
          // strip along the leading edge, and react-native-screens cancels the RN
          // touch of anything under it the moment the finger drifts horizontally
          // (`cancelTouchesInParent` in RNSScreenStack.mm — "cancel RN touch events"
          // for a Touchable "close to an edge"). A control pulled to the edge with a
          // negative margin, as this one used to be, loses exactly those taps.
          hitSlop={HEADER_LEADING_HIT_SLOP}
        />
      )}

      {/*
        Inert, and deliberately transparent to touches: the back control's reach
        extends 24pt inwards over this block, and a plain View in front of it would
        swallow that half of the target.
      */}
      <View testID="header-title" style={{ flex: 1, gap: 2 }} pointerEvents="none">
        <Text role={compact ? 'heading' : 'title'} accessibilityRole="header" numberOfLines={2}>
          {title}
        </Text>
        {subtitle ? (
          <Text role="secondary" tone="muted" numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
      </View>

      {actions ? (
        <View
          testID="header-actions"
          style={[
            { flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.sm },
            // Grows the frame an action's `hitSlop` can reach into, without moving
            // the action: the border pads, the margin takes the same space back.
            HEADER_ACTION_FRAME,
          ]}
        >
          {actions}
        </View>
      ) : null}
    </View>
  );
}
