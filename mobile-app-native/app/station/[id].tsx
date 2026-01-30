
import { useState, useEffect } from 'react';
import { View, Text, TouchableOpacity, ScrollView, ActivityIndicator, Image } from 'react-native';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { useStore } from '@/lib/store';
import { useI18n } from '@/lib/i18n';
import { ChevronLeft, Droplets, TrendingDown, Zap, AlertTriangle } from 'lucide-react-native';
import { getPackages, getInventory } from '@/lib/api';
import { normalizeFuelName, cn } from '@/lib/utils';
import { ProtectedRoute } from '@/components/ProtectedRoute';

interface DisplayFuel {
    id: string;
    name: string;
    stationId: string;
    basePrice: number;
    discountPrice: number;
}

export default function FuelSelectionScreen() {
    const { id } = useLocalSearchParams<{ id: string }>();
    const router = useRouter();
    const { selectedStation, selectFuel } = useStore();
    const [fuels, setFuels] = useState<DisplayFuel[]>([]);
    const [loading, setLoading] = useState(true);
    const { t } = useI18n();

    useEffect(() => {
        if (id) {
            loadData(id);
        }
    }, [id]);

    const loadData = async (stationId: string) => {
        try {
            setLoading(true);

            let allPackages: any[] = [];
            let inventory: any[] = [];

            try {
                allPackages = await getPackages();
            } catch (e) {
                console.error("Failed to load packages", e);
            }

            try {
                inventory = await getInventory();
            } catch (e) {
                console.warn("Inventory check failed, proceeding with all packages assumed available", e);
            }

            const normalizedInventoryFuels = new Set(
                inventory
                    .filter(item => {
                        const m = item.provider.toLowerCase() === stationId.toLowerCase();
                        const c = item.availableCount > 0;
                        return m && c;
                    })
                    .map(item => normalizeFuelName(item.fuelType))
            );

            const displayFuels: DisplayFuel[] = [];
            const seenFuels = new Set<string>();

            const stationPackages = allPackages.filter(p =>
                p.stationId.toLowerCase() === stationId.toLowerCase()
            );

            stationPackages.forEach(pkg => {
                const normName = normalizeFuelName(pkg.fuelName);
                if (!seenFuels.has(normName)) {
                    seenFuels.add(normName);
                    displayFuels.push({
                        id: pkg.fuelTypeId || pkg.fuelName,
                        name: pkg.fuelName,
                        stationId: pkg.stationId,
                        basePrice: pkg.originalPrice / pkg.liters,
                        discountPrice: pkg.price / pkg.liters
                    });
                }
            });

            setFuels(displayFuels);
        } catch (e) {
            console.error(e);
        } finally {
            setLoading(false);
        }
    };

    const handleSelect = (fuel: DisplayFuel) => {
        selectFuel(fuel);
        router.push("/packages");
    };

    if (!selectedStation) {
        return (
            <View className="flex-1 bg-[#050505] items-center justify-center p-8">
                <View className="w-24 h-24 bg-black border-2 border-[#FF3232] items-center justify-center mb-8 shadow-[0_0_40px_rgba(255,50,50,0.2)]">
                    <AlertTriangle size={48} color="#FF3232" />
                </View>
                <Text className="text-white font-heading text-2xl uppercase tracking-widest text-center mb-8">
                    STATION_DATA_MISSING
                </Text>
                <TouchableOpacity
                    onPress={() => router.replace("/")}
                    className="w-full bg-[#00FF80] py-6 items-center shadow-[0_0_40px_rgba(0,255,128,0.4)] active:scale-[0.98]"
                >
                    <Text className="text-black font-black font-heading text-xl uppercase tracking-widest">RETURN_TO_TERMINAL</Text>
                </TouchableOpacity>
            </View>
        );
    }

    if (loading) {
        return (
            <View className="flex-1 bg-[#050505] items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
                <Text className="text-[#00FF80] font-mono mt-8 uppercase tracking-[0.4em] text-glow-intense">// SYNCING_PRICES...</Text>
            </View>
        );
    }

    return (
        <ProtectedRoute>
            <View className="flex-1 bg-[#050505]">
                {/* Global Atmospheric Foundation */}
                <View
                    className="absolute top-0 left-1/2 -ml-[192px] w-[384px] h-[384px] bg-[#00FF80]/10 rounded-full opacity-20"
                    style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 150 }}
                />

                <ScrollView
                    className="flex-1 z-10"
                    contentContainerStyle={{ paddingBottom: 120 }}
                >
                    {/* Dynamic Header - AUTHORITATIVE CLONE */}
                    <View className="h-[256px] relative p-[24px] flex flex-col justify-end overflow-hidden">
                        <View className={cn("absolute inset-0 opacity-30",
                            selectedStation.id === 'okko' ? 'bg-[#22C55E]' :
                                selectedStation.id === 'wog' ? 'bg-[#10B981]' :
                                    selectedStation.id === 'upg' ? 'bg-[#06B6D4]' :
                                        'bg-[#EAB308]'
                        )} />

                        <View className="absolute inset-0 bg-black/40" />

                        <TouchableOpacity
                            onPress={() => router.back()}
                            className="absolute top-[24px] left-[24px] p-2 bg-black/60 border-2 border-white/20 z-20 active:scale-[0.98] shadow-2xl"
                        >
                            <ChevronLeft size={24} color="white" />
                        </TouchableOpacity>

                        <View className="relative z-10">
                            <View className="flex-row items-baseline gap-3 mb-2">
                                <Zap size={32} color="#00FF80" className="animate-pulse" />
                                <Text className={cn("text-[60px] font-black font-heading uppercase tracking-tighter leading-none",
                                    selectedStation.id === 'okko' ? 'text-[#22C55E]' :
                                        selectedStation.id === 'wog' ? 'text-[#34D399]' :
                                            selectedStation.id === 'upg' ? 'text-[#22D3EE]' :
                                                'text-[#FACC15]'
                                )} style={{ textShadowColor: '#000', textShadowOffset: { width: 0, height: 0 }, textShadowRadius: 20 }}>
                                    {selectedStation.logoText}
                                </Text>
                            </View>

                            <View className="flex-row items-center gap-3">
                                <View className="h-[2px] flex-1 bg-[#00FF80]/40" />
                                <View className="px-3 py-1 bg-[#FF3232]/20 border border-[#FF3232]/30 flex-row items-center gap-2">
                                    <AlertTriangle size={12} color="#FF3232" />
                                    <Text className="text-[#FF3232] text-[10px] font-mono tracking-[0.2em] uppercase font-black">
                                        LIVE RATES
                                    </Text>
                                </View>
                            </View>
                        </View>
                    </View>

                    <View className="p-[16px] -mt-[24px] gap-[12px]">
                        {fuels.length === 0 ? (
                            <View className="items-center justify-center py-[40px] border border-dashed border-white/10 bg-black/50 rounded-[8px]">
                                <Text className="text-gray-500 font-mono text-sm uppercase tracking-widest">
                                    NO FUEL AVAILABLE
                                </Text>
                            </View>
                        ) : fuels.map((fuel) => (
                            <TouchableOpacity
                                key={fuel.id}
                                onPress={() => handleSelect(fuel)}
                                className="flex-row bg-black/80 border-2 border-white/10 overflow-hidden active:scale-[0.99] transition-all"
                            >
                                <View className="w-[6px] bg-[#00FF80]" style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 10 }} />

                                <View className="flex-1 p-[20px] flex-row items-center justify-between">
                                    <View className="flex-row items-center gap-[16px]">
                                        <View className="w-[56px] h-[56px] bg-[#00FF80]/10 border-2 border-[#00FF80]/30 items-center justify-center">
                                            <Droplets size={28} color="#00FF80" />
                                        </View>
                                        <View>
                                            <Text className="font-black text-white text-[24px] font-heading uppercase tracking-tight leading-none mb-1">
                                                {fuel.name}
                                            </Text>
                                            <View className="flex-row items-center gap-3">
                                                <Text className="text-gray-600 line-through font-mono text-sm">
                                                    {fuel.basePrice.toFixed(2)}
                                                </Text>
                                                <Text className="text-[#00FF80] font-black font-mono text-[20px]" style={{ textShadowColor: '#00FF80', textShadowOffset: { width: 0, height: 0 }, textShadowRadius: 10 }}>
                                                    {fuel.discountPrice.toFixed(2)} ₴
                                                </Text>
                                            </View>
                                        </View>
                                    </View>

                                    <View className="bg-[#00FF80] px-[16px] py-[8px] flex-row items-center gap-2 shadow-[0_0_20px_rgba(0,255,128,0.5)]">
                                        <TrendingDown size={16} color="black" />
                                        <Text className="text-black font-black text-sm font-mono">
                                            -{(fuel.basePrice - fuel.discountPrice).toFixed(2)}
                                        </Text>
                                    </View>
                                </View>
                            </TouchableOpacity>
                        ))}
                    </View>

                    <View className="mt-[48px] items-center pb-[32px]">
                        <Text className="text-[10px] text-gray-600 font-mono tracking-[0.2em] uppercase">
                            [ BULK DISCOUNT RATES ACTIVE ]
                        </Text>
                    </View>
                </ScrollView>
            </View>
        </ProtectedRoute>
    );
}
