
import { useState } from 'react';
import { View, Text, TouchableOpacity, ScrollView, ActivityIndicator } from 'react-native';
import { useRouter } from 'expo-router';
import { useStore } from '@/lib/store';
import { useI18n } from '@/lib/i18n';
import { useAuth } from '@/hooks/use-auth';
import { ChevronLeft, CreditCard, ShieldCheck, Zap, Skull, AlertTriangle, Tag } from 'lucide-react-native';
import { cn } from '@/lib/utils';
import { ProtectedRoute } from '@/components/ProtectedRoute';

export default function CheckoutScreen() {
    const router = useRouter();
    const { user, isLoading: authLoading, isAuthenticated } = useAuth();
    const {
        cart,
        promocode,
        discount,
        getCartTotal,
        getDiscountedTotal,
        clearCart
    } = useStore();
    const { t } = useI18n();
    const [isProcessing, setIsProcessing] = useState(false);

    const total = getCartTotal();
    const discountedTotal = getDiscountedTotal();
    const discountAmount = total - discountedTotal;
    const totalCards = cart.reduce((sum, item) => sum + item.quantity, 0);

    if (authLoading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
            </View>
        );
    }

    if (cart.length === 0) {
        return (
            <View className="flex-1 bg-black items-center justify-center p-6">
                <Text className="text-gray-500 font-mono mb-4">{t('checkout.emptyCart')}</Text>
                <TouchableOpacity
                    onPress={() => router.replace("/")}
                    className="bg-[#00FF80] px-6 py-3 rounded-lg"
                >
                    <Text className="text-black font-bold">{t('checkout.browseStations')}</Text>
                </TouchableOpacity>
            </View>
        );
    }

    const handlePayment = async () => {
        setIsProcessing(true);
        try {
            router.push("/mock-payment");
        } catch (error: any) {
            console.error("Payment navigation error:", error);
            setIsProcessing(false);
        }
    };

    return (
        <ProtectedRoute>
            <View className="flex-1 bg-[#050505]">
                {/* Background Foundation - MECHANICAL REPLICATION */}
                <View className="absolute top-0 left-0 right-0 h-64 bg-[#00FF80]/10" />
                <View
                    className="absolute bottom-0 right-0 w-[256px] h-[256px] bg-[#FF3232]/10 rounded-full opacity-10"
                    style={{ shadowColor: '#FF3232', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                />

                <View className="bg-black/90 p-[16px] flex-row items-center gap-4 border-b-2 border-[#00FF80]/30 z-10 sticky top-0">
                    <TouchableOpacity
                        onPress={() => router.push("/basket")}
                        className="p-2 -ml-2 border-2 border-white/20 bg-black/50 active:scale-[0.98]"
                    >
                        <ChevronLeft size={24} color="#9CA3AF" />
                    </TouchableOpacity>
                    <Text className="font-black text-xl text-white font-heading tracking-wider uppercase flex-row items-center gap-2">
                        <Skull size={20} color="#EF4444" /> {t('checkout.title')}
                    </Text>
                </View>

                <ScrollView className="flex-1 p-[24px] z-10" contentContainerStyle={{ paddingBottom: 180 }}>
                    <View className="gap-[16px]">
                        <View className="flex-row items-center gap-2 mb-1">
                            <AlertTriangle size={16} color="#EF4444" />
                            <Text className="text-xs text-[#F87171] font-mono uppercase tracking-wider">
                                {t('checkout.orderSummaryLabel')}
                            </Text>
                        </View>

                        {cart.map((item) => (
                            <View
                                key={item.id}
                                className="bg-black/60 border-2 border-white/10 p-[16px] flex-row items-center justify-between"
                            >
                                <View>
                                    <Text className="font-black text-white font-heading uppercase">
                                        {item.station.name}
                                    </Text>
                                    <View className="flex-row items-center mt-1">
                                        <Text className="text-sm text-gray-400 font-mono">
                                            {item.fuel.name} • {item.package.liters}L x {item.quantity}
                                        </Text>
                                    </View>
                                </View>
                                <Text className="font-black text-white text-[20px] font-heading">
                                    {item.package.price * item.quantity} ₴
                                </Text>
                            </View>
                        ))}

                        {/* Price breakdown block - MECHANICAL CLONE */}
                        <View className="bg-black border-2 border-[#00FF80]/30 p-[24px] space-y-4">
                            <View className="flex-row justify-between">
                                <Text className="text-gray-400 font-mono">{t('checkout.cards')} ({totalCards})</Text>
                                <Text className="text-gray-400 font-mono">{total} ₴</Text>
                            </View>

                            {discount > 0 && (
                                <View className="flex-row justify-between text-[#00FF80]">
                                    <View className="flex-row items-center gap-2">
                                        <Tag size={16} color="#00FF80" />
                                        <Text className="text-[#00FF80] font-mono">{promocode} (-{discount}%)</Text>
                                    </View>
                                    <Text className="text-[#00FF80] font-mono">-{discountAmount} ₴</Text>
                                </View>
                            )}

                            <View className="border-t-2 border-white/10 pt-4 flex-row justify-between items-end">
                                <Text className="font-black text-xl text-white font-heading uppercase">{t('checkout.total')}</Text>
                                <View className="items-end">
                                    {discount > 0 && (
                                        <Text className="text-sm text-gray-500 line-through font-mono">{total} ₴</Text>
                                    )}
                                    <Text className="text-[36px] font-black text-white font-heading text-glow-intense tracking-tighter">
                                        {discountedTotal} ₴
                                    </Text>
                                </View>
                            </View>
                        </View>

                        <View className="flex-row items-center gap-2 justify-center mt-8">
                            <ShieldCheck size={16} color="#00FF80" />
                            <Text className="text-[10px] text-[#00FF80] uppercase tracking-[0.2em] font-mono">
                                {t('checkout.encryptedTransaction')}
                            </Text>
                        </View>
                    </View>
                </ScrollView>

                {/* Fixed bottom footer - MECHANICAL CLONE */}
                <View className="absolute bottom-0 left-0 right-0 p-[24px] bg-black border-t-2 border-[#00FF80]/30 z-50">
                    <TouchableOpacity
                        onPress={handlePayment}
                        disabled={isProcessing}
                        className="w-full bg-[#00FF80] py-[20px] flex-row items-center justify-center gap-3 shadow-[0_0_100px_rgba(0,255,128,1)] border-t border-white/20 animate-pulse active:scale-[0.98]"
                    >
                        {isProcessing ? (
                            <View className="flex-row items-center gap-3">
                                <Zap size={24} color="black" className="animate-spin" />
                                <Text className="text-black font-black text-[20px] font-heading uppercase">PROCESSING...</Text>
                            </View>
                        ) : (
                            <>
                                <CreditCard size={24} color="black" />
                                <Text className="text-black font-black text-[20px] font-heading uppercase tracking-wider">
                                    {t('checkout.pay')} {discountedTotal} ₴
                                </Text>
                            </>
                        )}
                    </TouchableOpacity>
                </View>
            </View>
        </ProtectedRoute>
    );
}
