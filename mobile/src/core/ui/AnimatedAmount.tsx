import { useEffect, useRef } from 'react';
import { Animated, Easing } from 'react-native';

/**
 * Fades and lifts a figure when it changes.
 *
 * Switching the validity term changes the card's unit price, its total and the saving beneath it. Snapping
 * between the numbers is fine on paper and cheap in a screenshot, but on a device the price appears to
 * flicker rather than move, and the customer cannot tell whether the 15 ₴ they saved came from their tap or
 * from the card re-rendering underneath them.
 *
 * A short fade-and-lift makes the change read as a consequence of the tap. Deliberately not a counting
 * animation: that is a flourish that costs a full second of attention on the one screen where the customer is
 * trying to decide something.
 *
 * The children are rendered once and only their opacity and offset are animated, so the numbers themselves
 * are never re-created mid-flight.
 */
export function AnimatedAmount({
  children,
  offset = 6,
}: {
  children: React.ReactNode;
  /** How far the figure travels as it changes, in pixels. */
  offset?: number;
}) {
  const progress = useRef(new Animated.Value(1)).current;
  const previous = useRef(children);

  useEffect(() => {
    if (previous.current === children) return;
    previous.current = children;

    progress.setValue(0);
    Animated.timing(progress, {
      toValue: 1,
      duration: 200,
      // Decelerate: the figure arrives gently, which is what makes it read as settling rather than jumping.
      easing: Easing.out(Easing.quad),
      useNativeDriver: true,
    }).start();
  }, [children, progress]);

  return (
    <Animated.View
      style={{
        opacity: progress,
        transform: [
          {
            translateY: progress.interpolate({
              inputRange: [0, 1],
              outputRange: [offset, 0],
            }),
          },
        ],
      }}
    >
      {children}
    </Animated.View>
  );
}
