
import React, { useState } from 'react';
import { View, Text, TextInput, TouchableOpacity, ActivityIndicator } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Navigation, Search, ChevronLeft, Map as MapIcon } from 'lucide-react-native';
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

export default function StationsMapWeb() {
    const [search, setSearch] = useState("");
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

    if (isLoading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
            </View>
        );
    }

    return (
        <ProtectedRoute>
            <View className="flex-1 bg-black p-6 pt-12">
                <View className="flex-row gap-2 mb-6">
                    <TouchableOpacity
                        onPress={() => router.back()}
                        className="w-12 h-12 bg-white/5 border border-white/10 items-center justify-center rounded-xl"
                    >
                        <ChevronLeft size={24} color="white" />
                    </TouchableOpacity>

                    <View className="flex-1 relative">
                        <View className="absolute left-3 top-3 z-20">
                            <Search size={18} color="#888" />
                        </View>
                        <TextInput
                            placeholder="Search stations..."
                            placeholderTextColor="#666"
                            value={search}
                            onChangeText={setSearch}
                            className="bg-white/5 border border-white/10 pl-10 pr-4 h-12 rounded-xl text-white font-mono"
                        />
                    </View>
                </View>

                <View className="flex-1 bg-white/5 border border-white/10 rounded-3xl items-center justify-center p-8">
                    <MapIcon size={64} color="#00FF80" opacity={0.2} />
                    <Text className="text-white font-black text-2xl font-heading uppercase mt-6 mb-2">Interactive Map</Text>
                    <Text className="text-gray-400 text-center font-mono text-sm mb-8">
                        The interactive map is optimized for mobile devices. On web, please select a station from the list below.
                    </Text>

                    <View className="w-full gap-2">
                        {filteredStations.map(station => (
                            <TouchableOpacity
                                key={station.id}
                                onPress={() => router.push(`/station/${station.id}`)}
                                className="flex-row items-center justify-between bg-black/40 border border-white/10 p-4 rounded-xl"
                            >
                                <View className="flex-row items-center gap-3">
                                    <View className="w-8 h-8 rounded-full items-center justify-center" style={{ backgroundColor: station.color }}>
                                        <Text className="text-black font-bold text-[10px]">{station.logoText}</Text>
                                    </View>
                                    <Text className="text-white font-heading uppercase">{station.name}</Text>
                                </View>
                                <ChevronLeft size={16} color="#888" style={{ transform: [{ rotate: '180deg' }] }} />
                            </TouchableOpacity>
                        ))}
                    </View>
                </View>
            </View>
        </ProtectedRoute>
    );
}
