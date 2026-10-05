/// <reference types="nativewind/types" />
import { useState } from 'react';
import { View, Text, Pressable, StyleSheet, ActivityIndicator } from 'react-native';
import { useRouter, Redirect } from 'expo-router';
import { User, Building2, Zap } from 'lucide-react-native';
import { useStore } from '../src/core/state/appStore';
import { useCartStore } from '../src/features/cart/store/cartStore';
import type { CartItem } from '../src/features/cart/types';
import { useI18n } from '../src/core/i18n';
import { createBulkMonobankInvoice } from '../src/features/vouchers/api/purchases';
import { useAccountContext } from '../src/features/company/hooks/useAccountContext';
import { GridBackground, GridPageLayout, ScreenHeader } from '../src/core/ui';
import { PhoneAuthForm } from '../src/features/auth/components/PhoneAuthForm';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { useDesignTokens } from '../src/core/hooks/useTheme';

import { formatMoney } from '../src/core/utils/currency';
import * as Linking from 'expo-linking';

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
  const soft = tokens.surface.soft;
  const { t } = useI18n();
  const { isAuthenticated: storeAuth, login } = useStore();
  const { cart, getDiscountedTotal, clearCart } = useCartStore();
  const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
  const isAuthenticated = storeAuth || hookAuth;
  const [isProcessing, setIsProcessing] = useState(false);

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

  const handlePaymentEnd = async () => {
    try {
      setIsProcessing(true);

      if (cart.length === 0) return;

      // Checked: createMonobankInvoice below will trigger SecurityService.signPayload
      // inside apiFetch, which handles the single, cryptographically secure Face ID prompt.
      // We no longer need this manual LocalAuthentication block which caused a double prompt.

      // Aggregate all cart items into one Monobank invoice
      const items = cart.map((item) => ({
        packageId: item.package.id,
        stationId: item.station.id,
        stationName: item.station.name,
        fuelType: item.fuel.id,
        fuelName: item.fuel.name,
        liters: item.package.liters,
        quantity: item.quantity,
        price: lineTotalFor(item),
        termCode: item.termCode,
      }));

      // Buy into the active company context, or personal when none resolves.
      const legalEntityId = activeCompany?.id;

      const response = await createBulkMonobankInvoice(items, legalEntityId);

      if (response.pageUrl) {
        // Open Monobank payment page
        await Linking.openURL(response.pageUrl);

        // Clear cart and move to my-codes (status will update via webhook)
        clearCart();
        router.push('/my-codes');
      } else {
        throw new Error('No payment URL received');
      }
    } catch (e) {
      console.error('Payment error details:', e);
      alert(e instanceof Error ? e.message : 'Payment initialization failed');
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
      <Pressable
        onPress={handlePaymentEnd}
        disabled={isProcessing}
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
    ) : null;

  if (!isAuthenticated && !authLoading) {
    return (
      <GridPageLayout background={<GridBackground />}>
        <View style={{ flex: 1, justifyContent: 'center', paddingBottom: 40 }}>
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
      <View style={{ gap: 24, paddingHorizontal: GLOBAL_PADDING }}>
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
                    stated, not re-offered: a second editor at payment reads as a surcharge. */}
                {item.termCode && (
                  <Text
                    allowFontScaling={false}
                    style={[styles.summaryItemSubtitle, { color: tokens.colors.text.dim }]}
                  >
                    {t(`term.${item.termCode}`)}
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

const styles = StyleSheet.create({
  sectionLabel: {
    fontFamily: 'Inter-Bold',
    fontSize: 9,
    letterSpacing: 2,
    textTransform: 'uppercase',
    marginBottom: 12,
    paddingHorizontal: 4,
  },
  summaryCard: {
    borderWidth: 1,
    borderRadius: 2,
    padding: 20,
    gap: 16,
  },
  summaryRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  summaryItemTitle: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 16,
    textTransform: 'uppercase',
  },
  summaryItemSubtitle: {
    fontFamily: 'Inter-Bold',
    fontSize: 9,
    textTransform: 'uppercase',
  },
  summaryItemPrice: {
    fontFamily: 'Inter-Black',
    fontSize: 18,
  },
  contextRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    padding: 20,
    borderWidth: 1,
    borderRadius: 4,
  },
  payButton: {
    width: '100%',
    paddingVertical: 18,
    borderRadius: 12,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
  },
  payButtonText: {
    fontFamily: 'Inter-Black',
    fontSize: 14,
    letterSpacing: 1,
    textTransform: 'uppercase',
  },
  contextText: {
    flex: 1,
    fontFamily: 'Inter-Black',
    fontSize: 14,
    textTransform: 'uppercase',
  },
});
