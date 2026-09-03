import React from 'react';
import {
  KeyboardAvoidingView,
  Platform,
  RefreshControlProps,
  ScrollView,
  StyleProp,
  View,
  ViewStyle,
} from 'react-native';
import { SafeAreaView, useSafeAreaInsets } from 'react-native-safe-area-context';
import { usePathname } from 'expo-router';
import { useDesignTokens } from '../hooks/useTheme';
import { isTabBarVisible } from '../navigation/tabBar';

export interface PageLayoutProps {
  children: React.ReactNode;
  /** Sticky header — normally a `ScreenHeader`. Does not scroll. */
  header?: React.ReactNode;
  /**
   * Pinned action bar at the bottom. Receives the safe-area inset and clears the
   * tab bar automatically; the screen must not add its own bottom padding.
   */
  footer?: React.ReactNode;
  /** Render content in a fixed frame instead of a scroll view (maps, QR). */
  scroll?: boolean;
  /** Pull-to-refresh. Ignored when `scroll` is false. */
  refreshControl?: React.ReactElement<RefreshControlProps>;
  /**
   * Horizontal content padding. `none` for full-bleed lists that pad their own
   * rows; the default applies `spacing.containerPadding`.
   */
  padding?: 'default' | 'none';
  /**
   * Override tab-bar clearance. By default it is derived from the current route
   * via `isTabBarVisible`, so a screen never has to know or guess.
   */
  hasTabBar?: boolean;
  /** A decorative background layer. There is no default — see the note below. */
  background?: React.ReactNode;
  contentContainerStyle?: StyleProp<ViewStyle>;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

export interface ContentInsets {
  /** Bottom padding that clears the tab bar, a footer and the home indicator. */
  bottom: number;
  /** The standard horizontal content padding. */
  horizontal: number;
  /** Whether the tab bar is on screen for the current route. */
  hasTabBar: boolean;
}

/**
 * The derived content insets for the current route.
 *
 * `PageLayout` applies these itself. Use the hook directly only when a screen owns
 * its own scroll container — a `FlatList`, a `SectionList`, or a `ScrollView` that
 * needs a `refreshControl` and its own `onScroll`. The point is that the number
 * still comes from one place.
 *
 * Do not add anything to the returned `bottom`. If content is still clipped, the
 * chrome measurement in `layout.ts` is wrong and should be corrected there.
 */
export function useContentInsets(options?: {
  hasFooter?: boolean;
  hasTabBar?: boolean;
}): ContentInsets {
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const pathname = usePathname();

  const hasTabBar = options?.hasTabBar ?? isTabBarVisible(pathname);

  return {
    hasTabBar,
    horizontal: tokens.spacing.containerPadding,
    bottom:
      insets.bottom +
      (hasTabBar ? tokens.chrome.tabBarHeight : 0) +
      (options?.hasFooter ? tokens.chrome.footerHeight : 0) +
      tokens.chrome.contentBottomGap,
  };
}

/**
 * The screen container. Owns every ergonomic rule so no screen has to.
 *
 * What it owns:
 * - **Safe areas.** Top/left/right via `SafeAreaView`; the bottom inset is applied
 *   to content padding and to the footer, never to the canvas, so backgrounds
 *   still reach the edge of the display.
 * - **Bottom clearance.** Content bottom padding is derived:
 *   `insets.bottom + (tab bar ? tabBarHeight : 0) + (footer ? footerHeight : 0) +
 *   contentBottomGap`. This replaces the previous fixed `paddingBottom: 150` in
 *   the shared layout plus per-screen additions of 84, 100 and 120.
 * - **Keyboard.** `KeyboardAvoidingView` on iOS, `automaticallyAdjustKeyboardInsets`
 *   on the scroll view, and `keyboardShouldPersistTaps="handled"` so a tap on a
 *   button while the keyboard is up hits the button rather than only dismissing
 *   the keyboard.
 * - **Scroll behaviour.** Vertical indicator hidden, `contentInsetAdjustmentBehavior`
 *   left automatic, overscroll consistent.
 *
 * On backgrounds: there is deliberately **no default**. The previous layout
 * defaulted to `<GridBackground />`, which put a decorative grid behind every
 * screen in the product including the cart and the checkout total. A background
 * is now opt-in per screen.
 */
export function PageLayout({
  children,
  header,
  footer,
  scroll = true,
  refreshControl,
  padding = 'default',
  hasTabBar,
  background,
  contentContainerStyle,
  style,
  testID,
}: PageLayoutProps) {
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const { bottom: contentBottomPadding, hasTabBar: tabBarShown } = useContentInsets({
    hasFooter: !!footer,
    hasTabBar,
  });

  const horizontal = padding === 'none' ? 0 : tokens.spacing.containerPadding;

  const body = scroll ? (
    <ScrollView
      style={{ flex: 1 }}
      contentContainerStyle={[
        {
          paddingHorizontal: horizontal,
          paddingBottom: contentBottomPadding,
          gap: tokens.spacing.lg,
        },
        contentContainerStyle,
      ]}
      showsVerticalScrollIndicator={false}
      keyboardShouldPersistTaps="handled"
      keyboardDismissMode="interactive"
      automaticallyAdjustKeyboardInsets
      refreshControl={refreshControl}
    >
      {children}
    </ScrollView>
  ) : (
    <View
      style={[
        {
          flex: 1,
          paddingHorizontal: horizontal,
          paddingBottom: contentBottomPadding,
        },
        contentContainerStyle,
      ]}
    >
      {children}
    </View>
  );

  return (
    <SafeAreaView
      edges={['top', 'left', 'right']}
      style={[{ flex: 1, backgroundColor: tokens.colors.background }, style]}
      testID={testID}
    >
      {background ? (
        <View
          pointerEvents="none"
          style={{
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            zIndex: tokens.zIndex.background,
          }}
        >
          {background}
        </View>
      ) : null}

      <KeyboardAvoidingView
        style={{ flex: 1 }}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        {header ? (
          <View style={{ zIndex: tokens.zIndex.header, paddingTop: tokens.spacing.sm }}>
            {header}
          </View>
        ) : null}

        {body}

        {footer ? (
          <View
            style={{
              zIndex: tokens.zIndex.footer,
              // A pinned action bar is always inset, even when the scroll content
              // is full-bleed. Footer content must not add its own padding.
              paddingHorizontal: tokens.spacing.containerPadding,
              paddingTop: tokens.spacing.lg,
              // The footer clears the tab bar and the home indicator itself, so
              // screens never write a bottom padding number.
              paddingBottom:
                insets.bottom +
                (tabBarShown ? tokens.chrome.tabBarHeight : 0) +
                tokens.spacing.lg,
              borderTopWidth: 1,
              borderTopColor: tokens.colors.borderSubtle,
              backgroundColor: tokens.colors.background,
            }}
          >
            {footer}
          </View>
        ) : null}
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
