import React from 'react';
import { ImageSourcePropType, View } from 'react-native';
import { ChevronDown, ChevronLeft, ChevronRight, Fuel } from 'lucide-react-native';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Badge, Card, EmptyState, IconButton, Text } from '../../../core/ui';
import { formatMoney } from '../../../core/utils/currency';
import { BRAND_COLORS, BRAND_LOGOS } from '../../../core/design/tokens';
import type { StationNode } from '../../../core/types/api';
import { formatKm, formatShortAddress, type BrandRank } from '../lib/radar';
import { fuelLabel } from '../lib/fuelLabels';
import { BrandLogoChip } from './StationMarker';

interface HeaderProps {
  /** The drilled-in network, or null on the leaderboard itself. */
  activeBrand: BrandRank | null;
  fuel: string;
  radiusKm: number | null;
  locationDenied: boolean;
  onBack: () => void;
  onClose: () => void;
}

/**
 * The sheet's non-scrolling head: where you are (the leaderboard or one network),
 * which fuel the prices are for, and how far the radar reached. The fuel moved out
 * of the title string and into a `Badge`, so the two facts can wrap independently
 * instead of one long line truncating mid-word.
 */
export function NetworkRankingHeader({
  activeBrand,
  fuel,
  radiusKm,
  locationDenied,
  onBack,
  onClose,
}: HeaderProps) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);

  return (
    <View style={{ gap: tokens.spacing.xs }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.sm }}>
        {activeBrand ? (
          <IconButton
            icon={<ChevronLeft />}
            onPress={onBack}
            accessibilityLabel={t('map.back')}
            variant="plain"
            size="sm"
          />
        ) : null}
        <Text role="heading" numberOfLines={1} style={{ flex: 1 }}>
          {activeBrand ? activeBrand.brandSlug.toUpperCase() : t('map.networkRanking')}
        </Text>
        <Badge status="primary" label={fuelLabel(fuel)} />
        <IconButton
          icon={<ChevronDown />}
          onPress={onClose}
          accessibilityLabel={t('common.close')}
          variant="plain"
          size="sm"
        />
      </View>

      {radiusKm != null ? (
        <Text role="caption" tone="muted">
          {t('map.withinKm', String(radiusKm))}
        </Text>
      ) : null}
      {locationDenied ? (
        <Text role="caption" tone="warning">
          {t('map.locationDenied')}
        </Text>
      ) : null}
      {!activeBrand ? (
        <Text role="caption" tone="muted">
          {t('map.tapBrandHint')}
        </Text>
      ) : null}
    </View>
  );
}

interface ListProps {
  brands: BrandRank[];
  activeBrand: BrandRank | null;
  onSelectBrand: (brandId: string) => void;
  onSelectStation: (node: StationNode) => void;
}

/**
 * The scrolling body: networks cheapest-first on level one, one network's nearest
 * АЗК on level two.
 *
 * Each level is a stack of `Card`s rather than a hairline-divided list. The sheet is
 * already a raised surface (`surfaceElevated`), so cards on it give the rows their
 * own edge without the sheet's border/shadow being repeated per row.
 *
 * The cheapest network's badge lives under its name, on its own line: in the old
 * layout it shared a row with the name inside an unconstrained flex row, so it
 * wrapped onto the name and painted over the sub-line. A row that can only grow
 * downwards cannot do that.
 */
export function NetworkRankingList({
  brands,
  activeBrand,
  onSelectBrand,
  onSelectStation,
}: ListProps) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);

  if (activeBrand) {
    return (
      <View style={{ gap: tokens.spacing.sm }}>
        {activeBrand.nodes.slice(0, 30).map((ranked) => {
          const address = formatShortAddress(ranked.node) || t('map.noAddress');
          const distance = ranked.distanceKm;
          return (
            <Card
              key={ranked.node.id}
              onPress={() => onSelectStation(ranked.node)}
              padding="sm"
              accessibilityLabel={
                distance != null
                  ? `${ranked.node.name}, ${address}, ${formatKm(distance)} ${t('map.km')}`
                  : `${ranked.node.name}, ${address}`
              }
            >
              <View
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: tokens.spacing.md,
                }}
              >
                <View style={{ flex: 1, gap: tokens.spacing.xxs }}>
                  <Text role="bodyStrong" numberOfLines={1}>
                    {ranked.node.name}
                  </Text>
                  <Text role="secondary" tone="muted" numberOfLines={1}>
                    {address}
                  </Text>
                </View>
                {distance != null ? (
                  <Text role="numericSmall" tone="accent">
                    {formatKm(distance)} {t('map.km')}
                  </Text>
                ) : null}
                <ChevronRight size={18} color={tokens.colors.text.muted} />
              </View>
            </Card>
          );
        })}
      </View>
    );
  }

  if (brands.length === 0) {
    return (
      <EmptyState
        compact
        icon={<Fuel />}
        title={t('map.noOffersTitle')}
        description={t('map.noOffersDescription')}
      />
    );
  }

  return (
    <View style={{ gap: tokens.spacing.sm }}>
      {brands.map((brand, index) => (
        <BrandRow
          key={brand.brandId}
          brand={brand}
          cheapest={index === 0 && brand.inRange}
          onPress={() => onSelectBrand(brand.brandId)}
        />
      ))}
    </View>
  );
}

function BrandRow({
  brand,
  cheapest,
  onPress,
}: {
  brand: BrandRank;
  cheapest: boolean;
  onPress: () => void;
}) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);

  const logo: ImageSourcePropType | undefined = BRAND_LOGOS[brand.brandSlug];
  const brandColor = BRAND_COLORS[brand.brandSlug] ?? tokens.colors.primary;
  const { voucherPerLiter, savingsPerLiter } = brand.price;

  // The count and the nearest distance in one short line. "Найближча 1,2 км" was
  // tried here and left the name line no room at a large system font: the row is
  // already ordered by distance, and the sheet's hint says so.
  const distance =
    brand.nearestDistanceKm != null ? `${formatKm(brand.nearestDistanceKm)} ${t('map.km')}` : null;
  const meta = brand.inRange
    ? [distance, `${brand.nodes.length} ${t('map.stationsShort')}`].filter(Boolean).join(' · ')
    : [distance, t('map.outOfRange')].filter(Boolean).join(' · ');

  const price = formatMoney(voucherPerLiter, { decimals: 2, hideSymbol: true });
  const saving = formatMoney(savingsPerLiter, { decimals: 2, hideSymbol: true });

  return (
    <Card
      onPress={onPress}
      padding="sm"
      accent={cheapest ? 'primary' : undefined}
      // A network with no АЗК in reach keeps its row — "no KLO near me" reads
      // differently from "KLO does not exist" — but recedes, so it cannot compete
      // with the pumps the customer can turn into.
      style={brand.inRange ? undefined : { opacity: 0.6 }}
      accessibilityLabel={
        savingsPerLiter > 0
          ? `${brand.brandSlug}, ${price} ${t('map.perLiter')}, −${saving} ${t('map.vsPump')}, ${meta}`
          : `${brand.brandSlug}, ${price} ${t('map.perLiter')}, ${meta}`
      }
    >
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.md }}>
        {logo ? (
          <BrandLogoChip logo={logo} size={36} />
        ) : (
          <View
            style={{
              width: 36,
              height: 36,
              borderRadius: tokens.radius.full,
              backgroundColor: brandColor,
            }}
          />
        )}

        <View style={{ flex: 1, gap: tokens.spacing.xxs }}>
          <Text role="bodyStrong" numberOfLines={1}>
            {brand.brandSlug.toUpperCase()}
          </Text>
          <Text role="caption" tone="muted" numberOfLines={1}>
            {meta}
          </Text>
          {cheapest ? (
            <Badge
              status="primary"
              label={t('map.cheapest')}
              style={{ marginTop: tokens.spacing.xxs }}
            />
          ) : null}
        </View>

        <View style={{ alignItems: 'flex-end', gap: tokens.spacing.xxs }}>
          <View style={{ flexDirection: 'row', alignItems: 'baseline', gap: tokens.spacing.xs }}>
            <Text role="numericSmall" tone={brand.inRange ? 'accent' : 'muted'}>
              {price}
            </Text>
            <Text role="caption" tone="muted">
              {t('map.perLiter')}
            </Text>
          </View>
          {savingsPerLiter > 0 ? (
            <Text role="caption" tone="success">
              −{saving} {t('map.vsPump')}
            </Text>
          ) : null}
        </View>
      </View>
    </Card>
  );
}
