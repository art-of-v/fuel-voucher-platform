import { useState } from "react";
import { View, Text, Pressable, TextInput, ScrollView } from "react-native";
import { useRouter } from "expo-router";
import { ChevronLeft, Minus, Plus, Trash2, Tag, Zap, ShoppingCart, X, Check } from "lucide-react-native";
import { useStore } from "../src/lib/store";
import { useI18n } from "../src/lib/i18n";
import { PageLayout } from "../src/components/page-layout";

export default function BasketScreen() {
    const router = useRouter();
    const { t } = useI18n();
    const {
        cart,
        updateQuantity,
        removeFromCart,
        clearCart,
        promocode,
        discount,
        applyPromocode,
        clearPromocode,
        getCartTotal,
        getDiscountedTotal
    } = useStore();

    const [promoInput, setPromoInput] = useState("");
    const [promoError, setPromoError] = useState(false);

    const total = getCartTotal();
    const discountedTotal = getDiscountedTotal();
    const discountAmount = total - discountedTotal;

    const handleApplyPromo = () => {
        setPromoError(false);
        if (applyPromocode(promoInput)) {
            setPromoInput("");
        } else {
            setPromoError(true);
        }
    };

    const Header = (
        <View className="bg-black border-b-2 border-[#00FF8030] p-6 pt-12 flex-row items-center gap-4">
            <Pressable
                onPress={() => router.push("/")}
                className="p-2 border border-white/20 bg-black/50 rounded"
            >
                <ChevronLeft size={24} color="#FFF" />
            </Pressable>
            <View className="flex-1">
                <View className="flex-row items-center gap-2">
                    <ShoppingCart size={20} color="#00FF80" />
                    <Text className="font-bold text-xl text-white uppercase tracking-wider">{t('basket.title')}</Text>
                </View>
                <Text className="text-[10px] text-gray-400 font-bold uppercase tracking-widest">{cart.length} {t('basket.cards')}</Text>
            </View>
            <Pressable
                onPress={() => clearCart()}
            >
                <Text className="text-red-500 text-[10px] font-bold uppercase tracking-widest">{t('basket.remove')}</Text>
            </Pressable>
        </View>
    );

    const fixedFooter = cart.length > 0 ? (
        <View className="p-4 bg-black/95">
            <View className="space-y-4 mb-4">
                <View className="flex-row items-center gap-2 mb-2">
                    <Tag size={16} color="#00FF80" />
                    <Text className="text-[10px] text-gray-400 font-bold uppercase tracking-widest">{t('basket.promocode')}</Text>
                </View>

                {promocode ? (
                    <View className="flex-row items-center justify-between bg-[#00FF8010] border border-[#00FF8030] p-3 rounded">
                        <View className="flex-row items-center gap-2">
                            <Check size={20} color="#00FF80" />
                            <Text className="font-bold text-[#00FF80]">{promocode}</Text>
                            <Text className="text-gray-400 text-xs">(-{discount}%)</Text>
                        </View>
                        <Pressable onPress={clearPromocode}>
                            <X size={20} color="#EF4444" />
                        </Pressable>
                    </View>
                ) : (
                    <View className="flex-row gap-2">
                        <TextInput
                            value={promoInput}
                            onChangeText={(text) => {
                                setPromoInput(text);
                                setPromoError(false);
                            }}
                            placeholder={t('basket.enterCode')}
                            placeholderTextColor="#666"
                            className={`flex-1 bg-black/50 border ${promoError ? 'border-red-500' : 'border-white/20'} px-4 py-3 text-white font-bold uppercase tracking-wider rounded`}
                        />
                        <Pressable
                            onPress={handleApplyPromo}
                            disabled={!promoInput}
                            className="bg-[#00FF8020] border border-[#00FF8050] px-4 items-center justify-center rounded"
                        >
                            <Text className="text-[#00FF80] font-bold uppercase">{t('basket.apply')}</Text>
                        </Pressable>
                    </View>
                )}
            </View>

            <View className="space-y-2 border-t border-white/10 pt-4 mb-4">
                <View className="flex-row justify-between">
                    <Text className="text-gray-400 font-bold text-xs">{t('basket.subtotal')}</Text>
                    <Text className="text-gray-400 font-bold text-xs">{total} ₴</Text>
                </View>

                {discount > 0 && (
                    <View className="flex-row justify-between">
                        <Text className="text-[#00FF80] font-bold text-xs">{t('basket.discount')} ({discount}%)</Text>
                        <Text className="text-[#00FF80] font-bold text-xs">-{discountAmount} ₴</Text>
                    </View>
                )}

                <View className="flex-row items-end justify-between pt-2">
                    <Text className="font-bold text-white text-lg uppercase">{t('basket.totalToPay')}</Text>
                    <Text className="text-3xl font-black text-white">{discountedTotal} ₴</Text>
                </View>
            </View>

            <Pressable
                onPress={() => router.push("/checkout")}
                className="w-full bg-[#00FF80] py-4 rounded flex-row items-center justify-center gap-3 active:scale-95"
            >
                <Zap size={24} color="#000" />
                <Text className="text-black font-black text-xl uppercase tracking-widest">{t('basket.checkout')}</Text>
            </Pressable>
        </View>
    ) : null;

    if (cart.length === 0) {
        return (
            <PageLayout header={Header}>
                <View className="flex-1 items-center justify-center p-8 py-20">
                    <ShoppingCart size={80} color="#333" />
                    <Text className="text-2xl font-black text-white uppercase mt-4 mb-2">{t('basket.empty')}</Text>
                    <Text className="text-gray-500 text-center mb-8">{t('basket.browseStations')}</Text>
                    <Pressable
                        onPress={() => router.push("/")}
                        className="bg-[#00FF80] px-8 py-4 rounded"
                    >
                        <Text className="text-black font-black text-lg uppercase tracking-widest">{t('basket.browseStations')}</Text>
                    </Pressable>
                </View>
            </PageLayout>
        );
    }

    return (
        <PageLayout
            header={Header}
            fixedFooter={fixedFooter}
            scrollClassName="p-4 pt-0"
        >
            <View className="gap-3">
                {cart.map((item) => (
                    <View
                        key={item.id}
                        className="bg-zinc-900/80 border border-white/10 p-4 rounded-lg"
                    >
                        <View className="flex-row items-start justify-between mb-4">
                            <View className="flex-1">
                                <Text className="font-bold text-white text-lg uppercase">
                                    {item.station.name} - {item.fuel.name}
                                </Text>
                                <Text className="text-[#00FF80] text-xs font-bold uppercase">{item.package.liters}L Card</Text>
                            </View>
                            <Pressable
                                onPress={() => removeFromCart(item.id)}
                                className="p-2"
                            >
                                <Trash2 size={20} color="#EF4444" />
                            </Pressable>
                        </View>

                        <View className="flex-row items-center justify-between">
                            <View className="flex-row items-center gap-4">
                                <Pressable
                                    onPress={() => updateQuantity(item.id, item.quantity - 1)}
                                    className="w-10 h-10 bg-white/5 border border-white/10 items-center justify-center rounded active:bg-red-500/20"
                                >
                                    <Minus size={20} color="#FFF" />
                                </Pressable>
                                <Text className="text-2xl font-black text-[#00FF80] w-10 text-center">{item.quantity}</Text>
                                <Pressable
                                    onPress={() => updateQuantity(item.id, item.quantity + 1)}
                                    className="w-10 h-10 bg-white/5 border border-white/10 items-center justify-center rounded active:bg-[#00FF8020]"
                                >
                                    <Plus size={20} color="#FFF" />
                                </Pressable>
                            </View>
                            <View className="items-end">
                                <Text className="text-[10px] text-gray-500 font-bold uppercase">
                                    {item.quantity} x {item.package.price} ₴
                                </Text>
                                <Text className="text-white font-bold text-xl">
                                    {item.package.price * item.quantity} ₴
                                </Text>
                            </View>
                        </View>
                    </View>
                ))}
            </View>
        </PageLayout>
    );
}
