import { useState } from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { useRouter } from 'expo-router';
import { ShoppingCart, Package, ShoppingBag } from 'lucide-react-native';
import { useCartStore } from '../src/features/cart/store/cartStore';
import { useI18n } from '../src/core/i18n';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { usePackages } from '../src/features/stations/hooks/usePackages';
import { useQueryClient } from '@tanstack/react-query';
import {
  EmptyState,
  ErrorState,
  GridPageLayout,
  IconButton,
  LoadingState,
  ScreenHeader,
} from '../src/core/ui';
import { PackageCard } from '../src/features/stations/components/PackageCard';
import { BRAND_COLORS } from '../src/core/design/tokens';
import { useAccountContext } from '../src/features/company/hooks/useAccountContext';
import { canBuyInContext } from '../src/features/company/lib/context';

export default function PackagesScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { selectedStation, selectedFuel, addToCart } = useCartStore();
  const cartItemCount = useCartStore((state) => state.getCartItemCount());
  const { t } = useI18n();
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [addedItems, setAddedItems] = useState<Set<string>>(new Set());
  const queryClient = useQueryClient();
  // Multi-company epic #103 S5 / planning #158: a worker browsing the catalog in a
  // company they work for is a normal consumer, so the list, prices and the radar all
  // stay — only the purchase is withheld. Buying happens in the personal context, and
  // buying *for the company* is the owner-only act this protects. The basket tab is
  // dropped in this context and /basket + /checkout refuse it as a backstop.
  const canPurchase = canBuyInContext(useAccountContext());

  const GLOBAL_PADDING = tokens.spacing.containerPadding;

  const { data: packages, isLoading, error } = usePackages(selectedStation?.id, selectedFuel?.name);

  if (!selectedStation || !selectedFuel) return null;

  const brandColor = BRAND_COLORS[selectedStation.id] || tokens.colors.primary;

  const handleAddToCart = (pkg: any) => {
    if (!canPurchase) return;
    const qty = quantities[pkg.id] || 1;
    addToCart({ package: pkg, station: selectedStation, fuel: selectedFuel, quantity: qty });
    setAddedItems((prev) => new Set(prev).add(pkg.id));
    setTimeout(
      () =>
        setAddedItems((prev) => {
          const n = new Set(prev);
          n.delete(pkg.id);
          return n;
        }),
      2000,
    );
  };

  const Header = (
    <ScreenHeader
      title={selectedFuel.name}
      subtitle={t('packages.selectCards')}
      actions={
        canPurchase ? (
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
                style={[
                  styles.badge,
                  { backgroundColor: tokens.colors.primary, borderColor: tokens.colors.background },
                ]}
              >
                <Text style={[styles.badgeText, { color: tokens.colors.text.onPrimary }]}>
                  {cartItemCount}
                </Text>
              </View>
            )}
          </View>
        ) : undefined
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
            onRetry={() =>
              queryClient.invalidateQueries({
                queryKey: ['packages', selectedStation.id, selectedFuel.name],
              })
            }
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
            {!canPurchase && (
              // Say why the purchase controls are gone instead of showing a dead card.
              <View
                style={[
                  styles.notice,
                  { borderColor: tokens.colors.borderLight, backgroundColor: tokens.colors.card },
                ]}
              >
                <ShoppingBag size={18} color={tokens.colors.text.dim} />
                <Text
                  style={{
                    flex: 1,
                    fontSize: 12,
                    fontFamily: 'Inter-Medium',
                    color: tokens.colors.text.dim,
                  }}
                >
                  {t('packages.browseOnly')}
                </Text>
              </View>
            )}
            {(packages || []).map((pkg, index) => (
              <PackageCard
                key={pkg.id}
                pkg={pkg}
                brandColor={brandColor}
                index={index}
                quantity={quantities[pkg.id] || 1}
                isAdded={addedItems.has(pkg.id)}
                canPurchase={canPurchase}
                onAdd={() => handleAddToCart(pkg)}
                onQuantityChange={(qty) => setQuantities((prev) => ({ ...prev, [pkg.id]: qty }))}
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
  notice: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    padding: 14,
    borderRadius: 12,
    borderWidth: 1,
  },
  badge: {
    position: 'absolute',
    top: -6,
    right: -6,
    minWidth: 20,
    height: 20,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
  },
  badgeText: { fontSize: 11, fontFamily: 'Inter-Black' },
});
