import React from 'react';
import { ImageSourcePropType, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { MapPin, Navigation, TrendingDown, X } from 'lucide-react-native';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Button, Card, IconButton, Price, Text } from '../../../core/ui';
import { formatMoney } from '../../../core/utils/currency';
import type { StationNode } from '../../../core/types/api';
import { formatKm, formatShortAddress, type StationPrice } from '../lib/radar';
import { fuelLabel } from '../lib/fuelLabels';
import { BrandLogoChip } from './StationMarker';

interface Props {
  station: StationNode;
  logo?: ImageSourcePropType;
  brandColor: string;
  price: StationPrice | null;
  fuel: string;
  distanceKm: number | null;
  /** False when the node carries no usable coordinates; the CTA says so. */
  routeAvailable: boolean;
  onClose: () => void;
  onBuildRoute: () => void;
}

/**
 * The station card for a tapped pin: who it is, what a litre costs on the selected
 * fuel, what that saves against the pump, and the one thing to do next.
 *
 * A floating surface — `surfaceElevated`, sheet radius, `elevation.medium` — and no
 * top border. It used to be a blurred pane with a 2px brand-coloured top edge,
 * which turned the theme's primary into a neon seam without telling the eye
 * anything the sheet's shadow does not.
 */
export function StationDetailSheet({
  station,
  logo,
  brandColor,
  price,
  fuel,
  distanceKm,
  routeAvailable,
  onClose,
  onBuildRoute,
}: Props) {
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const t = useI18n((s) => s.t);

  const address = formatShortAddress(station) || t('map.noAddress');
  const saving = price
    ? formatMoney(price.savingsPerLiter, { decimals: 2, hideSymbol: true })
    : null;
  const pump = price ? formatMoney(price.pumpPerLiter, { decimals: 2 }) : null;

  return (
    <View
      accessibilityViewIsModal
      style={[
        {
          position: 'absolute',
          left: 0,
          right: 0,
          bottom: 0,
          backgroundColor: tokens.colors.surfaceElevated,
          borderTopLeftRadius: tokens.radius.xl,
          borderTopRightRadius: tokens.radius.xl,
          paddingTop: tokens.spacing.md,
          // Inside a sheet the home-indicator inset is the caller's to add: this
          // surface is anchored to the display, not to the padded content box.
          paddingBottom: insets.bottom + tokens.spacing['2xl'],
          paddingHorizontal: tokens.spacing['2xl'],
          zIndex: tokens.zIndex.sheet,
        },
        tokens.elevation.medium,
      ]}
    >
      <View style={{ flexDirection: 'row', alignItems: 'flex-start', gap: tokens.spacing.md }}>
        {logo ? (
          <BrandLogoChip logo={logo} size={44} />
        ) : (
          <View
            style={{
              width: 44,
              height: 44,
              borderRadius: tokens.radius.full,
              backgroundColor: brandColor,
            }}
          />
        )}

        <View style={{ flex: 1, gap: tokens.spacing.xxs }}>
          <Text role="heading" numberOfLines={2}>
            {station.name}
          </Text>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.xs }}>
            <MapPin size={14} color={tokens.colors.text.muted} />
            <Text role="secondary" tone="muted" numberOfLines={2} style={{ flex: 1 }}>
              {address}
            </Text>
          </View>
        </View>

        <IconButton
          icon={<X />}
          onPress={onClose}
          accessibilityLabel={t('common.close')}
          variant="plain"
          size="md"
        />
      </View>

      <Card tone="sunken" padding="md" style={{ marginTop: tokens.spacing.lg }}>
        <Text role="label" tone="muted">
          {`${t('map.voucherPrice')} · ${fuelLabel(fuel)}`}
        </Text>

        {price ? (
          <>
            <View
              style={{
                flexDirection: 'row',
                alignItems: 'baseline',
                gap: tokens.spacing.xs,
                marginTop: tokens.spacing.xs,
              }}
            >
              <Price amount={price.voucherPerLiter} decimals={2} size="lg" tone="accent" />
              <Text role="body" tone="muted">
                / {t('map.litre')}
              </Text>
            </View>
            {price.savingsPerLiter > 0 ? (
              <View
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: tokens.spacing.xs,
                  marginTop: tokens.spacing.xs,
                }}
              >
                <TrendingDown size={14} color={tokens.colors.status.success.base} />
                <Text role="caption" tone="success">
                  −{saving} {t('map.vsPump')}
                </Text>
              </View>
            ) : null}
          </>
        ) : (
          <Text role="body" tone="muted" style={{ marginTop: tokens.spacing.xs }}>
            {t('map.noPriceForFuel')}
          </Text>
        )}
      </Card>

      <View
        style={{
          flexDirection: 'row',
          flexWrap: 'wrap',
          justifyContent: 'space-between',
          gap: tokens.spacing.sm,
          marginTop: tokens.spacing.md,
        }}
      >
        {distanceKm != null ? (
          <Text role="caption" tone="muted">
            {`${t('map.yourDistance')} · ${formatKm(distanceKm)} ${t('map.km')}`}
          </Text>
        ) : null}
        {pump && price && price.savingsPerLiter > 0 ? (
          <Text role="caption" tone="muted">
            {`${t('map.pumpPrice')} ${pump}`}
          </Text>
        ) : null}
      </View>

      <Button
        label={routeAvailable ? t('map.buildRoute') : t('map.routeUnavailable')}
        onPress={onBuildRoute}
        disabled={!routeAvailable}
        icon={<Navigation />}
        size="lg"
        fullWidth
        style={{ marginTop: tokens.spacing.lg }}
        accessibilityLabel={routeAvailable ? t('map.buildRoute') : t('map.routeUnavailable')}
      />
    </View>
  );
}
