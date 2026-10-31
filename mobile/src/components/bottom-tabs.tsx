import React, { useState } from 'react';
import { View, Pressable, StyleSheet, ViewStyle } from 'react-native';
import {
  Building2,
  Home,
  ShoppingCart,
  QrCode,
  User,
  MapPin,
  MessageSquare,
} from 'lucide-react-native';
import { Link, usePathname } from 'expo-router';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useStore } from '../core/state/appStore';
import { useAuth } from '../features/auth/hooks/useAuth';
import { useCartStore } from '../features/cart/store/cartStore';
import { useUnreadNotificationCount } from '../features/notifications/hooks/useNotifications';
import { useDesignTokens } from '../core/hooks/useTheme';
import { useI18n } from '../core/i18n';
import { useIsTabBarVisible } from '../core/navigation/tabBar';
import { useTabBarOverride } from '../core/navigation/tabBarOverride';
import { useAccountContext } from '../features/company/hooks/useAccountContext';
import { useFeedbackChannel } from '../features/feedback/hooks/useFeedbackChannel';
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
 *
 * Multi-company epic #103 S5: in a *worker* context the basket tab is dropped —
 * a worker redeems the fuel their company issued and buys nothing on the
 * employer's behalf. Buying stays available by switching to the personal context.
 *
 * The owner hub (`/company`) is the other context-dependent tab, and the other way
 * round: it appears in an *owner* context and nowhere else. A worker has no roster
 * to manage and no fuel to hand out, and the screen behind the tab redirects to the
 * wallet outside an owner context, so a tab that is always there would be a dead
 * destination for most of the people who see it. The owner's decision (planning #294,
 * 2026-10-21) made the hub a permanent destination rather than a row on the profile:
 * roughly 40% of the screen surface was reachable only through the profile, and the
 * hub is the part of it an owner opens daily.
 *
 * ## Making a tab press unmistakable
 *
 * The bar used to fail on three independent counts, which together made a press
 * easy to miss. All three are fixed here; see `bottom-tabs.test.tsx` for the
 * regression tests that hold them in place.
 *
 * 1. **No visual press feedback at all.** The `Pressable` took a plain style
 *    array, so pressing a tab changed nothing on screen. `IconButton` has
 *    swapped its background on press since Phase 2 — the tab bar never got the
 *    same treatment. It now fills `colors.primarySubtle` while held, which also
 *    makes the target's outline visible for the first time, plus a bounded
 *    `android_ripple` so Android gets a material ripple rather than nothing.
 *    A background rather than `PressableScale`'s scale: this is a fixed-height
 *    row, and scaling a 56pt bar reads as a layout wobble, not a press.
 * 2. **The haptic fired on `onPress`.** `onPress` only runs once the gesture has
 *    been fully recognised as a tap, so a miss produced *no* feedback at all and
 *    a hit produced nothing until the finger came back up — the confirmation was
 *    always late and never confirmed the initial touch-down. It now fires on
 *    `onPressIn`, as `PressableScale` already does, and is `Medium` rather than
 *    `Light`: on Android `Light` is a tick short enough to miss under a moving
 *    thumb.
 * 3. **A fifth of the bar belonged to no tab.** Tabs were a fixed `width: 56`
 *    inside a `justifyContent: 'space-around'` row with `paddingHorizontal: 12`.
 *    On a 390pt screen with five tabs that leaves ~86pt of free space, which
 *    `space-around` splits into ~8.6pt of dead gutter on *each* side of every
 *    tab. Those gutters were where misses landed, and because the visible
 *    affordance is a bare 24pt icon inside a 56pt cell, the boundary was
 *    invisible — a near miss looked exactly like a hit. Tabs are now `flex: 1`
 *    and the outer padding is gone, so the bar is live edge to edge and a miss
 *    is only possible off the ends of the bar.
 *
 * Deliberately **no `hitSlop`**, which is the usual answer here and would be
 * wrong: design rules §10.2 require it only when a target falls *below* 44pt,
 * and `flex: 1` guarantees at least `touchTarget.min` in both axes. Worse,
 * horizontal `hitSlop` on adjacent tabs makes the hit regions overlap, and RN
 * resolves overlapping siblings by z-order rather than nearest-target — which
 * turns a miss into pressing the *wrong* tab instead of into no press. There is
 * no dead space left for it to cover.
 *
 * ## Why the tab's `style` is a plain object, built by hand
 *
 * Every tab is a `Link asChild`, so its `style` does not reach the tab directly —
 * it passes through expo-router's `Slot`, and the installed `@radix-ui/react-slot`
 * merges the two sides with `{ ...slotStyle, ...childStyle }`. That is a *spread*,
 * not a merge, so it only understands plain objects:
 *
 * - a **function** style spreads to `{}` — nothing survives, including `flex`
 * - an **array** style spreads to `{ 0: ..., 1: ... }`, and Yoga reads none of it
 *
 * So a tab styled with `style={({ pressed }) => [...]}` silently renders with no
 * layout at all: shrink-wrapped around its icon and packed at the row's start,
 * which is exactly what the bar looked like when this was first written. The
 * pressed state and the full-width share both need a style prop, so the only
 * thing that satisfies `Slot` is one flat object.
 *
 * Pressed state therefore lives in `useState` here instead of in a style function,
 * and `TAB_TARGET` is a plain literal rather than a `StyleSheet.create` entry,
 * whose value this RN version returns as an opaque identifier. Anything else that
 * needs a dynamic style under a `Link asChild` has the same constraint.
 */
export function BottomTabs() {
  const pathname = usePathname();
  const tabBarVisible = useIsTabBarVisible(pathname);
  // A screen can take the bottom of the display for itself (the map's ranking
  // sheet does). This is a real subscription, not a flag: the bar unmounts.
  const tabBarOverridden = useTabBarOverride((state) => state.hidden);
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const t = useI18n((s) => s.t);
  const storeAuth = useStore((state) => state.isAuthenticated);
  const { isAuthenticated: hookAuth } = useAuth();
  const isAuthenticated = storeAuth || hookAuth;
  const cartCount = useCartStore((state) => state.getCartItemCount());
  const unreadCount = useUnreadNotificationCount();
  // One hook, two answers: the basket is dropped for a worker and the hub is shown
  // only for an owner, so the bar's shape follows the context the user is acting in.
  const accountContext = useAccountContext();
  const isWorkerContext = accountContext.kind === 'worker';
  const isOwnerContext = accountContext.kind === 'owner';
  const [pressedTab, setPressedTab] = useState<string | null>(null);
  // Server-decided, and hidden until the server has answered - see useFeedbackChannel. The tab
  // is absent rather than disabled so a switched-off channel costs the bar no width.
  const feedbackChannel = useFeedbackChannel();

  if (!isAuthenticated) return null;
  if (!tabBarVisible || tabBarOverridden) return null;

  const tabs = [
    { name: 'index', icon: Home, path: '/', label: t('nav.stations') },
    { name: 'map', icon: MapPin, path: '/map', label: t('map.title') },
    ...(isWorkerContext
      ? []
      : [
          {
            name: 'basket',
            icon: ShoppingCart,
            path: '/basket',
            label: t('nav.basket'),
            badge: cartCount,
          },
        ]),
    { name: 'my-codes', icon: QrCode, path: '/my-codes', label: t('nav.codes') },
    ...(feedbackChannel.available
      ? [{ name: 'feedback', icon: MessageSquare, path: '/feedback', label: t('nav.feedback') }]
      : []),
    // The hub sits next to the profile: both are the owner's own tools, and the
    // profile keeps the last slot.
    ...(isOwnerContext
      ? [{ name: 'company', icon: Building2, path: '/company', label: t('nav.company') }]
      : []),
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
      testID="bottom-tab-bar"
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
        const pressed = pressedTab === tab.name;
        const badgeLabel =
          typeof tab.badge === 'number' && tab.badge > 0 ? String(tab.badge) : null;

        return (
          <Link key={tab.name} href={tab.path as any} asChild>
            <Pressable
              onPressIn={() => {
                setPressedTab(tab.name);
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
              }}
              onPressOut={() => setPressedTab((current) => (current === tab.name ? null : current))}
              android_ripple={{ color: tokens.colors.primarySubtle, borderless: false }}
              accessibilityRole="tab"
              accessibilityLabel={badgeLabel ? `${tab.label}, ${badgeLabel}` : tab.label}
              accessibilityState={{ selected: active }}
              // One flat object, never an array or a function — see the note above.
              style={{
                ...TAB_TARGET,
                minWidth: tokens.touchTarget.min,
                ...(pressed
                  ? {
                      backgroundColor: tokens.colors.primarySubtle,
                      borderRadius: tokens.radius.full,
                    }
                  : null),
              }}
            >
              <View style={styles.iconWrapper}>
                <Icon
                  size={24}
                  color={active ? tokens.colors.primary : inactiveColor}
                  strokeWidth={active ? 2.2 : 1.6}
                />
                {badgeLabel ? (
                  <View
                    style={[styles.badge, { backgroundColor: tokens.colors.status.danger.base }]}
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

/**
 * A tab's own layout, kept as a plain literal rather than a `StyleSheet.create`
 * entry: it is spread into the style object handed to `Link asChild`, and this RN
 * version's `StyleSheet.create` yields an opaque identifier rather than an object.
 *
 * `flex: 1` rather than a fixed width, so the row shares its full width between
 * the tabs and no part of the bar is unowned. `minWidth` is added per tab from the
 * tokens, flooring it at the 44pt minimum on a screen too narrow to share out.
 */
const TAB_TARGET: ViewStyle = {
  flex: 1,
  height: '100%',
  alignItems: 'center',
  justifyContent: 'center',
  // Keeps the Android ripple inside the pill's rounded corners rather than letting
  // it bleed out square.
  overflow: 'hidden',
};

const styles = StyleSheet.create({
  bar: {
    position: 'absolute',
    left: 0,
    right: 0,
    flexDirection: 'row',
    alignItems: 'center',
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
