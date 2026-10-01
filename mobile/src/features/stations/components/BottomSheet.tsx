import React from 'react';
import { StyleSheet, useWindowDimensions, View, ScrollView } from 'react-native';
import { Gesture, GestureDetector } from 'react-native-gesture-handler';
import Animated, {
  useAnimatedStyle,
  useSharedValue,
  withSpring,
  runOnJS,
} from 'react-native-reanimated';
import { BlurView } from 'expo-blur';
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
  isDark: boolean;
  /** Hairline accent along the sheet's top edge (the radar's primary colour). */
  borderColor: string;
  /** Non-scrolling, drag-handle region: the title row, back control and hints. */
  header: React.ReactNode;
  /** Scrolling body: the leaderboard rows or a brand's АЗК list. */
  children: React.ReactNode;
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
 */
export const BottomSheet = React.forwardRef<BottomSheetHandle, Props>(function BottomSheet(
  { onClose, isDark, borderColor, header, children },
  ref,
) {
  const { height: screenH } = useWindowDimensions();

  // The sheet is a fixed-height surface anchored to the bottom; `translateY` slides it down to
  // reveal less. Offsets are measured from fully-open (0). Peek shows ~a header + one row.
  const FULL = Math.round(screenH * 0.86);
  const PEEK = 236;
  const HALF = Math.round(screenH * 0.5);
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
    <Animated.View style={[styles.sheet, { height: FULL, borderTopColor: borderColor }, sheetStyle]}>
      <BlurView intensity={isDark ? 80 : 95} tint={isDark ? 'dark' : 'light'} style={StyleSheet.absoluteFill} />
      <GestureDetector gesture={pan}>
        <View style={styles.dragZone}>
          <View style={[styles.grabber, { backgroundColor: isDark ? 'rgba(255,255,255,0.3)' : 'rgba(0,0,0,0.2)' }]} />
          {header}
        </View>
      </GestureDetector>
      <ScrollView
        style={styles.body}
        contentContainerStyle={styles.bodyContent}
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
    borderTopWidth: 2,
    borderTopLeftRadius: 32,
    borderTopRightRadius: 32,
    overflow: 'hidden',
    zIndex: 200,
  },
  dragZone: {
    paddingTop: 10,
    paddingHorizontal: 20,
  },
  grabber: {
    alignSelf: 'center',
    width: 40,
    height: 5,
    borderRadius: 3,
    marginBottom: 10,
  },
  body: {
    flex: 1,
    paddingHorizontal: 20,
  },
  bodyContent: {
    paddingBottom: 28,
  },
});
