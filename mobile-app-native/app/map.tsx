import React from 'react';
import { View, Text, StyleSheet, Pressable, Platform, TextInput } from 'react-native';
import { PageLayout } from '../src/components/page-layout';
import { GridBackground } from '../src/components/grid-background';
import { useDesignTokens } from '../src/lib/design-tokens';
import { useI18n } from '../src/lib/i18n';
import { Haptics } from '../src/lib/haptics';
import { Search, Navigation as NavIcon } from 'lucide-react-native';
const MapView = Platform.OS !== 'web' ? require('react-native-maps').default : View;
const { UrlTile, Marker, Callout } = Platform.OS !== 'web' ? require('react-native-maps') : { UrlTile: View, Marker: View, Callout: View };
import { useStations } from '../src/hooks/useStations';
import { useStationNodes } from '../src/hooks/useStationNodes';
import { Station, StationNode } from '../src/lib/api';

import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { GlowText } from '../src/components/glow-text';
import { ChevronDown, MapPin } from 'lucide-react-native';

const KYIV_REGION = {
    latitude: 50.4501,
    longitude: 30.5234,
    latitudeDelta: 0.15,
    longitudeDelta: 0.15,
};

// Mock coordinates for stations
const MOCK_COORDS: Record<string, { lat: number, lng: number }> = {
    'okko': { lat: 50.4501, lng: 30.5234 },
    'wog': { lat: 50.4401, lng: 30.5134 },
    'klo': { lat: 50.4601, lng: 30.5334 },
    'upg': { lat: 50.4551, lng: 30.5034 },
};

export default function MapScreen() {
    const tokens = useDesignTokens();
    const insets = useSafeAreaInsets();
    const { t } = useI18n();
    const { data: stations } = useStations();
    const { data: nodes, isLoading } = useStationNodes();
    const [searchQuery, setSearchQuery] = React.useState("");
    const [selectedStation, setSelectedStation] = React.useState<Station | StationNode | null>(null);

    React.useEffect(() => {
        if (nodes) {
            console.log(`[MAP] Loaded ${nodes.length} station nodes`);
        }
    }, [nodes]);

    const GLOBAL_PADDING = tokens.spacing.containerPadding;

    const allPoints = React.useMemo(() => {
        // We only want to show individual station nodes (AZK) on the map, 
        // not the generic brand markers from the stations table.
        const points: (Station | StationNode)[] = [];
        if (nodes) points.push(...nodes);
        return points;
    }, [nodes]);

    const filteredPoints = React.useMemo(() => {
        if (!allPoints) return [];

        const q = searchQuery.toLowerCase().trim();

        return allPoints.filter(s => {
            // Robust coordinate validation
            const fLat = parseFloat(s.lat || "0");
            const fLng = parseFloat(s.lng || "0");

            // 50.4501 is our Kyiv placeholder
            const isPlaceholder = Math.abs(fLat - 50.4501) < 0.0001 && Math.abs(fLng - 30.5234) < 0.0001;

            if (!fLat || !fLng || isPlaceholder) return false;

            if (!q) return true;

            // Better search: match Cyrillic 'окко' if user types 'okko'
            const searchTargets = [
                s.name.toLowerCase(),
                (s as any).address?.toLowerCase() || '',
                (s as any).city?.toLowerCase() || ''
            ];

            const matches = searchTargets.some(target => {
                if (target.includes(q)) return true;
                // Special mapping for major brands (Latin -> Cyrillic)
                if (q === 'okko' && target.includes('окко')) return true;
                if (q === 'wog' && target.includes('вог')) return true;
                if (q === 'klo' && target.includes('кло')) return true;
                if (q === 'upg' && target.includes('юпі')) return true;
                return false;
            });

            return matches;
        });
    }, [allPoints, searchQuery]);

    const headerComponent = (
        <View style={[styles.header, {
            paddingTop: insets.top + 10,
            paddingHorizontal: GLOBAL_PADDING,
            backgroundColor: tokens.colors.background,
            borderBottomWidth: 1,
            borderBottomColor: tokens.colors.borderLight
        }]}>
            <GlowText
                intensity="high"
                style={[styles.title, { color: tokens.colors.text.primary }]}
            >
                {t('map.title')}
            </GlowText>
            <View style={{ flexDirection: 'row', alignItems: 'center', marginTop: 4, opacity: 0.6 }}>
                <MapPin size={10} color={tokens.colors.primary} />
                <Text style={[styles.subtitle, { color: tokens.colors.text.dim, marginLeft: 4 }]}>
                    {filteredPoints.length} {t('map.stations_nearby') || 'Stations Found'}
                </Text>
            </View>
        </View>
    );

    // Map Tiles based on theme
    const tileUrl = tokens.colors.isDark
        ? "https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png"
        : "https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}@2x.png";

    return (
        <PageLayout header={headerComponent} background={<GridBackground />} disableScroll>
            <View style={styles.container}>

                <View style={styles.mapWrapper}>
                    <MapView
                        style={StyleSheet.absoluteFill}
                        initialRegion={KYIV_REGION}
                        onPress={() => setSelectedStation(null)}
                    >
                        <UrlTile
                            urlTemplate={tileUrl}
                            maximumZ={19}
                            flipY={false}
                        />

                        {filteredPoints.map(point => {
                            const isNode = 'stationId' in point;
                            const coordinate = {
                                latitude: parseFloat(point.lat!),
                                longitude: parseFloat(point.lng!),
                            };

                            const brandColor = isNode && (point as any).stationId.toLowerCase() === 'okko'
                                ? '#16FF00' // OKKO Green
                                : tokens.colors.primary;

                            return (
                                <Marker
                                    key={`${isNode ? 'node' : 'station'}-${point.id}`}
                                    coordinate={coordinate}
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                        setSelectedStation(point);
                                    }}
                                >
                                    <View style={[styles.markerContainer]}>
                                        <View style={[styles.marker, { borderColor: brandColor, backgroundColor: tokens.colors.background }]}>
                                            <View style={[styles.markerInner, { backgroundColor: brandColor }]} />
                                        </View>
                                        <View style={[styles.markerStem, { backgroundColor: brandColor }]} />
                                    </View>
                                    <Callout tooltip>
                                        <View style={[styles.calloutContainer, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.primary }]}>
                                            <Text style={[styles.calloutTitle, { color: tokens.colors.text.primary }]}>{point.name}</Text>
                                            {(point as any).address && <Text style={[styles.calloutText, { color: tokens.colors.text.secondary }]}>{(point as any).address}</Text>}
                                            {(point as any).phone && <Text style={[styles.calloutPhone, { color: tokens.colors.primary }]}>{(point as any).phone}</Text>}
                                        </View>
                                    </Callout>
                                </Marker>
                            );
                        })}
                    </MapView>

                    {/* Search Bar Overlay */}
                    <View style={[styles.searchOverlay, { paddingHorizontal: GLOBAL_PADDING }]}>
                        <View style={[styles.searchBox, {
                            backgroundColor: 'rgba(5, 5, 5, 0.85)',
                            borderColor: tokens.colors.border,
                            shadowColor: tokens.colors.primary,
                            shadowOpacity: 0.2,
                            shadowRadius: 10
                        }]}>
                            <Search size={20} color={tokens.colors.primary} />
                            <TextInput
                                style={[styles.searchInput, { color: tokens.colors.text.primary }]}
                                placeholder="Search by brand or address..."
                                placeholderTextColor="rgba(255,255,255,0.3)"
                                value={searchQuery}
                                onChangeText={setSearchQuery}
                                autoCapitalize="none"
                            />
                        </View>
                    </View>

                    {/* Loading Indicator */}
                    {isLoading && (
                        <View style={styles.loadingOverlay}>
                            <Text style={{ color: tokens.colors.primary, fontFamily: 'Rajdhani-Bold' }}>SYNCING NETWORK...</Text>
                        </View>
                    )}

                    {/* Station Detail Summary at Bottom */}
                    {selectedStation && (
                        <View style={[styles.detailPanel, {
                            backgroundColor: tokens.colors.card,
                            borderTopColor: tokens.colors.primary,
                            paddingBottom: insets.bottom + 20
                        }]}>
                            <View style={styles.detailHeader}>
                                <View style={{ flex: 1 }}>
                                    <Text style={[styles.detailName, { color: tokens.colors.text.primary }]}>{selectedStation.name}</Text>
                                    <View style={{ flexDirection: 'row', alignItems: 'center', marginTop: 4 }}>
                                        <MapPin size={14} color={tokens.colors.primary} style={{ marginRight: 4 }} />
                                        <Text style={{ color: tokens.colors.text.dim, fontFamily: 'Inter', fontSize: 13 }}>
                                            {(selectedStation as any).city ? `${(selectedStation as any).city}, ` : ''}Ukraine
                                        </Text>
                                    </View>
                                </View>
                                <Pressable
                                    onPress={() => setSelectedStation(null)}
                                    style={styles.closeBtn}
                                >
                                    <ChevronDown size={24} color={tokens.colors.text.dim} />
                                </Pressable>
                            </View>

                            <View style={styles.divider} />

                            {selectedStation.address && (
                                <Text style={[styles.detailText, { color: tokens.colors.text.secondary }]}>{selectedStation.address}</Text>
                            )}

                            {selectedStation.stationType && (
                                <View style={[styles.tag, { backgroundColor: `${tokens.colors.primary}15`, borderColor: tokens.colors.primary }]}>
                                    <Text style={[styles.tagText, { color: tokens.colors.primary }]}>{selectedStation.stationType}</Text>
                                </View>
                            )}
                        </View>
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
    header: {
        paddingBottom: 20,
        alignItems: 'center',
        zIndex: 100,
    },
    title: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 28,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    subtitle: {
        fontFamily: 'Inter-Medium',
        fontSize: 10,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    mapWrapper: {
        flex: 1,
        position: 'relative',
    },
    searchOverlay: {
        position: 'absolute',
        top: 20,
        left: 0,
        right: 0,
        zIndex: 100,
    },
    searchBox: {
        height: 56,
        borderWidth: 1,
        borderRadius: 12,
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 20,
        gap: 15,
    },
    loadingOverlay: {
        position: 'absolute',
        top: 90,
        alignSelf: 'center',
        backgroundColor: 'rgba(0,0,0,0.8)',
        paddingHorizontal: 16,
        paddingVertical: 8,
        borderRadius: 20,
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 106, 0.3)',
    },
    searchInput: {
        flex: 1,
        fontFamily: 'Inter-Medium',
        fontSize: 16,
    },
    locationBtn: {
        width: 52,
        height: 52,
        borderRadius: 4,
        alignItems: 'center',
        justifyContent: 'center',
    },
    markerContainer: {
        alignItems: 'center',
    },
    marker: {
        width: 32,
        height: 32,
        borderRadius: 16,
        borderWidth: 3,
        alignItems: 'center',
        justifyContent: 'center',
        shadowColor: '#000',
        shadowOffset: { width: 0, height: 2 },
        shadowOpacity: 0.5,
        shadowRadius: 4,
    },
    markerInner: {
        width: 12,
        height: 12,
        borderRadius: 6,
    },
    markerStem: {
        width: 2,
        height: 6,
        marginTop: -1,
    },
    calloutContainer: {
        width: 200,
        padding: 12,
        borderRadius: 8,
        borderWidth: 1,
    },
    calloutTitle: {
        fontFamily: 'Inter-Bold',
        fontSize: 14,
        marginBottom: 4,
    },
    calloutText: {
        fontFamily: 'Inter-Regular',
        fontSize: 12,
    },
    calloutPhone: {
        fontFamily: 'Inter-Medium',
        fontSize: 11,
        marginTop: 4,
    },
    detailPanel: {
        position: 'absolute',
        bottom: 0,
        left: 0,
        right: 0,
        borderTopWidth: 3,
        padding: 24,
        zIndex: 200,
        borderTopLeftRadius: 30,
        borderTopRightRadius: 30,
    },
    detailHeader: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
    },
    detailName: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 26,
    },
    closeBtn: {
        padding: 5,
    },
    divider: {
        height: 1,
        backgroundColor: 'rgba(255,255,255,0.05)',
        marginVertical: 16,
    },
    detailText: {
        fontFamily: 'Inter-Medium',
        fontSize: 15,
        lineHeight: 22,
        marginBottom: 20,
    },
    tag: {
        alignSelf: 'flex-start',
        paddingHorizontal: 10,
        paddingVertical: 4,
        borderRadius: 6,
        borderWidth: 1,
    },
    tagText: {
        fontSize: 10,
        fontFamily: 'Inter-Bold',
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    attribution: {
        position: 'absolute',
        bottom: 10,
        right: 12,
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
    },
    attributionText: {
        fontFamily: 'Inter-Medium',
        fontSize: 10,
    },
});
