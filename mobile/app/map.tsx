import React from 'react';
import { StyleSheet, View } from 'react-native';
import MapView, { UrlTile } from 'react-native-maps';
import { Navigation, TrendingDown } from 'lucide-react-native';
import { useQueryClient } from '@tanstack/react-query';

import {
  Button,
  Card,
  IconButton,
  InlineFeedback,
  LoadingState,
  PageLayout,
  ScreenHeader,
} from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { useTabBarOverride } from '../src/core/navigation/tabBarOverride';
import { BRAND_COLORS, BRAND_LOGOS, BRAND_SYMBOLS } from '../src/core/design/tokens';
import { useStationNodes } from '../src/features/stations/hooks/useStationNodes';
import { useAllPackages } from '../src/features/stations/hooks/useAllPackages';
import { useUserLocation } from '../src/features/stations/hooks/useUserLocation';
import {
  availableFuels,
  bestPriceByBrand,
  haversineKm,
  radarWithinRadius,
  rankBrands,
  rankStations,
} from '../src/features/stations/lib/radar';
import { DEFAULT_FUEL } from '../src/features/stations/lib/fuelLabels';
import { resolveCartoApiKey } from '../src/features/stations/lib/basemap';
import { filterStationsByQuery } from '../src/features/stations/lib/search';
import { routeTarget } from '../src/features/stations/lib/navigation';
import { isMarkerPressEcho } from '../src/features/stations/lib/selection';
import {
  assignToSlots,
  isRegionCoveredByBox,
  type MapRegion,
} from '../src/features/stations/lib/viewport';
import { NavigatorPickerSheet } from '../src/features/stations/components/NavigatorPickerSheet';
import { MapSearchDeck } from '../src/features/stations/components/MapSearchDeck';
import {
  NetworkRankingHeader,
  NetworkRankingList,
} from '../src/features/stations/components/NetworkRanking';
import { StationDetailSheet } from '../src/features/stations/components/StationDetailSheet';
import { StationMarker } from '../src/features/stations/components/StationMarker';
import {
  BottomSheet,
  type BottomSheetHandle,
} from '../src/features/stations/components/BottomSheet';
import type { StationNode } from '../src/core/types/api';

const KYIV_REGION: MapRegion = {
  latitude: 50.4501,
  longitude: 30.5234,
  latitudeDelta: 0.15,
  longitudeDelta: 0.15,
};

/**
 * The network map and its price radar.
 *
 * The screen owns the data and the three decisions worth failing on — which fuel,
 * which stations are in view, and what the radar ranks — and hands the drawing to
 * `features/stations/components`: the search deck, the ranking sheet, the station
 * sheet and the pin. Everything visible here reads from the design system, so the
 * screen follows all eight themes instead of the two it used to be tuned for.
 *
 * Two surfaces own the bottom of the display: the ranking sheet and a station's
 * card. While either is open the tab bar is hidden (`useTabBarOverride`) and
 * `useContentInsets` gives the bar's clearance back — the sheet reaches the home
 * indicator instead of leaving a strip of empty map under the bar.
 */
export default function MapScreen() {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);
  const queryClient = useQueryClient();

  const { data: nodes, isLoading, error } = useStationNodes();
  const { data: packages } = useAllPackages();
  const { location, status: locationStatus, request: requestLocation } = useUserLocation();

  const [searchQuery, setSearchQuery] = React.useState('');
  const [selectedStation, setSelectedStation] = React.useState<StationNode | null>(null);
  const [selectedFuel, setSelectedFuel] = React.useState<string>(DEFAULT_FUEL);
  const [showList, setShowList] = React.useState(false);
  const [showNavigatorPicker, setShowNavigatorPicker] = React.useState(false);
  const [selectedBrand, setSelectedBrand] = React.useState<string | null>(null);

  // The tap that opens a station also reaches the map's press handler on iOS - see
  // isMarkerPressEcho.
  const lastMarkerPressAt = React.useRef<number | null>(null);
  const mapRef = React.useRef<MapView | null>(null);
  const sheetRef = React.useRef<BottomSheetHandle>(null);

  // The tab bar is a sibling of this scene inside the navigator, so it paints over
  // anything the screen draws at the bottom whatever z-index the sheet carries.
  // RN only compares z-index between siblings. Hand the bar over instead.
  const setTabBarHidden = useTabBarOverride((s) => s.setHidden);
  const bottomOwned = showList || selectedStation != null;
  React.useEffect(() => {
    setTabBarHidden(bottomOwned);
    return () => setTabBarHidden(false);
  }, [bottomOwned, setTabBarHidden]);

  const allPoints = React.useMemo<StationNode[]>(() => nodes ?? [], [nodes]);

  const filteredPoints = React.useMemo(
    () => filterStationsByQuery(allPoints, searchQuery),
    [allPoints, searchQuery],
  );

  // Which pins to show, and how often to work that out. The count tracks the *area* on screen —
  // which is what a pan changes — so it is bounded to MAX_MOUNTED_MARKERS, and
  // `markersForRegion` keeps a spread of that area rather than the first `limit` stations the API
  // listed, on a grid anchored to the ground so a pan cannot re-shuffle it. The box is frozen at
  // whatever region it was last computed for and recomputed only once the screen genuinely leaves
  // it, so the settles in the middle of a long pan change nothing at all.
  const [markerBox, setMarkerBox] = React.useState<MapRegion>(KYIV_REGION);

  // The other half is that nothing is ever mounted after the pool exists. The screen waits for the
  // map's own first render, then builds `MARKER_POOL_SIZE` markers already carrying the pins of the
  // region on screen, and afterwards only changes which АЗК each one shows — because a `Marker` is a
  // legacy view manager: adding or removing one makes the Fabric interop layer replay that change
  // into the map's own child array at an index the array does not necessarily have, and both ways it
  // fails fatally, in Objective-C, with no JS error to read. See `assignToSlots`.
  //
  // `null` is "the pool does not exist yet". The wait is not ceremony, but it is not what puts a brand
  // badge on a pin either: under Fabric a marker is childless when the map registers it, so which view
  // MapKit displays is settled by props — the `image` on `StationMarker` — not by which commit the pool
  // arrives in. Deferring keeps a hundred and twenty markers, and the subtrees inside them, out of the
  // commit that creates the map; the second half of the gate keeps a pool that could show nothing off
  // the screen altogether.
  const [mapIsReady, setMapIsReady] = React.useState(false);
  const [slots, setSlots] = React.useState<(StationNode | null)[] | null>(null);

  React.useEffect(() => {
    // Both halves of the gate, and the second is not redundant: an all-empty pool would be a hundred
    // and twenty markers mounted only to be updated one frame later.
    if (!mapIsReady || filteredPoints.length === 0) return;
    setSlots((previous) => {
      const next = assignToSlots(filteredPoints, markerBox, previous ?? []);
      // Every station still in the same slot means every marker gets the props it already has, so
      // bail out instead of re-rendering a hundred and twenty pins for a search keystroke.
      const unchanged =
        previous != null && next.every((point, index) => point?.id === previous[index]?.id);
      return unchanged ? previous : next;
    });
  }, [mapIsReady, filteredPoints, markerBox]);

  const fuelOptions = React.useMemo(() => availableFuels(packages ?? []), [packages]);

  // Keep the selected fuel valid as packages load: prefer А95, else the first available.
  React.useEffect(() => {
    if (!fuelOptions.length || fuelOptions.includes(selectedFuel)) return;
    setSelectedFuel(fuelOptions.includes(DEFAULT_FUEL) ? DEFAULT_FUEL : fuelOptions[0]);
  }, [fuelOptions, selectedFuel]);

  const priceByBrand = React.useMemo(
    () => bestPriceByBrand(packages ?? [], selectedFuel),
    [packages, selectedFuel],
  );

  const rankedNearby = React.useMemo(
    () => rankStations(filteredPoints, priceByBrand, location),
    [filteredPoints, priceByBrand, location],
  );

  // The price radar widens its radius 5→10→20→50→100 km until it holds enough
  // priced АЗК, so a cheaper station on the far side of the country can't outrank
  // the pumps actually near the user.
  const radar = React.useMemo(
    () => radarWithinRadius(rankedNearby, location),
    [rankedNearby, location],
  );

  // Network leaderboard: one row per brand, cheapest voucher грн/л first. The whole
  // priced list goes in, not just `radar.stations`: a network with nothing inside
  // the ring would otherwise vanish and look like it does not exist.
  const brandRanks = React.useMemo(
    () =>
      rankBrands(
        rankedNearby.filter((r) => r.price != null),
        radar.radiusKm,
      ),
    [rankedNearby, radar.radiusKm],
  );

  const activeBrand = React.useMemo(
    () => (selectedBrand ? (brandRanks.find((b) => b.brandId === selectedBrand) ?? null) : null),
    [brandRanks, selectedBrand],
  );

  // Centre on the user whenever a fresh position arrives (locate tap, or an on-mount
  // restore of a previously-granted permission).
  React.useEffect(() => {
    if (!location) return;
    mapRef.current?.animateToRegion(
      {
        latitude: location.lat,
        longitude: location.lng,
        latitudeDelta: 0.08,
        longitudeDelta: 0.08,
      },
      500,
    );
  }, [location]);

  const focusStation = React.useCallback((node: StationNode) => {
    setSelectedStation(node);
    setShowList(false);
    setSelectedBrand(null);
    if (node.lat && node.lng) {
      mapRef.current?.animateToRegion(
        { latitude: node.lat, longitude: node.lng, latitudeDelta: 0.05, longitudeDelta: 0.05 },
        400,
      );
    }
  }, []);

  const handleLocate = React.useCallback(() => {
    void requestLocation();
  }, [requestLocation]);

  // A marker hands its slot's station here. An empty slot gets no handler at all, and being
  // transparent it is not hit-tested either.
  const pressMarker = React.useCallback((node: StationNode) => {
    lastMarkerPressAt.current = Date.now();
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    setSelectedStation(node);
  }, []);

  // A route needs a real position: a node without coordinates used to deep-link to
  // 0,0 — Null Island in the Atlantic — so the button reports that instead.
  const routeDestination = React.useMemo(
    () => (selectedStation ? routeTarget(selectedStation.lat, selectedStation.lng) : null),
    [selectedStation],
  );

  const selectedDistanceKm = React.useMemo(() => {
    if (!selectedStation || !location) return null;
    const { lat, lng } = selectedStation;
    if (lat == null || lng == null || (lat === 0 && lng === 0)) return null;
    return haversineKm(location, { lat, lng });
  }, [selectedStation, location]);

  // CARTO's public basemap CDN watermarks unauthenticated tiles with
  // "API KEY REQUIRED" (policy change, 2025). Without a key we render NO raster
  // overlay and fall back to the native basemap, so the map degrades to a plain but
  // clean map instead of showing the watermark. See planning #102.
  const cartoKey = resolveCartoApiKey();
  const tileUrl = cartoKey
    ? tokens.colors.isDark
      ? `https://basemaps.cartocdn.com/rastertiles/dark_all/{z}/{x}/{y}@2x.png?key=${cartoKey}`
      : `https://basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}@2x.png?key=${cartoKey}`
    : null;

  /*
   * The background is load-bearing. The header slot reserves the 28pt band its
   * corner targets answer in by pulling the content up under it, and without an
   * opaque fill the map's pins painted straight through the title and subtitle.
   */
  const header = (
    <ScreenHeader
      title={t('map.title')}
      subtitle={`${filteredPoints.length} ${t('map.stationsShort')}`}
      hideBack
      style={{ backgroundColor: tokens.colors.background }}
    />
  );

  return (
    <View style={styles.screen}>
      <PageLayout header={header} padding="none" scroll={false}>
        <View style={styles.mapWrapper}>
          <MapView
            ref={mapRef}
            style={StyleSheet.absoluteFill}
            initialRegion={KYIV_REGION}
            onPress={() => {
              if (isMarkerPressEcho(lastMarkerPressAt.current, Date.now())) return;
              setSelectedStation(null);
            }}
            onMapReady={() => setMapIsReady(true)}
            onRegionChangeComplete={(region) => {
              if (isRegionCoveredByBox(region, markerBox)) return;
              setMarkerBox(region);
            }}
            userInterfaceStyle={tokens.colors.isDark ? 'dark' : 'light'}
            showsUserLocation={locationStatus === 'granted'}
            showsMyLocationButton={false}
          >
            {(slots ?? []).map((point, index) => (
              <StationMarker
                key={`slot-${index}`}
                point={point}
                brandColor={
                  point
                    ? BRAND_COLORS[point.brandSlug] || tokens.colors.primary
                    : tokens.colors.primary
                }
                logo={
                  point
                    ? (BRAND_SYMBOLS[point.brandSlug] ?? BRAND_LOGOS[point.brandSlug])
                    : undefined
                }
                selected={point != null && selectedStation?.id === point.id}
                onPress={point ? () => pressMarker(point) : undefined}
              />
            ))}

            {/*
             * Last among the map's children, on purpose. `resolveCartoApiKey` can answer late — a
             * native build reads `Constants.expoConfig` only once the manifest is there — so this
             * overlay is a child that *appears* after mount, and where it appears decides whether
             * that is safe: the interop layer mounts a child appended at the end
             * (`index == reactSubviews.count`) synchronously, but anything else is queued as a mount
             * instruction against an index the array may not have. First position means every one of
             * the 120 markers is re-indexed, which is the mutation that dies in `finalizeUpdates:` —
             * the same crash the pool was built to avoid, arriving through a component the pool does
             * not control. Overlays and annotations are separate arrays in MapKit, so this changes no
             * stacking: the tiles stay under the pins.
             */}
            {tileUrl && <UrlTile urlTemplate={tileUrl} maximumZ={19} flipY={false} />}
          </MapView>

          <View
            style={{
              position: 'absolute',
              // Below the header row, not under it: the content box starts one corner
              // drop above the row's bottom edge.
              top: tokens.chrome.headerCornerDrop + tokens.spacing.md,
              left: tokens.spacing.containerPadding,
              right: tokens.spacing.containerPadding,
              zIndex: tokens.zIndex.sticky,
            }}
          >
            <MapSearchDeck
              query={searchQuery}
              onQueryChange={setSearchQuery}
              fuels={fuelOptions}
              selectedFuel={selectedFuel}
              onSelectFuel={setSelectedFuel}
            />
          </View>

          {!showList && !selectedStation ? (
            <View
              pointerEvents="box-none"
              style={[
                styles.fabColumn,
                {
                  right: tokens.spacing.containerPadding,
                  bottom: tokens.spacing.lg,
                  gap: tokens.spacing.md,
                  zIndex: tokens.zIndex.sticky,
                },
              ]}
            >
              {/* A floating fill needs a layer of its own to cast the shadow: the
                  button's own fill lives on a view inside its pressable. */}
              <View
                style={[
                  { borderRadius: tokens.radius.md, backgroundColor: tokens.colors.primary },
                  tokens.elevation.low,
                ]}
              >
                <Button
                  label={t('map.networkRanking')}
                  icon={<TrendingDown />}
                  size="md"
                  fullWidth={false}
                  onPress={() => {
                    setSelectedBrand(null);
                    setShowList(true);
                  }}
                />
              </View>

              <View
                style={[
                  {
                    borderRadius: tokens.radius.full,
                    backgroundColor: tokens.colors.surfaceElevated,
                  },
                  tokens.elevation.low,
                ]}
              >
                <IconButton
                  icon={<Navigation />}
                  onPress={handleLocate}
                  accessibilityLabel={t('map.locateMe')}
                  variant={locationStatus === 'granted' ? 'filled' : 'plain'}
                  size="lg"
                  round
                />
              </View>
            </View>
          ) : null}

          {isLoading ? (
            <View
              pointerEvents="none"
              style={[styles.centerOverlay, { zIndex: tokens.zIndex.sticky }]}
            >
              <Card tone="elevated" padding="md">
                <LoadingState variant="inline" message={t('map.loadingStations')} />
              </Card>
            </View>
          ) : null}

          {error && !isLoading ? (
            <View
              style={[
                styles.centerOverlay,
                {
                  paddingHorizontal: tokens.spacing.containerPadding,
                  zIndex: tokens.zIndex.sticky,
                },
              ]}
            >
              <InlineFeedback
                kind="danger"
                message={error instanceof Error ? error.message : t('state.errorDescription')}
                action={{
                  label: t('common.retry'),
                  onPress: () => queryClient.invalidateQueries({ queryKey: ['station-nodes'] }),
                }}
              />
            </View>
          ) : null}
        </View>
      </PageLayout>

      {showList && !selectedStation ? (
        <BottomSheet
          ref={sheetRef}
          onClose={() => {
            setShowList(false);
            setSelectedBrand(null);
          }}
          header={
            <NetworkRankingHeader
              activeBrand={activeBrand}
              fuel={selectedFuel}
              radiusKm={radar.radiusKm}
              locationDenied={locationStatus === 'denied'}
              onBack={() => setSelectedBrand(null)}
              onClose={() => sheetRef.current?.close()}
            />
          }
        >
          <NetworkRankingList
            brands={brandRanks}
            activeBrand={activeBrand}
            onSelectBrand={(brandId) => {
              setSelectedBrand(brandId);
              sheetRef.current?.expand();
            }}
            onSelectStation={focusStation}
          />
        </BottomSheet>
      ) : null}

      {selectedStation ? (
        <StationDetailSheet
          station={selectedStation}
          logo={BRAND_LOGOS[selectedStation.brandSlug]}
          brandColor={BRAND_COLORS[selectedStation.brandSlug] || tokens.colors.primary}
          price={priceByBrand.get(selectedStation.brandId) ?? null}
          fuel={selectedFuel}
          distanceKm={selectedDistanceKm}
          routeAvailable={routeDestination != null}
          onClose={() => setSelectedStation(null)}
          onBuildRoute={() => setShowNavigatorPicker(true)}
        />
      ) : null}

      {/* Which navigator to hand the route to — the platform's choice is the user's.
          Mounted only with a real destination, so its probe has a stable target. */}
      {routeDestination ? (
        <NavigatorPickerSheet
          visible={showNavigatorPicker}
          target={routeDestination}
          destinationName={selectedStation?.name ?? ''}
          onClose={() => setShowNavigatorPicker(false)}
        />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
  },
  mapWrapper: {
    flex: 1,
    position: 'relative',
  },
  fabColumn: {
    position: 'absolute',
    alignItems: 'flex-end',
  },
  centerOverlay: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
