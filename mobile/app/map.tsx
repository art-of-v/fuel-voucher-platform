import React from 'react';
import { View, Text, StyleSheet, Pressable, Platform, TextInput, Linking } from 'react-native';
import { InlineFeedback, LoadingState, PageLayout, ScreenHeader } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { Search } from 'lucide-react-native';
const MapView = Platform.OS !== 'web' ? require('react-native-maps').default : View;
const { UrlTile, Marker, Callout } = Platform.OS !== 'web' ? require('react-native-maps') : { UrlTile: View, Marker: View, Callout: View };
import { useStationNodes } from '../src/features/stations/hooks/useStationNodes';
import { useQueryClient } from '@tanstack/react-query';
import type { Station, StationNode } from '../src/core/types/api';

import { ChevronDown, MapPin } from 'lucide-react-native';
import { BlurView } from 'expo-blur';

const KYIV_REGION = {
    latitude: 50.4501,
    longitude: 30.5234,
    latitudeDelta: 0.15,
    longitudeDelta: 0.15,
};

export default function MapScreen() {
    const tokens = useDesignTokens();
    const { t } = useI18n();
    const queryClient = useQueryClient();
    const { data: nodes, isLoading, error } = useStationNodes();
    const [searchQuery, setSearchQuery] = React.useState("");
    const [selectedStation, setSelectedStation] = React.useState<Station | StationNode | null>(null);

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

    const tileUrl = tokens.colors.isDark
        ? "https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png"
        : "https://a.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}@2x.png";

    return (
        <PageLayout header={headerComponent} padding="none" scroll={false}>
            <View style={styles.container}>
                <View style={styles.mapWrapper}>
                    <MapView
                        style={StyleSheet.absoluteFill}
                        initialRegion={KYIV_REGION}
                        onPress={() => setSelectedStation(null)}
                        userInterfaceStyle={tokens.colors.isDark ? 'dark' : 'light'}
                    >
                        <UrlTile urlTemplate={tileUrl} maximumZ={19} flipY={false} />

                        {filteredPoints.map(point => {
                            const isNode = 'stationId' in point;
                            const coordinate = {
                                latitude: parseFloat(point.lat!),
                                longitude: parseFloat(point.lng!),
                            };
                            const brandColor = tokens.colors.primary;

                            return (
                                <Marker
                                    key={`${isNode ? 'node' : 'station'}-${point.id}`}
                                    coordinate={coordinate}
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                        setSelectedStation(point);
                                    }}
                                    tracksViewChanges={false}
                                >
                                    <View style={[styles.markerContainer]}>
                                        <View style={[styles.marker, { borderColor: brandColor, backgroundColor: tokens.colors.background }]}>
                                            <View style={[styles.markerInner, { backgroundColor: brandColor, shadowColor: brandColor, shadowRadius: 5, shadowOpacity: 0.5 }]} />
                                        </View>
                                        <View style={[styles.markerStem, { backgroundColor: brandColor }]} />
                                    </View>

                                    <Callout tooltip>
                                        <BlurView
                                            intensity={tokens.colors.isDark ? 80 : 90}
                                            tint={tokens.colors.isDark ? "dark" : "light"}
                                            style={[styles.calloutContainer, { borderColor: tokens.colors.borderLight }]}
                                        >
                                            <Text style={[styles.calloutTitle, { color: tokens.colors.text.primary }]}>{point.name}</Text>
                                            <Text style={[styles.calloutText, { color: tokens.colors.text.dim }]}>{point.address || t('map.noAddress')}</Text>
                                        </BlurView>
                                    </Callout>
                                </Marker>
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
                                        <View style={[styles.brandBadge, { backgroundColor: tokens.colors.primary }]}>
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
                                        <Text style={[styles.detailName, { color: tokens.colors.text.primary }]}>{selectedStation.name}</Text>
                                    </View>
                                    <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                                        <MapPin size={14} color={tokens.colors.primary} style={{ marginRight: 4 }} />
                                        <Text style={{ color: tokens.colors.text.dim, fontFamily: 'Inter', fontSize: 13 }}>
                                            {(selectedStation as any).city ? `${(selectedStation as any).city}, ` : ''}{t('map.country')}
                                        </Text>
                                    </View>
                                </View>
                                <Pressable onPress={() => setSelectedStation(null)} style={styles.closeBtn}>
                                    <ChevronDown size={24} color={tokens.colors.text.dim} />
                                </Pressable>
                            </View>

                            <View style={[styles.divider, { backgroundColor: tokens.colors.borderLight }]} />

                            <View style={styles.infoRow}>
                                <Text style={[styles.infoLabel, { color: tokens.colors.text.dim }]}>{t('map.address')}</Text>
                                <Text style={[styles.detailText, { color: tokens.colors.text.secondary }]}>
                                    {selectedStation.address || t('map.noAddress')}
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
                                        try {
                                            const lat = parseFloat(selectedStation.lat || '0');
                                            const lng = parseFloat(selectedStation.lng || '0');
                                            if (Platform.OS === 'ios') {
                                                Linking.openURL('https://maps.apple.com/?daddr=' + lat + ',' + lng);
                                            } else {
                                                Linking.openURL('geo:0,0?q=' + lat + ',' + lng);
                                            }
                                        } catch (err) {
                                            console.log('Error opening maps:', err);
                                        }
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
