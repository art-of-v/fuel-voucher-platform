import { useState, useEffect, useMemo } from 'react';
import { View, Text, Pressable, ActivityIndicator, StyleSheet } from 'react-native';
import { useRouter } from 'expo-router';
import { ChevronLeft, ShoppingCart } from 'lucide-react-native';
import { useCartStore } from '../src/features/cart/store/cartStore';
import { useI18n } from '../src/core/i18n';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { usePackages } from '../src/features/stations/hooks/usePackages';
import { GlowText } from '../src/components/glow-text';
import { PageLayout } from '../src/components/page-layout';
import { PackageCard } from '../src/features/stations/components/PackageCard';
import { BRAND_COLORS } from '../src/core/design/tokens';
import { Haptics } from '../src/core/utils/haptics';

export default function PackagesScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { selectedStation, selectedFuel, addToCart } = useCartStore();
  const cartItemCount = useCartStore(state => state.getCartItemCount());
  const { t } = useI18n();
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [addedItems, setAddedItems] = useState<Set<string>>(new Set());

  const GLOBAL_PADDING = tokens.spacing.containerPadding;

  const { data: packages, isLoading } = usePackages(
    selectedStation?.id,
    selectedFuel?.name,
  );

  if (!selectedStation || !selectedFuel) return null;

  const brandColor = BRAND_COLORS[selectedStation.id] || tokens.colors.primary;

  const handleAddToCart = (pkg: any) => {
    const qty = quantities[pkg.id] || 1;
    addToCart({ package: pkg, station: selectedStation, fuel: selectedFuel, quantity: qty });
    setAddedItems(prev => new Set(prev).add(pkg.id));
    setTimeout(() => setAddedItems(prev => { const n = new Set(prev); n.delete(pkg.id); return n; }), 2000);
  };

  const Header = (
    <View style={[styles.header, { paddingHorizontal: GLOBAL_PADDING }]}>
      <View style={styles.headerTop}>
        <Pressable onPress={() => router.back()} style={[styles.iconBox, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
          <ChevronLeft size={28} color={tokens.colors.text.primary} />
        </Pressable>
        <View style={styles.headerCenter}>
          <GlowText intensity="high" color={brandColor} glowColor={brandColor} style={styles.headerTitle}>
            {selectedFuel.name}
          </GlowText>
          <Text allowFontScaling={false} style={[styles.headerSubtitle, { color: brandColor }]}>
            [ {t('packages.selectCards')} ]
          </Text>
        </View>
        <Pressable
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
            router.push('/basket');
          }}
          style={[styles.iconBox, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }, cartItemCount > 0 && { borderColor: '#EF4444' }]}
        >
          <ShoppingCart size={28} color={brandColor} />
          {cartItemCount > 0 && (
            <View style={[styles.badge, { backgroundColor: tokens.colors.primary, borderColor: tokens.colors.background }]}>
              <Text allowFontScaling={false} style={[styles.badgeText, { color: tokens.colors.isDark ? '#000' : '#FFF' }]}>{cartItemCount}</Text>
            </View>
          )}
        </Pressable>
      </View>
    </View>
  );

  return (
    <PageLayout header={Header}>
      <View style={{ paddingHorizontal: GLOBAL_PADDING }}>
        {isLoading ? (
          <ActivityIndicator size="small" color={brandColor} style={{ marginTop: 100 }} />
        ) : (
          <View style={styles.container}>
            {(packages || []).map((pkg, index) => (
              <PackageCard
                key={pkg.id}
                pkg={pkg}
                brandColor={brandColor}
                index={index}
                quantity={quantities[pkg.id] || 1}
                isAdded={addedItems.has(pkg.id)}
                onAdd={() => handleAddToCart(pkg)}
                onQuantityChange={(qty) => setQuantities(prev => ({ ...prev, [pkg.id]: qty }))}
              />
            ))}
          </View>
        )}
      </View>
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  header: { paddingTop: 8, paddingBottom: 24 },
  headerTop: { flexDirection: 'row', alignItems: 'center', marginHorizontal: 12 },
  headerCenter: { flex: 1, alignItems: 'center' },
  iconBox: { width: 56, height: 56, borderWidth: 1, alignItems: 'center', justifyContent: 'center', borderRadius: 4 },
  headerTitle: { fontFamily: 'Rajdhani-Bold', fontSize: 32, lineHeight: 38, letterSpacing: -0.5, textTransform: 'uppercase' },
  headerSubtitle: { fontFamily: 'Inter-Black', fontSize: 9, letterSpacing: 4, marginTop: 4, opacity: 0.6 },
  container: { paddingBottom: 44, gap: 16 },
  badge: { position: 'absolute', top: -6, right: -6, minWidth: 20, height: 20, borderRadius: 10, alignItems: 'center', justifyContent: 'center', borderWidth: 1 },
  badgeText: { fontSize: 11, fontFamily: 'Inter-Black' },
});
