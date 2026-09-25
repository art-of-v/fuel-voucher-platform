import React from 'react';
import { View, Pressable, StyleSheet } from 'react-native';
import { Home, ShoppingCart, QrCode, User, MapPin } from 'lucide-react-native';
import { Link, usePathname } from 'expo-router';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useStore } from '../core/state/appStore';
import { useAuth } from '../features/auth/hooks/useAuth';
import { useCartStore } from '../features/cart/store/cartStore';
import { useUnreadNotificationCount } from '../features/notifications/hooks/useNotifications';
import { useDesignTokens } from '../core/hooks/useTheme';
import { useI18n } from '../core/i18n';
import { isTabBarVisible } from '../core/navigation/tabBar';
import { Haptics } from '../core/utils/haptics';
import { Text } from '../core/ui';

/**
 * The bottom tab bar.
 *
 * Phase 2 changed how it is positioned and coloured, not what it looks like — the
 * frameless treatment is deliberate. Specifically:
 *
 * - It sat at a hardcoded `bottom: 8`, which put it under the home indicator on
 *   devices with a gesture bar. It now sits on `insets.bottom`.
 * - Its height is `chrome.tabBarHeight`, the same constant `PageLayout` uses to
 *   reserve clearance, so content can no longer be hidden behind it.
 * - The cart badge used a literal `#EF4444`, ignoring the theme. It now uses the
 *   `danger` status role, which is resolved per canvas.
 * - The `activeGlow` shadow blob behind the active icon is gone. Colour and stroke
 *   weight already carry the active state, and glow is not part of this system.
 * - Icons had no accessible names. Each tab now announces its label and selected
 *   state; the visible labels stay off by design.
 *
 * Which routes hide the bar lives in `core/navigation/tabBar`, shared with the
 * layout so the two cannot disagree.
 */
export function BottomTabs() {
  const pathname = usePathname();
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const t = useI18n((s) => s.t);
  const storeAuth = useStore((state) => state.isAuthenticated);
  const { isAuthenticated: hookAuth } = useAuth();
  const isAuthenticated = storeAuth || hookAuth;
  const cartCount = useCartStore((state) => state.getCartItemCount());
  const unreadCount = useUnreadNotificationCount();

  if (!isAuthenticated) return null;
  if (!isTabBarVisible(pathname)) return null;

  const tabs = [
    { name: 'index', icon: Home, path: '/', label: t('nav.stations') },
    { name: 'map', icon: MapPin, path: '/map', label: t('map.title') },
    {
      name: 'basket',
      icon: ShoppingCart,
      path: '/basket',
      label: t('nav.basket'),
      badge: cartCount,
    },
    { name: 'my-codes', icon: QrCode, path: '/my-codes', label: t('nav.codes') },
    {
      name: 'profile',
      icon: User,
      path: '/profile',
      label: t('nav.profile'),
      badge: unreadCount,
    },
  ];

  const isActive = (path: string) => {
    if (path === '/') return pathname === '/';
    return pathname.startsWith(path);
  };

  const inactiveColor = tokens.colors.text.secondary;

  return (
    <View
      style={[
        styles.bar,
        {
          bottom: insets.bottom,
          height: tokens.chrome.tabBarHeight,
          zIndex: tokens.zIndex.tabBar,
        },
      ]}
    >
      {tabs.map((tab) => {
        const active = isActive(tab.path);
        const Icon = tab.icon;
        const badgeLabel =
          typeof tab.badge === 'number' && tab.badge > 0 ? String(tab.badge) : null;

        return (
          <Link key={tab.name} href={tab.path as any} asChild>
            <Pressable
              onPress={() => Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light)}
              accessibilityRole="tab"
              accessibilityLabel={
                badgeLabel ? `${tab.label}, ${badgeLabel}` : tab.label
              }
              accessibilityState={{ selected: active }}
              style={[styles.tab, { minWidth: tokens.touchTarget.min }]}
            >
              <View style={styles.iconWrapper}>
                <Icon
                  size={24}
                  color={active ? tokens.colors.primary : inactiveColor}
                  strokeWidth={active ? 2.2 : 1.6}
                />
                {badgeLabel ? (
                  <View
                    style={[
                      styles.badge,
                      { backgroundColor: tokens.colors.status.danger.base },
                    ]}
                  >
                    <Text
                      role="caption"
                      tone="inherit"
                      // A 16pt badge cannot grow with the system font without
                      // clipping the count; the label on the tab carries the
                      // number for assistive tech.
                      allowFontScaling={false}
                      style={{
                        color: tokens.colors.status.danger.onBase,
                        fontSize: 10,
                        lineHeight: 12,
                      }}
                    >
                      {badgeLabel}
                    </Text>
                  </View>
                ) : null}
              </View>
            </Pressable>
          </Link>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  bar: {
    position: 'absolute',
    left: 0,
    right: 0,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-around',
    paddingHorizontal: 12,
  },
  tab: {
    width: 56,
    height: '100%',
    alignItems: 'center',
    justifyContent: 'center',
  },
  iconWrapper: {
    position: 'relative',
    alignItems: 'center',
    justifyContent: 'center',
  },
  badge: {
    position: 'absolute',
    top: -6,
    right: -8,
    minWidth: 16,
    height: 16,
    borderRadius: 8,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 4,
    zIndex: 10,
  },
});
