/// <reference types="nativewind/types" />
import { useMemo, useState } from 'react';
import { View, Text, Pressable, StyleSheet, ActivityIndicator } from 'react-native';
import { useRouter, Redirect } from 'expo-router';
import { User, Building2, Zap, Tag } from 'lucide-react-native';
import { useStore } from '../src/core/state/appStore';
import { useCartStore } from '../src/features/cart/store/cartStore';
import type { CartItem } from '../src/features/cart/types';
import { useI18n } from '../src/core/i18n';
import {
  createBulkMonobankInvoice,
  purchaseErrorKey,
  type CheckoutSettlementRoute,
} from '../src/features/vouchers/api/purchases';
import { paymentHref } from '../src/features/payments/paymentSession';
import { useAccountContext } from '../src/features/company/hooks/useAccountContext';
import { GridBackground, GridPageLayout, ScreenHeader } from '../src/core/ui';
import { PhoneAuthForm } from '../src/features/auth/components/PhoneAuthForm';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { useDesignTokens } from '../src/core/hooks/useTheme';

import { formatMoney } from '../src/core/utils/currency';
import { Haptics } from '../src/core/utils/haptics';

/**
 * What this line costs, per what the customer was quoted on the package card. A line with no term falls
 * back to the package price, which is the full remaining term at the normal rate.
 */
function lineTotalFor(item: CartItem): number {
  return (item.termLinePrice ?? item.package.price) * item.quantity;
}

export default function CheckoutScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();

  // Built where `tokens` is in scope rather than at module level. The values below
  // are all theme-independent, but a themed colour added here later would silently
  // freeze at the default theme instead of following a switch.
  const styles = useMemo(
    () =>
      StyleSheet.create({
        sectionLabel: {
          fontFamily: 'Inter-Bold',
          fontSize: tokens.type.caption.fontSize,
          letterSpacing: 2,
          textTransform: 'uppercase',
          marginBottom: tokens.spacing.md,
          paddingHorizontal: tokens.spacing.xs,
        },
        summaryCard: {
          borderWidth: 1,
          borderRadius: tokens.radius.lg,
          padding: tokens.spacing.xl,
          gap: tokens.spacing.lg,
        },
        summaryRow: {
          flexDirection: 'row',
          justifyContent: 'space-between',
          alignItems: 'center',
        },
        summaryItemTitle: {
          fontFamily: 'Rajdhani-Bold',
          fontSize: tokens.type.numericSmall.fontSize,
          textTransform: 'uppercase',
        },
        summaryItemSubtitle: {
          fontFamily: 'Inter-Bold',
          fontSize: tokens.type.caption.fontSize,
          textTransform: 'uppercase',
        },
        summaryItemPrice: {
          fontFamily: 'Inter-Black',
          fontSize: tokens.type.heading.fontSize,
        },
        contextRow: {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.md,
          padding: tokens.spacing.xl,
          borderWidth: 1,
          borderRadius: tokens.radius.lg,
        },
        payButton: {
          width: '100%',
          paddingVertical: tokens.spacing.fine18,
          borderRadius: tokens.radius.fine12,
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'center',
          gap: tokens.spacing.md,
        },
        payButtonText: {
          fontFamily: 'Inter-Black',
          fontSize: tokens.type.numericSmall.fontSize,
          letterSpacing: 1,
          textTransform: 'uppercase',
        },
        footerButtons: {
          gap: tokens.spacing.md,
        },
        promoButton: {
          width: '100%',
          paddingVertical: tokens.spacing.fine18,
          borderRadius: tokens.radius.fine12,
          borderWidth: 1,
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'center',
          gap: tokens.spacing.md,
        },
        promoButtonText: {
          fontFamily: 'Inter-Black',
          fontSize: tokens.type.numericSmall.fontSize,
          letterSpacing: 1,
          textTransform: 'uppercase',
        },
        contextText: {
          flex: 1,
          fontFamily: 'Inter-Black',
          fontSize: tokens.type.numericSmall.fontSize,
          textTransform: 'uppercase',
        },
      }),
    [tokens],
  );
  const soft = tokens.surface.soft;
  const { t } = useI18n();
  const { isAuthenticated: storeAuth, login } = useStore();
  const { cart, getDiscountedTotal, clearCart } = useCartStore();
  const { isAuthenticated: hookAuth, isLoading: authLoading, user } = useAuth();
  const isAuthenticated = storeAuth || hookAuth;
  const [isProcessing, setIsProcessing] = useState(false);

  // The second button, and nothing else. A Manager decides which accounts are on the offer
  // (planning #889), the server answers the decision as `promoAccessAvailable` on the account, and an
  // absent or false value means the customer is not on it — the app never offers a route it would be
  // refused for. The flag is a hint: the checkout re-checks it, so a copy that went stale in the
  // background cannot order fuel by the promotion either.
  const isOnPromoOffer = user?.promoAccessAvailable === true;

  // Multi-company (epic #103 S4 + S5): a purchase always lands in the ACTIVE context,
  // never a separate in-checkout choice. We resolve the context the top switcher
  // set (`currentLegalEntityId`) — personal root, a stale/foreign id, or a company
  // the user only works for resolves to a personal purchase, so a checkout can never
  // buy into a company the user does not own.
  const context = useAccountContext();
  const activeCompany = context.kind === 'owner' ? context.company : null;
  // No buying in a worker context (S5): a worker never spends the employer's money.
  // The basket tab is already hidden there; this is the backstop.
  const isWorkerContext = context.kind === 'worker';

  const GLOBAL_PADDING = tokens.spacing.containerPadding;
  const discountedTotal = getDiscountedTotal();

  const handlePaymentEnd = async (route?: CheckoutSettlementRoute) => {
    // Before the guard, not after it: returning past `setIsProcessing(true)` left the
    // pay button disabled behind a spinner for good. Unreachable from the UI today —
    // the footer only renders with a non-empty basket — but the handler should not
    // depend on the caller for that.
    if (cart.length === 0) return;

    try {
      setIsProcessing(true);

      // Checked: createMonobankInvoice below will trigger SecurityService.signPayload
      // inside apiFetch, which handles the single, cryptographically secure Face ID prompt.
      // We no longer need this manual LocalAuthentication block which caused a double prompt.

      // Aggregate all cart items into one Monobank invoice
      const items = cart.map((item) => ({
        packageId: item.package.id,
        brandId: item.station.id,
        brandName: item.station.name,
        fuelType: item.fuel.id,
        fuelName: item.fuel.name,
        liters: item.package.liters,
        quantity: item.quantity,
        price: lineTotalFor(item),
        termCode: item.termCode,
      }));

      // Buy into the active company context, or personal when none resolves.
      const legalEntityId = activeCompany?.id;

      const response = await createBulkMonobankInvoice(items, legalEntityId, route);

      if (response.alreadyPaid) {
        // Trigger 2 (planning #867): the platform was already holding money for this customer that
        // pays for this cart, so the checkout settled the order instead of asking the bank. There is
        // no page to open and nothing left to pay — which is why the branch is above the `pageUrl`
        // check below and not folded into it: the same empty URL means a failure without this flag.
        clearCart();
        alert(t('purchase.alreadyPaid'));
        router.replace('/my-codes');
        return;
      }

      if (response.settlementRoute === 'Declared') {
        // The promotional button's answer, and the only shape of it the app can tell apart from a
        // failure by what the server said rather than by what is missing: the order exists, the bank
        // was never asked, and the money that will pay for it arrives on the settlement channel —
        // either already held, or declared for it later. An empty `pageUrl` on its own cannot say
        // that, which is why this branch sits above the one below and is not folded into it.
        //
        // The cart goes with the order, exactly as on the paid path: the order is placed and a second
        // tap would be refused as a duplicate. The wallet's processing list is where the customer
        // watches it, and where the order says what it is waiting on.
        clearCart();
        alert(t('purchase.promoOrdered'));
        router.replace('/my-codes');
        return;
      }

      if (response.pageUrl) {
        // Pay in the app's own payment screen rather than handing the customer to a browser:
        // Monobank returns an embeddable page (displayType:"iframe") plus, when we ask for it,
        // a deep link that opens the Monobank app directly. Both are on /pay.
        router.push(
          paymentHref({
            orderId: response.orderIds[0] ?? '',
            pageUrl: response.pageUrl,
            appUrl: response.appUrl,
            // The payment screen shows this above the acquirer's own frame, so the amount is
            // visible even if that frame never renders. The cart is the authority here: it is the
            // figure the customer was just shown on the confirm button.
            amount: discountedTotal,
          }),
        );

        // Clear the cart now: the order exists whether or not the payment completes, and
        // /pay returns to the wallet where its real status is shown.
        clearCart();
      } else {
        throw new Error('No payment URL received');
      }
    } catch (e) {
      console.error('Payment error details:', e);
      // Prefer the caller's own wording for a code we recognise. Without this the customer reads the
      // server's English sentence on a Ukrainian screen — for `below_cost` it even explained our
      // margin decision, which is nothing they can act on.
      const code = e instanceof Error ? (e as Error & { code?: string }).code : undefined;
      const key = purchaseErrorKey(code);
      alert(key ? t(key) : e instanceof Error ? e.message : t('common.error'));
      setIsProcessing(false);
    }
  };

  const Header = (
    <ScreenHeader title={t('basket.checkoutTitle')} subtitle={t('checkout.verifyOrder')} />
  );

  const fixedFooter =
    cart.length > 0 ? (
      // Padding, the hairline and the bottom safe-area inset are owned by
      // PageLayout's footer slot — the screen no longer sets paddingBottom: 72.
      //
      // Two buttons stack rather than sit side by side: each carries a full-width uppercase label
      // («СПЛАТИТИ 1 250,00 ₴», «ОТРИМАТИ ЗА ПРОМОАКЦІЄЮ»), and half a phone screen truncates the
      // money in the first one. They are two different money decisions, so neither is styled as the
      // other's decoration.
      <View style={styles.footerButtons}>
        <Pressable
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
            handlePaymentEnd();
          }}
          disabled={isProcessing}
          accessibilityRole="button"
          style={[
            styles.payButton,
            { backgroundColor: tokens.colors.primary },
            isProcessing && { opacity: 0.5 },
          ]}
        >
          {isProcessing ? (
            <ActivityIndicator size="small" color={tokens.colors.text.onPrimary} />
          ) : (
            <>
              <Zap size={18} color={tokens.colors.text.onPrimary} />
              <Text style={[styles.payButtonText, { color: tokens.colors.text.onPrimary }]}>
                {`${t('packages.payTitle')} ${formatMoney(discountedTotal)}`}
              </Text>
            </>
          )}
        </Pressable>

        {isOnPromoOffer && (
          <Pressable
            onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
              handlePaymentEnd('Declared');
            }}
            disabled={isProcessing}
            accessibilityRole="button"
            style={[
              styles.promoButton,
              { borderColor: tokens.colors.borderLight },
              isProcessing && { opacity: 0.5 },
            ]}
          >
            <Tag size={18} color={tokens.colors.primary} />
            <Text style={[styles.promoButtonText, { color: tokens.colors.text.primary }]}>
              {t('packages.promoTitle')}
            </Text>
          </Pressable>
        )}
      </View>
    ) : null;

  if (!isAuthenticated && !authLoading) {
    return (
      <GridPageLayout background={<GridBackground />}>
        <View style={{ flex: 1, justifyContent: 'center', paddingBottom: tokens.spacing['4xl'] }}>
          <PhoneAuthForm
            onSuccess={() => {
              login();
            }}
          />
        </View>
      </GridPageLayout>
    );
  }

  if (isWorkerContext) {
    return <Redirect href="/my-codes" />;
  }

  return (
    <GridPageLayout header={Header} fixedFooter={fixedFooter}>
      <View style={{ gap: tokens.spacing['2xl'], paddingHorizontal: GLOBAL_PADDING }}>
        {/* Order Summary */}
        <View>
          <Text
            allowFontScaling={false}
            style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}
          >
            {t('checkout.orderSummary')}
          </Text>
          <View
            style={[
              styles.summaryCard,
              {
                backgroundColor: tokens.colors.card,
                borderColor: tokens.colors.borderLight,
                borderRadius: soft ? tokens.surface.card : undefined,
              },
            ]}
          >
            {cart.map((item) => (
              <View key={item.id} style={styles.summaryRow}>
                <View>
                  <Text
                    allowFontScaling={false}
                    style={[styles.summaryItemTitle, { color: tokens.colors.text.primary }]}
                  >
                    {item.station.logoText || item.station.name}
                  </Text>
                  <Text
                    allowFontScaling={false}
                    style={[styles.summaryItemSubtitle, { color: tokens.colors.primary }]}
                  >
                    {item.fuel.name} x {item.quantity}
                  </Text>
                </View>
                <Text
                  allowFontScaling={false}
                  style={[styles.summaryItemPrice, { color: tokens.colors.text.primary }]}
                >
                  {formatMoney(lineTotalFor(item))}
                </Text>
                {/* The term was chosen on the package card, next to the price it changed. Here it is
                    stated, not re-offered: a second editor at payment reads as a surcharge.
                    Labelled, and in the short form: a bare "1 тиждень" beside a price reads as a
                    quantity, not as how long the fuel stays valid (#174). Both keys already exist,
                    so no new copy and no i18n parity work. */}
                {item.termCode && (
                  <Text
                    allowFontScaling={false}
                    style={[styles.summaryItemSubtitle, { color: tokens.colors.text.dim }]}
                  >
                    {`${t('term.label')}: ${t(`term.short.${item.termCode}`)}`}
                  </Text>
                )}
              </View>
            ))}
          </View>
        </View>

        {/* Where the purchase lands — the active context, not a choice (epic #103 S4).
                    Read-only so the user can see the target before paying; switch it via the
                    top context switcher, not here. */}
        <View>
          <Text
            allowFontScaling={false}
            style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}
          >
            {t('checkout.buyingFor')}
          </Text>
          <View
            style={[
              styles.contextRow,
              {
                backgroundColor: tokens.colors.card,
                borderColor: tokens.colors.borderLight,
                borderRadius: soft ? 12 : 4,
              },
            ]}
          >
            {activeCompany ? (
              <Building2 size={18} color={tokens.colors.primary} />
            ) : (
              <User size={18} color={tokens.colors.primary} />
            )}
            <Text
              allowFontScaling={false}
              numberOfLines={1}
              style={[styles.contextText, { color: tokens.colors.text.primary }]}
            >
              {activeCompany ? activeCompany.name : t('checkout.buyingPersonal')}
            </Text>
          </View>
        </View>
      </View>
    </GridPageLayout>
  );
}
