import { useState, useEffect } from "react";
import { View, Text, Pressable, ScrollView, ActivityIndicator } from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { ChevronLeft, Droplets, TrendingDown, Zap, AlertTriangle } from "lucide-react-native";
import { useStore } from "../../src/lib/store";
import { getPackages, getInventory } from "../../src/lib/api";
import { normalizeFuelName } from "../../src/lib/utils";
import { PageLayout } from "../../src/components/page-layout";

interface DisplayFuel {
    id: string;
    name: string;
    stationId: string;
    basePrice: number;
    discountPrice: number;
}

export default function FuelSelectionScreen() {
    const { id } = useLocalSearchParams();
    const router = useRouter();
    const { selectedStation, selectFuel } = useStore();
    const [fuels, setFuels] = useState<DisplayFuel[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        if (id) {
            loadData(id as string);
        }
    }, [id]);

    const loadData = async (stationId: string) => {
        try {
            setLoading(true);
            let allPackages: any[] = [];
            try {
                allPackages = await getPackages();
            } catch (e) {
                console.error("Failed to load packages", e);
            }

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
            <View className="flex-1 bg-black items-center justify-center">
                <Text className="text-white font-bold uppercase">Please select a station first.</Text>
            </View>
        );
    }

    const Header = (
        <View className="h-48 justify-end p-6 border-b border-white/10 relative overflow-hidden">
            <View
                style={{
                    backgroundColor:
                        selectedStation.id === 'okko' ? '#22C55E' :
                            selectedStation.id === 'wog' ? '#10B981' :
                                selectedStation.id === 'upg' ? '#22D3EE' :
                                    '#FACC15'
                }}
                className="absolute inset-0 opacity-20"
            />

            <Pressable
                onPress={() => router.back()}
                className="absolute top-12 left-6 p-2 bg-black/60 border border-white/20 rounded z-50"
            >
                <ChevronLeft size={24} color="#FFF" />
            </Pressable>

            <View className="flex-row items-center gap-3 mb-2">
                <Zap size={32} color="#00FF80" />
                <Text
                    style={{
                        color:
                            selectedStation.id === 'okko' ? '#22C55E' :
                                selectedStation.id === 'wog' ? '#10B981' :
                                    selectedStation.id === 'upg' ? '#22D3EE' :
                                        '#FACC15'
                    }}
                    className="text-5xl font-black uppercase tracking-tighter"
                >
                    {selectedStation.logoText}
                </Text>
            </View>
            <View className="flex-row items-center gap-2">
                <View className="h-px flex-1 bg-[#00FF80]" />
                <Text className="text-red-500 text-[10px] font-bold uppercase tracking-widest bg-red-500/10 border border-red-500/30 px-2 py-1">
                    <AlertTriangle size={10} color="#EF4444" /> LIVE RATES
                </Text>
            </View>
        </View>
    );

    return (
        <PageLayout header={Header} scrollClassName="p-4 space-y-3">
            {loading ? (
                <ActivityIndicator size="large" color="#00FF80" className="py-20" />
            ) : fuels.length === 0 ? (
                <View className="p-10 border border-dashed border-white/10 items-center rounded-lg">
                    <Text className="text-gray-500 font-bold uppercase tracking-widest text-center">NO FUEL AVAILABLE</Text>
                </View>
            ) : (
                fuels.map((fuel) => (
                    <Pressable
                        key={fuel.id}
                        onPress={() => handleSelect(fuel)}
                        className="flex-row bg-black/80 border-2 border-white/10 rounded-lg overflow-hidden active:scale-95"
                    >
                        <View className="w-1.5 bg-[#00FF80]" />
                        <View className="flex-1 p-5 flex-row items-center justify-between">
                            <View className="flex-row items-center gap-4">
                                <View className="w-12 h-12 bg-[#00FF8010] border border-[#00FF8030] items-center justify-center rounded">
                                    <Droplets size={24} color="#00FF80" />
                                </View>
                                <View>
                                    <Text className="text-white text-xl font-bold uppercase">{fuel.name}</Text>
                                    <View className="flex-row items-center gap-3 mt-1">
                                        <Text className="text-gray-600 line-through text-xs font-bold">{fuel.basePrice.toFixed(2)}</Text>
                                        <Text className="text-[#00FF80] text-lg font-black">{fuel.discountPrice.toFixed(2)} ₴</Text>
                                    </View>
                                </View>
                            </View>
                            <View className="bg-[#00FF8020] border border-[#00FF8050] px-3 py-1 rounded">
                                <Text className="text-[#00FF80] text-[10px] font-bold">
                                    -{(fuel.basePrice - fuel.discountPrice).toFixed(2)} ₴/L
                                </Text>
                            </View>
                        </View>
                    </Pressable>
                ))
            )}
            <View className="py-8 items-center">
                <Text className="text-[10px] text-gray-600 font-bold uppercase tracking-widest">[ BULK DISCOUNT RATES ACTIVE ]</Text>
            </View>
        </PageLayout>
    );
}
