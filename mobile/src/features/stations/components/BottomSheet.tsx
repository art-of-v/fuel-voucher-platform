import React from 'react';
import { StyleSheet, useWindowDimensions, View, ScrollView } from 'react-native';
import { Gesture, GestureDetector } from 'react-native-gesture-handler';
import Animated, {
  useAnimatedStyle,
  useSharedValue,
  withSpring,
  runOnJS,
} from 'react-native-reanimated';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { resolveSheetRelease } from '../lib/bottomSheet';

const SPRING = { damping: 22, stiffness: 220, mass: 0.7 } as const;

export interface BottomSheetHandle {
  /** Raise the sheet to its half snap if it is resting lower (e.g. on drill-in). */
  expand: () => void;
  /** Animate the sheet off-screen, then unmount via `onClose` — the chevron's exit. */
  close: () => void;
}

interface Props {
  onClose: () => void;
  /** Non-scrolling, drag-handle region: the title row, back control and hints. */
  header: React.ReactNode;
  /** Scrolling body: the leaderboard rows or a brand's АЗК list. */
  children: React.ReactNode;
  testID?: string;
}

/**
 * A hand-rolled, gesture-driven bottom sheet for the map's price ranking. Three snap points —
 * peek (one row over the map), half and full — with velocity-aware spring snapping and a drag
 * handle; a hard downward fling or a drag past peek dismisses it.
 *
 * Built on the reanimated + gesture-handler already in the bundle (so it ships over OTA, no
 * native rebuild). The pan lives on the handle/header region only, leaving the body a normal
 * `ScrollView` — this sidesteps the scroll-vs-drag gesture arbitration entirely, which is the
 * fragile part of a sheet, in exchange for "grab the handle to resize" rather than drag-anywhere.
 *
 * Presentation: a floating surface, so it carries the design system's sheet shadow and no
 * border; the fill is `surfaceElevated`, which separates it from the map on every theme. The
 * previous version was a blurred pane with a 2px primary border along its top edge, which read
 * as a neon seam over the tiles. The bottom padding is the home-indicator inset only — the
 * caller hides the tab bar while the sheet owns the bottom, and `useContentInsets` gives the
 * bar's clearance back, so this really is the bottom of the display.
 */
export const BottomSheet = React.forwardRef<BottomSheetHandle, Props>(function BottomSheet(
  { onClose, header, children, testID },
  ref,
) {
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const { height: screenH } = useWindowDimensions();

  // The sheet is a fixed-height surface anchored to the bottom; `translateY` slides it down to
  // reveal less. Offsets are measured from fully-open (0). Peek shows the header and one row.
  const FULL = Math.round(screenH * 0.88);
  const PEEK = 252;
  const HALF = Math.round(screenH * 0.54);
  const HIDDEN = FULL + 48; // fully below the screen edge

  const offFull = 0;
  const offHalf = Math.max(0, FULL - HALF);
  const offPeek = Math.max(0, FULL - PEEK);
  const offsets = [offFull, offHalf, offPeek]; // ascending; last = peek

  const translateY = useSharedValue(HIDDEN);
  const start = useSharedValue(0);

  // Rise to peek on mount; the FAB that renders this sheet already fired the open haptic.
  React.useEffect(() => {
    translateY.value = withSpring(offPeek, SPRING);
    // offPeek is stable for a given screen height.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const dismiss = React.useCallback(() => {
    translateY.value = withSpring(HIDDEN, SPRING, (finished) => {
      'worklet';
      if (finished) runOnJS(onClose)();
    });
    // translateY/HIDDEN are stable refs/derived; onClose is the only real dep.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [onClose]);

  React.useImperativeHandle(ref, () => ({
    expand: () => {
      if (translateY.value > offHalf) translateY.value = withSpring(offHalf, SPRING);
    },
    close: dismiss,
  }));

  const pan = Gesture.Pan()
    .onStart(() => {
      start.value = translateY.value;
    })
    .onUpdate((e) => {
      const next = start.value + e.translationY;
      // Clamp to [full, a little past peek] so an over-drag can arm a dismiss.
      translateY.value = Math.min(offPeek + 140, Math.max(0, next));
    })
    .onEnd((e) => {
      const res = resolveSheetRelease(translateY.value, e.velocityY, offsets);
      if (res.dismiss) {
        translateY.value = withSpring(HIDDEN, SPRING, (finished) => {
          if (finished) runOnJS(onClose)();
        });
      } else {
        translateY.value = withSpring(res.offset, SPRING);
      }
    });

  const sheetStyle = useAnimatedStyle(() => ({
    transform: [{ translateY: translateY.value }],
  }));

  return (
    <Animated.View
      testID={testID}
      accessibilityViewIsModal
      style={[
        styles.sheet,
        {
          height: FULL,
          backgroundColor: tokens.colors.surfaceElevated,
          borderTopLeftRadius: tokens.radius.xl,
          borderTopRightRadius: tokens.radius.xl,
          zIndex: tokens.zIndex.sheet,
        },
        tokens.elevation.medium,
        sheetStyle,
      ]}
    >
      <GestureDetector gesture={pan}>
        <View
          style={[
            styles.dragZone,
            {
              paddingTop: tokens.spacing.sm,
              paddingHorizontal: tokens.spacing['2xl'],
            },
          ]}
        >
          <View
            style={[
              styles.grabber,
              {
                width: tokens.chrome.sheetGrabberWidth,
                height: 4,
                borderRadius: tokens.radius.full,
                marginBottom: tokens.spacing.md,
                backgroundColor: tokens.colors.borderStrong,
              },
            ]}
          />
          {header}
        </View>
      </GestureDetector>
      <ScrollView
        style={styles.body}
        contentContainerStyle={{
          paddingHorizontal: tokens.spacing['2xl'],
          paddingBottom: insets.bottom + tokens.spacing['2xl'],
        }}
        showsVerticalScrollIndicator={false}
      >
        {children}
      </ScrollView>
    </Animated.View>
  );
});

const styles = StyleSheet.create({
  sheet: {
    position: 'absolute',
    bottom: 0,
    left: 0,
    right: 0,
  },
  dragZone: {
    paddingBottom: 0,
  },
  grabber: {
    alignSelf: 'center',
  },
  body: {
    flex: 1,
  },
});
