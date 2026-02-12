import React from 'react';
import { View, Text, TouchableOpacity, Image, ScrollView } from 'react-native';
import { useRouter } from 'expo-router';
import { ProtectedRoute } from '@/components/ProtectedRoute';
import Layout from '@/components/layout';
import { STATIONS } from '@/lib/mock-data';
import { useStore } from '@/lib/store';
import { useI18n } from '@/lib/i18n';
import { ArrowRight, AlertTriangle, MapPin, Zap } from 'lucide-react-native';
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
                <View className="flex-1 p-6 pt-12 relative overflow-hidden">
                    {/* Red Background Shadow - React Mirror */}
                    <View className="absolute -right-20 top-1/2 w-64 h-64 bg-red-500/20 rounded-full blur-[80px]" style={{ filter: 'blur(80px)' } as any} />

                    <ScrollView contentContainerStyle={{ paddingBottom: 100 }} showsVerticalScrollIndicator={false}>
                        {/* Header Section */}
                        <View className="mb-8 flex-row items-center gap-4 relative">
                            {/* Logo Box */}
                            <View className="w-28 h-28 bg-black border-4 border-[#00FF80] flex items-center justify-center relative overflow-hidden animate-pulse-glow" style={{ boxShadow: '0 0 80px rgba(0,255,128,0.8)' }}>
                                {/* Background Gradients - Web Only Styles */}
                                <View className="absolute inset-0 z-0 bg-[#00FF80]/20" />

                                {/** Logo Image with Web-specific objectFit */}
                                <Image
                                    source={lionLogo}
                                    className="w-24 h-24 relative z-10"
                                    style={{
                                        width: 96,
                                        height: 96,
                                        objectFit: 'contain',
                                        filter: 'drop-shadow(0 0 40px rgba(0,255,128,1)) saturate(1.5) contrast(1.25)'
                                    } as any}
                                />

                                {/* Corners */}
                                <View className="absolute top-0 left-0 w-4 h-4 border-t-4 border-l-4 border-[#00FF80] z-20" />
                                <View className="absolute top-0 right-0 w-4 h-4 border-t-4 border-r-4 border-[#00FF80] z-20" />
                                <View className="absolute bottom-0 left-0 w-4 h-4 border-b-4 border-l-4 border-red-500 z-20" />
                                <View className="absolute bottom-0 right-0 w-4 h-4 border-b-4 border-r-4 border-red-500 z-20" />
                            </View>

                            <View className="flex-1">
                                <View className="absolute -inset-10 bg-[#00FF80]/20 blur-[60px] rounded-full" />
                                <Text className="text-[#00FF80] text-4xl font-heading-bold uppercase tracking-[0.15em] leading-none mb-1 text-glow-intense animate-flicker" style={{ textShadow: '0 0 50px rgba(0,255,128,1)' }}>LEMBERG</Text>
                                <Text className="text-white text-xl font-heading-bold uppercase tracking-[0.3em] leading-none text-glow" style={{ textShadow: '0 0 30px rgba(255,255,255,0.6)' }}>FUEL CORP.</Text>
                                <View className="flex-row items-center gap-2 mt-2">
                                    <View className="h-[1px] flex-1 bg-[#00FF80]" />
                                    <Text className="text-[10px] text-[#00FF80] font-mono tracking-[0.3em] uppercase">{t('stations.dominate')}</Text>
                                    <View className="h-[1px] flex-1 bg-[#00FF80]" />
                                </View>
                            </View>
                        </View>

                        {/* Title Area */}
                        <View className="mb-8 border-l-4 border-[#00FF80] pl-4">
                            <Text className="text-4xl text-white font-black font-heading uppercase text-glow" style={{ textShadow: '0 0 20px rgba(255,255,255,0.4)' }}>{t('stations.title')}</Text>
                            <Text className="text-4xl text-[#00FF80] font-black font-heading uppercase text-glow-intense" style={{ textShadow: '0 0 30px rgba(0,255,128,0.8)' }}>{t('stations.title2')}</Text>
                        </View>

                        {/* Action Buttons */}
                        <View className="flex-row gap-4 mb-6">
                            <View className="flex-1 flex-row items-center justify-center gap-2 bg-red-900/20 border border-red-500/50 p-2 rounded">
                                <AlertTriangle size={16} color="#ef4444" />
                                <Text className="text-red-500 font-mono text-[9px] uppercase tracking-widest leading-tight text-center">
                                    // {t('stations.authorized').replace(' ', '\n')}
                                </Text>
                            </View>
                            <TouchableOpacity onPress={() => router.push('/map')} className="flex-1 flex-row items-center justify-center gap-2 bg-[#00FF80]/10 border border-[#00FF80]/50 p-2 rounded active:bg-[#00FF80]/20">
                                <MapPin size={16} color="#00FF80" />
                                <Text className="text-[#00FF80] font-mono text-[10px] uppercase tracking-widest">{t('map.view')}</Text>
                            </TouchableOpacity>
                        </View>

                        {/* Stations List */}
                        <View className="gap-3">
                            {STATIONS.map((station) => {
                                const stationColor = station.id === 'okko' ? '#22c55e' :
                                    station.id === 'wog' ? '#4ade80' :
                                        station.id === 'upg' ? '#22d3ee' :
                                            '#facc15';
                                return (
                                    <TouchableOpacity
                                        key={station.id}
                                        onPress={() => handleSelect(station)}
                                        className="bg-[#0f0f0f] border-2 rounded-lg overflow-hidden transition-all duration-300 hover:scale-[1.02]"
                                        style={{
                                            borderColor: stationColor,
                                            backgroundColor: `${stationColor}10`, // 6% opacity (hex 10)
                                            boxShadow: `0 0 30px ${stationColor}60`
                                        }}
                                    >
                                        <View className="flex-row h-full">
                                            <View className="w-2 h-full" style={{ backgroundColor: stationColor }} />
                                            <View className="flex-1 p-5 flex-row items-center justify-between">
                                                <View>
                                                    <View className="flex-row items-center gap-2">
                                                        <Text
                                                            className="text-3xl font-heading-bold font-black uppercase tracking-wider text-glow"
                                                            style={{ color: stationColor, textShadow: `0 0 20px ${stationColor}80` }}
                                                        >
                                                            {station.name}
                                                        </Text>
                                                        <Zap
                                                            size={20}
                                                            color={stationColor}
                                                            className="opacity-50"
                                                        />
                                                    </View>
                                                    <View className="flex-row items-center gap-2 mt-1">
                                                        <View className="w-2 h-2 rounded-full animate-pulse shadow-[0_0_10px_rgba(0,255,128,0.8)]" style={{ backgroundColor: stationColor }} />
                                                        <Text className="text-[10px] text-gray-400 font-mono uppercase tracking-[0.2em]">{t('stations.online')} • {t('stations.ready')}</Text>
                                                    </View>
                                                </View>
                                                <View
                                                    className="w-10 h-10 items-center justify-center rounded border-2"
                                                    style={{
                                                        borderColor: `${stationColor}80`,
                                                        backgroundColor: `${stationColor}20`
                                                    }}
                                                >
                                                    <ArrowRight color={stationColor} size={20} />
                                                </View>
                                            </View>
                                        </View>
                                    </TouchableOpacity>
                                );
                            })}
                        </View>

                        {/* Bottom Footer Text */}
                        <View className="items-center py-6 border-t border-white/5 mt-4">
                            <Text className="text-[10px] text-gray-600 font-mono tracking-[0.3em] uppercase">
                                [ ENCRYPTED TRANSACTION PROTOCOL v2.4 ]
                            </Text>
                        </View>
                    </ScrollView>
                </View>
            </Layout>
        </ProtectedRoute>
    );
}
