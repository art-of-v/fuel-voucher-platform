
import React, { useEffect, useState } from 'react';
import { View, Text, TextInput, TouchableOpacity, StyleSheet, ActivityIndicator, Dimensions } from 'react-native';
import MapView, { Marker, Callout, PROVIDER_GOOGLE } from 'react-native-maps';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Navigation, MapPin, Search, ChevronLeft } from 'lucide-react-native';
import { apiFetch } from '@/lib/api';
import { ProtectedRoute } from '@/components/ProtectedRoute';

type Station = {
    id: string;
    name: string;
    color: string;
    logoText: string;
    lat?: string;
    lng?: string;
};

export default function StationsMap() {
    const [search, setSearch] = useState("");
    const [region, setRegion] = useState({
        latitude: 50.4501,
        longitude: 30.5234,
        latitudeDelta: 0.1,
        longitudeDelta: 0.1,
    });
    const router = useRouter();

    const { data: stations = [], isLoading } = useQuery<Station[]>({
        queryKey: ["/api/stations"],
        queryFn: async () => {
            const res = await apiFetch("/api/stations");
            return res.json();
        }
    });

    const filteredStations = stations.filter(
        (s) =>
            s.lat &&
            s.lng &&
            (s.name.toLowerCase().includes(search.toLowerCase()) ||
                s.logoText.toLowerCase().includes(search.toLowerCase()))
    );

    const handleGetCurrentLocation = () => {
        // In a real app, use expo-location
        // For now, just a placeholder or re-center on Kyiv
        setRegion({
            ...region,
            latitude: 50.4501,
            longitude: 30.5234,
        });
    };

    if (isLoading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
            </View>
        );
    }

    return (
        <ProtectedRoute>
            <View className="flex-1 bg-[#050505]">
                {/* Search Header AUTHORITATIVE MECHANICAL TRANSLATION */}
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
                            onChangeText={setSearch}
                            className="bg-black border-2 border-white/10 pl-12 pr-4 h-14 text-white font-mono text-xs tracking-widest uppercase shadow-2xl"
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
