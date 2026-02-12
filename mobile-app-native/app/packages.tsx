
import { useState, useEffect } from 'react';
import { View, Text, TouchableOpacity, ScrollView, ActivityIndicator, Alert, Image } from 'react-native';
import { useRouter } from 'expo-router';
import { useStore } from '@/lib/store';
import { useI18n } from '@/lib/i18n';
import { ChevronLeft, Zap, Flame, Skull, Minus, Plus, ShoppingCart, Check } from 'lucide-react-native';
import { getPackages, getInventory, FuelPackage } from '@/lib/api';
import { normalizeFuelName, cn } from '@/lib/utils';
import { ProtectedRoute } from '@/components/ProtectedRoute';
import Layout from '@/components/layout';

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
            const [allPkgs, inventory] = await Promise.all([getPackages(), getInventory()]);

            const filtered = allPkgs.filter(pkg => {
                if (pkg.stationId !== selectedStation!.id) return false;
                if (normalizeFuelName(pkg.fuelName) !== normalizeFuelName(selectedFuel!.name)) return false;
                return true;
            });

            filtered.sort((a, b) => a.liters - b.liters);
            setPackages(filtered);
        } catch (e) {
            console.error("Failed to load packages", e);
            Alert.alert("Error", "Failed to load packages");
        } finally {
            setLoading(false);
        }
    };

    if (!selectedStation || !selectedFuel) {
        return (
            <View className="flex-1 bg-black items-center justify-center p-6">
                <Text className="text-white text-center">Missing selection data. Please start from home.</Text>
                <TouchableOpacity
                    onPress={() => router.replace("/")}
                    className="mt-4 bg-[#00FF80] px-6 py-3 rounded-lg"
                >
                    <Text className="text-black font-bold">Go Back</Text>
                </TouchableOpacity>
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

        setAddedItems((prev) => new Set(prev).add(pkg.id));

        setTimeout(() => {
            setAddedItems((prev) => {
                const next = new Set(prev);
                next.delete(pkg.id);
                return next;
            });
        }, 2000);
    };

    if (loading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
            </View>
        );
    }

    return (
        <ProtectedRoute>
            <Layout>
                <View className="flex-1 bg-[#050505]">
                    {/* Background glow - MECHANICAL REPLICATION */}
                    <View
                        className="absolute top-[25%] right-0 w-[256px] h-[256px] bg-[#00FF80]/10 rounded-full opacity-20"
                        style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 120 }}
                    />

                    {/* Header Section - MECHANICAL COMPUTED CLONE */}
                    <View className="bg-black/90 p-[24px] pb-[16px] border-b-2 border-[#00FF80]/30 z-10">
                        <View className="flex-row items-center gap-4">
                            <TouchableOpacity
                                onPress={() => router.push(`/station/${selectedStation.id}`)}
                                className="p-2 -ml-2 border-2 border-white/20 bg-black/50 active:scale-[0.98]"
                            >
                                <ChevronLeft size={24} color="white" />
                            </TouchableOpacity>
                            <View className="flex-1">
                                <Text className="font-black text-3xl text-white font-heading uppercase tracking-tight leading-none text-glow" style={{ textShadowRadius: 10 }}>
                                    <Zap size={24} color="#00FF80" /> {selectedFuel.name}
                                </Text>
                                <Text className="text-xs text-[#FF3232] font-mono tracking-[0.2em] uppercase mt-1 flex-row items-center font-bold">
                                    <Skull size={12} color="#FF3232" /> {t('packages.selectCards')}
                                </Text>
                            </View>

                            <TouchableOpacity
                                onPress={() => router.push("/basket")}
                                className="relative p-3 bg-[#00FF80]/20 border-2 border-[#00FF80]/50 active:scale-[0.98]"
                            >
                                <ShoppingCart size={24} color="#00FF80" />
                                {cartCount > 0 && (
                                    <View className="absolute -top-2 -right-2 w-6 h-6 bg-[#FF3232] rounded-full flex items-center justify-center shadow-[0_0_10px_rgba(255,50,50,0.5)]">
                                        <Text className="text-white text-[10px] font-black">{cartCount}</Text>
                                    </View>
                                )}
                            </TouchableOpacity>
                        </View>
                    </View>

                    <ScrollView
                        className="flex-1 p-[16px] z-10"
                        contentContainerStyle={{ paddingBottom: 90 }}
                        showsVerticalScrollIndicator={false}
                    >
                        <View className="gap-[16px]">
                            {packages.length === 0 ? (
                                <View className="items-center justify-center py-[40px] border-2 border-dashed border-white/10 bg-black/50">
                                    <Text className="text-gray-500 font-mono text-sm uppercase tracking-widest">NO PACKAGES AVAILABLE</Text>
                                </View>
                            ) : packages.map((pkg) => {
                                const savings = pkg.originalPrice - pkg.price;
                                const qty = getQuantity(pkg.id);
                                const totalPrice = pkg.price * qty;
                                const totalOriginal = pkg.originalPrice * qty;
                                const totalSavings = totalOriginal - totalPrice;
                                const isAdded = addedItems.has(pkg.id);

                                return (
                                    <View
                                        key={pkg.id}
                                        className="bg-black/80 border-2 border-white/10 overflow-hidden"
                                    >
                                        {/* Header with savings */}
                                        <View className="flex-row items-center justify-between p-[16px] border-b-2 border-white/10">
                                            <View className="flex-row items-center gap-[16px]">
                                                <View className="w-[80px] h-[80px] bg-[#00FF80]/10 border-2 border-[#00FF80]/30 items-center justify-center">
                                                    <Text className="text-[36px] font-black text-white font-heading">{pkg.liters}</Text>
                                                    <Text className="text-xs text-[#00FF80] font-mono uppercase">{t('packages.liters')}</Text>
                                                </View>
                                                <View>
                                                    <Text className="text-[24px] font-black text-white font-heading">{pkg.price} ₴</Text>
                                                    <Text className="text-sm text-gray-500 line-through font-mono">{pkg.originalPrice} ₴</Text>
                                                </View>
                                            </View>
                                            <View className="bg-[#00FF80] px-[16px] py-[8px] flex-row items-center gap-2 shadow-[0_0_20px_rgba(0,255,128,0.5)]">
                                                <Flame size={16} color="black" />
                                                <Text className="text-black font-black text-sm font-heading">-{savings} ₴</Text>
                                            </View>
                                        </View>

                                        <View className="p-[16px] gap-[16px]">
                                            <View className="flex-row items-center justify-between">
                                                <Text className="text-gray-400 font-mono text-sm uppercase tracking-wider">{t('packages.quantity')}</Text>
                                                <View className="flex-row items-center gap-[12px]">
                                                    <TouchableOpacity
                                                        onPress={() => updateQuantity(pkg.id, -1)}
                                                        className="w-[40px] h-[40px] bg-white/10 border-2 border-white/20 items-center justify-center active:scale-[0.98]"
                                                    >
                                                        <Minus size={20} color="white" />
                                                    </TouchableOpacity>
                                                    <Text className="text-[30px] font-black text-[#00FF80] font-mono w-[64px] text-center text-glow-intense">{qty}</Text>
                                                    <TouchableOpacity
                                                        onPress={() => updateQuantity(pkg.id, 1)}
                                                        className="w-[40px] h-[40px] bg-white/10 border-2 border-white/20 items-center justify-center active:scale-[0.98]"
                                                    >
                                                        <Plus size={20} color="white" />
                                                    </TouchableOpacity>
                                                </View>
                                            </View>

                                            {/* Price summary - MECHANICAL CLONE */}
                                            <View className="bg-black/50 border-2 border-white/10 p-[16px]">
                                                <View className="flex-row justify-between mb-2">
                                                    <Text className="text-xs text-gray-400 font-mono uppercase tracking-wider font-bold">{qty}x {pkg.liters}L {t('packages.cards')}</Text>
                                                    <Text className="text-xs text-gray-500 line-through font-mono">{totalOriginal} ₴</Text>
                                                </View>
                                                <View className="flex-row justify-between items-end">
                                                    <View>
                                                        <Text className="text-[10px] text-gray-500 uppercase font-mono tracking-wider font-bold">{t('packages.totalSavings')}</Text>
                                                        <Text className="text-[#00FF80] font-black text-[18px]">{totalSavings} ₴</Text>
                                                    </View>
                                                    <View className="items-end">
                                                        <Text className="text-[10px] text-gray-500 uppercase font-mono tracking-wider font-bold">{t('packages.pay')}</Text>
                                                        <Text className="text-white font-black text-[30px] font-heading">{totalPrice} ₴</Text>
                                                    </View>
                                                </View>
                                            </View>

                                            {/* Add to cart button */}
                                            <TouchableOpacity
                                                onPress={() => handleAddToCart(pkg)}
                                                disabled={isAdded}
                                                className={cn("w-full py-[16px] flex-row items-center justify-center gap-3 active:scale-[0.98] font-heading tracking-wider uppercase",
                                                    isAdded ? 'bg-[#16a34a] text-white' : 'bg-[#00FF80] text-black shadow-[0_0_80px_rgba(0,255,128,0.8)] border-2 border-[#00FF80]/50'
                                                )}
                                            >
                                                {isAdded ? (
                                                    <>
                                                        <Check size={24} color="white" />
                                                        <Text className="text-white font-black text-lg font-heading uppercase">
                                                            {t('packages.addedToCart')}
                                                        </Text>
                                                    </>
                                                ) : (
                                                    <>
                                                        <ShoppingCart size={24} color="black" />
                                                        <Text className="text-black font-black text-lg font-heading uppercase">
                                                            {t('packages.addToCart')}
                                                        </Text>
                                                    </>
                                                )}
                                            </TouchableOpacity>
                                        </View>
                                    </View>
                                );
                            })}
                        </View>

                        <View className="mt-[48px] items-center pb-[40px]">
                            <Text className="text-[10px] text-gray-600 font-mono tracking-[0.2em] uppercase">
                                // SECURED TRANSACTION PROTOCOL
                            </Text>
                        </View>
                    </ScrollView>

                    {cartCount > 0 && (
                        <>
                            {/* Gradient Background for Button Area */}
                            <View className="absolute bottom-0 left-0 right-0 h-[120px] z-40 pointer-events-none">
                                <View className="absolute inset-0 bg-gradient-to-t from-black via-black/80 to-transparent" style={{ backgroundImage: 'linear-gradient(to top, #050505 20%, transparent)' } as any} />
                            </View>

                            <View className="absolute bottom-[74px] left-4 right-4 z-50">
                                <TouchableOpacity
                                    onPress={() => router.push("/basket")}
                                    className="w-full bg-[#00FF80] py-[16px] flex-row items-center justify-center gap-3 shadow-[0_0_100px_rgba(0,255,128,1)] border-2 border-[#00FF80] font-heading tracking-wider uppercase active:scale-[0.98]"
                                >
                                    <ShoppingCart size={24} color="black" />
                                    <Text className="text-black font-black text-lg uppercase">
                                        {t('packages.viewCart')} ({cartCount})
                                    </Text>
                                </TouchableOpacity>
                            </View>
                        </>
                    )}
                </View>
            </Layout>
        </ProtectedRoute>
    );
}
