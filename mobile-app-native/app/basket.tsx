
import { useState } from 'react';
import { View, Text, TouchableOpacity, ScrollView, TextInput, Alert, KeyboardAvoidingView, Platform, Image } from 'react-native';
import { useRouter } from 'expo-router';
import { useStore } from '@/lib/store';
import { useI18n } from '@/lib/i18n';
import { ChevronLeft, Minus, Plus, Trash2, Tag, Zap, ShoppingCart, X, Check } from 'lucide-react-native';
import { cn } from '@/lib/utils';
import { ProtectedRoute } from '@/components/ProtectedRoute';

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
            Alert.alert("Error", "Invalid promocode");
        }
    };

    if (cart.length === 0) {
        return (
            <View className="flex-1 bg-[#050505]">
                <View className="bg-black/90 p-4 flex-row items-center gap-4 border-b-2 border-[#00FF80]/30 z-10">
                    <TouchableOpacity
                        onPress={() => router.replace("/")}
                        className="p-2 -ml-2 border-2 border-white/20 bg-black/50 active:scale-[0.98]"
                    >
                        <ChevronLeft size={24} color="white" />
                    </TouchableOpacity>
                    <Text className="font-black text-xl text-white font-heading tracking-wider uppercase">{t('basket.title')}</Text>
                </View>

                <View className="flex-1 items-center justify-center p-8 z-10">
                    <View className="w-32 h-32 bg-white/5 border-2 border-white/10 items-center justify-center mb-8">
                        <ShoppingCart size={64} color="#333" />
                    </View>
                    <Text className="text-4xl font-black text-white font-heading uppercase mb-2 text-center text-glow-intense">
                        {t('basket.empty')}
                    </Text>
                    <Text className="text-gray-500 font-mono text-xs mb-8 text-center uppercase tracking-[0.2em]">{t('basket.browseStations')}</Text>
                    <TouchableOpacity
                        onPress={() => router.replace("/")}
                        className="bg-[#00FF80] px-12 py-5 shadow-[0_0_30px_rgba(0,255,128,0.4)] active:scale-[0.98]"
                    >
                        <Text className="text-black font-black text-lg font-heading uppercase tracking-widest">
                            {t('basket.browseStations')}
                        </Text>
                    </TouchableOpacity>
                </View>
            </View>
        );
    }

    return (
        <ProtectedRoute>
            <KeyboardAvoidingView
                behavior={Platform.OS === "ios" ? "padding" : "height"}
                className="flex-1 bg-[#050505]"
            >
                {/* Background glow - MECHANICAL REPLICATION */}
                <View
                    className="absolute top-0 right-0 w-[256px] h-[256px] bg-[#00FF80]/10 rounded-full opacity-10"
                    style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                />

                <View className="bg-black/90 p-[16px] flex-row items-center gap-4 border-b-2 border-[#00FF80]/30 z-20 sticky top-0">
                    <TouchableOpacity
                        onPress={() => router.back()}
                        className="p-2 -ml-2 border-2 border-white/20 bg-black/50 active:scale-[0.98]"
                    >
                        <ChevronLeft size={24} color="white" />
                    </TouchableOpacity>
                    <View className="flex-1">
                        <View className="flex-row items-center gap-2">
                            <ShoppingCart size={20} color="#00FF80" />
                            <Text className="font-black text-[20px] text-white font-heading uppercase tracking-wider">
                                {t('basket.title')}
                            </Text>
                        </View>
                        <Text className="text-xs text-gray-400 font-mono">{cart.length} {t('basket.cards')}</Text>
                    </View>
                    <TouchableOpacity
                        onPress={() => clearCart()}
                        className="active:opacity-70"
                    >
                        <Text className="text-[#FF3232] text-xs font-mono uppercase tracking-wider">{t('basket.remove')}</Text>
                    </TouchableOpacity>
                </View>

                <ScrollView className="flex-1 p-[16px] z-10" contentContainerStyle={{ paddingBottom: 320 }}>
                    <View className="gap-[12px]">
                        {cart.map((item) => (
                            <View
                                key={item.id}
                                className="bg-black/80 border-2 border-white/10 p-[16px]"
                            >
                                <View className="flex-row items-start justify-between mb-[12px]">
                                    <View className="flex-1">
                                        <Text className="font-black text-white text-[18px] font-heading uppercase">
                                            {item.station.name} - {item.fuel.name}
                                        </Text>
                                        <Text className="text-[#00FF80] font-mono text-sm">{item.package.liters}L Card</Text>
                                    </View>
                                    <TouchableOpacity
                                        onPress={() => removeFromCart(item.id)}
                                        className="p-2 text-[#FF3232] active:scale-[0.98]"
                                    >
                                        <Trash2 size={20} color="#FF3232" />
                                    </TouchableOpacity>
                                </View>

                                <View className="flex-row items-center justify-between">
                                    <View className="flex-row items-center gap-[16px]">
                                        <TouchableOpacity
                                            onPress={() => updateQuantity(item.id, item.quantity - 1)}
                                            className="w-[40px] h-[40px] bg-white/10 border-2 border-white/20 items-center justify-center active:scale-[0.98]"
                                        >
                                            <Minus size={20} color="white" />
                                        </TouchableOpacity>
                                        <Text className="text-[30px] font-black text-[#00FF80] font-mono w-[56px] text-center">{item.quantity}</Text>
                                        <TouchableOpacity
                                            onPress={() => updateQuantity(item.id, item.quantity + 1)}
                                            className="w-[40px] h-[40px] bg-white/10 border-2 border-white/20 items-center justify-center active:scale-[0.98]"
                                        >
                                            <Plus size={20} color="white" />
                                        </TouchableOpacity>
                                    </View>
                                    <View className="items-end">
                                        <Text className="text-[10px] text-gray-500 font-mono uppercase">
                                            {item.quantity} x {item.package.price} ₴
                                        </Text>
                                        <Text className="text-white font-black text-[20px] font-heading">
                                            {item.package.price * item.quantity} ₴
                                        </Text>
                                    </View>
                                </View>
                            </View>
                        ))}
                    </View>
                </ScrollView>

                {/* Fixed bottom checkout section - MECHANICAL COMPUTED CLONE */}
                <View className="bg-black border-t-2 border-[#00FF80]/30 p-[16px] gap-[16px] z-20">
                    <View className="gap-[8px]">
                        <View className="flex-row items-center gap-2 text-xs text-gray-400 font-mono uppercase tracking-wider">
                            <Tag size={16} color="#00FF80" />
                            <Text className="text-gray-400 text-xs font-mono uppercase tracking-wider">{t('basket.promocode')}</Text>
                        </View>

                        {promocode ? (
                            <View className="flex-row items-center justify-between bg-[#00FF80]/10 border-2 border-[#00FF80]/30 p-[12px]">
                                <View className="flex-row items-center gap-2">
                                    <Check size={20} color="#00FF80" />
                                    <Text className="font-black text-[#00FF80] font-mono">{promocode}</Text>
                                    <Text className="text-gray-400 text-sm">(-{discount}%)</Text>
                                </View>
                                <TouchableOpacity onPress={clearPromocode} className="active:scale-[0.98]">
                                    <X size={20} color="#FF3232" />
                                </TouchableOpacity>
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
                                    placeholderTextColor="#444"
                                    autoCapitalize="characters"
                                    className={cn("flex-1 bg-black border-2 px-4 py-3 text-white font-mono uppercase tracking-wider",
                                        promoError ? 'border-[#FF3232]' : 'border-white/20'
                                    )}
                                />
                                <TouchableOpacity
                                    onPress={handleApplyPromo}
                                    disabled={!promoInput}
                                    className="bg-[#00FF80]/20 border-2 border-[#00FF80]/50 px-[24px] items-center justify-center active:scale-[0.98] disabled:opacity-50"
                                >
                                    <Text className="text-[#00FF80] font-black uppercase">{t('basket.apply')}</Text>
                                </TouchableOpacity>
                            </View>
                        )}
                    </View>

                    <View className="gap-[8px] border-t-2 border-white/10 pt-[16px]">
                        <View className="flex-row justify-between text-gray-400 font-mono text-sm">
                            <Text className="text-gray-400 font-mono text-sm">{t('basket.subtotal')}</Text>
                            <Text className="text-gray-400 font-mono text-sm">{total} ₴</Text>
                        </View>

                        {discount > 0 && (
                            <View className="flex-row justify-between text-[#00FF80] font-mono text-sm">
                                <Text className="text-[#00FF80] font-mono text-sm">{t('basket.discount')} ({discount}%)</Text>
                                <Text className="text-[#00FF80] font-mono text-sm">-{discountAmount} ₴</Text>
                            </View>
                        )}

                        <View className="flex-row justify-between items-end pt-[8px]">
                            <Text className="font-black text-white text-[18px] font-heading uppercase">{t('basket.totalToPay')}</Text>
                            <Text className="text-[36px] font-black text-white font-heading text-glow-intense">{discountedTotal} ₴</Text>
                        </View>
                    </View>

                    <TouchableOpacity
                        onPress={() => router.push("/checkout")}
                        className="w-full bg-[#00FF80] py-[20px] flex-row items-center justify-center gap-3 shadow-[0_0_40px_rgba(0,255,128,0.5)] active:scale-[0.98]"
                    >
                        <Zap size={24} color="black" />
                        <Text className="text-black font-black text-[20px] font-heading tracking-wider uppercase">{t('basket.checkout')}</Text>
                    </TouchableOpacity>
                </View>
            </KeyboardAvoidingView>
        </ProtectedRoute>
    );
}
