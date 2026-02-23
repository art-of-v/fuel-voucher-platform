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

const KYIV_REGION = {
    latitude: 50.4501,
    longitude: 30.5234,
    latitudeDelta: 0.1,
    longitudeDelta: 0.1,
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
    const { t } = useI18n();
    const { data: stations } = useStations();
    const { data: nodes } = useStationNodes();
    const [searchQuery, setSearchQuery] = React.useState("");
    const [selectedStation, setSelectedStation] = React.useState<Station | StationNode | null>(null);

    const GLOBAL_PADDING = tokens.spacing.containerPadding;

    const allPoints = React.useMemo(() => {
        const points: (Station | StationNode)[] = [];
        if (stations) points.push(...stations);
        if (nodes) points.push(...nodes);
        return points;
    }, [stations, nodes]);

    const filteredPoints = React.useMemo(() => {
        if (!allPoints) return [];
        if (!searchQuery.trim()) return allPoints;
        return allPoints.filter(s =>
            s.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
            s.id.toLowerCase().includes(searchQuery.toLowerCase()) ||
            (s as any).address?.toLowerCase().includes(searchQuery.toLowerCase())
        );
    }, [allPoints, searchQuery]);

    const headerComponent = (
        <View style={[styles.header, { paddingHorizontal: GLOBAL_PADDING, backgroundColor: tokens.colors.background }]}>
            <Text allowFontScaling={false} style={[styles.title, { color: tokens.colors.text.primary, textShadowColor: tokens.colors.primaryGlow }]}>{t('map.title')}</Text>
        </View>
    );

    // Map Tiles based on theme
    const tileUrl = tokens.colors.isDark
        ? "https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png"
        : "https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}@2x.png";

    return (
        <PageLayout header={headerComponent} background={<GridBackground />} disableScroll>
            <View style={[styles.container, { paddingHorizontal: GLOBAL_PADDING }]}>

                <View style={[styles.mapWrapper, { borderTopColor: tokens.colors.border, backgroundColor: tokens.colors.background }]}>
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
                            const brandId = isNode ? (point as StationNode).stationId.toLowerCase() : point.id.toLowerCase();
                            const mockCoords = MOCK_COORDS[brandId] || MOCK_COORDS['okko'];

                            const coordinate = {
                                latitude: point.lat ? parseFloat(point.lat) : mockCoords.lat,
                                longitude: point.lng ? parseFloat(point.lng) : mockCoords.lng,
                            };

                            const brandColor = !isNode ?
                                (point.color === 'bg-yellow-500' ? '#EAB308' : tokens.colors.primary) :
                                tokens.colors.primary;

                            return (
                                <Marker
                                    key={`${isNode ? 'node' : 'station'}-${point.id}`}
                                    coordinate={coordinate}
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                        setSelectedStation(point);
                                    }}
                                >
                                    <View style={[styles.marker, { borderColor: brandColor, opacity: isNode ? 0.8 : 1, backgroundColor: tokens.colors.background }]}>
                                        <View style={[styles.markerInner, { backgroundColor: isNode ? 'transparent' : tokens.colors.text.primary }]} />
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
                    <View style={[styles.searchOverlay, { left: GLOBAL_PADDING, right: GLOBAL_PADDING }]}>
                        <View style={[styles.searchBox, { backgroundColor: tokens.colors.isDark ? 'rgba(0,0,0,0.9)' : 'rgba(255,255,255,0.9)', borderColor: tokens.colors.borderLight }]}>
                            <Search size={18} color={tokens.colors.text.dim} />
                            <TextInput
                                style={[styles.searchInput, { color: tokens.colors.text.primary }]}
                                placeholder="Search stations..."
                                placeholderTextColor={tokens.colors.text.dim}
                                value={searchQuery}
                                onChangeText={setSearchQuery}
                                autoCapitalize="none"
                                autoCorrect={false}
                            />
                        </View>
                        <Pressable
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                            }}
                            style={[styles.locationBtn, { backgroundColor: tokens.colors.primary }]}
                        >
                            <NavIcon size={18} color={tokens.colors.isDark ? "#000" : "#FFF"} />
                        </Pressable>
                    </View>

                    {/* Station Detail Summary at Bottom */}
                    {selectedStation && (
                        <View style={[styles.detailPanel, { backgroundColor: tokens.colors.card, borderTopColor: tokens.colors.primary, paddingBottom: Platform.OS === 'ios' ? 40 : 24 }]}>
                            <View style={styles.detailHeader}>
                                <Text style={[styles.detailName, { color: tokens.colors.text.primary }]}>{selectedStation.name}</Text>
                                <Pressable onPress={() => setSelectedStation(null)}>
                                    <Text style={{ color: tokens.colors.primary, fontSize: 12, fontFamily: 'Inter-Bold' }}>CLOSE</Text>
                                </Pressable>
                            </View>
                            {'stationId' in selectedStation && (
                                <Text style={[styles.detailText, { color: tokens.colors.text.dim }]}>Station ID: {(selectedStation as StationNode).stationId}</Text>
                            )}
                            {selectedStation.address && (
                                <Text style={[styles.detailText, { color: tokens.colors.text.secondary }]}>{selectedStation.address}</Text>
                            )}
                            {selectedStation.stationType && (
                                <View style={[styles.tag, { backgroundColor: `${tokens.colors.primary}11`, borderColor: `${tokens.colors.primary}33` }]}>
                                    <Text style={[styles.tagText, { color: tokens.colors.primary }]}>{selectedStation.stationType}</Text>
                                </View>
                            )}
                        </View>
                    )}

                    {/* Attribution Link */}
                    {!selectedStation && (
                        <View style={[styles.attribution, { backgroundColor: tokens.colors.isDark ? 'rgba(5, 5, 5, 0.8)' : 'rgba(255, 255, 255, 0.8)', borderColor: tokens.colors.borderLight }]}>
                            <Text style={[styles.attributionText, { color: tokens.colors.text.dim }]}>
                                🇺🇦 Leaflet | © OpenStreetMap
                            </Text>
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
        paddingTop: Platform.OS === 'ios' ? 10 : 20,
        paddingBottom: 24,
        alignItems: 'center',
    },
    title: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 32,
        lineHeight: 32,
        letterSpacing: -1,
        textTransform: 'uppercase',
        textShadowOffset: { width: 0, height: 0 },
        textShadowRadius: 10,
    },
    subtitle: {
        color: 'rgba(0, 255, 106, 0.6)', // This was tokens.colors.primary, but not used in the provided snippet
        fontFamily: 'Inter-Bold', // Changed from Inter-Black
        fontSize: 8,
        letterSpacing: 4,
        textTransform: 'uppercase',
        opacity: 0.6,
        marginTop: 4,
    },
    mapWrapper: {
        flex: 1,
        borderTopWidth: 1,
        position: 'relative',
        overflow: 'hidden',
    },
    searchOverlay: {
        position: 'absolute',
        top: 12, // Slightly more compact padding from the map top border
        flexDirection: 'row',
        gap: 12,
        zIndex: 100,
    },
    searchBox: {
        flex: 1,
        height: 52,
        borderWidth: 1,
        borderRadius: 4,
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 16,
        gap: 12,
    },
    searchInput: {
        flex: 1,
        fontFamily: 'Inter-Medium',
        fontSize: 14,
        height: '100%',
    },
    locationBtn: {
        width: 52,
        height: 52,
        borderRadius: 4,
        alignItems: 'center',
        justifyContent: 'center',
    },
    marker: {
        width: 24,
        height: 24,
        borderRadius: 12,
        borderWidth: 2,
        alignItems: 'center',
        justifyContent: 'center',
    },
    markerInner: {
        width: 8,
        height: 8,
        borderRadius: 4,
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
        borderTopWidth: 2,
        padding: 24,
        zIndex: 200,
    },
    detailHeader: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
        marginBottom: 12,
    },
    detailName: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 24,
        flex: 1,
        marginRight: 12,
    },
    detailText: {
        fontFamily: 'Inter-Medium',
        fontSize: 14,
        lineHeight: 20,
        marginBottom: 16,
    },
    tag: {
        alignSelf: 'flex-start',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
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
