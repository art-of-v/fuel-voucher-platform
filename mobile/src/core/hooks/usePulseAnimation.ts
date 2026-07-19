import { useRef, useEffect } from 'react';
import { Animated } from 'react-native';

interface UsePulseAnimationOptions {
  duration?: number;
  minValue?: number;
  maxValue?: number;
}

export function usePulseAnimation(options: UsePulseAnimationOptions = {}) {
  const { duration = 2000, minValue = 0.6, maxValue = 1 } = options;
  const pulseAnim = useRef(new Animated.Value(maxValue)).current;

  useEffect(() => {
    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(pulseAnim, {
          toValue: minValue,
          duration,
          useNativeDriver: true,
        }),
        Animated.timing(pulseAnim, {
          toValue: maxValue,
          duration,
          useNativeDriver: true,
        }),
      ]),
    );
    animation.start();
    return () => animation.stop();
  }, [duration, minValue, maxValue, pulseAnim]);

  return pulseAnim;
}
