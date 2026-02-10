import React from 'react';
import { View, Text, TouchableOpacity, Image, ScrollView } from 'react-native';
import { useRouter } from 'expo-router';
import { ProtectedRoute } from '@/components/ProtectedRoute';
import Layout from '@/components/layout';
import { STATIONS } from '@/lib/mock-data';
import { useStore } from '@/lib/store';
import { useI18n } from '@/lib/i18n';
import { ArrowRight, AlertTriangle, MapPin } from 'lucide-react-native';
import lionLogo from '@assets/generated_images/profile_cyberpunk_lion_logo.png';

export default function StationsScreen() {
    const router = useRouter();
    const selectStation = useStore((state) => state.selectStation);
    const { t } = useI18n();

    const handleSelect = (station: typeof STATIONS[0]) => {
        selectStation(station);
        router.push(`/station/${station.id}`);
    };

    return (
        <ProtectedRoute>
            <Layout>
                <View className="flex-1 p-6 pt-12">
                    <ScrollView contentContainerStyle={{ paddingBottom: 100 }} showsVerticalScrollIndicator={false}>
                        {/* Header Section */}
                        <View className="mb-8 flex-row items-center gap-4">
                            <View className="w-24 h-24 bg-black border-2 border-[#00FF80] flex items-center justify-center overflow-hidden">
                                <Image source={lionLogo} className="w-20 h-20" resizeMode="contain" />
                            </View>
                            <View>
                                <Text className="text-[#00FF80] text-3xl font-black font-heading uppercase tracking-widest">LEMBERG</Text>
                                <Text className="text-white text-xl font-bold font-heading uppercase tracking-widest">FUEL CORP.</Text>
                                <View className="flex-row items-center gap-2 mt-2">
                                    <View className="h-[1px] flex-1 bg-[#00FF80]" />
                                    <Text className="text-[10px] text-[#00FF80] font-mono tracking-[0.3em] uppercase">{t('stations.dominate')}</Text>
                                    <View className="h-[1px] flex-1 bg-[#00FF80]" />
                                </View>
                            </View>
                        </View>

                        {/* Title Area */}
                        <View className="mb-8 border-l-4 border-[#00FF80] pl-4">
                            <Text className="text-4xl text-white font-black font-heading uppercase">{t('stations.title')}</Text>
                            <Text className="text-4xl text-[#00FF80] font-black font-heading uppercase">{t('stations.title2')}</Text>
                        </View>

                        {/* Action Buttons */}
                        <View className="flex-row gap-4 mb-6">
                            <View className="flex-1 flex-row items-center justify-center gap-2 bg-red-500/10 border border-red-500/30 p-3 rounded">
                                <AlertTriangle size={16} color="#ef4444" />
                                <Text className="text-red-500 font-mono text-xs uppercase tracking-widest text-center" style={{ fontSize: 10 }}>// {t('stations.authorized')}</Text>
                            </View>
                            <TouchableOpacity onPress={() => router.push('/map')} className="flex-1 flex-row items-center justify-center gap-2 bg-[#00FF80]/10 border border-[#00FF80]/50 p-3 rounded active:bg-[#00FF80]/20">
                                <MapPin size={16} color="#00FF80" />
                                <Text className="text-[#00FF80] font-mono text-xs uppercase tracking-widest">{t('map.view')}</Text>
                            </TouchableOpacity>
                        </View>

                        {/* Stations List */}
                        <View className="gap-3">
                            {STATIONS.map((station) => (
                                <TouchableOpacity
                                    key={station.id}
                                    onPress={() => handleSelect(station)}
                                    className="bg-black/80 border-2 border-white/10 rounded-lg overflow-hidden active:border-[#00FF80]"
                                >
                                    <View className="flex-row">
                                        <View className={`w-2 ${station.id === 'okko' ? 'bg-green-600' :
                                            station.id === 'wog' ? 'bg-green-500' :
                                                station.id === 'upg' ? 'bg-emerald-500' : 'bg-yellow-500'}`} />
                                        <View className="flex-1 p-4 flex-row items-center justify-between">
                                            <View>
                                                <Text className="text-2xl font-black text-white font-heading uppercase tracking-wider">{station.name}</Text>
                                                <Text className="text-xs text-gray-400 font-mono uppercase tracking-widest mt-1">{t('stations.online')}</Text>
                                            </View>
                                            <ArrowRight color="#00FF80" size={24} />
                                        </View>
                                    </View>
                                </TouchableOpacity>
                            ))}
                        </View>
                    </ScrollView>
                </View>
            </Layout>
        </ProtectedRoute>
    );
}
