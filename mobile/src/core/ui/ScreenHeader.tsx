import React from 'react';
import { Insets, StyleProp, View, ViewStyle } from 'react-native';
import { ChevronLeft } from 'lucide-react-native';
import { router } from 'expo-router';
import { chrome, spacing } from '../design/layout';
import { useDesignTokens } from '../hooks/useTheme';
import { useI18n } from '../i18n';
import { IconButton, frameToStyle } from './IconButton';
import { useHeaderTopBand } from './PageLayout';
import { Text } from './Text';

/**
 * How far a header corner answers past the box it draws.
 *
 * The header is the one row where each end has an empty corner, and the one row
 * people press without looking — a hand arriving at the top-left corner lands as
 * often 20-40pt under the control as on it, and as often over it, in the safe-area
 * strip, when it comes down from the status bar. So the corner is not a ring around
 * a button: the whole region is the button, and the drawn control is what sits in
 * it (`lg`: 52pt box, 24pt glyph).
 *
 * The region cannot come from `hitSlop` — React Native clips it to every ancestor's
 * frame, and overlapping slops resolve by z-order — so it is the pressable's own
 * frame, padded with transparent borders and pulled back with matching negative
 * margins: the layout does not move, only the region a touch may land in. The row
 * spans the same bands for the same reason, and is `pointerEvents="box-none"` so
 * the pixels away from a corner still reach the content.
 *
 * `left: 0` on the leading corner is deliberate: iOS starts its swipe-back gesture
 * in a strip along the leading edge and react-native-screens cancels the touch of
 * anything under it, so the leading corner may only spend its width inwards.
 */
export const HEADER_REACH = 12;
/** How far below the row a corner keeps answering taps. */
export const HEADER_DROP = chrome.headerCornerDrop;
/**
 * The row's own padding below the controls — the corner runs past it by
 * `HEADER_DROP` more. It is the token, not a copy of it: the `lg` box fills a 64pt
 * row with this much left under it, so a change here moves the corner's geometry.
 */
const HEADER_ROW_PAD = spacing.md;
/** How far below a control's bottom edge the corner runs, to the row's frame. */
export const HEADER_BOTTOM_HIT = HEADER_ROW_PAD + HEADER_DROP;
/** How far a corner spends inwards, over the title's edge. */
export const HEADER_CORNER_INWARD = 32;

/**
 * The frame each header corner answers in, as `IconButton`'s `frame`.
 *
 * The top is the whole safe-area band `PageLayout` reserves, so a hand coming down
 * from the status bar lands in the corner; the bottom reaches the row's own frame.
 * Both ends derive from the same numbers the row uses, so a corner cannot describe a
 * region its row does not cover.
 */
export function useHeaderCornerFrames(): { leading: Insets; trailing: Insets } {
  const band = useHeaderTopBand();
  return {
    leading: { top: band, bottom: HEADER_BOTTOM_HIT, left: 0, right: HEADER_CORNER_INWARD },
    trailing: {
      top: band,
      bottom: HEADER_BOTTOM_HIT,
      left: HEADER_CORNER_INWARD,
      right: HEADER_REACH,
    },
  };
}

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
  // The safe-area band above the row that `PageLayout` reserves. This row's frame
  // has to include it: a touch never reaches past an ancestor's frame, so a corner
  // cannot answer a hand that comes down from the status bar unless the frame does.
  const headerBand = useHeaderTopBand();
  const cornerFrames = useHeaderCornerFrames();

  const handleBack = onBack ?? (() => router.back());

  return (
    <View
      testID={testID}
      pointerEvents="box-none"
      style={[
        {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.md,
          minHeight: tokens.chrome.headerHeight,
          paddingHorizontal: tokens.spacing.containerPadding,
          paddingBottom: spacing.md,
          // The bands a corner keeps answering in, above and below the row. The
          // borders grow the frame, the margins take the same space back, so the
          // layout does not move. `box-none` keeps the bands from swallowing
          // content taps outside the corner targets.
          borderTopWidth: headerBand,
          borderBottomWidth: HEADER_DROP,
          borderColor: 'transparent',
          marginTop: -headerBand,
          marginBottom: -HEADER_DROP,
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
          // `lg` in the header only: 52pt box, 24pt glyph. Every other icon button
          // sits in a row the eye leads to; this one is reached blind, at the far
          // corner, so it gets the largest box the 64pt row holds without moving.
          size="lg"
          // The whole corner is this button. The frame goes right, never left: iOS
          // starts its swipe-back gesture in a strip along the leading edge, and
          // react-native-screens cancels the RN touch of anything under it the moment
          // the finger drifts horizontally (`cancelTouchesInParent` in
          // RNSScreenStack.mm — "cancel RN touch events" for a Touchable "close to an
          // edge"). A control pulled to the edge with a negative margin, as this one
          // used to be, loses exactly those taps.
          frame={cornerFrames.leading}
        />
      )}

      {/*
        Inert, and deliberately transparent to touches: the back corner runs through
        the row's gap and into this block, and a plain View in front of it would
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
          pointerEvents="box-none"
          style={[
            { flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.sm },
            // The slot spans the corner its action answers in, or the action's frame
            // would be clipped by the slot's own — the bug this whole area keeps
            // producing. `box-none` keeps the slot itself out of the hit test.
            frameToStyle(cornerFrames.trailing),
          ]}
        >
          {actions}
        </View>
      ) : null}
    </View>
  );
}
