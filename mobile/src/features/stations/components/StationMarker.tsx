import React from 'react';
import { View, Image, StyleSheet, type ImageSourcePropType } from 'react-native';
import { Marker, Callout } from 'react-native-maps';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Text } from '../../../core/ui';
import { formatMoney } from '../../../core/utils/currency';
import { formatShortAddress, type StationPrice } from '../lib/radar';
import type { StationNode } from '../../../core/types/api';

/**
 * The round brand badge used in the ranking list and the station sheet. Pure
 * presentation; sized by the caller.
 *
 * Extracted from `app/map.tsx` so the 1300-line screen is not also the home of its
 * two map-pin components. Behaviour is unchanged -- the markers keep their own
 * `tracksViewChanges` lifecycle (see StationMarker).
 */
export function BrandLogoChip({ logo, size }: { logo: ImageSourcePropType; size: number }) {
  return (
    <View style={[styles.brandLogoChip, { width: size, height: size, borderRadius: size / 2 }]}>
      <Image
        source={logo}
        style={{ width: size * 0.74, height: size * 0.74 }}
        resizeMode="contain"
      />
    </View>
  );
}

/**
 * A single map pin. Owns its `tracksViewChanges` flag: react-native-maps rasterises
 * a custom-view marker once, and with tracking off from the first frame an <Image>
 * can freeze blank before it has painted. Tracking stays on until the logo reports
 * `onLoad` (colour-only pins are ready at mount), then freezes -- otherwise 700+ live
 * markers re-raster every frame.
 *
 * A selected pin keeps tracking while it is selected: the ring and the scale change
 * after the raster would otherwise have frozen, and only one pin is ever selected.
 *
 * The pin is a white disc with the brand's ring, so a logo reads on dark tiles and
 * light ones alike. The callout is the same `Card` language as the rest of the app —
 * opaque `surfaceElevated`, sheet radius, a shadow — where it used to be a blurred
 * pane with a hairline border, which was unreadable against the tiles it floated
 * over.
 */
export function StationMarker({
  point,
  coordinate,
  brandColor,
  logo,
  price,
  selected = false,
  onPress,
}: {
  point: StationNode;
  coordinate: { latitude: number; longitude: number };
  brandColor: string;
  logo?: ImageSourcePropType;
  price: StationPrice | null;
  selected?: boolean;
  onPress: () => void;
}) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);
  const [tracks, setTracks] = React.useState(true);

  React.useEffect(() => {
    if (!logo) {
      const id = setTimeout(() => setTracks(false), 0);
      return () => clearTimeout(id);
    }
  }, [logo]);

  const saving =
    price && price.savingsPerLiter > 0
      ? formatMoney(price.savingsPerLiter, { decimals: 2, hideSymbol: true })
      : null;

  return (
    <Marker
      coordinate={coordinate}
      onPress={onPress}
      tracksViewChanges={tracks || selected}
      accessibilityLabel={`${point.name}, ${formatShortAddress(point) || t('map.noAddress')}`}
    >
      <View style={styles.markerContainer}>
        <View
          style={[
            styles.pin,
            tokens.elevation.low,
            {
              borderRadius: tokens.radius.full,
              borderColor: brandColor,
              borderWidth: selected ? 3 : 2,
              transform: [{ scale: selected ? 1.12 : 1 }],
            },
          ]}
        >
          {logo ? (
            <Image
              source={logo}
              style={styles.markerLogoImg}
              resizeMode="contain"
              onLoad={() => setTracks(false)}
            />
          ) : (
            <View
              style={{
                width: selected ? 14 : 12,
                height: selected ? 14 : 12,
                borderRadius: tokens.radius.full,
                backgroundColor: brandColor,
              }}
            />
          )}
        </View>
        <View
          style={{
            width: 3,
            height: selected ? 7 : 5,
            borderBottomLeftRadius: 2,
            borderBottomRightRadius: 2,
            backgroundColor: brandColor,
          }}
        />
      </View>

      <Callout tooltip>
        <View
          style={[
            {
              width: 232,
              padding: tokens.spacing.fine14,
              borderRadius: tokens.radius.lg,
              gap: tokens.spacing.xxs,
              backgroundColor: tokens.colors.surfaceElevated,
            },
            tokens.elevation.medium,
          ]}
        >
          <Text role="bodyStrong" numberOfLines={1}>
            {point.name}
          </Text>
          <Text role="caption" tone="muted" numberOfLines={2}>
            {formatShortAddress(point) || t('map.noAddress')}
          </Text>
          {price ? (
            <View
              style={{
                flexDirection: 'row',
                alignItems: 'baseline',
                flexWrap: 'wrap',
                gap: tokens.spacing.xs,
                marginTop: tokens.spacing.xs,
              }}
            >
              <Text role="numericSmall" tone="accent">
                {formatMoney(price.voucherPerLiter, { decimals: 2, hideSymbol: true })}
              </Text>
              <Text role="caption" tone="muted">
                {t('map.perLiter')}
              </Text>
              {saving ? (
                <Text role="caption" tone="success">
                  −{saving} {t('map.vsPump')}
                </Text>
              ) : null}
            </View>
          ) : null}
        </View>
      </Callout>
    </Marker>
  );
}

const styles = StyleSheet.create({
  markerContainer: {
    alignItems: 'center',
  },
  pin: {
    width: 34,
    height: 34,
    // White backing is identity, not theme: every brand logo was drawn for a light
    // background, and it is what keeps them legible on the dark basemap.
    backgroundColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
  },
  markerLogoImg: {
    width: 24,
    height: 24,
  },
  brandLogoChip: {
    backgroundColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
  },
});
