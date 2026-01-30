
import { View, Text, TouchableOpacity, ScrollView, Image } from 'react-native';
import { useRouter } from 'expo-router';
import { useStore } from '@/lib/store';
import { useI18n } from '@/lib/i18n';
import { STATIONS } from '@/lib/mock-data';
import { ArrowRight, Zap, AlertTriangle, MapPin } from 'lucide-react-native';
import { ProtectedRoute } from '@/components/ProtectedRoute';
import { LanguageSelector } from '@/components/language-selector';
import { cn } from '@/lib/utils';

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
            <View className="flex-1 bg-[#050505]">
                {/* Global Atmospheric Foundation */}
                <View
                    className="absolute -top-[80px] -left-[80px] w-[256px] h-[256px] bg-[#00FF80]/20 rounded-full opacity-20"
                    style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                />

                <ScrollView
                    className="flex-1 z-10"
                    contentContainerStyle={{
                        paddingHorizontal: 24,
                        paddingTop: 32,
                        paddingBottom: 120
                    }}
                >
                    <View className="flex-row justify-end mb-2">
                        <LanguageSelector />
                    </View>

                    <View className="mb-6">
                        {/* Branding Section - AUTHORITATIVE CLONE */}
                        <View className="flex-row items-center gap-4 mb-8">
                            <View className="w-[112px] h-[112px] bg-black border-4 border-[#00FF80] items-center justify-center relative overflow-hidden">
                                {/* Intense Glow Background Layer */}
                                <View className="absolute inset-0 bg-[#00FF80]/20" />
                                <View
                                    className="absolute w-[80px] h-[80px] bg-[#00FF80]/30 rounded-full"
                                    style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 30 }}
                                />

                                {/* Scanline Effect */}
                                <View className="absolute inset-0 opacity-20">
                                    {[...Array(28)].map((_, i) => (
                                        <View key={i} className="h-[2px] w-full bg-black mb-[2px]" />
                                    ))}
                                </View>

                                {/* Logo (Restored Detail) */}
                                <Image
                                    source={require('../assets/generated_images/profile_cyberpunk_lion_logo.png')}
                                    style={{ width: 88, height: 88, resizeMode: 'contain' }}
                                    className="relative z-10 saturate-150 contrast-125"
                                />

                                {/* Corner Accents */}
                                <View className="absolute top-0 left-0 w-4 h-4 border-t-4 border-l-4 border-[#00FF80] shadow-[0_0_10px_#00FF80]" />
                                <View className="absolute top-0 right-0 w-4 h-4 border-t-4 border-r-4 border-[#00FF80] shadow-[0_0_10px_#00FF80]" />
                                <View className="absolute bottom-0 left-0 w-4 h-4 border-b-4 border-l-4 border-[#FF3232] shadow-[0_0_10px_#FF3232]" />
                                <View className="absolute bottom-0 right-0 w-4 h-4 border-b-4 border-r-4 border-[#FF3232] shadow-[0_0_10px_#FF3232]" />
                            </View>

                            <View className="flex-1">
                                <Text className="text-[#00FF80] font-heading font-black uppercase tracking-[0.15em] text-[36px] leading-none mb-1" style={{ textShadowColor: '#00FF80', textShadowOffset: { width: 0, height: 0 }, textShadowRadius: 15 }}>
                                    LEMBERG
                                </Text>
                                <Text className="text-white font-heading font-bold uppercase tracking-[0.3em] text-[20px] leading-none">
                                    FUEL CORP.
                                </Text>
                                <View className="flex-row items-center gap-2 mt-2">
                                    <View className="h-[2px] flex-1 bg-[#00FF80]/40" />
                                    <Text className="text-[10px] text-[#00FF80] font-mono tracking-[0.3em] uppercase">{t('stations.dominate')}</Text>
                                    <View className="h-[2px] flex-1 bg-[#00FF80]/40" />
                                </View>
                            </View>
                        </View>

                        {/* Aggressive Title Block */}
                        <View className="relative mb-6">
                            <View className="absolute -left-6 top-1 bottom-1 w-[4px] bg-[#00FF80]" style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 20 }} />
                            <View className="pl-4">
                                <Text className="text-[48px] font-black text-white leading-[0.9] tracking-tighter uppercase font-heading">
                                    {t('stations.title')}
                                </Text>
                                <Text
                                    className="text-[48px] font-black text-[#00FF80] leading-[0.9] tracking-tighter uppercase font-heading"
                                    style={{ textShadowColor: '#00FF80', textShadowOffset: { width: 0, height: 0 }, textShadowRadius: 30 }}
                                >
                                    {t('stations.title2')}
                                </Text>
                            </View>
                        </View>

                        <View className="flex-row items-center justify-between mb-8">
                            <View className="flex-row items-center gap-3 bg-[#FF3232]/10 border border-[#FF3232]/30 px-[16px] py-[8px]">
                                <AlertTriangle size={16} color="#FF3232" />
                                <Text className="text-[#FF3232] font-mono text-[10px] tracking-widest uppercase font-black">
                                    // {t('stations.authorized')}
                                </Text>
                            </View>
                            <TouchableOpacity
                                onPress={() => router.push('/map')}
                                className="flex-row items-center gap-2 bg-[#00FF80]/10 border border-[#00FF80]/50 px-[16px] py-[8px] active:scale-[0.98]"
                            >
                                <MapPin size={16} color="#00FF80" />
                                <Text className="text-[#00FF80] font-mono text-[10px] tracking-widest uppercase font-black">
                                    {t('map.view')}
                                </Text>
                            </TouchableOpacity>
                        </View>
                    </View>

                    <View className="gap-[12px] relative">
                        {/* Background Lion Watermark */}
                        <Image
                            source={require('../assets/generated_images/profile_cyberpunk_lion_logo.png')}
                            className="absolute -right-12 -bottom-24 w-[300px] h-[300px] opacity-[0.05] grayscale"
                            style={{ tintColor: '#00FF80' }}
                        />

                        {STATIONS.map((station) => (
                            <TouchableOpacity
                                key={station.id}
                                onPress={() => handleSelect(station)}
                                activeOpacity={0.7}
                                className="flex-row bg-black/80 border-2 border-white/10 rounded-[8px] active:scale-[0.98] overflow-hidden"
                            >
                                <View className={cn("w-[8px]",
                                    station.id === 'okko' ? 'bg-[#22C55E]' :
                                        station.id === 'wog' ? 'bg-[#34D399]' :
                                            station.id === 'upg' ? 'bg-[#22D3EE]' :
                                                'bg-[#FACC15]'
                                )} style={{ shadowColor: station.id === 'okko' ? '#22C55E' : station.id === 'wog' ? '#34D399' : station.id === 'upg' ? '#22D3EE' : '#FACC15', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 10 }} />

                                <View className="flex-1 p-[20px] flex-row items-center justify-between bg-black/40">
                                    <View>
                                        <View className="flex-row items-center gap-3">
                                            <Text className={cn("text-[32px] font-black font-heading uppercase tracking-tighter",
                                                station.id === 'okko' ? 'text-[#22C55E]' :
                                                    station.id === 'wog' ? 'text-[#34D399]' :
                                                        station.id === 'upg' ? 'text-[#22D3EE]' :
                                                            'text-[#FACC15]'
                                            )} style={{ textShadowColor: station.id === 'okko' ? '#22C55E' : station.id === 'wog' ? '#34D399' : station.id === 'upg' ? '#22D3EE' : '#FACC15', textShadowOffset: { width: 0, height: 0 }, textShadowRadius: 10 }}>
                                                {station.logoText}
                                            </Text>
                                            <Zap size={18} color={
                                                station.id === 'okko' ? '#22C55E' :
                                                    station.id === 'wog' ? '#34D399' :
                                                        station.id === 'upg' ? '#22D3EE' :
                                                            '#FACC15'
                                            } opacity={0.6} />
                                        </View>
                                        <View className="flex-row items-center gap-2 mt-2">
                                            <View className="w-2 h-2 rounded-full bg-[#00FF80] shadow-[0_0_10px_#00FF80]" />
                                            <Text className="text-[10px] text-gray-500 font-mono uppercase tracking-[0.2em] font-bold">{t('stations.online')} • {t('stations.ready')}</Text>
                                        </View>
                                    </View>

                                    <View className="w-[48px] h-[48px] bg-[#00FF80]/10 border-2 border-[#00FF80]/50 items-center justify-center">
                                        <ArrowRight size={24} color="#00FF80" />
                                    </View>
                                </View>
                            </TouchableOpacity>
                        ))}
                    </View>

                    <View className="mt-[48px] pt-[32px] border-t border-white/5 items-center">
                        <Text className="text-[10px] text-gray-700 font-mono tracking-[0.3em] uppercase">
                            [ ENCRYPTED TRANSACTION PROTOCOL v2.4 ]
                        </Text>
                    </View>
                </ScrollView>
            </View>
        </ProtectedRoute>
    );
}
