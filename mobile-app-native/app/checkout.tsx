import { useState } from "react";
import { View, Text, Pressable, ScrollView, ActivityIndicator } from "react-native";
import { useRouter } from "expo-router";
import { ChevronLeft, CreditCard, ShieldCheck, Zap, Skull, AlertTriangle, Tag } from "lucide-react-native";
import { useStore } from "../src/lib/store";
import { useAuth } from "../src/hooks/useAuth";
import { useI18n } from "../src/lib/i18n";
import { PageLayout } from "../src/components/page-layout";

export default function CheckoutScreen() {
    const router = useRouter();
    const { user, isLoading: authLoading, isAuthenticated } = useAuth();
    const {
        cart,
        promocode,
        discount,
        getCartTotal,
        getDiscountedTotal
    } = useStore();
    const { t } = useI18n();
    const [isProcessing, setIsProcessing] = useState(false);

    const total = getCartTotal();
    const discountedTotal = getDiscountedTotal();
    const discountAmount = total - discountedTotal;
    const totalCards = cart.reduce((sum, item) => sum + item.quantity, 0);

    const Header = (
        <View className="bg-black border-b-2 border-[#00FF8030] p-6 pt-12 flex-row items-center gap-4">
            <Pressable
                onPress={() => router.back()}
                className="p-2 border border-white/20 bg-black/50 rounded"
            >
                <ChevronLeft size={24} color="#FFF" />
            </Pressable>
            <View className="flex-row items-center gap-2">
                <Skull size={20} color="#EF4444" />
                <Text className="font-bold text-xl text-white uppercase tracking-wider">{t('checkout.title')}</Text>
            </View>
        </View>
    );

    const fixedFooter = cart.length > 0 ? (
        <View className="p-4 bg-black/95 border-t-2 border-[#00FF8030]">
            <Pressable
                onPress={async () => {
                    setIsProcessing(true);
                    router.push("/payment");
                }}
                disabled={isProcessing}
                className="w-full bg-[#00FF80] py-5 rounded flex-row items-center justify-center gap-3 active:scale-95 disabled:opacity-50"
            >
                {isProcessing ? (
                    <View className="flex-row items-center gap-3">
                        <ActivityIndicator color="#000" />
                        <Text className="text-black font-black text-xl uppercase tracking-widest">PROCESSING...</Text>
                    </View>
                ) : (
                    <>
                        <CreditCard size={24} color="#000" />
                        <Text className="text-black font-black text-xl uppercase tracking-widest">
                            {t('checkout.pay')} {discountedTotal} ₴
                        </Text>
                    </>
                )}
            </Pressable>
        </View>
    ) : null;

    if (authLoading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
                <Text className="text-[#00FF80] font-bold mt-4 uppercase">LOADING ACCESS...</Text>
            </View>
        );
    }

    if (!isAuthenticated) {
        return (
            <PageLayout>
                <View className="flex-1 items-center justify-center p-8 py-20">
                    <Skull size={80} color="#EF4444" className="mb-4" />
                    <Text className="text-2xl font-black text-white uppercase mb-2">ACCESS DENIED</Text>
                    <Text className="text-gray-500 text-center mb-8 uppercase font-bold text-xs tracking-widest">Sign in to complete your purchase</Text>
                    <Pressable
                        onPress={() => router.push("/profile")}
                        className="bg-[#00FF80] px-8 py-4 rounded"
                    >
                        <Text className="text-black font-black text-lg uppercase tracking-widest">SIGN IN</Text>
                    </Pressable>
                </View>
            </PageLayout>
        );
    }

    if (cart.length === 0) {
        return (
            <PageLayout header={Header}>
                <View className="flex-1 items-center justify-center p-8 py-20">
                    <Text className="text-gray-500 font-bold uppercase mb-4">{t('checkout.emptyCart')}</Text>
                    <Pressable
                        onPress={() => router.push("/")}
                        className="bg-[#00FF80] px-8 py-4 rounded"
                    >
                        <Text className="text-black font-black text-lg uppercase tracking-widest">{t('checkout.browseStations')}</Text>
                    </Pressable>
                </View>
            </PageLayout>
        );
    }

    return (
        <PageLayout
            header={Header}
            fixedFooter={fixedFooter}
            scrollClassName="p-6 pt-0"
        >
            <View className="mb-6">
                <View className="flex-row items-center gap-2 mb-4">
                    <AlertTriangle size={16} color="#EF4444" />
                    <Text className="text-[10px] text-red-500 font-black uppercase tracking-widest">{t('checkout.orderSummaryLabel')}</Text>
                </View>

                <View className="gap-2">
                    {cart.map((item) => (
                        <View
                            key={item.id}
                            className="bg-zinc-900/60 border border-white/10 p-4 rounded flex-row items-center justify-between"
                        >
                            <View>
                                <Text className="font-bold text-white uppercase">{item.station.name}</Text>
                                <Text className="text-xs text-gray-500">
                                    {item.fuel.name} • {item.package.liters}L x {item.quantity}
                                </Text>
                            </View>
                            <Text className="font-bold text-white text-lg">{item.package.price * item.quantity} ₴</Text>
                        </View>
                    ))}
                </View>
            </View>

            <View className="bg-black border border-[#00FF8030] p-6 rounded-lg space-y-4">
                <View className="flex-row justify-between">
                    <Text className="text-gray-400 font-bold text-xs">{t('checkout.cards')} ({totalCards})</Text>
                    <Text className="text-gray-400 font-bold text-xs">{total} ₴</Text>
                </View>

                {discount > 0 && (
                    <View className="flex-row justify-between">
                        <View className="flex-row items-center gap-2">
                            <Tag size={14} color="#00FF80" />
                            <Text className="text-[#00FF80] font-bold text-xs uppercase">{promocode} (-{discount}%)</Text>
                        </View>
                        <Text className="text-[#00FF80] font-bold text-xs">-{discountAmount} ₴</Text>
                    </View>
                )}

                <View className="border-t border-white/10 pt-4 flex-row justify-between items-end">
                    <Text className="font-black text-xl text-white uppercase">{t('checkout.total')}</Text>
                    <View className="items-end">
                        {discount > 0 && (
                            <Text className="text-xs text-gray-600 line-through mb-1">{total} ₴</Text>
                        )}
                        <Text className="text-4xl font-black text-white">{discountedTotal} ₴</Text>
                    </View>
                </View>
            </View>

            <View className="flex-row items-center gap-2 justify-center py-8">
                <ShieldCheck size={16} color="#00FF80" />
                <Text className="text-[10px] text-[#00FF80] font-bold uppercase tracking-widest">{t('checkout.encryptedTransaction')}</Text>
            </View>
        </PageLayout>
    );
}
