import { useState, useRef } from 'react';
import { View, Text, Pressable, TextInput, StyleSheet, Animated } from 'react-native';
import { useRouter } from 'expo-router';
import { ChevronLeft, ShoppingCart, Tag, Zap, Check, X } from 'lucide-react-native';
import { useCartStore } from '../src/features/cart/store/cartStore';
import { useI18n } from '../src/core/i18n';
import { PageLayout } from '../src/components/page-layout';
import { GlowText } from '../src/components/glow-text';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { Haptics } from '../src/core/utils/haptics';
import { CartItemCard } from '../src/features/cart/components/CartItemCard';

export default function BasketScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
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
  const scaleAnim = useRef(new Animated.Value(1)).current;

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
      <Pressable onPress={() => router.push('/')} style={[styles.backButton, { borderColor: tokens.colors.borderLight, backgroundColor: tokens.colors.card }]}>
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
      <View style={styles.promoIndicator}>
        <Tag size={16} color={tokens.colors.primary} />
        <Text allowFontScaling={false} style={[styles.promoIndicatorText, { color: tokens.colors.text.dim }]}>{t('basket.promocode')}</Text>
      </View>

      {promocode ? (
        <View style={[styles.activePromo, { backgroundColor: `${tokens.colors.primary}11`, borderColor: `${tokens.colors.primary}33` }]}>
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
        <View style={styles.promoInputRow}>
          <TextInput
            value={promoInput}
            onChangeText={(text) => { setPromoInput(text); setPromoError(false); }}
            placeholder={t('basket.enterCode')}
            placeholderTextColor={tokens.colors.text.dim}
            style={[styles.promoInput, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight, color: tokens.colors.text.primary }, promoError && { borderColor: tokens.colors.error }]}
          />
          <Pressable
            onPress={handleApplyPromo}
            disabled={!promoInput}
            style={[styles.applyButton, { backgroundColor: `${tokens.colors.primary}22`, borderColor: `${tokens.colors.primary}44` }]}
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
          <View style={[styles.summaryRow, { marginBottom: 2 }]}>
            <Text style={{ color: tokens.colors.primary, fontWeight: '700', fontSize: 10 }}>{t('basket.discount')} ({discount}%)</Text>
            <Text style={{ color: tokens.colors.primary, fontWeight: '700', fontSize: 10 }}>-{discountAmount} ₴</Text>
          </View>
        )}
        <View style={styles.totalRow}>
          <Text style={[styles.totalLabel, { color: tokens.colors.text.primary }]}>{t('basket.totalToPay')}</Text>
          <GlowText style={{ fontSize: 24, fontFamily: 'Rajdhani-Bold' }} color={tokens.colors.text.primary} glowColor={tokens.colors.primary} intensity="high">
            {discountedTotal} ₴
          </GlowText>
        </View>
      </View>

      <Animated.View style={{ transform: [{ scale: scaleAnim }] }}>
        <Pressable
          onPressIn={() => Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light)}
          onPressOut={() => Animated.spring(scaleAnim, { toValue: 1, useNativeDriver: true, friction: 10, tension: 100 }).start()}
          onPress={() => router.push('/checkout')}
          style={[styles.checkoutButton, { backgroundColor: tokens.colors.primary }]}
        >
          <Zap size={20} color={tokens.colors.isDark ? '#000' : '#FFF'} />
          <Text style={[styles.checkoutButtonText, { color: tokens.colors.isDark ? '#000' : '#FFF' }]}>{t('basket.checkout')}</Text>
        </Pressable>
      </Animated.View>
    </View>
  ) : null;

  if (cart.length === 0) {
    return (
      <PageLayout header={Header}>
        <View style={styles.emptyState}>
          <ShoppingCart size={80} color={tokens.colors.borderLight} />
          <Text style={[styles.emptyStateTitle, { color: tokens.colors.text.primary }]}>{t('basket.empty')}</Text>
          <Text style={[styles.emptyStateSub, { color: tokens.colors.text.dim }]}>{t('basket.browseStations')}</Text>
          <Pressable onPress={() => router.push('/')} style={[styles.browseButton, { backgroundColor: tokens.colors.primary }]}>
            <Text style={[styles.browseButtonText, { color: tokens.colors.isDark ? '#000' : '#FFF' }]}>{t('basket.browseStations')}</Text>
          </Pressable>
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
  backButton: { padding: 6, borderWidth: 1, borderRadius: 4 },
  headerTitle: { fontWeight: 'bold', fontSize: 18, textTransform: 'uppercase', letterSpacing: 0.5 },
  headerSubtitle: { fontSize: 10, fontWeight: '700', textTransform: 'uppercase', letterSpacing: 1 },
  removeText: { fontSize: 10, fontWeight: '700', textTransform: 'uppercase', letterSpacing: 1 },
  footer: { paddingBottom: 72, paddingTop: 8, borderTopWidth: 1 },
  promoIndicator: { flexDirection: 'row', alignItems: 'center', gap: 6, marginBottom: 4 },
  promoIndicatorText: { fontSize: 9, fontWeight: '700', textTransform: 'uppercase', letterSpacing: 1 },
  promoInputRow: { flexDirection: 'row', gap: 8, marginBottom: 8 },
  promoInput: { flex: 1, borderWidth: 1, paddingHorizontal: 12, height: 48, fontWeight: '700', fontSize: 14, textTransform: 'uppercase', borderRadius: 2 },
  applyButton: { borderWidth: 1, paddingHorizontal: 16, height: 48, justifyContent: 'center', borderRadius: 2 },
  applyButtonText: { fontWeight: '700', fontSize: 12, textTransform: 'uppercase' },
  activePromo: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', borderWidth: 1, padding: 10, borderRadius: 2, marginBottom: 8 },
  activePromoCode: { fontWeight: '800', fontSize: 14 },
  activePromoDiscount: { fontSize: 12 },
  summary: { borderTopWidth: 1, paddingTop: 8, marginBottom: 12 },
  summaryRow: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 4 },
  summaryLabel: { fontWeight: '700', fontSize: 11 },
  summaryValue: { fontWeight: '700', fontSize: 11 },
  totalRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 4 },
  totalLabel: { fontWeight: '700', fontSize: 16, textTransform: 'uppercase' },
  checkoutButton: { width: '100%', height: 56, borderRadius: 2, flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 12 },
  checkoutButtonText: { fontWeight: '900', fontSize: 18, textTransform: 'uppercase', letterSpacing: 1 },
  emptyState: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 32, paddingVertical: 100 },
  emptyStateTitle: { fontSize: 28, fontWeight: '900', textTransform: 'uppercase', marginTop: 24, marginBottom: 12 },
  emptyStateSub: { textAlign: 'center', marginBottom: 40, fontSize: 14, lineHeight: 20 },
  browseButton: { paddingHorizontal: 32, paddingVertical: 18, borderRadius: 2 },
  browseButtonText: { fontWeight: '900', fontSize: 18, textTransform: 'uppercase', letterSpacing: 1 },
});
