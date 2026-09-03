import { useState } from 'react';
import { View, Text, TextInput, StyleSheet } from 'react-native';
import { useRouter } from 'expo-router';
import { ShoppingCart, Tag, Zap, Check, X } from 'lucide-react-native';
import { useCartStore } from '../src/features/cart/store/cartStore';
import { useI18n } from '../src/core/i18n';
import { PageLayout } from '../src/components/page-layout';
import { GlowText } from '../src/components/glow-text';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { Button, ConfirmDialog, IconButton, ScreenHeader } from '../src/core/ui';
import { formatMoney, formatPercent } from '../src/core/utils/currency';
import { CartItemCard } from '../src/features/cart/components/CartItemCard';

export default function BasketScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const soft = tokens.surface.soft;
  const { t } = useI18n();
  const {
    cart,
    updateQuantity,
    removeFromCart,
    clearCart,
    promocode,
    discount,
    applyPromocode,
    clearPromocode,
    getCartTotal,
    getDiscountedTotal,
  } = useCartStore();

  const [promoInput, setPromoInput] = useState('');
  const [promoError, setPromoError] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);
  const GLOBAL_PADDING = tokens.spacing.containerPadding;
  const total = getCartTotal();
  const discountedTotal = getDiscountedTotal();
  const discountAmount = total - discountedTotal;

  const handleApplyPromo = () => {
    setPromoError(false);
    if (applyPromocode(promoInput)) {
      setPromoInput('');
    } else {
      setPromoError(true);
    }
  };

  // The back control used to be `router.push('/')`, which pushed a *second*
  // stations screen onto the stack instead of popping the basket. ScreenHeader's
  // default is `router.back()`.
  const Header = (
    <ScreenHeader
      title={t('basket.title')}
      subtitle={`${cart.length} ${t('basket.cards')}`}
      actions={
        cart.length > 0 ? (
          /*
           * Emptying the basket is irreversible, so it asks first. It used to fire
           * on the first tap, which made the cheapest gesture on the screen the
           * most expensive one.
           */
          <Button
            label={t('basket.remove')}
            onPress={() => setConfirmClear(true)}
            variant="destructive"
            size="sm"
            fullWidth={false}
          />
        ) : undefined
      }
    />
  );

  const fixedFooter = cart.length > 0 ? (
    <View style={styles.footer}>
      {promocode ? (
        <View style={[styles.activePromo, { backgroundColor: `${tokens.colors.primary}11`, borderColor: `${tokens.colors.primary}33`, borderRadius: soft ? 12 : undefined }]}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
            <Check size={20} color={tokens.colors.primary} />
            <Text style={[styles.activePromoCode, { color: tokens.colors.primary }]}>{promocode}</Text>
            <Text style={[styles.activePromoDiscount, { color: tokens.colors.text.dim }]}>{t('basket.discount')} ({discount}%)</Text>
          </View>
          <IconButton
            icon={<X />}
            onPress={clearPromocode}
            accessibilityLabel={t('common.dismiss')}
            variant="danger"
            size="sm"
          />
        </View>
      ) : (
        <View style={styles.promoRow}>
          <Tag size={16} color={tokens.colors.primary} />
          <TextInput
            value={promoInput}
            onChangeText={(text) => { setPromoInput(text); setPromoError(false); }}
            placeholder={t('basket.enterCode')}
            placeholderTextColor={tokens.colors.text.dim}
            style={[styles.promoInput, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight, color: tokens.colors.text.primary, borderRadius: soft ? 12 : undefined }, promoError && { borderColor: tokens.colors.error }]}
          />
          {/*
            Applying a promocode is a supporting action, so it is outlined — it
            used to be the only filled control on this screen while "proceed to
            checkout" was an outline, which inverted the funnel. It also had no
            disabled appearance despite `disabled={!promoInput}`; `Button` owns
            that state.
          */}
          <Button
            label={t('basket.apply')}
            onPress={handleApplyPromo}
            disabled={!promoInput}
            variant="secondary"
            size="sm"
            hapticStyle="light"
          />
        </View>
      )}

      <View style={[styles.summary, { borderTopColor: tokens.colors.borderLight }]}>
        <View style={styles.summaryRow}>
          <Text style={[styles.summaryLabel, { color: tokens.colors.text.dim }]}>{t('basket.subtotal')}</Text>
          <Text style={[styles.summaryValue, { color: tokens.colors.text.dim }]}>{formatMoney(total)}</Text>
        </View>
        {discount > 0 && (
          <View style={[styles.summaryRow, { marginBottom: tokens.spacing.xs }]}>
            <Text style={{ color: tokens.colors.primary, fontWeight: '700', fontSize: 10 }}>{t('basket.discount')} ({formatPercent(discount)})</Text>
            <Text style={{ color: tokens.colors.primary, fontWeight: '700', fontSize: 10 }}>{formatMoney(-discountAmount)}</Text>
          </View>
        )}
        <View style={styles.totalRow}>
          <Text style={[styles.totalLabel, { color: tokens.colors.text.primary }]}>{t('basket.totalToPay')}</Text>
          {soft ? (
            <Text style={{ fontSize: 24, fontFamily: 'Rajdhani-Bold', color: tokens.colors.text.primary }}>
              {formatMoney(discountedTotal)}
            </Text>
          ) : (
            <GlowText style={{ fontSize: 24, fontFamily: 'Rajdhani-Bold' }} color={tokens.colors.text.primary} glowColor={tokens.colors.primary} intensity="high">
              {formatMoney(discountedTotal)}
            </GlowText>
          )}
        </View>
      </View>

      {/*
        The one action this screen exists for, so it is the filled primary bar.
        It was `variant="secondary"`, which resolves to a transparent fill with a
        45%-alpha hairline: at device scale a customer sees green text, not a
        button — and the promocode "apply" beside it was the filled one.
      */}
      <Button
        label={t('basket.checkout')}
        onPress={() => router.push('/checkout')}
        hapticStyle="light"
        icon={<Zap />}
      />
    </View>
  ) : null;

  if (cart.length === 0) {
    return (
      <PageLayout header={Header}>
        <View style={styles.emptyState}>
          <ShoppingCart size={80} color={tokens.colors.borderLight} />
          <Text style={[styles.emptyStateTitle, { color: tokens.colors.text.primary }]}>{t('basket.empty')}</Text>
          <Text style={[styles.emptyStateSub, { color: tokens.colors.text.dim }]}>{t('basket.browseStations')}</Text>
          <Button
            label={t('basket.continueShopping')}
            onPress={() => router.push('/')}
            fullWidth={false}
          />
        </View>
      </PageLayout>
    );
  }

  return (
    <PageLayout header={Header} fixedFooter={fixedFooter}>
      {/* Bottom clearance is derived by PageLayout — no paddingBottom here. */}
      <View style={{ padding: GLOBAL_PADDING }}>
        {cart.map(item => (
          <CartItemCard
            key={item.id}
            item={item}
            onUpdateQuantity={updateQuantity}
            onRemove={removeFromCart}
          />
        ))}
      </View>

      <ConfirmDialog
        visible={confirmClear}
        tone="destructive"
        title={t('basket.clearTitle')}
        message={t('basket.clearMessage')}
        confirmLabel={t('basket.clearConfirm')}
        cancelLabel={t('common.cancel')}
        onConfirm={() => {
          setConfirmClear(false);
          clearCart();
        }}
        onCancel={() => setConfirmClear(false)}
      />
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  // PageLayout's footer slot owns the hairline, the padding and the safe-area
  // inset; this used to carry `paddingBottom: 84` and its own border.
  footer: {},
  promoRow: { flexDirection: 'row', alignItems: 'center', gap: 12, marginBottom: 16 },
  promoInput: { flex: 1, borderWidth: 1, paddingHorizontal: 12, height: 44, fontWeight: '700', fontSize: 13, textTransform: 'uppercase', borderRadius: 2 },
  activePromo: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', borderWidth: 1, padding: 14, borderRadius: 2, marginBottom: 12 },
  activePromoCode: { fontWeight: '800', fontSize: 14 },
  activePromoDiscount: { fontSize: 12 },
  summary: { borderTopWidth: 1, paddingTop: 8, marginBottom: 16 },
  summaryRow: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 4 },
  summaryLabel: { fontWeight: '700', fontSize: 11 },
  summaryValue: { fontWeight: '700', fontSize: 11 },
  totalRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 4 },
  totalLabel: { fontWeight: '700', fontSize: 16, textTransform: 'uppercase' },
  emptyState: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 32, paddingVertical: 100 },
  emptyStateTitle: { fontSize: 28, fontWeight: '900', textTransform: 'uppercase', marginTop: 24, marginBottom: 12 },
  emptyStateSub: { textAlign: 'center', marginBottom: 40, fontSize: 14, lineHeight: 20 },
});
