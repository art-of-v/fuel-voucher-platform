import { View, Text, Pressable, Image, ScrollView } from "react-native";
import { Link, useRouter } from "expo-router";
import { ArrowRight, Zap, AlertTriangle, MapPin } from "lucide-react-native";
import { PageLayout } from "../src/components/page-layout";
import { STATIONS } from "../src/lib/mock-data";
import { useStore } from "../src/lib/store";
import { useI18n } from "../src/lib/i18n";

export default function StationsScreen() {
    const router = useRouter();
    const selectStation = useStore((state) => state.selectStation);
    const { t } = useI18n();

    const handleSelect = (station: any) => {
        selectStation(station);
        router.push(`/station/${station.id}`);
    };

    const Header = (
        <View className="p-6 pb-0">
            <View className="flex-row items-center gap-4 mb-8">
                <View className="w-24 h-24 bg-black border-4 border-[#00FF80] items-center justify-center overflow-hidden">
                    <View className="absolute inset-0 bg-[#00FF8020]" />
                    {/* Placeholder for lionLogo */}
                    <View className="w-16 h-16 bg-[#00FF8040] rounded-full" />
                </View>
                <View className="flex-1">
                    <Text className="text-[#00FF80] font-bold uppercase tracking-widest text-3xl">LEMBERG</Text>
                    <Text className="text-white font-bold uppercase tracking-[0.2em] text-lg">FUEL CORP.</Text>
                </View>
            </View>

            <View className="relative mb-6">
                <View className="absolute -left-6 top-0 bottom-0 w-1 bg-[#00FF80]" />
                <Text className="text-3xl font-black text-white uppercase pl-4">
                    {t('stations.title')}
                    {"\n"}
                    <Text className="text-[#00FF80]">{t('stations.title2')}</Text>
                </Text>
            </View>

            <View className="flex-row gap-4 mb-4">
                <View className="flex-1 flex-row items-center justify-center gap-2 bg-red-500/10 border border-red-500/30 p-3 rounded">
                    <AlertTriangle size={16} color="#EF4444" />
                    <Text className="text-red-500 text-[10px] font-bold uppercase tracking-widest">// {t('stations.authorized')}</Text>
                </View>
                <Link href="/map" asChild>
                    <Pressable className="flex-1 flex-row items-center justify-center gap-2 bg-[#00FF8010] border border-[#00FF8050] p-3 rounded">
                        <MapPin size={16} color="#00FF80" />
                        <Text className="text-[#00FF80] text-[10px] font-bold uppercase tracking-widest">{t('map.view')}</Text>
                    </Pressable>
                </Link>
            </View>
        </View>
    );

    return (
        <PageLayout header={Header} scrollClassName="p-6 pt-0">
            <View className="gap-3">
                {STATIONS.map((station) => (
                    <Pressable
                        key={station.id}
                        onPress={() => handleSelect(station)}
                        className="flex-row bg-black/80 border-2 border-white/10 rounded-lg overflow-hidden active:scale-95"
                    >
                        <View
                            style={{
                                backgroundColor:
                                    station.id === 'okko' ? '#22C55E' :
                                        station.id === 'wog' ? '#10B981' :
                                            station.id === 'upg' ? '#22D3EE' :
                                                '#FACC15'
                            }}
                            className="w-2"
                        />
                        <View className="flex-1 p-5 flex-row items-center justify-between">
                            <View>
                                <View className="flex-row items-center gap-2">
                                    <Text
                                        style={{
                                            color:
                                                station.id === 'okko' ? '#22C55E' :
                                                    station.id === 'wog' ? '#10B981' :
                                                        station.id === 'upg' ? '#22D3EE' :
                                                            '#FACC15'
                                        }}
                                        className="text-3xl font-black uppercase"
                                    >
                                        {station.logoText}
                                    </Text>
                                    <Zap size={20} color="#00FF8080" />
                                </View>
                                <View className="flex-row items-center gap-2 mt-1">
                                    <View className="w-2 h-2 rounded-full bg-[#00FF80]" />
                                    <Text className="text-[10px] text-gray-400 uppercase tracking-widest">{t('stations.online')} • {t('stations.ready')}</Text>
                                </View>
                            </View>
                            <View className="w-10 h-10 bg-[#00FF8020] border border-[#00FF8050] items-center justify-center rounded">
                                <ArrowRight size={20} color="#00FF80" />
                            </View>
                        </View>
                    </Pressable>
                ))}
            </View>

            <View className="text-center py-8">
                <Text className="text-[10px] text-gray-600 font-bold uppercase tracking-[0.3em] text-center">
                    [ ENCRYPTED TRANSACTION PROTOCOL v2.4 ]
                </Text>
            </View>
        </PageLayout>
    );
}
