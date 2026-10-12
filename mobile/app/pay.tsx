import { useCallback, useEffect, useState } from 'react';
import { View, Alert, BackHandler, Pressable, ActivityIndicator, StyleSheet } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import Constants from 'expo-constants';
import { WebView, type WebViewNavigation } from 'react-native-webview';
import * as Linking from 'expo-linking';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Smartphone, X } from 'lucide-react-native';

import { Button, ErrorState, ScreenHeader, Text } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { formatMoney } from '../src/core/utils/currency';
import { Haptics } from '../src/core/utils/haptics';
import { isAppDeepLink } from '../src/features/payments/paymentSession';

/**
 * The in-app payment screen: Monobank's embeddable page rendered full-screen, so paying
 * never throws the customer out into a browser.
 *
 * WHY A WEBVIEW RATHER THAN `expo-web-browser`
 * SFSafariViewController keeps the browser chrome out of the app but it is still a browser -
 * a URL bar, a different app's identity, and on iOS it breaks the Apple Pay flow that is
 * what makes 3DS a non-issue. `displayType: "iframe"` gives us a page built for this.
 *
 * WHAT THIS SCREEN HAS TO HANDLE (each is a way a payment silently dies)
 *  1. `postMessage` from the frame. Monobank's widget drives its host through it:
 *     `close-button` (the customer's own "back") and `monopay-link` (go to the Monobank app
 *     on mobile). Handled in onMessage; without it the customer taps back and nothing happens.
 *  2. The post-payment redirect. `redirectUrl` is a deep link like `fuelflow://payment-result`.
 *     Inside a WebView that navigates the WebView rather than the app, so the status screen
 *     never opens and the customer is left on a blank page. Caught in
 *     onShouldStartLoadWithRequest, where the scheme list comes from the manifest instead of
 *     a constant, so a new scheme in app.config.ts needs no change here.
 *  3. WebView failure. A provider can block embedded user agents, and a WebView that never
 *     loads is an empty screen on a checkout where the customer has already committed money.
 *     Every failure path falls back to the appUrl deep link, which resolves to the Monobank
 *     app or, failing that, the web.
 *
 * WHAT IT DELIBERATELY DOES NOT DO
 * It does not decide whether the payment succeeded. The webhook does that, asynchronously,
 * and the wallet screen re-reads it. Calling it a success from here would be a guess: the
 * customer's bank can still decline after the redirect.
 *
 * WHAT THE APP OWNS AROUND THE FRAME
 * Monobank paints the middle of this screen; everything above and below it is ours, and it is
 * where the screen either feels finished or feels like a debug view:
 *
 *  - The amount. The frame is a remote page we do not control, and the moments it matters most -
 *    slow network, or the fallback after it fails - are exactly the moments the customer should
 *    still see what they are paying for. It arrives as a route param from the cart rather than
 *    being fetched here: a request on the payment screen is a request that can hang on the one
 *    screen whose whole job is not to.
 *  - The first paint. Monobank's page is light and ours is dark, so an unpainted WebView is a
 *    white flash followed by a dark frame - jank on the highest-stakes screen in the app. A
 *    branded loading state covers it and reappears on every subsequent navigation inside the frame
 *    (3DS), because each of those is a full page load too.
 *  - The failure state. A muted sentence and a button is what a dead end looks like. It gets the
 *    same icon-in-a-circle treatment as the rest of the app's empty states, and names the
 *    recovery rather than the symptom.
 */

/** Messages Monobank's embedded widget posts to its host. */
type MonobankFrameMessage = { message?: string; value?: string };

export default function PaymentScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const params = useLocalSearchParams<{
    url?: string | string[];
    appUrl?: string | string[];
    amount?: string | string[];
  }>();

  const pageUrl = first(params.url);
  const appUrl = first(params.appUrl);
  const amount = first(params.amount);

  const [failed, setFailed] = useState(false);
  // True until the frame's first paint, and again on every in-frame navigation: 3DS and the
  // acquirer's own redirect both replace the document, and each one blanks it.
  const [loading, setLoading] = useState(true);

  const finish = useCallback(() => {
    // `replace`, not `push`: /pay is a step between checkout and the wallet, and leaving it on
    // the stack makes "back" from the wallet return to a payment page for a resolved order.
    router.replace('/my-codes');
  }, [router]);

  const openMonobankApp = useCallback(async () => {
    if (!appUrl) {
      // No deep link - an order re-opened from the wallet, where appUrl is not persisted. The
      // page we are already showing is the only route, so send the customer to it externally
      // rather than doing nothing.
      if (pageUrl) await Linking.openURL(pageUrl);
      return;
    }
    await Linking.openURL(appUrl);
  }, [appUrl, pageUrl]);

  const fallbackToMonobankApp = useCallback(() => {
    Alert.alert(t('payment.cantRenderTitle'), t('payment.cantRenderBody'), [
      { text: t('common.cancel'), style: 'cancel' },
      {
        text: t('payment.openMonobank'),
        onPress: () => {
          void openMonobankApp();
        },
      },
    ]);
  }, [openMonobankApp, t]);

  // Hardware back must not silently drop the customer off a live payment: ask first, and keep
  // the fallback within reach of the same dialog.
  useEffect(() => {
    const sub = BackHandler.addEventListener('hardwareBackPress', () => {
      Alert.alert(t('payment.leaveTitle'), t('payment.leaveBody'), [
        { text: t('common.cancel'), style: 'cancel' },
        { text: t('payment.leaveConfirm'), style: 'destructive', onPress: () => router.back() },
      ]);
      return true;
    });
    return () => sub.remove();
  }, [router, t]);

  const onMessage = useCallback(
    (event: { nativeEvent: { data: string } }) => {
      let payload: MonobankFrameMessage;
      try {
        payload = JSON.parse(event.nativeEvent.data);
      } catch {
        // The frame can post anything; a message that is not ours to act on gets ignored.
        return;
      }

      if (payload.message === 'close-button') {
        finish();
        return;
      }

      // On mobile the frame asks us to hand the payment to the Monobank app rather than trying
      // to switch apps itself - a page inside a WebView cannot.
      if (payload.message === 'monopay-link' && payload.value) {
        void Haptics.selectionAsync();
        void Linking.openURL(payload.value);
      }
    },
    [finish],
  );

  const onShouldStartLoadWithRequest = useCallback(
    (request: WebViewNavigation) => {
      if (isAppDeepLink(request.url, collectSchemes())) {
        // Do not navigate the WebView to it: this is the post-payment redirect, and the wallet
        // it returns to reads the real, webhook-settled status.
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        finish();
        return false;
      }
      return true;
    },
    [finish],
  );

  if (!pageUrl) {
    // Unreachable by navigation - /pay with no url. Say so rather than render an empty screen
    // that looks like a hung payment.
    return (
      <SafeAreaView style={{ flex: 1, backgroundColor: tokens.colors.background }}>
        <ScreenHeader title={t('payment.title')} />
        <View
          style={{
            flex: 1,
            alignItems: 'center',
            justifyContent: 'center',
            padding: tokens.spacing['2xl'],
          }}
        >
          <Text tone="muted" center testID="payment-missing-url">
            {t('payment.missingUrl')}
          </Text>
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: tokens.colors.background }} edges={['top']}>
      <ScreenHeader
        title={t('payment.title')}
        subtitle={t('payment.subtitle')}
        onBack={() => router.back()}
        actions={
          <Pressable
            onPress={finish}
            accessibilityRole="button"
            accessibilityLabel={t('payment.close')}
            hitSlop={12}
          >
            <X size={22} color={tokens.colors.text.dim} />
          </Pressable>
        }
      />

      {failed ? (
        <ErrorState
          testID="payment-failed"
          fullScreen
          title={t('payment.cantRenderTitle')}
          description={t('payment.cantRenderBody')}
          // Not a retry: reloading the frame the acquirer already refused to serve is the one
          // thing that will not help. The way out is the Monobank app, where the same invoice
          // opens natively and 3DS is handled by the bank.
          secondaryAction={{
            label: t('payment.openMonobank'),
            onPress: fallbackToMonobankApp,
          }}
        />
      ) : (
        <View style={{ flex: 1 }}>
          <WebView
            source={{ uri: pageUrl }}
            style={{ flex: 1, backgroundColor: tokens.colors.background }}
            originWhitelist={['https://*', 'http://*']}
            onShouldStartLoadWithRequest={onShouldStartLoadWithRequest}
            onMessage={onMessage}
            onError={fallbackToMonobankApp}
            onHttpError={() => setFailed(true)}
            onLoadStart={() => setLoading(true)}
            onLoadEnd={() => setLoading(false)}
            allowsInlineMediaPlayback
            javaScriptEnabled
            domStorageEnabled
            sharedCookiesEnabled
            setSupportMultipleWindows={false}
          />

          {/* Covers the frame until it paints. Absolutely positioned so it never changes the
              frame's own size: a layout that grows when the loader disappears is a second
              reflow on the way into a payment. */}
          {loading && (
            <View
              testID="payment-loading"
              accessibilityLabel={t('payment.loading')}
              style={{
                ...StyleSheet.absoluteFillObject,
                alignItems: 'center',
                justifyContent: 'center',
                gap: tokens.spacing.md,
                backgroundColor: tokens.colors.surfaceSunken,
              }}
            >
              <ActivityIndicator size="large" color={tokens.colors.primary} />
              <Text tone="muted">{t('payment.loading')}</Text>
            </View>
          )}
        </View>
      )}

      {/* What is being paid, in the app's own words, above a page we do not control.

          Rendered only when checkout passed the amount. It is a route param and not a fetch on
          purpose - see the header note - so on an order re-opened from the wallet it is simply
          absent and this row does not appear, rather than showing a spinner that can hang. */}
      {amount !== undefined && Number.isFinite(Number(amount)) && (
        <View
          testID="payment-amount"
          style={{
            flexDirection: 'row',
            alignItems: 'baseline',
            justifyContent: 'space-between',
            gap: tokens.spacing.md,
            paddingHorizontal: tokens.spacing.lg,
            paddingVertical: tokens.spacing.md,
            borderBottomWidth: tokens.spacing.hairline,
            borderBottomColor: tokens.colors.borderSubtle,
            backgroundColor: tokens.colors.surface,
          }}
        >
          <Text tone="muted">{t('payment.payingFor')}</Text>
          <Text role="numericLarge">{formatMoney(Number(amount))}</Text>
        </View>
      )}

      {/* Always rendered, never gated on `appUrl`.

          `appUrl` is a route parameter, not something read from the order, so it is simply absent
          whenever the customer returns to an unpaid order from the wallet - which is the common
          path after an abandoned or failed checkout. Gating the button on it meant those customers
          got no Monobank-app route at all and only the card form inside this WebView, with no 3DS
          hand-off of our own.

          `openMonobankApp` already handles the missing case by opening `pageUrl` externally, so
          rendering unconditionally turns that existing fallback back into a reachable path instead
          of leaving it dead code behind a render guard. */}
      <View
        style={{
          paddingHorizontal: tokens.spacing.lg,
          paddingVertical: tokens.spacing.md,
          borderTopWidth: tokens.spacing.hairline,
          borderTopColor: tokens.colors.borderSubtle,
          backgroundColor: tokens.colors.surface,
        }}
      >
        <Button
          label={t('payment.openMonobank')}
          testID="payment-open-monobank"
          variant="secondary"
          size="md"
          fullWidth
          icon={<Smartphone size={18} color={tokens.colors.text.primary} />}
          onPress={() => void openMonobankApp()}
        />
      </View>
    </SafeAreaView>
  );
}

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/**
 * The app's own URL schemes, taken from the native manifest rather than a hard-coded list, so
 * production (`fuelflow`) and the TestFlight build (`fuelflow-staging`) are both covered by the
 * redirect guard without editing this file.
 *
 * Defensive about the lookup: `expo-constants`' `scheme` is absent in bare test renderers, and
 * this runs during navigation rather than behind a try/catch.
 */
function collectSchemes(): string[] {
  const fromManifest = Constants.expoConfig?.scheme as string | undefined;
  const schemes = ['fuelflow', 'fuelflow-staging'];
  if (fromManifest) schemes.push(fromManifest);
  return Array.from(new Set(schemes));
}
