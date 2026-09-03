import React, { useEffect, useRef } from 'react';
import { Animated, Easing, Pressable } from 'react-native';
import { CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { usePathname } from 'expo-router';
import { useDesignTokens } from '../hooks/useTheme';
import { isTabBarVisible } from '../navigation/tabBar';
import { Haptics } from '../utils/haptics';
import { Text } from './Text';
import { DEFAULT_DURATION, useToastStore } from '../feedback/toastStore';
import type { FeedbackKind } from './InlineFeedback';

const ICONS: Record<FeedbackKind, React.ComponentType<{ size?: number; color?: string }>> = {
  success: CircleCheck,
  warning: TriangleAlert,
  danger: CircleAlert,
  info: Info,
};

export interface ToastHostProps {
  /**
   * Extra bottom offset. Defaults to the tab bar's height on routes that show it,
   * derived from the current route, so a toast never covers navigation. The
   * safe-area inset is added on top of this.
   */
  bottomOffset?: number;
}

/**
 * Renders the current toast. Mount exactly once, near the root, above everything
 * except dialogs.
 *
 * Enters from the bottom, sits above the tab bar and the home indicator, and
 * dismisses on tap or on its own after a kind-dependent interval. It never covers
 * the primary action of a screen, and it is not blocking — the app remains
 * usable while it is visible, which is the whole difference from `Alert.alert`.
 */
export function ToastHost({ bottomOffset }: ToastHostProps) {
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const pathname = usePathname();
  const current = useToastStore((s) => s.current);
  const hide = useToastStore((s) => s.hide);

  const offset =
    bottomOffset ?? (isTabBarVisible(pathname) ? tokens.chrome.tabBarHeight : 0);

  const anim = useRef(new Animated.Value(0)).current;
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (timer.current) clearTimeout(timer.current);

    if (!current) {
      Animated.timing(anim, {
        toValue: 0,
        duration: tokens.motion.fast,
        easing: Easing.in(Easing.cubic),
        useNativeDriver: true,
      }).start();
      return;
    }

    if (current.kind === 'success') {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } else if (current.kind === 'danger') {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    }

    anim.setValue(0);
    Animated.timing(anim, {
      toValue: 1,
      duration: tokens.motion.normal,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: true,
    }).start();

    const duration = current.duration ?? DEFAULT_DURATION[current.kind];
    timer.current = setTimeout(hide, duration);

    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [current, anim, hide, tokens.motion.fast, tokens.motion.normal]);

  if (!current) return null;

  const role = tokens.colors.status[current.kind];
  const Icon = ICONS[current.kind];

  return (
    <Animated.View
      pointerEvents="box-none"
      style={{
        position: 'absolute',
        left: tokens.spacing.lg,
        right: tokens.spacing.lg,
        bottom: insets.bottom + offset + tokens.spacing.md,
        zIndex: tokens.zIndex.toast,
        opacity: anim,
        transform: [
          { translateY: anim.interpolate({ inputRange: [0, 1], outputRange: [24, 0] }) },
        ],
      }}
    >
      <Pressable
        onPress={hide}
        accessibilityRole={current.kind === 'danger' ? 'alert' : undefined}
        accessibilityLabel={current.message}
        accessibilityLiveRegion={current.kind === 'danger' ? 'assertive' : 'polite'}
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.md,
          paddingVertical: tokens.spacing.md,
          paddingHorizontal: tokens.spacing.lg,
          borderRadius: tokens.radius.md,
          backgroundColor: tokens.colors.surfaceElevated,
          borderWidth: 1,
          borderColor: role.border,
          ...tokens.elevation.medium,
        }}
      >
        <Icon size={20} color={role.base} />
        <Text role="secondary" tone="primary" style={{ flex: 1 }} numberOfLines={3}>
          {current.message}
        </Text>
        {current.action ? (
          <Text
            role="buttonSmall"
            tone="inherit"
            accessibilityRole="button"
            onPress={() => {
              current.action?.onPress();
              hide();
            }}
            style={{ color: role.base }}
          >
            {current.action.label}
          </Text>
        ) : null}
      </Pressable>
    </Animated.View>
  );
}

export { toast, useToastStore } from '../feedback/toastStore';
