import { useState } from 'react';
import { View, Text, Pressable, TextInput, StyleSheet } from 'react-native';
import { useRouter } from 'expo-router';
import { ChevronLeft, ShoppingCart, Tag, Zap, Check, X } from 'lucide-react-native';
import { useCartStore } from '../src/features/cart/store/cartStore';
import { useI18n } from '../src/core/i18n';
import { PageLayout } from '../src/components/page-layout';
import { GlowText } from '../src/components/glow-text';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { Button } from '../src/core/ui';
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

  const Header = (
    <View style={[styles.header, { paddingHorizontal: GLOBAL_PADDING, backgroundColor: tokens.colors.background, borderBottomColor: tokens.colors.borderLight }]}>
      <Pressable onPress={() => router.push('/')} style={[styles.backButton, { padding: tokens.spacing.sm, borderColor: tokens.colors.borderLight, backgroundColor: tokens.colors.card, borderRadius: soft ? 12 : undefined }]}>
        <ChevronLeft size={24} color={tokens.colors.text.primary} />
      </Pressable>
      <View style={{ flex: 1 }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
          <ShoppingCart size={20} color={tokens.colors.primary} />
          <Text allowFontScaling={false} style={[styles.headerTitle, { color: tokens.colors.text.primary }]}>{t('basket.title')}</Text>
        </View>
        <Text allowFontScaling={false} style={[styles.headerSubtitle, { color: tokens.colors.text.dim }]}>{cart.length} {t('basket.cards')}</Text>
      </View>
      <Pressable onPress={() => clearCart()}>
        <Text allowFontScaling={false} style={[styles.removeText, { color: tokens.colors.error }]}>{t('basket.remove')}</Text>
      </Pressable>
    </View>
  );

  const fixedFooter = cart.length > 0 ? (
    <View style={[styles.footer, { paddingHorizontal: GLOBAL_PADDING, backgroundColor: tokens.colors.background, borderTopColor: tokens.colors.borderLight }]}>
      {promocode ? (
        <View style={[styles.activePromo, { backgroundColor: `${tokens.colors.primary}11`, borderColor: `${tokens.colors.primary}33`, borderRadius: soft ? 12 : undefined }]}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
            <Check size={20} color={tokens.colors.primary} />
            <Text style={[styles.activePromoCode, { color: tokens.colors.primary }]}>{promocode}</Text>
            <Text style={[styles.activePromoDiscount, { color: tokens.colors.text.dim }]}>{t('basket.discount')} ({discount}%)</Text>
          </View>
          <Pressable onPress={clearPromocode}>
            <X size={20} color={tokens.colors.error} />
          </Pressable>
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
          <Pressable
            onPress={handleApplyPromo}
            disabled={!promoInput}
            style={[styles.applyButton, { backgroundColor: `${tokens.colors.primary}22`, borderColor: `${tokens.colors.primary}44`, borderRadius: soft ? 12 : undefined }]}
          >
            <Text style={[styles.applyButtonText, { color: tokens.colors.primary }]}>{t('basket.apply')}</Text>
          </Pressable>
        </View>
      )}

      <View style={[styles.summary, { borderTopColor: tokens.colors.borderLight }]}>
        <View style={styles.summaryRow}>
          <Text style={[styles.summaryLabel, { color: tokens.colors.text.dim }]}>{t('basket.subtotal')}</Text>
          <Text style={[styles.summaryValue, { color: tokens.colors.text.dim }]}>{total} ₴</Text>
        </View>
        {discount > 0 && (
          <View style={[styles.summaryRow, { marginBottom: tokens.spacing.xs }]}>
            <Text style={{ color: tokens.colors.primary, fontWeight: '700', fontSize: 10 }}>{t('basket.discount')} ({discount}%)</Text>
            <Text style={{ color: tokens.colors.primary, fontWeight: '700', fontSize: 10 }}>-{discountAmount} ₴</Text>
          </View>
        )}
        <View style={styles.totalRow}>
          <Text style={[styles.totalLabel, { color: tokens.colors.text.primary }]}>{t('basket.totalToPay')}</Text>
          {soft ? (
            <Text style={{ fontSize: 24, fontFamily: 'Rajdhani-Bold', color: tokens.colors.text.primary }}>
              {discountedTotal} ₴
            </Text>
          ) : (
            <GlowText style={{ fontSize: 24, fontFamily: 'Rajdhani-Bold' }} color={tokens.colors.text.primary} glowColor={tokens.colors.primary} intensity="high">
              {discountedTotal} ₴
            </GlowText>
          )}
        </View>
      </View>

      <Button
        title={t('basket.checkout')}
        onPress={() => router.push('/checkout')}
        hapticStyle="light"
        textStyle={{ fontSize: 18 }}
        icon={<Zap size={20} color={tokens.colors.isDark ? '#000' : '#FFF'} />}
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
            title={t('basket.continueShopping')}
            onPress={() => router.push('/')}
            textStyle={{ fontSize: 18 }}
            style={{ width: 'auto', paddingHorizontal: 32 }}
          />
        </View>
      </PageLayout>
    );
  }

  return (
    <PageLayout header={Header} fixedFooter={fixedFooter}>
      <View style={{ padding: GLOBAL_PADDING, paddingBottom: 100 }}>
        {cart.map(item => (
          <CartItemCard
            key={item.id}
            item={item}
            onUpdateQuantity={updateQuantity}
            onRemove={removeFromCart}
          />
        ))}
      </View>
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  header: { borderBottomWidth: 1, paddingVertical: 12, flexDirection: 'row', alignItems: 'center', gap: 12 },
  backButton: { borderWidth: 1, borderRadius: 4 },
  headerTitle: { fontWeight: 'bold', fontSize: 18, textTransform: 'uppercase', letterSpacing: 0.5 },
  headerSubtitle: { fontSize: 10, fontWeight: '700', textTransform: 'uppercase', letterSpacing: 1 },
  removeText: { fontSize: 10, fontWeight: '700', textTransform: 'uppercase', letterSpacing: 1 },
  footer: { paddingBottom: 84, paddingTop: 8, borderTopWidth: 1 },
  promoRow: { flexDirection: 'row', alignItems: 'center', gap: 12, marginBottom: 16 },
  promoInput: { flex: 1, borderWidth: 1, paddingHorizontal: 12, height: 44, fontWeight: '700', fontSize: 13, textTransform: 'uppercase', borderRadius: 2 },
  applyButton: { borderWidth: 1, paddingHorizontal: 14, height: 44, justifyContent: 'center', borderRadius: 2 },
  applyButtonText: { fontWeight: '700', fontSize: 12, textTransform: 'uppercase' },
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
