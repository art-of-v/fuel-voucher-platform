import { useState, useEffect } from "react";
import { View, Text, Pressable, ScrollView, ActivityIndicator } from "react-native";
import { useRouter } from "expo-router";
import { ChevronLeft, Zap, Flame, Minus, Plus, ShoppingCart, Check } from "lucide-react-native";
import { useStore } from "../src/lib/store";
import { useI18n } from "../src/lib/i18n";
import { getPackages, FuelPackage } from "../src/lib/api";
import { normalizeFuelName } from "../src/lib/utils";
import { PageLayout } from "../src/components/page-layout";

export default function PackagesScreen() {
    const router = useRouter();
    const { t } = useI18n();
    const { selectedStation, selectedFuel, addToCart, getCartItemCount } = useStore();
    const [quantities, setQuantities] = useState<Record<string, number>>({});
    const [addedItems, setAddedItems] = useState<Set<string>>(new Set());
    const [packages, setPackages] = useState<FuelPackage[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        if (selectedStation && selectedFuel) {
            loadData();
        }
    }, [selectedStation, selectedFuel]);

    const loadData = async () => {
        try {
            setLoading(true);
            const allPkgs = await getPackages();

            const filtered = allPkgs.filter(pkg => {
                if (pkg.stationId !== selectedStation!.id) return false;
                if (normalizeFuelName(pkg.fuelName) !== normalizeFuelName(selectedFuel!.name)) return false;
                return true;
            });

            filtered.sort((a, b) => a.liters - b.liters);
            setPackages(filtered);
        } catch (e) {
            console.error("Failed to load packages", e);
        } finally {
            setLoading(false);
        }
    };

    if (!selectedStation || !selectedFuel) {
        return (
            <View className="flex-1 bg-black items-center justify-center p-6">
                <Text className="text-white font-bold uppercase text-center">Missing selection data. Please start from home.</Text>
            </View>
        );
    }

    const cartCount = getCartItemCount();
    const getQuantity = (pkgId: string) => quantities[pkgId] || 1;

    const updateQuantity = (pkgId: string, delta: number) => {
        setQuantities((prev) => ({
            ...prev,
            [pkgId]: Math.max(1, Math.min(99, (prev[pkgId] || 1) + delta)),
        }));
    };

    const handleAddToCart = (pkg: FuelPackage) => {
        const qty = getQuantity(pkg.id);
        addToCart({
            package: pkg,
            station: selectedStation,
            fuel: selectedFuel,
            quantity: qty,
        });

        setAddedItems((prev) => {
            const next = new Set(prev);
            next.add(pkg.id);
            return next;
        });

        setTimeout(() => {
            setAddedItems((prev) => {
                const next = new Set(prev);
                next.delete(pkg.id);
                return next;
            });
        }, 2000);
    };

    const Header = (
        <View className="bg-black border-b-2 border-[#00FF8030] p-6 pt-12">
            <View className="flex-row items-center gap-4">
                <Pressable
                    onPress={() => router.back()}
                    className="p-2 border border-white/20 bg-black/50 rounded"
                >
                    <ChevronLeft size={24} color="#FFF" />
                </Pressable>
                <View className="flex-1">
                    <View className="flex-row items-center gap-2">
                        <Zap size={20} color="#00FF80" />
                        <Text className="font-bold text-2xl text-white uppercase tracking-tight">{selectedFuel.name}</Text>
                    </View>
                    <Text className="text-[10px] text-red-400 font-bold tracking-widest uppercase mt-1">
                        {t('packages.selectCards')}
                    </Text>
                </View>

                <Pressable
                    onPress={() => router.push("/basket")}
                    className="relative p-3 bg-[#00FF8020] border border-[#00FF8050] rounded"
                >
                    <ShoppingCart size={24} color="#00FF80" />
                    {cartCount > 0 && (
                        <View className="absolute -top-2 -right-2 w-6 h-6 bg-red-500 rounded-full items-center justify-center">
                            <Text className="text-white text-[10px] font-bold">{cartCount}</Text>
                        </View>
                    )}
                </Pressable>
            </View>
        </View>
    );

    const fixedFooter = cartCount > 0 ? (
        <View className="p-4 bg-black/80">
            <Pressable
                onPress={() => router.push("/basket")}
                className="w-full bg-[#00FF80] flex-row items-center justify-center gap-3 py-4 rounded active:scale-95"
            >
                <ShoppingCart size={24} color="#000" />
                <Text className="text-black font-black text-lg uppercase tracking-widest">
                    {t('packages.viewCart')} ({cartCount})
                </Text>
            </Pressable>
        </View>
    ) : null;

    return (
        <PageLayout
            header={Header}
            fixedFooter={fixedFooter}
            scrollClassName="p-4 space-y-4"
        >
            {loading ? (
                <ActivityIndicator size="large" color="#00FF80" className="py-20" />
            ) : packages.length === 0 ? (
                <View className="py-20 items-center">
                    <Text className="text-gray-500 font-bold uppercase tracking-widest">NO PACKAGES AVAILABLE</Text>
                </View>
            ) : (
                packages.map((pkg) => {
                    const savings = pkg.originalPrice - pkg.price;
                    const qty = getQuantity(pkg.id);
                    const totalPrice = pkg.price * qty;
                    const totalOriginal = pkg.originalPrice * qty;
                    const totalSavings = totalOriginal - totalPrice;
                    const isAdded = addedItems.has(pkg.id);

                    return (
                        <View
                            key={pkg.id}
                            className="bg-zinc-900/80 border border-white/10 rounded-lg overflow-hidden mb-4"
                        >
                            <View className="flex-row items-center justify-between p-4 border-b border-white/10">
                                <View className="flex-row items-center gap-4">
                                    <View className="w-16 h-16 bg-[#00FF8010] border border-[#00FF8030] items-center justify-center rounded">
                                        <Text className="text-3xl font-black text-white">{pkg.liters}</Text>
                                        <Text className="text-[10px] text-[#00FF80] font-bold">L</Text>
                                    </View>
                                    <View>
                                        <Text className="text-xl font-bold text-white">{pkg.price} ₴</Text>
                                        <Text className="text-xs text-gray-500 line-through">{pkg.originalPrice} ₴</Text>
                                    </View>
                                </View>
                                <View className="bg-[#00FF80] px-3 py-1 rounded-sm flex-row items-center gap-1">
                                    <Flame size={14} color="#000" />
                                    <Text className="text-black font-black text-xs">-{savings} ₴</Text>
                                </View>
                            </View>

                            <View className="p-4 space-y-4">
                                <View className="flex-row items-center justify-between">
                                    <Text className="text-gray-400 font-bold text-xs uppercase tracking-widest">QUANTITY</Text>
                                    <View className="flex-row items-center gap-4">
                                        <Pressable
                                            onPress={() => updateQuantity(pkg.id, -1)}
                                            className="w-10 h-10 bg-white/5 border border-white/10 items-center justify-center rounded active:bg-red-500/20"
                                        >
                                            <Minus size={20} color="#FFF" />
                                        </Pressable>
                                        <Text className="text-2xl font-black text-[#00FF80] w-10 text-center">{qty}</Text>
                                        <Pressable
                                            onPress={() => updateQuantity(pkg.id, 1)}
                                            className="w-10 h-10 bg-white/5 border border-white/10 items-center justify-center rounded active:bg-[#00FF8020]"
                                        >
                                            <Plus size={20} color="#FFF" />
                                        </Pressable>
                                    </View>
                                </View>

                                <View className="bg-white/5 p-4 rounded border border-white/5">
                                    <View className="flex-row justify-between mb-2">
                                        <Text className="text-gray-400 text-xs">{qty}x {pkg.liters}L cards</Text>
                                        <Text className="text-gray-600 line-through text-xs">{totalOriginal} ₴</Text>
                                    </View>
                                    <View className="flex-row justify-between items-end">
                                        <View>
                                            <Text className="text-[10px] text-gray-500 uppercase font-bold">SAVINGS</Text>
                                            <Text className="text-[#00FF80] font-bold text-lg">{totalSavings} ₴</Text>
                                        </View>
                                        <View className="items-end">
                                            <Text className="text-[10px] text-gray-500 uppercase font-bold">TOTAL</Text>
                                            <Text className="text-white font-black text-2xl">{totalPrice} ₴</Text>
                                        </View>
                                    </View>
                                </View>

                                <Pressable
                                    onPress={() => handleAddToCart(pkg)}
                                    disabled={isAdded}
                                    className={`w-full py-4 rounded flex-row items-center justify-center gap-3 ${isAdded ? 'bg-green-600' : 'bg-[#00FF80]'
                                        } active:scale-95`}
                                >
                                    {isAdded ? (
                                        <>
                                            <Check size={24} color="#FFF" />
                                            <Text className="text-white font-bold uppercase tracking-widest">ADDED</Text>
                                        </>
                                    ) : (
                                        <>
                                            <ShoppingCart size={24} color="#000" />
                                            <Text className="text-black font-bold uppercase tracking-widest">ADD TO CART</Text>
                                        </>
                                    )}
                                </Pressable>
                            </View>
                        </View>
                    );
                })
            )}
        </PageLayout>
    );
}
