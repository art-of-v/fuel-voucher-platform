
import React, { useState } from 'react';
import { View, Text, TextInput, TouchableOpacity, StyleSheet, ActivityIndicator, Dimensions, Platform, ScrollView } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Navigation, MapPin, Search, ChevronLeft } from 'lucide-react-native';
import { apiFetch } from '@/lib/api';
import { ProtectedRoute } from '@/components/ProtectedRoute';

// Conditional imports for native vs web
let MapView: any, Marker: any, Callout: any, PROVIDER_GOOGLE: any;
let LeafletMap: any, TileLayer: any, LeafletMarker: any, Popup: any, L: any;

if (Platform.OS !== 'web') {
    const maps = require('react-native-maps');
    MapView = maps.default;
    Marker = maps.Marker;
    Callout = maps.Callout;
    PROVIDER_GOOGLE = maps.PROVIDER_GOOGLE;
} else {
    // Web only imports
    const reactLeaflet = require('react-leaflet');
    LeafletMap = reactLeaflet.MapContainer;
    TileLayer = reactLeaflet.TileLayer;
    LeafletMarker = reactLeaflet.Marker;
    Popup = reactLeaflet.Popup;
    L = require('leaflet');
    require('leaflet/dist/leaflet.css');

    // Fix Leaflet marker icons on web
    const markerIcon2x = require('leaflet/dist/images/marker-icon-2x.png');
    const markerIcon = require('leaflet/dist/images/marker-icon.png');
    const markerShadow = require('leaflet/dist/images/marker-shadow.png');

    delete L.Icon.Default.prototype._getIconUrl;
    L.Icon.Default.mergeOptions({
        iconUrl: markerIcon,
        iconRetinaUrl: markerIcon2x,
        shadowUrl: markerShadow,
    });
}

type Station = {
    id: string;
    name: string;
    color: string;
    logoText: string;
    lat?: string;
    lng?: string;
};

// Web Map Component
function WebMapView({ stations, search, onSearchChange }: { stations: Station[], search: string, onSearchChange: (text: string) => void }) {
    const router = useRouter();
    const filteredStations = stations.filter(
        (s) =>
            s.lat &&
            s.lng &&
            (s.name.toLowerCase().includes(search.toLowerCase()) ||
                s.logoText.toLowerCase().includes(search.toLowerCase()))
    );

    const [center] = useState<[number, number]>([50.4501, 30.5234]); // Kyiv default

    return (
        <View className="flex-1 bg-[#050505]">
            {/* Header / Search Overlay */}
            <View className="absolute top-6 left-6 right-6 z-[1000] flex-row gap-2">
                <TouchableOpacity
                    onPress={() => router.back()}
                    className="w-12 h-12 bg-black border-2 border-white/20 items-center justify-center active:scale-[0.98] shadow-2xl"
                >
                    <ChevronLeft size={24} color="white" />
                </TouchableOpacity>

                <View className="flex-1 relative">
                    <View className="absolute left-4 top-3.5 z-20">
                        <Search size={18} color="#00FF80" />
                    </View>
                    <TextInput
                        placeholder="FIND_STATION_PROTOCOL..."
                        placeholderTextColor="#666"
                        value={search}
                        onChangeText={onSearchChange}
                        className="bg-black/95 border-2 border-white/10 pl-11 pr-4 h-12 text-white font-mono text-xs tracking-widest uppercase shadow-2xl"
                        style={{ color: 'white' }}
                    />
                </View>

                <TouchableOpacity
                    onPress={() => {
                        if (typeof window !== 'undefined' && navigator.geolocation) {
                            navigator.geolocation.getCurrentPosition((pos) => {
                                // Normally we'd set the center, but react-leaflet center is handled by MapContainer or useMap
                                console.log("Location:", pos.coords);
                            });
                        }
                    }}
                    className="w-12 h-12 bg-[#00FF80] items-center justify-center shadow-[0_0_40px_rgba(0,255,128,0.4)] active:scale-[0.98]"
                >
                    <Navigation size={22} color="black" />
                </TouchableOpacity>
            </View>

            {/* Leaflet Map Wrapper */}
            <View className="flex-1">
                {Platform.OS === 'web' && (
                    <LeafletMap
                        center={center}
                        zoom={12}
                        style={{ height: "100%", width: "100%", background: "#1a1a1a" }}
                        zoomControl={false}
                    >
                        <TileLayer
                            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
                            url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png"
                        />
                        {filteredStations.map((station) => {
                            const customIcon = L.divIcon({
                                className: 'custom-div-icon',
                                html: `
                                    <div style="
                                        width: 40px;
                                        height: 40px;
                                        background-color: ${station.color};
                                        border: 2px solid rgba(255, 255, 255, 0.3);
                                        display: flex;
                                        align-items: center;
                                        justify-content: center;
                                        box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05);
                                    ">
                                        <span style="color: black; font-weight: 900; font-size: 10px; letter-spacing: -0.05em;">${station.logoText}</span>
                                    </div>
                                `,
                                iconSize: [40, 40],
                                iconAnchor: [20, 20],
                                popupAnchor: [0, -20]
                            });

                            return (
                                <LeafletMarker
                                    key={station.id}
                                    position={[parseFloat(station.lat!), parseFloat(station.lng!)]}
                                    icon={customIcon}
                                >
                                    <Popup>
                                        <div className="p-3 bg-black text-white border-2 border-[#00FF80] min-w-[200px]" style={{ margin: -10 }}>
                                            <div className="flex flex-row items-center gap-3 mb-3 border-b-2 border-white/5 pb-3">
                                                <div
                                                    className="w-10 h-10 flex items-center justify-center border border-white/20"
                                                    style={{ backgroundColor: station.color }}
                                                >
                                                    <span className="text-black font-black text-xs">{station.logoText}</span>
                                                </div>
                                                <div className="flex-1">
                                                    <h3 className="text-white font-black uppercase tracking-tight text-lg leading-none m-0">
                                                        {station.name}
                                                    </h3>
                                                    <p className="text-[8px] text-[#00FF80] font-mono tracking-[0.2em] mt-1 uppercase m-0">LOC_VERIFIED // 100%</p>
                                                </div>
                                            </div>
                                            <button
                                                onClick={() => router.push(`/station/${station.id}`)}
                                                className="w-full bg-[#00FF80] py-2 text-black font-black text-[10px] uppercase tracking-[0.2em] border-none cursor-pointer"
                                            >
                                                VIEW_FUEL_DATA
                                            </button>
                                        </div>
                                    </Popup>
                                </LeafletMarker>
                            );
                        })}
                    </LeafletMap>
                )}
            </View>
        </View>
    );
}

// Native Map Component
function NativeMapView({ stations, search, onSearchChange }: { stations: Station[], search: string, onSearchChange: (text: string) => void }) {
    const [region, setRegion] = useState({
        latitude: 50.4501,
        longitude: 30.5234,
        latitudeDelta: 0.1,
        longitudeDelta: 0.1,
    });
    const router = useRouter();

    const filteredStations = stations.filter(
        (s) =>
            s.lat &&
            s.lng &&
            (s.name.toLowerCase().includes(search.toLowerCase()) ||
                s.logoText.toLowerCase().includes(search.toLowerCase()))
    );

    const handleGetCurrentLocation = () => {
        setRegion({
            ...region,
            latitude: 50.4501,
            longitude: 30.5234,
        });
    };

    return (
        <View className="flex-1 bg-[#050505]">
            {/* Search Header */}
            <View className="absolute top-12 left-6 right-6 z-10 flex-row gap-2">
                <TouchableOpacity
                    onPress={() => router.back()}
                    className="w-14 h-14 bg-black border-2 border-white/20 items-center justify-center active:scale-[0.98] shadow-2xl"
                >
                    <ChevronLeft size={28} color="white" />
                </TouchableOpacity>

                <View className="flex-1 relative">
                    <View className="absolute left-4 top-4 z-20">
                        <Search size={20} color="#00FF80" />
                    </View>
                    <TextInput
                        placeholder="FIND_STATION_PROTOCOL..."
                        placeholderTextColor="#333"
                        value={search}
                        onChangeText={onSearchChange}
                        className="bg-black border-2 border-white/10 pl-12 pr-4 h-14 text-white font-mono text-xs tracking-widest uppercase shadow-2xl"
                        style={{ color: 'white' }}
                    />
                </View>

                <TouchableOpacity
                    onPress={handleGetCurrentLocation}
                    className="w-14 h-14 bg-[#00FF80] items-center justify-center shadow-[0_0_40px_rgba(0,255,128,0.4)] active:scale-[0.98]"
                >
                    <Navigation size={24} color="black" />
                </TouchableOpacity>
            </View>

            <MapView
                provider={PROVIDER_GOOGLE}
                style={styles.map}
                region={region}
                customMapStyle={darkMapStyle}
                onRegionChangeComplete={setRegion}
            >
                {filteredStations.map((station) => (
                    <Marker
                        key={station.id}
                        coordinate={{
                            latitude: parseFloat(station.lat!),
                            longitude: parseFloat(station.lng!),
                        }}
                    >
                        <View
                            className="w-10 h-10 border-2 border-white/30 items-center justify-center shadow-xl"
                            style={{ backgroundColor: station.color }}
                        >
                            <Text className="text-black font-black text-[10px] tracking-tighter">{station.logoText}</Text>
                        </View>
                        <Callout
                            tooltip
                            onPress={() => router.push(`/station/${station.id}`)}
                        >
                            <View className="bg-black border-2 border-[#00FF80] p-0 w-56 shadow-[0_0_60px_rgba(0,255,128,0.3)]">
                                <View className="p-4">
                                    <View className="flex-row items-center gap-3 mb-3 border-b-2 border-white/5 pb-3">
                                        <View
                                            className="w-8 h-8 items-center justify-center border border-white/20"
                                            style={{ backgroundColor: station.color }}
                                        >
                                            <Text className="text-black font-black text-[10px]">{station.logoText}</Text>
                                        </View>
                                        <View className="flex-1">
                                            <Text className="text-white font-black font-heading uppercase tracking-tight text-lg leading-none" numberOfLines={1}>
                                                {station.name}
                                            </Text>
                                            <Text className="text-[8px] text-[#00FF80] font-mono tracking-[0.2em] mt-1 uppercase">LOC_VERIFIED // 100%</Text>
                                        </View>
                                    </View>
                                    <View className="bg-[#00FF80] py-3 items-center">
                                        <Text className="text-black font-black text-[10px] uppercase tracking-[0.2em]">VIEW_FUEL_DATA</Text>
                                    </View>
                                </View>
                            </View>
                        </Callout>
                    </Marker>
                ))}
            </MapView>
        </View>
    );
}

export default function StationsMap() {
    const [search, setSearch] = useState("");

    const { data: stations = [], isLoading } = useQuery<Station[]>({
        queryKey: ["/api/stations"],
        queryFn: async () => {
            const res = await apiFetch("/api/stations");
            return res.json();
        }
    });

    if (isLoading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
            </View>
        );
    }

    return (
        <ProtectedRoute>
            {Platform.OS === 'web' ? (
                <WebMapView stations={stations} search={search} onSearchChange={setSearch} />
            ) : (
                <NativeMapView stations={stations} search={search} onSearchChange={setSearch} />
            )}
        </ProtectedRoute>
    );
}

const styles = StyleSheet.create({
    map: {
        width: Dimensions.get('window').width,
        height: Dimensions.get('window').height,
    },
});

const darkMapStyle = [
    {
        "elementType": "geometry",
        "stylers": [
            {
                "color": "#212121"
            }
        ]
    },
    {
        "elementType": "labels.icon",
        "stylers": [
            {
                "visibility": "off"
            }
        ]
    },
    {
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#757575"
            }
        ]
    },
    {
        "elementType": "labels.text.stroke",
        "stylers": [
            {
                "color": "#212121"
            }
        ]
    },
    {
        "featureType": "administrative",
        "elementType": "geometry",
        "stylers": [
            {
                "color": "#757575"
            }
        ]
    },
    {
        "featureType": "administrative.country",
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#9e9e9e"
            }
        ]
    },
    {
        "featureType": "administrative.land_parcel",
        "stylers": [
            {
                "visibility": "off"
            }
        ]
    },
    {
        "featureType": "administrative.locality",
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#bdbdbd"
            }
        ]
    },
    {
        "featureType": "poi",
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#757575"
            }
        ]
    },
    {
        "featureType": "poi.park",
        "elementType": "geometry",
        "stylers": [
            {
                "color": "#181818"
            }
        ]
    },
    {
        "featureType": "poi.park",
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#616161"
            }
        ]
    },
    {
        "featureType": "poi.park",
        "elementType": "labels.text.stroke",
        "stylers": [
            {
                "color": "#1b1b1b"
            }
        ]
    },
    {
        "featureType": "road",
        "elementType": "geometry.fill",
        "stylers": [
            {
                "color": "#2c2c2c"
            }
        ]
    },
    {
        "featureType": "road",
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#8a8a8a"
            }
        ]
    },
    {
        "featureType": "road.arterial",
        "elementType": "geometry",
        "stylers": [
            {
                "color": "#373737"
            }
        ]
    },
    {
        "featureType": "road.highway",
        "elementType": "geometry",
        "stylers": [
            {
                "color": "#3c3c3c"
            }
        ]
    },
    {
        "featureType": "road.highway.controlled_access",
        "elementType": "geometry",
        "stylers": [
            {
                "color": "#4e4e4e"
            }
        ]
    },
    {
        "featureType": "road.local",
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#616161"
            }
        ]
    },
    {
        "featureType": "transit",
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#757575"
            }
        ]
    },
    {
        "featureType": "water",
        "elementType": "geometry",
        "stylers": [
            {
                "color": "#000000"
            }
        ]
    },
    {
        "featureType": "water",
        "elementType": "labels.text.fill",
        "stylers": [
            {
                "color": "#3d3d3d"
            }
        ]
    }
];
