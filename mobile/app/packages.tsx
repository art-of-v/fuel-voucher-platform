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
  HEADER_ACTION_FRAME,
  HEADER_TRAILING_HIT_SLOP,
  IconButton,
  LoadingState,
  ScreenHeader,
} from '../src/core/ui';
import { PackageCard } from '../src/features/stations/components/PackageCard';
import { BRAND_COLORS } from '../src/core/design/tokens';
import { useAccountContext } from '../src/features/company/hooks/useAccountContext';
import { canBuyInContext } from '../src/features/company/lib/context';
import type { FuelPackage } from '../src/core/types/api';

export default function PackagesScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { selectedStation, selectedFuel, addToCart } = useCartStore();
  const cartItemCount = useCartStore((state) => state.getCartItemCount());
  const { t } = useI18n();
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  // One chosen term per package: the ladder is per station + fuel + nominal, so each card has its own.
  const [terms, setTerms] = useState<Record<string, string | undefined>>({});
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

  const handleAddToCart = (pkg: FuelPackage, termCode?: string, termLinePrice?: number) => {
    if (!canPurchase) return;
    // The card's button is already disabled for an empty shelf; this is the backstop behind it, so a
    // stale render or a re-entrant press cannot put an unbuyable line in the basket. The server refuses
    // it too — three layers, because the failure costs the customer their money, not a tap.
    if (pkg.isAvailable === false) return;
    const qty = quantities[pkg.id] || 1;
    // The quoted price travels with the line: the basket and payment screen must show what the customer
    // agreed to, not the undiscounted package price.
    addToCart({
      package: pkg,
      station: selectedStation,
      fuel: selectedFuel,
      quantity: qty,
      ...(termCode ? { termCode, termLinePrice } : {}),
    });
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
          <View style={HEADER_ACTION_FRAME}>
            <IconButton
              icon={<ShoppingCart />}
              onPress={() => router.push('/basket')}
              accessibilityLabel={t('basket.title')}
              variant="outlined"
              hapticStyle="medium"
              // The basket is one of the two controls people miss on the first tap,
              // and the header's corner is empty space nobody else needs.
              hitSlop={HEADER_TRAILING_HIT_SLOP}
            />
            {cartItemCount > 0 && (
              <View
                pointerEvents="none"
                style={[
                  styles.badge,
                  {
                    borderRadius: tokens.radius.full,
                    backgroundColor: tokens.colors.primary,
                    borderColor: tokens.colors.background,
                  },
                ]}
              >
                <Text style={[tokens.type.label, { color: tokens.colors.text.onPrimary }]}>
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
          <View style={{ gap: tokens.spacing.lg }}>
            {!canPurchase && (
              // Say why the purchase controls are gone instead of showing a dead card.
              <View
                style={[
                  styles.notice,
                  {
                    gap: tokens.spacing.md,
                    padding: tokens.spacing.fine14,
                    borderRadius: tokens.radius.fine12,
                    borderColor: tokens.colors.borderLight,
                    backgroundColor: tokens.colors.card,
                  },
                ]}
              >
                <ShoppingBag size={18} color={tokens.colors.text.dim} />
                <Text
                  style={{
                    flex: 1,
                    fontSize: tokens.type.caption.fontSize,
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
                term={terms[pkg.id]}
                onTermChange={(termCode) => setTerms((prev) => ({ ...prev, [pkg.id]: termCode }))}
                onAdd={(line) => handleAddToCart(pkg, line?.termCode, line?.termLinePrice)}
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
  notice: {
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1,
  },
  badge: {
    // Inside the button's own box, not hanging off it: a badge that overlaps
    // empty space reads as part of the control, and a tap on it must count.
    position: 'absolute',
    top: 0,
    right: 0,
    minWidth: 20,
    height: 20,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
  },
});
