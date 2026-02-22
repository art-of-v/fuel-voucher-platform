import React from 'react';
import { View, Text, StyleSheet, Pressable, Platform, TextInput } from 'react-native';
import { PageLayout } from '@/components/page-layout';
import { GridBackground } from '@/components/grid-background';
import { tokens } from '@/lib/design-tokens';
import { useI18n } from '@/lib/i18n';
import { Haptics } from '@/lib/haptics';
import { Search, Navigation as NavIcon } from 'lucide-react-native';
const MapView = Platform.OS !== 'web' ? require('react-native-maps').default : View;
const { UrlTile, Marker, Callout } = Platform.OS !== 'web' ? require('react-native-maps') : { UrlTile: View, Marker: View, Callout: View };
import { useStations } from '@/hooks/useStations';
import { Station } from '@/lib/api';

const GLOBAL_PADDING = tokens.spacing.containerPadding;

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
    const { t } = useI18n();
    const { data: stations } = useStations();
    const [searchQuery, setSearchQuery] = React.useState("");
    const [selectedStation, setSelectedStation] = React.useState<Station | null>(null);

    const filteredStations = React.useMemo(() => {
        if (!stations) return [];
        if (!searchQuery.trim()) return stations;
        return stations.filter(s =>
            s.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
            s.id.toLowerCase().includes(searchQuery.toLowerCase()) ||
            s.address?.toLowerCase().includes(searchQuery.toLowerCase())
        );
    }, [stations, searchQuery]);

    const headerComponent = (
        <View style={styles.header}>
            <Text allowFontScaling={false} style={styles.title}>{t('map.title')}</Text>
        </View>
    );

    return (
        <PageLayout header={headerComponent} background={<GridBackground />} disableScroll>
            <View style={[styles.container, { paddingHorizontal: GLOBAL_PADDING }]}>

                <View style={styles.mapWrapper}>
                    <MapView
                        style={StyleSheet.absoluteFill}
                        initialRegion={KYIV_REGION}
                        onPress={() => setSelectedStation(null)}
                    >
                        <UrlTile
                            urlTemplate="https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png"
                            maximumZ={19}
                            flipY={false}
                        />

                        {filteredStations.map(station => {
                            const mockCoords = MOCK_COORDS[station.id.toLowerCase()] || MOCK_COORDS['okko'];
                            const coordinate = {
                                latitude: station.lat ? parseFloat(station.lat) : mockCoords.lat,
                                longitude: station.lng ? parseFloat(station.lng) : mockCoords.lng,
                            };
                            return (
                                <Marker
                                    key={station.id}
                                    coordinate={coordinate}
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                        setSelectedStation(station);
                                    }}
                                >
                                    <View style={[styles.marker, { borderColor: station.color === 'bg-yellow-500' ? '#EAB308' : '#00FF6A' }]}>
                                        <View style={styles.markerInner} />
                                    </View>
                                    <Callout tooltip>
                                        <View style={styles.calloutContainer}>
                                            <Text style={styles.calloutTitle}>{station.name}</Text>
                                            {station.address && <Text style={styles.calloutText}>{station.address}</Text>}
                                            {station.phone && <Text style={styles.calloutPhone}>{station.phone}</Text>}
                                        </View>
                                    </Callout>
                                </Marker>
                            );
                        })}
                    </MapView>

                    {/* Search Bar Overlay */}
                    <View style={styles.searchOverlay}>
                        <View style={styles.searchBox}>
                            <Search size={18} color="rgba(255,255,255,0.4)" />
                            <TextInput
                                style={styles.searchInput}
                                placeholder="Search stations..."
                                placeholderTextColor="rgba(255,255,255,0.4)"
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
                            style={styles.locationBtn}
                        >
                            <NavIcon size={18} color="#000" />
                        </Pressable>
                    </View>

                    {/* Station Detail Summary at Bottom */}
                    {selectedStation && (
                        <View style={styles.detailPanel}>
                            <View style={styles.detailHeader}>
                                <Text style={styles.detailName}>{selectedStation.name}</Text>
                                <Pressable onPress={() => setSelectedStation(null)}>
                                    <Text style={{ color: tokens.colors.primary, fontSize: 12 }}>CLOSE</Text>
                                </Pressable>
                            </View>
                            {selectedStation.address && (
                                <Text style={styles.detailText}>{selectedStation.address}</Text>
                            )}
                            {selectedStation.stationType && (
                                <View style={styles.tag}>
                                    <Text style={styles.tagText}>{selectedStation.stationType}</Text>
                                </View>
                            )}
                        </View>
                    )}

                    {/* Attribution Link */}
                    {!selectedStation && (
                        <View style={styles.attribution}>
                            <Text style={styles.attributionText}>
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
        paddingHorizontal: GLOBAL_PADDING,
        paddingTop: Platform.OS === 'ios' ? 10 : 20,
        paddingBottom: 24,
        alignItems: 'center',
        backgroundColor: '#000', // Ensure map content doesn't bleed through
    },
    title: {
        color: '#FFFFFF',
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 32,
        lineHeight: 32,
        letterSpacing: -1,
        textTransform: 'uppercase',
        textShadowColor: 'rgba(0, 255, 106, 0.3)',
        textShadowOffset: { width: 0, height: 0 },
        textShadowRadius: 10,
    },
    subtitle: {
        color: tokens.colors.primary,
        fontFamily: 'Inter-Black',
        fontSize: 8,
        letterSpacing: 4,
        textTransform: 'uppercase',
        opacity: 0.6,
        marginTop: 4,
    },
    mapWrapper: {
        flex: 1,
        borderTopWidth: 1,
        borderTopColor: 'rgba(0, 255, 106, 0.15)',
        backgroundColor: '#000',
        position: 'relative',
        overflow: 'hidden',
    },
    searchOverlay: {
        position: 'absolute',
        top: 12, // Slightly more compact padding from the map top border
        left: GLOBAL_PADDING,
        right: GLOBAL_PADDING,
        flexDirection: 'row',
        gap: 12,
        zIndex: 100,
    },
    searchBox: {
        flex: 1,
        height: 52,
        backgroundColor: 'rgba(0,0,0,0.9)',
        borderWidth: 1,
        borderColor: 'rgba(255, 255, 255, 0.1)',
        borderRadius: 4,
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 16,
        gap: 12,
    },
    searchInput: {
        flex: 1,
        fontFamily: 'Inter-Medium',
        color: '#FFF',
        fontSize: 14,
        height: '100%',
    },
    locationBtn: {
        width: 52,
        height: 52,
        backgroundColor: tokens.colors.primary,
        borderRadius: 4,
        alignItems: 'center',
        justifyContent: 'center',
    },
    marker: {
        width: 24,
        height: 24,
        borderRadius: 12,
        borderWidth: 2,
        backgroundColor: 'rgba(0,0,0,0.8)',
        alignItems: 'center',
        justifyContent: 'center',
    },
    markerInner: {
        width: 8,
        height: 8,
        borderRadius: 4,
        backgroundColor: '#FFF',
    },
    calloutContainer: {
        width: 200,
        backgroundColor: '#000',
        padding: 12,
        borderRadius: 8,
        borderWidth: 1,
        borderColor: tokens.colors.primary,
    },
    calloutTitle: {
        color: '#FFF',
        fontFamily: 'Inter-Bold',
        fontSize: 14,
        marginBottom: 4,
    },
    calloutText: {
        color: 'rgba(255,255,255,0.7)',
        fontFamily: 'Inter-Regular',
        fontSize: 12,
    },
    calloutPhone: {
        color: tokens.colors.primary,
        fontFamily: 'Inter-Medium',
        fontSize: 11,
        marginTop: 4,
    },
    detailPanel: {
        position: 'absolute',
        bottom: 0,
        left: 0,
        right: 0,
        backgroundColor: '#000',
        borderTopWidth: 2,
        borderTopColor: tokens.colors.primary,
        padding: 24,
        paddingBottom: Platform.OS === 'ios' ? 40 : 24,
        zIndex: 200,
    },
    detailHeader: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
        marginBottom: 12,
    },
    detailName: {
        color: '#FFF',
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 24,
        flex: 1,
        marginRight: 12,
    },
    detailText: {
        color: 'rgba(255,255,255,0.6)',
        fontFamily: 'Inter-Medium',
        fontSize: 14,
        lineHeight: 20,
        marginBottom: 16,
    },
    tag: {
        alignSelf: 'flex-start',
        backgroundColor: 'rgba(0, 255, 106, 0.1)',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 106, 0.2)',
    },
    tagText: {
        color: tokens.colors.primary,
        fontSize: 10,
        fontFamily: 'Inter-Bold',
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    attribution: {
        position: 'absolute',
        bottom: 10,
        right: 12,
        backgroundColor: 'rgba(5, 5, 5, 0.8)',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.05)',
    },
    attributionText: {
        fontFamily: 'Inter-Medium',
        color: 'rgba(255,255,255,0.4)',
        fontSize: 10,
    },
});
