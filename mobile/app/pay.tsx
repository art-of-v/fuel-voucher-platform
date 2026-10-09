import { useCallback, useEffect, useState } from 'react';
import { View, Alert, BackHandler, Pressable } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import Constants from 'expo-constants';
import { WebView, type WebViewNavigation } from 'react-native-webview';
import * as Linking from 'expo-linking';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Smartphone, X } from 'lucide-react-native';

import { Button, ScreenHeader, Text } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
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
  }>();

  const pageUrl = first(params.url);
  const appUrl = first(params.appUrl);

  const [failed, setFailed] = useState(false);

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
        <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', padding: tokens.spacing['2xl'] }}>
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
        <View
          style={{
            flex: 1,
            alignItems: 'center',
            justifyContent: 'center',
            padding: tokens.spacing['2xl'],
            gap: tokens.spacing.lg,
          }}
        >
          <Text tone="muted" center>
            {t('payment.cantRenderBody')}
          </Text>
          <Button label={t('payment.openMonobank')} onPress={fallbackToMonobankApp} fullWidth />
        </View>
      ) : (
        <WebView
          source={{ uri: pageUrl }}
          style={{ flex: 1, backgroundColor: tokens.colors.background }}
          originWhitelist={['https://*', 'http://*']}
          onShouldStartLoadWithRequest={onShouldStartLoadWithRequest}
          onMessage={onMessage}
          onError={fallbackToMonobankApp}
          onHttpError={() => setFailed(true)}
          allowsInlineMediaPlayback
          javaScriptEnabled
          domStorageEnabled
          sharedCookiesEnabled
          setSupportMultipleWindows={false}
        />
      )}

      {appUrl ? (
        <View
          style={{
            paddingHorizontal: tokens.spacing.lg,
            paddingVertical: tokens.spacing.md,
            borderTopWidth: 1,
            borderTopColor: tokens.colors.borderLight,
            backgroundColor: tokens.colors.card,
          }}
        >
          <Button
            label={t('payment.openMonobank')}
            variant="secondary"
            size="md"
            fullWidth
            icon={<Smartphone size={18} color={tokens.colors.text.primary} />}
            onPress={() => void openMonobankApp()}
          />
        </View>
      ) : null}
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
