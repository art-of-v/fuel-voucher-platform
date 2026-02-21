import React from 'react';
import { View, Text, StyleSheet, Pressable, Platform } from 'react-native';
import { PageLayout } from '@/components/page-layout';
import { GridBackground } from '@/components/grid-background';
import { tokens } from '@/lib/design-tokens';
import { useI18n } from '@/lib/i18n';
import { Haptics } from '@/lib/haptics';
import { Search, Navigation as NavIcon } from 'lucide-react-native';
const MapView = Platform.OS !== 'web' ? require('react-native-maps').default : View;
const { UrlTile, Marker } = Platform.OS !== 'web' ? require('react-native-maps') : { UrlTile: View, Marker: View };
import { useStations } from '@/hooks/useStations';

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

    return (
        <PageLayout background={<GridBackground />} disableScroll paddingHorizontal={GLOBAL_PADDING}>
            <View style={styles.container}>
                <View style={styles.header}>
                    <Text style={styles.title}>{t('map.title')}</Text>
                    <Text style={styles.subtitle}>{t('map.initializing')} [ GEO-TRACKING ]</Text>
                </View>

                <View style={styles.mapWrapper}>
                    <MapView
                        style={StyleSheet.absoluteFill}
                        initialRegion={KYIV_REGION}
                    >
                        <UrlTile
                            urlTemplate="https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png"
                            maximumZ={19}
                            flipY={false}
                        />

                        {stations?.map(station => {
                            const coords = MOCK_COORDS[station.id.toLowerCase()] || MOCK_COORDS['okko'];
                            return (
                                <Marker
                                    key={station.id}
                                    coordinate={{ latitude: coords.lat, longitude: coords.lng }}
                                    title={station.name}
                                >
                                    <View style={[styles.marker, { borderColor: station.color === 'bg-yellow-500' ? '#EAB308' : '#00FF6A' }]}>
                                        <View style={styles.markerInner} />
                                    </View>
                                </Marker>
                            );
                        })}
                    </MapView>

                    {/* Search Bar Overlay */}
                    <View style={styles.searchOverlay}>
                        <View style={styles.searchBox}>
                            <Search size={18} color="rgba(255,255,255,0.4)" />
                            <Text style={styles.searchText}>Search stations...</Text>
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

                    {/* Attribution Link */}
                    <View style={styles.attribution}>
                        <Text style={styles.attributionText}>
                            🇺🇦 Leaflet | © OpenStreetMap
                        </Text>
                    </View>
                </View>
            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        paddingTop: 8,
    },
    header: {
        paddingHorizontal: GLOBAL_PADDING,
        marginBottom: 24,
        alignItems: 'center',
    },
    title: {
        color: '#FFF',
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 48,
        lineHeight: 56,
        letterSpacing: -1,
        textTransform: 'uppercase',
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
        top: 20,
        left: GLOBAL_PADDING,
        right: GLOBAL_PADDING,
        flexDirection: 'row',
        gap: 12,
        zIndex: 100,
    },
    searchBox: {
        flex: 1,
        height: 52,
        backgroundColor: '#000',
        borderWidth: 1,
        borderColor: 'rgba(255, 255, 255, 0.1)',
        borderRadius: 4,
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 16,
        gap: 12,
    },
    searchText: {
        fontFamily: 'Inter-Medium',
        color: 'rgba(255, 255, 255, 0.4)',
        fontSize: 14,
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
    attribution: {
        position: 'absolute',
        bottom: 100,
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
