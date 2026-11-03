import React from 'react';
import { View, Image, StyleSheet, type ImageSourcePropType } from 'react-native';
import { Marker } from 'react-native-maps';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { formatShortAddress } from '../lib/radar';
import type { StationNode } from '../../../core/types/api';

/**
 * Where a marker with no station waits — Null Island, outside every region this network has, so
 * MapKit never builds a view for it. `opacity={0}` covers the case of a user pulling back far
 * enough to see the Gulf of Guinea, where the pool's empty markers would otherwise sit in it.
 */
const PARKED = { latitude: 0, longitude: 0 };

/**
 * The round brand badge used in the ranking list and the station sheet. Pure
 * presentation; sized by the caller.
 *
 * Extracted from `app/map.tsx` so the screen is not also the home of its two
 * map-pin components. Behaviour is unchanged -- the markers keep their own
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
 * A single map pin.
 *
 * It carries no `Callout`, and that is not a feature waiting to be added. `Callout` is a
 * second legacy view manager nested inside the marker's, and under the New Architecture the
 * Fabric interop layer resolves each pending child through that child's `contentView` — which
 * is nil until the child's own `finalizeUpdates` has run — and hands the result straight to
 * `insertReactSubview:atIndex:`. A marker that mounts with a callout inside it therefore
 * sometimes inserts `nil` into an `NSMutableArray` and dies with `NSInvalidArgumentException`:
 * fatal, with no JS error and no red screen. Upstream has carried it since the New Architecture
 * landed (react-native-maps #5217, #5521 — the reported workaround there is the same one), and
 * a viewport that re-mounts pins on every pan turns it into a lottery instead of a one-off.
 *
 * The tapped station's card is `StationDetailSheet`, which shows the same name, address, price
 * per litre and saving and adds the route action — as an ordinary React surface, so it is not in
 * anybody's native mount order.
 *
 * `point` may be `null`, and that is the other half of the same crash. The map mounts a fixed
 * number of these and hands each one a station to show (`assignToSlots` in `lib/viewport`),
 * because inserting or removing a `Marker` from the map mutates the
 * legacy child array the interop layer indexes into — and when that array and the shadow tree
 * disagree, the app dies in Objective-C. A `null` here is a marker showing nothing: parked where
 * no region of this network reaches, and transparent.
 *
 * The marker keeps its one child view even when empty. `AIRMapMarker` decides what to draw by
 * counting its own react subviews (`shouldUsePinView`), so a childless marker is not an empty
 * slot — it is the stock red MapKit pin, all over the map. What goes *inside* that view is
 * ordinary Fabric and free to come and go, and the pin subtree is keyed by the station so a
 * recycled slot builds it the way a fresh marker would.
 *
 * `tracksViewChanges` is read by the Android marker manager; the iOS MapKit marker has no such
 * prop. There a custom-view marker is rasterised once, so tracking stays on until the logo
 * reports `onLoad` (a colour-only pin is ready at mount) and freezes after — a pin frozen too
 * early keeps a blank disc. A selected pin keeps tracking while it is selected, because its ring
 * and scale change after that raster, and only one pin is ever selected.
 */
export function StationMarker({
  point,
  brandColor,
  logo,
  selected = false,
  onPress,
}: {
  point: StationNode | null;
  brandColor: string;
  logo?: ImageSourcePropType;
  selected?: boolean;
  onPress?: () => void;
}) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);
  const stationId = point?.id ?? null;
  const [tracks, setTracks] = React.useState(point != null);

  // A recycled slot used to show a different АЗК, so this pin has to be rasterised from scratch.
  React.useEffect(() => {
    if (stationId != null) setTracks(true);
  }, [stationId]);

  React.useEffect(() => {
    if (stationId == null) {
      // An empty slot has no pin to rasterise, so it never asks for a freeze. Left unsaid, the pool
      // would schedule a hundred and twenty of these on every render of the map screen.
      setTracks(false);
      return;
    }
    if (!logo) {
      const id = setTimeout(() => setTracks(false), 0);
      return () => clearTimeout(id);
    }
  }, [logo, stationId]);

  return (
    <Marker
      coordinate={
        point ? { latitude: point.lat as number, longitude: point.lng as number } : PARKED
      }
      opacity={point ? 1 : 0}
      onPress={onPress}
      tracksViewChanges={tracks || selected}
      accessibilityLabel={
        point ? `${point.name}, ${formatShortAddress(point) || t('map.noAddress')}` : undefined
      }
    >
      <View style={styles.markerContainer}>
        {point ? (
          <React.Fragment key={point.id}>
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
          </React.Fragment>
        ) : null}
      </View>
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
