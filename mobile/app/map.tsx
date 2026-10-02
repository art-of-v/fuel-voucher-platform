import React from 'react';
import { View, Text, StyleSheet, Pressable, Platform, TextInput, Linking, ScrollView, Image, type ImageSourcePropType } from 'react-native';
import { InlineFeedback, LoadingState, PageLayout, ScreenHeader } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { Search, ChevronDown, ChevronLeft, ChevronRight, MapPin, Navigation, Fuel, TrendingDown } from 'lucide-react-native';
import MapView, { UrlTile, Marker, Callout } from 'react-native-maps';
import { useStationNodes } from '../src/features/stations/hooks/useStationNodes';
import { useAllPackages } from '../src/features/stations/hooks/useAllPackages';
import { useUserLocation } from '../src/features/stations/hooks/useUserLocation';
import { availableFuels, bestPriceByStation, formatShortAddress, radarWithinRadius, rankBrands, rankStations, type StationPrice } from '../src/features/stations/lib/radar';
import { resolveCartoApiKey } from '../src/features/stations/lib/basemap';
import { wazeNavigationUrl, platformMapsUrl } from '../src/features/stations/lib/navigation';
import { BottomSheet, type BottomSheetHandle } from '../src/features/stations/components/BottomSheet';
import { BRAND_COLORS, BRAND_LOGOS } from '../src/core/design/tokens';
import { useQueryClient } from '@tanstack/react-query';
import type { Station, StationNode } from '../src/core/types/api';
import { BlurView } from 'expo-blur';

const KYIV_REGION = {
    latitude: 50.4501,
    longitude: 30.5234,
    latitudeDelta: 0.15,
    longitudeDelta: 0.15,
};

/*
 * Fuel names are API/brand values, not UI copy (see normalizeFuelName in
 * core/utils/formatters) — they read the same in every locale, so the selector labels
 * live here rather than in i18n. Keys are the canonical ids normalizeFuelName emits.
 */
const FUEL_LABELS: Record<string, string> = {
    'a-92': 'А92',
    'a-95': 'А95',
    'a-95 premium': 'А95 преміум',
    'a-98': 'А98',
    '100': '100',
    '100 premium': '100 преміум',
    diesel: 'ДП',
    'diesel premium': 'ДП преміум',
    gas: 'Газ',
    'gas premium': 'Газ преміум',
};
const DEFAULT_FUEL = 'a-95';
const fuelLabel = (canonical: string): string => FUEL_LABELS[canonical] ?? canonical.toUpperCase();

type Tokens = ReturnType<typeof useDesignTokens>;

/**
 * A network logo on a white chip — the primary brand mark now that the brand
 * accents (three greens + a yellow) no longer tell OKKO / WOG / UPG apart at a
 * glance. White backing keeps every logo legible on dark and light tiles alike.
 */
function BrandLogoChip({ logo, size }: { logo: ImageSourcePropType; size: number }) {
    return (
        <View style={[styles.brandLogoChip, { width: size, height: size, borderRadius: size / 2 }]}>
            <Image source={logo} style={{ width: size * 0.74, height: size * 0.74 }} resizeMode="contain" />
        </View>
    );
}

/**
 * A single map pin. Extracted so each marker owns its `tracksViewChanges` flag:
 * react-native-maps rasterises a custom-view marker once, and with tracking off
 * from the first frame an <Image> can freeze blank before it has painted. We keep
 * tracking until the logo reports `onLoad` (colour-only pins are ready at mount),
 * then freeze — otherwise 700+ live markers re-raster every frame.
 */
function StationMarker({
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
                    <View style={[styles.marker, { borderColor: brandColor, backgroundColor: tokens.colors.background }]}>
                        <View style={[styles.markerInner, { backgroundColor: brandColor, shadowColor: brandColor, shadowRadius: 5, shadowOpacity: 0.5 }]} />
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
                    <Text style={[styles.calloutTitle, { color: tokens.colors.text.primary }]}>{point.name}</Text>
                    <Text style={[styles.calloutText, { color: tokens.colors.text.dim }]}>{formatShortAddress(point) || t('map.noAddress')}</Text>
                    {price && (
                        <Text style={[styles.calloutPrice, { color: tokens.colors.primary }]}>
                            {price.voucherPerLiter.toFixed(2)} {t('map.perLiter')}
                            {price.savingsPerLiter > 0 ? `  −${price.savingsPerLiter.toFixed(2)} ${t('map.vsPump')}` : ''}
                        </Text>
                    )}
                </BlurView>
            </Callout>
        </Marker>
    );
}

export default function MapScreen() {
    const tokens = useDesignTokens();
    const { t } = useI18n();
    const queryClient = useQueryClient();
    const { data: nodes, isLoading, error } = useStationNodes();
    const [searchQuery, setSearchQuery] = React.useState("");
    const [selectedStation, setSelectedStation] = React.useState<Station | StationNode | null>(null);
    const [selectedFuel, setSelectedFuel] = React.useState<string>(DEFAULT_FUEL);
    const [showList, setShowList] = React.useState(false);
    const [selectedBrand, setSelectedBrand] = React.useState<string | null>(null);
    const { data: packages } = useAllPackages();
    const { location, status: locationStatus, request: requestLocation } = useUserLocation();
    const mapRef = React.useRef<MapView | null>(null);
    const sheetRef = React.useRef<BottomSheetHandle>(null);

    const GLOBAL_PADDING = tokens.spacing.containerPadding;

    const allPoints = React.useMemo(() => {
        const points: (Station | StationNode)[] = [];
        if (nodes) points.push(...nodes);
        return points;
    }, [nodes]);

    const filteredPoints = React.useMemo(() => {
        if (!allPoints) return [];
        const q = searchQuery.toLowerCase().trim();
        return allPoints.filter(s => {
            const fLat = parseFloat(s.lat || "0");
            const fLng = parseFloat(s.lng || "0");
            if (!fLat || !fLng) return false;
            if (!q) return true;
            const searchTargets = [s.name.toLowerCase(), (s as any).address?.toLowerCase() || '', (s as any).city?.toLowerCase() || ''];
            return searchTargets.some(target => {
                if (target.includes(q)) return true;
                if (q === 'okko' && target.includes('окко')) return true;
                if (q === 'wog' && target.includes('вог')) return true;
                if (q === 'klo' && target.includes('кло')) return true;
                if (q === 'upg' && target.includes('юпі')) return true;
                return false;
            });
        });
    }, [allPoints, searchQuery]);

    const fuelOptions = React.useMemo(() => availableFuels(packages ?? []), [packages]);

    // Keep the selected fuel valid as packages load: prefer А95, else the first available.
    React.useEffect(() => {
        if (!fuelOptions.length || fuelOptions.includes(selectedFuel)) return;
        setSelectedFuel(fuelOptions.includes(DEFAULT_FUEL) ? DEFAULT_FUEL : fuelOptions[0]);
    }, [fuelOptions, selectedFuel]);

    const priceByStation = React.useMemo(
        () => bestPriceByStation(packages ?? [], selectedFuel),
        [packages, selectedFuel],
    );

    // filteredPoints only ever holds station nodes (allPoints pushes nodes only), so the
    // cast is safe; ranking respects the current search filter.
    const rankedNearby = React.useMemo(
        () => rankStations(filteredPoints as StationNode[], priceByStation, location),
        [filteredPoints, priceByStation, location],
    );

    // The price radar: the nearby list widens its radius 5→10→20→50→100 km until it holds
    // enough priced АЗК, so a cheaper station on the far side of the country can't outrank
    // the pumps actually near the user. Falls back to all priced stations (no radius) when
    // location is unknown or nothing sits within the widest ring.
    const radar = React.useMemo(
        () => radarWithinRadius(rankedNearby, location),
        [rankedNearby, location],
    );

    // Network leaderboard: collapse the in-radius АЗК into one row per brand, cheapest
    // voucher грн/л first — the customer compares networks, then drills into a brand to see
    // how far its nearest pumps are. (Prices are per-brand, so a flat node list hid this.)
    const brandRanks = React.useMemo(() => rankBrands(radar.stations), [radar.stations]);

    const activeBrand = React.useMemo(
        () => (selectedBrand ? brandRanks.find(b => b.stationId === selectedBrand) ?? null : null),
        [brandRanks, selectedBrand],
    );

    // Centre on the user whenever a fresh position arrives (locate tap, or an on-mount
    // restore of a previously-granted permission).
    React.useEffect(() => {
        if (!location) return;
        mapRef.current?.animateToRegion(
            { latitude: location.lat, longitude: location.lng, latitudeDelta: 0.08, longitudeDelta: 0.08 },
            500,
        );
    }, [location]);

    const focusStation = React.useCallback((node: StationNode) => {
        setSelectedStation(node);
        setShowList(false);
        setSelectedBrand(null);
        const lat = parseFloat(node.lat || '0');
        const lng = parseFloat(node.lng || '0');
        if (lat && lng) {
            mapRef.current?.animateToRegion(
                { latitude: lat, longitude: lng, latitudeDelta: 0.05, longitudeDelta: 0.05 },
                400,
            );
        }
    }, []);

    const handleLocate = React.useCallback(() => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        void requestLocation();
    }, [requestLocation]);

    /*
     * Was a hand-rolled header: a `GlowText` title at a bespoke 24pt/3-letter-spacing
     * inside a `View` that applied `paddingTop: insets.top + 10`. Two problems, both
     * fixed by moving to the shared header:
     *
     * 1. `PageLayout` already wraps the screen in `SafeAreaView edges={['top', …]}`,
     *    so reading `useSafeAreaInsets()` here added the notch a second time. This
     *    was the only screen in the app still reading insets directly.
     * 2. It was the last header not on `ScreenHeader`, so the map's title sat at a
     *    size and alignment nothing else in the product used.
     *
     * The hairline stays: the map is the one edge-to-edge canvas in the app, so the
     * title needs a boundary that the other screens' whitespace gives them for free.
     */
    const headerComponent = (
        <ScreenHeader
            title={t('map.title')}
            subtitle={`${filteredPoints.length} ${t('map.stations_nearby')}`}
            hideBack
            style={{ borderBottomWidth: 1, borderBottomColor: tokens.colors.borderLight }}
        />
    );

    // CARTO's public basemap CDN now watermarks unauthenticated tiles with
    // "API KEY REQUIRED" (policy change, 2025). A free key (no account, 5M tiles/mo)
    // is passed as ?key= on every tile URL. resolveCartoApiKey() reads it from the
    // EXPO_PUBLIC_CARTO_API_KEY env var (EAS builds) and falls back to the committed
    // app.json copy (expo.extra.cartoApiKey), so a raw Xcode Archive — which never sees
    // that env — still gets the key. When absent we render NO raster overlay and fall
    // back to the native basemap, so the map degrades to a plain-but-clean map instead
    // of showing the watermark. See planning #102.
    const cartoKey = resolveCartoApiKey();
    const tileUrl = cartoKey
        ? (tokens.colors.isDark
            ? `https://basemaps.cartocdn.com/rastertiles/dark_all/{z}/{x}/{y}@2x.png?key=${cartoKey}`
            : `https://basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}@2x.png?key=${cartoKey}`)
        : null;

    return (
        <PageLayout header={headerComponent} padding="none" scroll={false}>
            <View style={styles.container}>
                <View style={styles.mapWrapper}>
                    <MapView
                        ref={mapRef}
                        style={StyleSheet.absoluteFill}
                        initialRegion={KYIV_REGION}
                        onPress={() => setSelectedStation(null)}
                        userInterfaceStyle={tokens.colors.isDark ? 'dark' : 'light'}
                        showsUserLocation={locationStatus === 'granted'}
                        showsMyLocationButton={false}
                    >
                        {tileUrl && <UrlTile urlTemplate={tileUrl} maximumZ={19} flipY={false} />}

                        {filteredPoints.map(point => {
                            const isNode = 'stationId' in point;
                            const coordinate = {
                                latitude: parseFloat(point.lat!),
                                longitude: parseFloat(point.lng!),
                            };
                            const stationId = isNode ? (point as StationNode).stationId : undefined;
                            // Pins carry each network's own logo (and colour as the border /
                            // fallback — data, not theme, see BRAND_LOGOS / BRAND_COLORS).
                            const brandColor = (stationId && BRAND_COLORS[stationId]) || tokens.colors.primary;
                            const logo = stationId ? BRAND_LOGOS[stationId] : undefined;
                            const price = stationId ? priceByStation.get(stationId) ?? null : null;

                            return (
                                <StationMarker
                                    key={`${isNode ? 'node' : 'station'}-${point.id}`}
                                    point={point}
                                    coordinate={coordinate}
                                    brandColor={brandColor}
                                    logo={logo}
                                    price={price}
                                    tokens={tokens}
                                    t={t}
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                        setSelectedStation(point);
                                    }}
                                />
                            );
                        })}
                    </MapView>

                    {/* Scrim behind the floating search field so it stays legible over tiles. */}
                    <View style={[styles.topGradient, { backgroundColor: tokens.colors.background, opacity: 0.3 }]} />

                    {/* Search Bar Overlay - Glassmorphism */}
                    <View style={[styles.searchOverlay, { paddingHorizontal: GLOBAL_PADDING }]}>
                        <BlurView
                            intensity={tokens.colors.isDark ? 30 : 60}
                            tint={tokens.colors.isDark ? "dark" : "light"}
                            style={[styles.searchBox, { borderColor: tokens.colors.border, borderWidth: 1 }]}
                        >
                            <Search size={20} color={tokens.colors.primary} />
                            <TextInput
                                style={[styles.searchInput, { color: tokens.colors.text.primary }]}
                                placeholder={t('map.searchPlaceholder')}
                                placeholderTextColor={tokens.colors.text.dim}
                                value={searchQuery}
                                onChangeText={setSearchQuery}
                                autoCapitalize="none"
                            />
                        </BlurView>
                    </View>

                    {/* Fuel-type selector — voucher грн/л is per brand and per fuel. */}
                    {fuelOptions.length > 0 && (
                        <View style={[styles.fuelRow, { paddingHorizontal: GLOBAL_PADDING }]} pointerEvents="box-none">
                            <ScrollView
                                horizontal
                                showsHorizontalScrollIndicator={false}
                                contentContainerStyle={styles.fuelRowContent}
                            >
                                {fuelOptions.map(fuel => {
                                    const active = fuel === selectedFuel;
                                    return (
                                        <Pressable
                                            key={fuel}
                                            onPress={() => {
                                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                                setSelectedFuel(fuel);
                                            }}
                                            style={[
                                                styles.fuelChip,
                                                {
                                                    backgroundColor: active ? tokens.colors.primary : tokens.colors.card,
                                                    borderColor: active ? tokens.colors.primary : tokens.colors.border,
                                                },
                                            ]}
                                        >
                                            <Fuel size={14} color={active ? tokens.colors.text.onPrimary : tokens.colors.text.dim} />
                                            <Text
                                                style={[
                                                    styles.fuelChipText,
                                                    { color: active ? tokens.colors.text.onPrimary : tokens.colors.text.secondary },
                                                ]}
                                            >
                                                {fuelLabel(fuel)}
                                            </Text>
                                        </Pressable>
                                    );
                                })}
                            </ScrollView>
                        </View>
                    )}

                    {/* Locate-me + cheapest-nearby controls; the list replaces them when open. */}
                    {!selectedStation && !showList && (
                        <View style={styles.fabColumn} pointerEvents="box-none">
                            {radar.stations.length > 0 && (
                                <Pressable
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                        setSelectedBrand(null);
                                        setShowList(true);
                                    }}
                                    style={[styles.nearbyToggle, { backgroundColor: tokens.colors.primary }]}
                                >
                                    <TrendingDown size={18} color={tokens.colors.text.onPrimary} />
                                    <Text style={[styles.nearbyToggleText, { color: tokens.colors.text.onPrimary }]}>
                                        {t('map.networkRanking')}
                                    </Text>
                                </Pressable>
                            )}
                            <Pressable
                                onPress={handleLocate}
                                style={[
                                    styles.locateFab,
                                    {
                                        backgroundColor: tokens.colors.card,
                                        borderColor: locationStatus === 'granted' ? tokens.colors.primary : tokens.colors.border,
                                    },
                                ]}
                                accessibilityLabel={t('map.locateMe')}
                            >
                                <Navigation
                                    size={22}
                                    color={locationStatus === 'granted' ? tokens.colors.primary : tokens.colors.text.secondary}
                                />
                            </Pressable>
                        </View>
                    )}

                    {/* Network leaderboard (brands cheapest-first), then a brand's nearest АЗК —
                        in a gesture-driven bottom sheet (peek / half / full). */}
                    {!selectedStation && showList && (
                        <BottomSheet
                            ref={sheetRef}
                            onClose={() => { setShowList(false); setSelectedBrand(null); }}
                            isDark={tokens.colors.isDark}
                            borderColor={tokens.colors.primary}
                            header={
                                <>
                                    <View style={styles.listHeader}>
                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flex: 1 }}>
                                            {activeBrand && (
                                                <Pressable
                                                    onPress={() => setSelectedBrand(null)}
                                                    style={styles.closeBtn}
                                                    accessibilityLabel={t('map.back')}
                                                >
                                                    <ChevronLeft size={22} color={tokens.colors.text.dim} />
                                                </Pressable>
                                            )}
                                            <Text style={[styles.listTitle, { color: tokens.colors.text.primary }]} numberOfLines={1}>
                                                {activeBrand
                                                    ? `${activeBrand.stationId.toUpperCase()} · ${fuelLabel(selectedFuel)}`
                                                    : `${t('map.networkRanking')} · ${fuelLabel(selectedFuel)}`}
                                            </Text>
                                        </View>
                                        <Pressable onPress={() => sheetRef.current?.close()} style={styles.closeBtn}>
                                            <ChevronDown size={22} color={tokens.colors.text.dim} />
                                        </Pressable>
                                    </View>
                                    {radar.radiusKm != null && (
                                        <Text style={[styles.listHint, { color: tokens.colors.text.dim }]}>
                                            {t('map.withinKm', String(radar.radiusKm))}
                                        </Text>
                                    )}
                                    {locationStatus === 'denied' && (
                                        <Text style={[styles.listHint, { color: tokens.colors.text.dim }]}>{t('map.locationDenied')}</Text>
                                    )}
                                    {!activeBrand && (
                                        <Text style={[styles.listHint, { color: tokens.colors.text.dim }]}>{t('map.tapBrandHint')}</Text>
                                    )}
                                </>
                            }
                        >
                            {/* Level 1 — brand leaderboard; the cheapest network is emphasised. */}
                            {!activeBrand && brandRanks.map((b, i) => {
                                const brandColor = BRAND_COLORS[b.stationId] || tokens.colors.primary;
                                const isLeader = i === 0;
                                const delta = b.price.voucherPerLiter - brandRanks[0].price.voucherPerLiter;
                                return (
                                    <Pressable
                                        key={b.stationId}
                                        onPress={() => {
                                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                            setSelectedBrand(b.stationId);
                                            sheetRef.current?.expand();
                                        }}
                                        style={[
                                            styles.listRow,
                                            { borderBottomColor: tokens.colors.borderLight },
                                            isLeader && {
                                                backgroundColor: tokens.colors.primaryDim,
                                                borderRadius: 12,
                                                borderBottomWidth: 0,
                                                paddingHorizontal: 10,
                                                marginBottom: 2,
                                            },
                                        ]}
                                    >
                                        <Text style={[styles.rankNum, { color: isLeader ? tokens.colors.primary : tokens.colors.text.dim }]}>{i + 1}</Text>
                                        {BRAND_LOGOS[b.stationId] ? (
                                            <BrandLogoChip logo={BRAND_LOGOS[b.stationId]} size={30} />
                                        ) : (
                                            <View style={[styles.brandDot, { backgroundColor: brandColor }]} />
                                        )}
                                        <View style={{ flex: 1 }}>
                                            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                                                <Text style={[styles.listName, { color: tokens.colors.text.primary }]} numberOfLines={1}>
                                                    {b.stationId.toUpperCase()}
                                                </Text>
                                                {isLeader && (
                                                    <View style={[styles.cheapestBadge, { backgroundColor: tokens.colors.primary }]}>
                                                        <Text style={[styles.cheapestBadgeText, { color: tokens.colors.text.onPrimary }]}>{t('map.cheapest')}</Text>
                                                    </View>
                                                )}
                                            </View>
                                            <Text style={[styles.listSub, { color: tokens.colors.text.dim }]} numberOfLines={1}>
                                                {b.nearestDistanceKm != null
                                                    ? `${t('map.nearest')} ${b.nearestDistanceKm.toFixed(1)} ${t('map.km')}`
                                                    : `${b.nodes.length} ${t('map.stations_nearby')}`}
                                            </Text>
                                        </View>
                                        <View style={{ alignItems: 'flex-end', gap: 3 }}>
                                            <Text style={[styles.listPrice, { color: tokens.colors.primary }]}>
                                                {b.price.voucherPerLiter.toFixed(2)} {t('map.perLiter')}
                                            </Text>
                                            {b.price.savingsPerLiter > 0 && (
                                                <View style={[styles.savingsChip, { backgroundColor: tokens.colors.status.success.subtle, borderColor: tokens.colors.status.success.border }]}>
                                                    <TrendingDown size={11} color={tokens.colors.status.success.base} />
                                                    <Text style={[styles.savingsChipText, { color: tokens.colors.status.success.base }]}>
                                                        −{b.price.savingsPerLiter.toFixed(2)} {t('map.vsPump')}
                                                    </Text>
                                                </View>
                                            )}
                                            {!isLeader && delta > 0 && (
                                                <Text style={[styles.priceDelta, { color: tokens.colors.text.dim }]}>+{delta.toFixed(2)}</Text>
                                            )}
                                        </View>
                                        <ChevronRight size={18} color={tokens.colors.text.dim} style={{ marginLeft: 4 }} />
                                    </Pressable>
                                );
                            })}

                            {/* Level 2 — the selected brand's nearest АЗК. */}
                            {activeBrand && activeBrand.nodes.slice(0, 30).map(r => (
                                <Pressable
                                    key={r.node.id}
                                    onPress={() => focusStation(r.node)}
                                    style={[styles.listRow, { borderBottomColor: tokens.colors.borderLight }]}
                                >
                                    <View style={{ flex: 1 }}>
                                        <Text style={[styles.listName, { color: tokens.colors.text.primary }]} numberOfLines={1}>
                                            {r.node.name}
                                        </Text>
                                        <Text style={[styles.listSub, { color: tokens.colors.text.dim }]} numberOfLines={1}>
                                            {formatShortAddress(r.node) || t('map.noAddress')}
                                        </Text>
                                    </View>
                                    {r.distanceKm != null && (
                                        <Text style={[styles.listPrice, { color: tokens.colors.primary }]}>
                                            {r.distanceKm.toFixed(1)} {t('map.km')}
                                        </Text>
                                    )}
                                </Pressable>
                            ))}
                        </BottomSheet>
                    )}

                    {isLoading && (
                        <View
                            style={[
                                styles.loadingOverlay,
                                { backgroundColor: tokens.colors.card, borderColor: tokens.colors.border },
                            ]}
                        >
                            {/*
                              Was a `GlowText` reading "SECURING DATA STREAM..." — a
                              hardcoded English string in a four-language app, and a
                              fifth hand-rolled loading treatment. `LoadingState`
                              inline is the shared one.
                            */}
                            <LoadingState variant="inline" message={t('map.loadingStations')} />
                        </View>
                    )}

                    {error && !isLoading && (
                        <View style={styles.errorOverlay}>
                            <InlineFeedback
                                kind="danger"
                                message={error instanceof Error ? error.message : t('state.errorDescription')}
                                action={{
                                    label: t('common.retry'),
                                    onPress: () => queryClient.invalidateQueries({ queryKey: ['station-nodes'] }),
                                }}
                            />
                        </View>
                    )}

                    {selectedStation && (
                        /*
                         * No bottom inset here. This panel is `bottom: 0` inside
                         * `PageLayout`'s content box, and that box is already padded
                         * clear of the tab bar and the home indicator — the previous
                         * `paddingBottom: insets.bottom + 20` counted the inset twice.
                         */
                        <BlurView
                            intensity={tokens.colors.isDark ? 80 : 95}
                            tint={tokens.colors.isDark ? "dark" : "light"}
                            style={[styles.detailPanel, { borderTopColor: tokens.colors.primary, borderTopWidth: 2 }]}
                        >
                            <View style={styles.detailHeader}>
                                <View style={{ flex: 1 }}>
                                    <View style={{ flexDirection: 'row', alignItems: 'center', marginBottom: 6, gap: 8 }}>
                                        {('stationId' in selectedStation) && BRAND_LOGOS[(selectedStation as StationNode).stationId] ? (
                                            <BrandLogoChip logo={BRAND_LOGOS[(selectedStation as StationNode).stationId]} size={32} />
                                        ) : (
                                            <View style={[styles.brandBadge, { backgroundColor: ('stationId' in selectedStation && BRAND_COLORS[(selectedStation as StationNode).stationId]) || tokens.colors.primary }]}>
                                                {/*
                                                  This label had no inline colour, so it took
                                                  the stylesheet's static `#000` on a
                                                  `primary` fill — unreadable on the five
                                                  themes whose primary is dark.
                                                */}
                                                <Text style={[styles.brandBadgeText, { color: tokens.colors.text.onPrimary }]}>
                                                    {('stationId' in selectedStation) ? (selectedStation as any).stationId.toUpperCase() : t('map.stationFallback')}
                                                </Text>
                                            </View>
                                        )}
                                        <Text style={[styles.detailName, { color: tokens.colors.text.primary }]}>{selectedStation.name}</Text>
                                    </View>
                                    <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                                        <MapPin size={14} color={tokens.colors.primary} style={{ marginRight: 4 }} />
                                        <Text style={{ color: tokens.colors.text.dim, fontFamily: 'Inter', fontSize: 13 }} numberOfLines={1}>
                                            {formatShortAddress(selectedStation) || t('map.country')}
                                        </Text>
                                    </View>
                                </View>
                                <Pressable onPress={() => setSelectedStation(null)} style={styles.closeBtn}>
                                    <ChevronDown size={24} color={tokens.colors.text.dim} />
                                </Pressable>
                            </View>

                            <View style={[styles.divider, { backgroundColor: tokens.colors.borderLight }]} />

                            {'stationId' in selectedStation && (() => {
                                const p = priceByStation.get((selectedStation as StationNode).stationId);
                                return (
                                    <View style={styles.priceBlock}>
                                        <Text style={[styles.infoLabel, { color: tokens.colors.text.dim }]}>
                                            {t('map.voucherPrice')} · {fuelLabel(selectedFuel)}
                                        </Text>
                                        {p ? (
                                            <View style={styles.priceValueRow}>
                                                <Text style={[styles.priceValue, { color: tokens.colors.primary }]}>
                                                    {p.voucherPerLiter.toFixed(2)} {t('map.perLiter')}
                                                </Text>
                                                {p.savingsPerLiter > 0 && (
                                                    <View style={[styles.savingsBadge, { backgroundColor: tokens.colors.primaryDim, borderColor: tokens.colors.primary }]}>
                                                        <TrendingDown size={13} color={tokens.colors.primary} />
                                                        <Text style={[styles.savingsText, { color: tokens.colors.primary }]}>
                                                            −{p.savingsPerLiter.toFixed(2)} {t('map.vsPump')}
                                                        </Text>
                                                    </View>
                                                )}
                                            </View>
                                        ) : (
                                            <Text style={[styles.detailText, { color: tokens.colors.text.dim, marginTop: 4, marginBottom: 0 }]}>
                                                {t('map.noPriceForFuel')}
                                            </Text>
                                        )}
                                    </View>
                                );
                            })()}

                            <View style={styles.infoRow}>
                                <Text style={[styles.infoLabel, { color: tokens.colors.text.dim }]}>{t('map.address')}</Text>
                                <Text style={[styles.detailText, { color: tokens.colors.text.secondary }]}>
                                    {formatShortAddress(selectedStation) || t('map.noAddress')}
                                </Text>
                            </View>

                            <View style={{ flexDirection: 'row', gap: 10, marginTop: 10 }}>
                                {selectedStation.stationType && (
                                    <View style={[styles.tag, { backgroundColor: tokens.colors.primaryDim, borderColor: tokens.colors.primary }]}>
                                        <Text style={[styles.tagText, { color: tokens.colors.primary }]}>{selectedStation.stationType}</Text>
                                    </View>
                                )}
                                <Pressable
                                    style={[styles.actionBtn, { backgroundColor: tokens.colors.primary }]}
                                    onPress={() => {
                                        const lat = parseFloat(selectedStation.lat || '0');
                                        const lng = parseFloat(selectedStation.lng || '0');
                                        // Prefer Waze (the owner's navigator). Its universal link opens the Waze
                                        // app if installed, else the Waze web page; only if it can't be opened
                                        // at all do we fall back to the platform's own maps app.
                                        Linking.openURL(wazeNavigationUrl(lat, lng)).catch(() => {
                                            Linking.openURL(platformMapsUrl(lat, lng, Platform.OS)).catch((err) =>
                                                console.log('Error opening maps:', err),
                                            );
                                        });
                                    }}
                                >
                                    <Text style={[styles.actionBtnText, { color: tokens.colors.text.onPrimary }]}>{t('map.buildRoute')}</Text>
                                </Pressable>
                            </View>
                        </BlurView>
                    )}
                </View>
            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
    },
    mapWrapper: {
        flex: 1,
        position: 'relative',
    },
    topGradient: {
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        height: 100,
        zIndex: 10,
    },
    searchOverlay: {
        position: 'absolute',
        top: 20,
        left: 0,
        right: 0,
        zIndex: 100,
    },
    searchBox: {
        height: 54,
        borderRadius: 12,
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 20,
        gap: 15,
        overflow: 'hidden',
    },
    loadingOverlay: {
        position: 'absolute',
        top: 90,
        alignSelf: 'center',
        paddingHorizontal: 20,
        paddingVertical: 10,
        borderRadius: 30,
        borderWidth: 1,
        zIndex: 150,
    },
    errorOverlay: {
        position: 'absolute',
        top: 90,
        left: 16,
        right: 16,
        zIndex: 150,
    },
    searchInput: {
        flex: 1,
        fontFamily: 'Inter-Medium',
        fontSize: 16,
    },
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
    detailPanel: {
        position: 'absolute',
        bottom: 0,
        left: 0,
        right: 0,
        borderTopWidth: 2,
        padding: 24,
        zIndex: 200,
        borderTopLeftRadius: 32,
        borderTopRightRadius: 32,
        overflow: 'hidden',
    },
    detailHeader: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
    },
    detailName: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 28,
        letterSpacing: 0.5,
    },
    closeBtn: {
        padding: 5,
    },
    divider: {
        height: 1,
        marginVertical: 20,
    },
    detailText: {
        fontFamily: 'Inter-Medium',
        fontSize: 15,
        lineHeight: 22,
        marginBottom: 24,
    },
    tag: {
        paddingHorizontal: 12,
        paddingVertical: 8,
        borderRadius: 8,
        borderWidth: 1,
    },
    tagText: {
        fontFamily: 'Rajdhani-SemiBold',
        fontSize: 12,
        textTransform: 'uppercase',
        letterSpacing: 1,
    },
    brandBadge: {
        paddingHorizontal: 8,
        paddingVertical: 2,
        borderRadius: 4,
    },
    brandBadgeText: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 12,
        letterSpacing: 0.5,
    },
    infoRow: {
        marginBottom: 16,
    },
    infoLabel: {
        fontFamily: 'Inter-Bold',
        fontSize: 10,
        letterSpacing: 1,
        marginBottom: 4,
        opacity: 0.8,
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
    actionBtn: {
        flex: 1,
        height: 48,
        borderRadius: 8,
        alignItems: 'center',
        justifyContent: 'center',
    },
    actionBtnText: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 14,
        letterSpacing: 1,
    },
    fuelRow: {
        position: 'absolute',
        top: 84,
        left: 0,
        right: 0,
        zIndex: 90,
    },
    fuelRowContent: {
        alignItems: 'center',
        paddingRight: 20,
    },
    fuelChip: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 6,
        paddingHorizontal: 14,
        paddingVertical: 8,
        borderRadius: 20,
        borderWidth: 1,
        marginRight: 8,
    },
    fuelChipText: {
        fontFamily: 'Rajdhani-SemiBold',
        fontSize: 13,
        letterSpacing: 0.5,
    },
    fabColumn: {
        position: 'absolute',
        right: 16,
        bottom: 24,
        alignItems: 'flex-end',
        gap: 12,
        zIndex: 120,
    },
    nearbyToggle: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
        paddingHorizontal: 16,
        paddingVertical: 12,
        borderRadius: 24,
        // Neutral lift off the map tiles, theme-independent (matches the marker shadow).
        shadowColor: '#000',
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.3,
        shadowRadius: 6,
        elevation: 4,
    },
    nearbyToggleText: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 13,
        letterSpacing: 0.5,
    },
    locateFab: {
        width: 52,
        height: 52,
        borderRadius: 26,
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
        shadowColor: '#000',
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.3,
        shadowRadius: 6,
        elevation: 4,
    },
    listHeader: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
        marginBottom: 8,
    },
    listTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 16,
        letterSpacing: 0.5,
    },
    listHint: {
        fontFamily: 'Inter-Medium',
        fontSize: 12,
        marginBottom: 8,
    },
    listRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
        paddingVertical: 12,
        borderBottomWidth: 1,
    },
    rankNum: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 16,
        width: 20,
        textAlign: 'center',
    },
    brandDot: {
        width: 14,
        height: 14,
        borderRadius: 7,
    },
    listName: {
        fontFamily: 'Inter-Medium',
        fontSize: 15,
    },
    listSub: {
        fontFamily: 'Inter',
        fontSize: 12,
        marginTop: 2,
    },
    listPrice: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 16,
    },
    cheapestBadge: {
        paddingHorizontal: 6,
        paddingVertical: 2,
        borderRadius: 5,
    },
    cheapestBadgeText: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 9,
        letterSpacing: 0.5,
    },
    savingsChip: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 3,
        paddingHorizontal: 6,
        paddingVertical: 2,
        borderRadius: 6,
        borderWidth: 1,
    },
    savingsChipText: {
        fontFamily: 'Inter-Medium',
        fontSize: 10,
    },
    priceDelta: {
        fontFamily: 'Rajdhani-Medium',
        fontSize: 11,
    },
    priceBlock: {
        marginBottom: 16,
    },
    priceValueRow: {
        flexDirection: 'row',
        alignItems: 'baseline',
        flexWrap: 'wrap',
        gap: 10,
        marginTop: 4,
    },
    priceValue: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 26,
        letterSpacing: 0.5,
    },
    savingsBadge: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 4,
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 8,
        borderWidth: 1,
    },
    savingsText: {
        fontFamily: 'Rajdhani-SemiBold',
        fontSize: 12,
        letterSpacing: 0.5,
    },
    calloutPrice: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 14,
        marginTop: 6,
    },
});

/*
 * Removed in Phase 2 (Step 9), all unreferenced:
 *
 * - `styles.header` / `styles.title` / `styles.subtitle` — replaced by `ScreenHeader`.
 * - `styles.locationBtn`, `styles.attribution`, `styles.attributionText` — styles for
 *   a "locate me" button and an OSM attribution chip that this screen never rendered.
 * - the `brandName` local inside the marker loop, computed on every point and used
 *   by nothing.
 * - the `background={<View backgroundColor: background />}` prop: `PageLayout`'s
 *   own `SafeAreaView` already paints `tokens.colors.background`.
 */
