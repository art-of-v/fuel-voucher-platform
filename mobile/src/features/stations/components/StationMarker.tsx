import React from 'react';
import { View, Image, Text, StyleSheet, type ImageSourcePropType } from 'react-native';
import { Marker, Callout } from 'react-native-maps';
import { BlurView } from 'expo-blur';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { formatShortAddress, type StationPrice } from '../lib/radar';
import type { Station, StationNode } from '../../../core/types/api';

type Tokens = ReturnType<typeof useDesignTokens>;

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
 */
export function StationMarker({
  point,
  coordinate,
  brandColor,
  logo,
  price,
  tokens,
  t,
  onPress,
}: {
  point: Station | StationNode;
  coordinate: { latitude: number; longitude: number };
  brandColor: string;
  logo?: ImageSourcePropType;
  price: StationPrice | null;
  tokens: Tokens;
  t: (key: string, ...params: string[]) => string;
  onPress: () => void;
}) {
  const [tracks, setTracks] = React.useState(true);
  React.useEffect(() => {
    if (!logo) {
      const id = setTimeout(() => setTracks(false), 0);
      return () => clearTimeout(id);
    }
  }, [logo]);

  return (
    <Marker coordinate={coordinate} onPress={onPress} tracksViewChanges={tracks}>
      <View style={styles.markerContainer}>
        {logo ? (
          <View style={[styles.markerLogo, { borderColor: brandColor }]}>
            <Image
              source={logo}
              style={styles.markerLogoImg}
              resizeMode="contain"
              onLoad={() => setTracks(false)}
            />
          </View>
        ) : (
          <View
            style={[
              styles.marker,
              { borderColor: brandColor, backgroundColor: tokens.colors.background },
            ]}
          >
            <View
              style={[
                styles.markerInner,
                {
                  backgroundColor: brandColor,
                  shadowColor: brandColor,
                  shadowRadius: 5,
                  shadowOpacity: 0.5,
                },
              ]}
            />
          </View>
        )}
        <View style={[styles.markerStem, { backgroundColor: brandColor }]} />
      </View>

      <Callout tooltip>
        <BlurView
          intensity={tokens.colors.isDark ? 80 : 90}
          tint={tokens.colors.isDark ? 'dark' : 'light'}
          style={[styles.calloutContainer, { borderColor: tokens.colors.borderLight }]}
        >
          <Text style={[styles.calloutTitle, { color: tokens.colors.text.primary }]}>
            {point.name}
          </Text>
          <Text style={[styles.calloutText, { color: tokens.colors.text.dim }]}>
            {formatShortAddress(point) || t('map.noAddress')}
          </Text>
          {price && (
            <Text style={[styles.calloutPrice, { color: tokens.colors.primary }]}>
              {price.voucherPerLiter.toFixed(2)} {t('map.perLiter')}
              {price.savingsPerLiter > 0
                ? `  -${price.savingsPerLiter.toFixed(2)} ${t('map.vsPump')}`
                : ''}
            </Text>
          )}
        </BlurView>
      </Callout>
    </Marker>
  );
}

const styles = StyleSheet.create({
  markerContainer: {
    alignItems: 'center',
  },
  marker: {
    width: 30,
    height: 30,
    borderRadius: 15,
    borderWidth: 2,
    alignItems: 'center',
    justifyContent: 'center',
    // A map pin needs to lift off the tiles regardless of theme, so this drop
    // shadow is deliberately a neutral black rather than a themed colour.
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.5,
    shadowRadius: 4,
  },
  markerInner: {
    width: 10,
    height: 10,
    borderRadius: 5,
  },
  markerStem: {
    width: 2,
    height: 4,
    marginTop: -1,
  },
  markerLogo: {
    width: 34,
    height: 34,
    borderRadius: 17,
    borderWidth: 2,
    backgroundColor: '#fff',
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
    // Lift the pin off the tiles regardless of theme (neutral black, like `marker`).
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.5,
    shadowRadius: 4,
  },
  markerLogoImg: {
    width: 24,
    height: 24,
  },
  brandLogoChip: {
    backgroundColor: '#fff',
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
  },
  calloutContainer: {
    width: 220,
    padding: 16,
    borderRadius: 16,
    borderWidth: 1,
    overflow: 'hidden',
  },
  calloutTitle: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 18,
    marginBottom: 4,
  },
  calloutText: {
    fontFamily: 'Inter-Medium',
    fontSize: 12,
    lineHeight: 16,
  },
  calloutPrice: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 14,
    marginTop: 6,
  },
});
