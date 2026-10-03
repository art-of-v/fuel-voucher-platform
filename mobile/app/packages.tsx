import { useState } from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { useRouter, Redirect } from 'expo-router';
import { ShoppingCart, Package } from 'lucide-react-native';
import { useCartStore } from '../src/features/cart/store/cartStore';
import { useI18n } from '../src/core/i18n';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { usePackages } from '../src/features/stations/hooks/usePackages';
import { useQueryClient } from '@tanstack/react-query';
import { EmptyState, ErrorState, GridPageLayout, IconButton, LoadingState, ScreenHeader } from '../src/core/ui';
import { PackageCard } from '../src/features/stations/components/PackageCard';
import { BRAND_COLORS } from '../src/core/design/tokens';
import { useAccountContext } from '../src/features/company/hooks/useAccountContext';

export default function PackagesScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { selectedStation, selectedFuel, addToCart } = useCartStore();
  const cartItemCount = useCartStore(state => state.getCartItemCount());
  const { t } = useI18n();
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [addedItems, setAddedItems] = useState<Set<string>>(new Set());
  const queryClient = useQueryClient();
  // Multi-company epic #103 S5: buying is a personal/owner action, so the package
  // picker is closed in a worker context — a worker redeems fuel, never buys it for
  // the employer. Returning to personal restores it.
  const isWorkerContext = useAccountContext().kind === 'worker';

  const GLOBAL_PADDING = tokens.spacing.containerPadding;

  const { data: packages, isLoading, error } = usePackages(
    selectedStation?.id,
    selectedFuel?.name,
  );

  if (!selectedStation || !selectedFuel) return null;
  if (isWorkerContext) return <Redirect href="/my-codes" />;

  const brandColor = BRAND_COLORS[selectedStation.id] || tokens.colors.primary;

  const handleAddToCart = (pkg: any) => {
    const qty = quantities[pkg.id] || 1;
    addToCart({ package: pkg, station: selectedStation, fuel: selectedFuel, quantity: qty });
    setAddedItems(prev => new Set(prev).add(pkg.id));
    setTimeout(() => setAddedItems(prev => { const n = new Set(prev); n.delete(pkg.id); return n; }), 2000);
  };

  const Header = (
    <ScreenHeader
      title={selectedFuel.name}
      subtitle={t('packages.selectCards')}
      actions={
        <View>
          <IconButton
            icon={<ShoppingCart />}
            onPress={() => router.push('/basket')}
            accessibilityLabel={t('basket.title')}
            variant="outlined"
            hapticStyle="medium"
          />
          {cartItemCount > 0 && (
            <View
              pointerEvents="none"
              style={[styles.badge, { backgroundColor: tokens.colors.primary, borderColor: tokens.colors.background }]}
            >
              <Text style={[styles.badgeText, { color: tokens.colors.text.onPrimary }]}>{cartItemCount}</Text>
            </View>
          )}
        </View>
      }
    />
  );

  return (
    <GridPageLayout header={Header}>
      <View style={{ paddingHorizontal: GLOBAL_PADDING }}>
        {isLoading ? (
          <LoadingState />
        ) : error ? (
          <ErrorState
            onRetry={() => queryClient.invalidateQueries({ queryKey: ['packages', selectedStation.id, selectedFuel.name] })}
            detail={error instanceof Error ? error.message : undefined}
          />
        ) : !packages || packages.length === 0 ? (
          <EmptyState
            title={t('packages.empty')}
            description={t('packages.emptyHint')}
            icon={<Package />}
          />
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
    </GridPageLayout>
  );
}

const styles = StyleSheet.create({
  // Bottom clearance comes from PageLayout, not a per-screen `paddingBottom: 44`.
  container: { gap: 16 },
  badge: { position: 'absolute', top: -6, right: -6, minWidth: 20, height: 20, borderRadius: 10, alignItems: 'center', justifyContent: 'center', borderWidth: 1 },
  badgeText: { fontSize: 11, fontFamily: 'Inter-Black' },
});