import { useState, useEffect } from "react";
import { View, Text, Pressable, ActivityIndicator } from "react-native";
import { useRouter } from "expo-router";
import { ChevronLeft, CreditCard, Lock, Zap } from "lucide-react-native";
import { useStore } from "../src/lib/store";
import { createPurchase, completePurchase } from "../src/lib/api";
import { useI18n } from "../src/lib/i18n";
import { PageLayout } from "../src/components/page-layout";

export default function PaymentPage() {
    const router = useRouter();
    const { cart, getDiscountedTotal, clearCart } = useStore();
    const [isProcessing, setIsProcessing] = useState(false);
    const { t } = useI18n();

    const total = getDiscountedTotal();

    useEffect(() => {
        if (cart.length === 0) {
            router.push("/");
        }
    }, [cart]);

    const handleMockPayment = async () => {
        setIsProcessing(true);
        try {
            // Mock the payment flow
            // 1. Create purchase
            const purchaseRes = await createPurchase(cart, total);

            // 2. Simulate delay
            await new Promise(resolve => setTimeout(resolve, 2000));

            // 3. Complete purchase
            await completePurchase(purchaseRes.purchaseId);

            // 4. Clear cart and redirect
            clearCart();
            router.push("/my-codes");
        } catch (e) {
            console.error("Payment error", e);
        } finally {
            setIsProcessing(false);
        }
    };

    const Header = (
        <View className="bg-black border-b-2 border-[#00FF8030] p-6 pt-12 flex-row items-center gap-4">
            <Pressable
                onPress={() => router.back()}
                className="p-2 border border-white/20 bg-black/50 rounded"
            >
                <ChevronLeft size={24} color="#FFF" />
            </Pressable>
            <View className="flex-row items-center gap-2">
                <CreditCard size={20} color="#00FF80" />
                <Text className="font-bold text-xl text-white uppercase tracking-wider">{t('payment.title')}</Text>
            </View>
        </View>
    );

    return (
        <PageLayout header={Header} scrollClassName="p-6">
            <View className="bg-zinc-900 border border-[#00FF8030] p-6 rounded-lg mb-6">
                <Text className="text-white font-bold text-lg mb-4 uppercase tracking-wider">{t('payment.orderSummary')}</Text>
                {cart.map((item) => (
                    <View key={item.id} className="flex-row justify-between mb-2">
                        <Text className="text-gray-400 text-xs">
                            {item.station.name} - {item.fuel.name} {item.package.liters}L x{item.quantity}
                        </Text>
                        <Text className="text-white font-bold text-xs">
                            {item.package.price * item.quantity} ₴
                        </Text>
                    </View>
                ))}
                <View className="border-t border-white/10 mt-4 pt-4 flex-row justify-between items-end">
                    <Text className="text-white font-black text-xl uppercase tracking-tighter">{t('checkout.total')}</Text>
                    <Text className="text-[#00FF80] font-black text-2xl">{total} ₴</Text>
                </View>
            </View>

            <View className="items-center gap-6">
                <View className="flex-row items-center gap-2">
                    <Lock size={16} color="#00FF80" />
                    <Text className="text-[10px] text-gray-400 uppercase font-bold tracking-widest">Secured by Lemberg Encryption</Text>
                </View>

                <Pressable
                    onPress={handleMockPayment}
                    disabled={isProcessing}
                    className="w-full bg-[#00FF80] py-5 rounded flex-row items-center justify-center gap-3 active:scale-95 disabled:opacity-50"
                >
                    {isProcessing ? (
                        <>
                            <ActivityIndicator color="#000" />
                            <Text className="text-black font-black text-xl uppercase tracking-widest">{t('payment.processing')}</Text>
                        </>
                    ) : (
                        <>
                            <Zap size={24} color="#000" />
                            <Text className="text-black font-black text-xl uppercase tracking-widest">
                                COMPLETE PAYMENT
                            </Text>
                        </>
                    )}
                </Pressable>

                <Text className="text-[10px] text-gray-700 text-center uppercase font-bold tracking-widest">
                    This is a simulation. No real money will be charged.
                </Text>
            </View>
        </PageLayout>
    );
}
