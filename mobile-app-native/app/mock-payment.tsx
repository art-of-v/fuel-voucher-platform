
import { useState, useEffect } from 'react';
import { View, Text, TouchableOpacity, ScrollView, ActivityIndicator, StyleSheet, Animated, Image } from 'react-native';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { useStore } from '@/lib/store';
import { useI18n } from '@/lib/i18n';
import { Zap, CreditCard, ShieldCheck } from 'lucide-react-native';
import { completePurchase, createPurchase } from '@/lib/api';
import { ProtectedRoute } from '@/components/ProtectedRoute';

export default function MockPaymentScreen() {
    const { purchase_ids, purchase_id } = useLocalSearchParams<{ purchase_ids?: string, purchase_id?: string }>();
    const router = useRouter();
    const clearCart = useStore((state) => state.clearCart);
    const { cart, getDiscountedTotal } = useStore();
    const { t } = useI18n();

    const [step, setStep] = useState<'methods' | 'processing' | 'error'>('methods');
    const [selectedMethod, setSelectedMethod] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    // Animation for processing bar
    const [progress] = useState(new Animated.Value(0));

    useEffect(() => {
        if (step === 'processing') {
            Animated.loop(
                Animated.sequence([
                    Animated.timing(progress, {
                        toValue: 1,
                        duration: 2000,
                        useNativeDriver: false,
                    })
                ])
            ).start();
        }
    }, [step]);

    const handleProcessPayment = async () => {
        if (!selectedMethod) return;
        setStep('processing');

        const delay = selectedMethod === 'card' ? 2000 : 1500;
        await new Promise(resolve => setTimeout(resolve, delay));

        try {
            let idsToProcess: number[] = [];

            // If we came from checkout, we might need to create the purchase first or we already have IDs
            if (purchase_ids) {
                idsToProcess = purchase_ids.split(',').map(id => parseInt(id, 10)).filter(n => !isNaN(n));
            } else if (purchase_id) {
                idsToProcess = [parseInt(purchase_id, 10)];
            } else {
                // If no IDs provided, create a new purchase from cart
                const total = getDiscountedTotal();
                const items = cart.map(item => ({
                    packageId: item.package.id,
                    quantity: item.quantity
                }));
                const purchase = await (createPurchase as any)(items, total);
                idsToProcess = [purchase.id];
            }

            if (idsToProcess.length === 0) throw new Error("Missing transaction ID(s)");

            // Complete all purchases
            for (const id of idsToProcess) {
                await completePurchase(id);
            }

            // Clear the cart on success
            clearCart();

            // Redirect to success page
            router.replace({
                pathname: "/success",
                params: { purchase_ids: idsToProcess.join(',') }
            });
        } catch (err: any) {
            console.error('Mock payment error:', err);
            setError(err.message || 'Payment failed');
            setStep('error');
        }
    };

    if (step === 'methods') {
        return (
            <ProtectedRoute>
                <View className="flex-1 bg-[#050505] p-[24px] pt-[48px] relative">
                    {/* Background Foundation - MECHANICAL REPLICATION */}
                    <View
                        className="absolute top-0 right-0 w-[256px] h-[256px] bg-[#00FF80]/10 rounded-full opacity-10"
                        style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                    />
                    <View
                        className="absolute bottom-0 left-0 w-[256px] h-[256px] bg-[#FF3232]/10 rounded-full opacity-10"
                        style={{ shadowColor: '#FF3232', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                    />

                    <View className="mb-[40px] border-b-2 border-[#00FF80]/30 pb-[24px] relative z-10">
                        <View className="flex-row items-center justify-center gap-4 mb-3">
                            <ShieldCheck size={40} color="#00FF80" />
                            <Text className="text-[36px] font-black font-heading text-white uppercase tracking-tighter text-glow-intense">
                                {t('mockPayment.checkout')}
                            </Text>
                        </View>
                        <Text className="text-center text-[#00FF80] font-mono text-[10px] uppercase tracking-[0.4em]">
                            // {t('mockPayment.selectMethod')}
                        </Text>
                    </View>

                    <ScrollView className="flex-1 relative z-10">
                        <View className="gap-[16px]">
                            {[
                                { id: 'card', name: 'CREDIT / DEBIT CARD', icon: '💳' },
                                { id: 'apple', name: 'APPLE PAY', icon: '' },
                                { id: 'google', name: 'GOOGLE PAY', icon: 'G' },
                            ].map(method => (
                                <TouchableOpacity
                                    key={method.id}
                                    onPress={() => setSelectedMethod(method.id)}
                                    className={`w-full p-[32px] border-2 flex-row items-center gap-[32px] active:scale-[0.98] ${selectedMethod === method.id
                                        ? 'border-[#00FF80] bg-[#00FF80]/10 shadow-[0_0_40px_rgba(0,255,128,0.15)]'
                                        : 'border-white/10 bg-black/60'
                                        }`}
                                >
                                    <View className="w-[48px] h-[48px] items-center justify-center bg-white/5">
                                        <Text className="text-[24px]">{method.icon}</Text>
                                    </View>
                                    <Text className={`font-heading text-[20px] uppercase tracking-wider ${selectedMethod === method.id ? 'text-white font-black text-glow-intense' : 'text-gray-500'
                                        }`}>
                                        {method.name}
                                    </Text>
                                </TouchableOpacity>
                            ))}
                        </View>
                    </ScrollView>

                    <View className="mt-[32px] gap-[16px] mb-[96px] relative z-10">
                        <TouchableOpacity
                            disabled={!selectedMethod}
                            onPress={handleProcessPayment}
                            className={`w-full bg-[#00FF80] py-[20px] flex-row items-center justify-center gap-4 shadow-[0_0_60px_rgba(0,255,128,0.5)] active:scale-[0.98] ${!selectedMethod ? 'opacity-30' : ''
                                }`}
                        >
                            <CreditCard size={28} color="black" />
                            <Text className="text-black font-black text-[20px] font-heading uppercase tracking-wider">
                                {t('mockPayment.payNow')}
                            </Text>
                        </TouchableOpacity>

                        <TouchableOpacity
                            onPress={() => router.back()}
                            className="w-full py-[16px] items-center active:scale-[0.98]"
                        >
                            <Text className="text-gray-600 font-mono text-[10px] uppercase tracking-[0.4em] font-black">
                                // {t('mockPayment.cancelTransaction')}
                            </Text>
                        </TouchableOpacity>
                    </View>
                </View>
            </ProtectedRoute>
        );
    }

    return (
        <View className="flex-1 bg-[#050505] items-center justify-center p-[32px]">
            <View className="w-full bg-black border-2 border-[#00FF80]/30 p-[40px] items-center shadow-[0_0_100px_rgba(0,255,128,0.1)] overflow-hidden">
                {step === 'processing' ? (
                    <>
                        <View className="absolute top-0 left-0 right-0 h-1 bg-white/5">
                            <Animated.View
                                style={{
                                    height: '100%',
                                    backgroundColor: '#00FF80',
                                    shadowColor: '#00FF80',
                                    shadowOffset: { width: 0, height: 0 },
                                    shadowOpacity: 1,
                                    shadowRadius: 10,
                                    width: progress.interpolate({
                                        inputRange: [0, 0.5, 1],
                                        outputRange: ['0%', '70%', '100%']
                                    }),
                                    transform: [{
                                        translateX: progress.interpolate({
                                            inputRange: [0, 1],
                                            outputRange: [-100, 400]
                                        })
                                    }]
                                }}
                            />
                        </View>
                        <ActivityIndicator size="large" color="#00FF80" className="mb-[40px] scale-[2]" />
                        <Text className="text-[30px] font-black text-white font-heading uppercase tracking-tighter mb-4 text-center text-glow-intense">
                            {t('mockPayment.processing')}
                        </Text>
                        <View className="flex-row items-center gap-3 py-2 px-4 bg-[#00FF80]/10 border border-[#00FF80]/30">
                            <Zap size={14} color="#00FF80" />
                            <Text className="text-[#00FF80] font-mono text-[10px] uppercase tracking-[0.3em] font-black">
                                {t('mockPayment.securingAssets')}
                            </Text>
                        </View>
                    </>
                ) : (
                    <>
                        <View className="w-[96px] h-[96px] bg-[#FF3232]/10 border-4 border-[#FF3232] items-center justify-center mb-[32px] shadow-[0_0_40px_rgba(255,50,50,0.3)]">
                            <Text className="text-[60px] text-[#FF3232] font-black">!</Text>
                        </View>
                        <Text className="text-[36px] font-black text-[#FF3232] font-heading uppercase mb-4 tracking-tighter text-glow-intense">
                            {t('mockPayment.paymentFailed')}
                        </Text>
                        <Text className="text-gray-500 font-mono text-xs text-center mb-[40px] leading-relaxed uppercase">{error}</Text>
                        <TouchableOpacity
                            onPress={() => setStep('methods')}
                            className="w-full py-[20px] border-2 border-white/20 items-center active:scale-[0.98]"
                        >
                            <Text className="text-white font-black font-mono text-xs uppercase tracking-[0.4em]">
                                RETURN_TO_TERMINAL
                            </Text>
                        </TouchableOpacity>
                    </>
                )}
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    // NativeWind doesn't support complex animations easily yet
});
