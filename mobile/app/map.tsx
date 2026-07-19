import React from 'react';
import { View, Text, StyleSheet, Pressable, Platform, TextInput } from 'react-native';
import { PageLayout } from '../src/components/page-layout';
import { GridBackground } from '../src/components/grid-background';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { Search, Navigation as NavIcon } from 'lucide-react-native';
const MapView = Platform.OS !== 'web' ? require('react-native-maps').default : View;
const { UrlTile, Marker, Callout } = Platform.OS !== 'web' ? require('react-native-maps') : { UrlTile: View, Marker: View, Callout: View };
import { useStations } from '../src/features/stations/hooks/useStations';
import { useStationNodes } from '../src/features/stations/hooks/useStationNodes';
import type { Station, StationNode } from '../src/core/types/api';

import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { GlowText } from '../src/components/glow-text';
import { ChevronDown, MapPin } from 'lucide-react-native';
import { BlurView } from 'expo-blur';

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
            const isPlaceholder = Math.abs(fLat - 50.4501) < 0.0001 && Math.abs(fLng - 30.5234) < 0.0001;
            if (!fLat || !fLng || isPlaceholder) return false;
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
                color={tokens.colors.primary}
                style={[styles.title]}
            >
                {t('map.title')}
            </GlowText>
            <View style={{ flexDirection: 'row', alignItems: 'center', marginTop: 4, opacity: 0.8 }}>
                <MapPin size={10} color={tokens.colors.primary} />
                <Text style={[styles.subtitle, { color: tokens.colors.text.dim, marginLeft: 4 }]}>
                    {filteredPoints.length} {t('map.stations_nearby') || 'Stations Found'}
                </Text>
            </View>
        </View>
    );

    const tileUrl = tokens.colors.isDark
        ? "https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png"
        : "https://a.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}@2x.png";

    return (
        <PageLayout
            header={headerComponent}
            background={<View style={{ flex: 1, backgroundColor: tokens.colors.background }} />}
            disableScroll
        >
            <View style={[styles.container, { backgroundColor: tokens.colors.background }]}>
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
                            const brandName = isNode ? (point as any).stationId.toUpperCase() : 'STATION';

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
                                        <BlurView intensity={tokens.colors.isDark ? 80 : 90} tint={tokens.colors.isDark ? "dark" : "light"} style={styles.calloutContainer}>
                                            <Text style={[styles.calloutTitle, { color: tokens.colors.text.primary }]}>{point.name}</Text>
                                            <Text style={[styles.calloutText, { color: tokens.colors.text.dim }]}>{point.address || 'No address'}</Text>
                                        </BlurView>
                                    </Callout>
                                </Marker>
                            );
                        })}
                    </MapView>

                    {/* Overlay for Top Header area to ensure readability */}
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
                                placeholder="Search stations..."
                                placeholderTextColor={tokens.colors.text.dim}
                                value={searchQuery}
                                onChangeText={setSearchQuery}
                                autoCapitalize="none"
                            />
                        </BlurView>
                    </View>

                    {isLoading && (
                        <View style={[styles.loadingOverlay, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.border }]}>
                            <GlowText intensity="low" color={tokens.colors.primary} style={{ fontSize: 10, fontFamily: 'Rajdhani-Bold' }}>
                                SECURING DATA STREAM...
                            </GlowText>
                        </View>
                    )}

                    {selectedStation && (
                        <BlurView
                            intensity={tokens.colors.isDark ? 80 : 95}
                            tint={tokens.colors.isDark ? "dark" : "light"}
                            style={[styles.detailPanel, { borderTopColor: tokens.colors.primary, borderTopWidth: 2, paddingBottom: insets.bottom + 20 }]}
                        >
                            <View style={styles.detailHeader}>
                                <View style={{ flex: 1 }}>
                                    <View style={{ flexDirection: 'row', alignItems: 'center', marginBottom: 6, gap: 8 }}>
                                        <View style={[styles.brandBadge, { backgroundColor: tokens.colors.primary }]}>
                                            <Text style={styles.brandBadgeText}>
                                                {('stationId' in selectedStation) ? (selectedStation as any).stationId.toUpperCase() : 'GAS'}
                                            </Text>
                                        </View>
                                        <Text style={[styles.detailName, { color: tokens.colors.text.primary }]}>{selectedStation.name}</Text>
                                    </View>
                                    <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                                        <MapPin size={14} color={tokens.colors.primary} style={{ marginRight: 4 }} />
                                        <Text style={{ color: tokens.colors.text.dim, fontFamily: 'Inter', fontSize: 13 }}>
                                            {(selectedStation as any).city ? `${(selectedStation as any).city}, ` : ''}Ukraine
                                        </Text>
                                    </View>
                                </View>
                                <Pressable onPress={() => setSelectedStation(null)} style={styles.closeBtn}>
                                    <ChevronDown size={24} color={tokens.colors.text.dim} />
                                </Pressable>
                            </View>

                            <View style={styles.divider} />

                            <View style={styles.infoRow}>
                                <Text style={[styles.infoLabel, { color: tokens.colors.text.dim }]}>ADDRESS</Text>
                                <Text style={[styles.detailText, { color: tokens.colors.text.secondary }]}>
                                    {selectedStation.address || 'Address information not available'}
                                </Text>
                            </View>

                            <View style={{ flexDirection: 'row', gap: 10, marginTop: 10 }}>
                                {selectedStation.stationType && (
                                    <View style={[styles.tag, { backgroundColor: tokens.colors.primaryDim, borderColor: tokens.colors.primary }]}>
                                        <Text style={[styles.tagText, { color: tokens.colors.primary }]}>{selectedStation.stationType}</Text>
                                    </View>
                                )}
                                <Pressable style={[styles.actionBtn, { backgroundColor: tokens.colors.primary }]}>
                                    <Text style={[styles.actionBtnText, { color: tokens.colors.isDark ? '#000' : '#FFF' }]}>BUILD ROUTE</Text>
                                </Pressable>
                            </View>
                        </BlurView>
                    )}
                </View>
            </View>
        </PageLayout>
    );
}

// Custom Dark Map Style to remove unnecessary noise
const DARK_MAP_STYLE = [
    { "elementType": "geometry", "stylers": [{ "color": "#212121" }] },
    { "elementType": "labels.icon", "stylers": [{ "visibility": "off" }] },
    { "elementType": "labels.text.fill", "stylers": [{ "color": "#757575" }] },
    { "elementType": "labels.text.stroke", "stylers": [{ "color": "#212121" }] },
    { "featureType": "administrative", "elementType": "geometry", "stylers": [{ "color": "#757575" }] },
    { "featureType": "poi", "elementType": "geometry", "stylers": [{ "color": "#181818" }] },
    { "featureType": "road", "elementType": "geometry.fill", "stylers": [{ "color": "#2c2c2c" }] },
    { "featureType": "water", "elementType": "geometry", "stylers": [{ "color": "#000000" }] }
];

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
        fontSize: 24,
        letterSpacing: 3,
        textTransform: 'uppercase',
    },
    subtitle: {
        fontFamily: 'Inter-Medium',
        fontSize: 10,
        letterSpacing: 2,
        textTransform: 'uppercase',
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
        backgroundColor: 'rgba(0,0,0,0.9)',
        paddingHorizontal: 20,
        paddingVertical: 10,
        borderRadius: 30,
        borderWidth: 1,
        borderColor: 'rgba(22, 255, 0, 0.4)',
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
        width: 30,
        height: 30,
        borderRadius: 15,
        borderWidth: 2,
        alignItems: 'center',
        justifyContent: 'center',
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
        backgroundColor: 'rgba(255,255,255,0.08)',
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
        color: '#000',
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
        borderColor: 'rgba(255,255,255,0.1)',
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
        color: '#000',
        fontFamily: 'Rajdhani-Bold',
        fontSize: 14,
        letterSpacing: 1,
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
