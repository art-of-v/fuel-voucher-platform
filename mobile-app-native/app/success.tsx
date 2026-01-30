
import { View, Text, TouchableOpacity, Image } from 'react-native';
import { useRouter } from 'expo-router';
import { CheckCircle2, QrCode, Zap, Skull, ArrowRight } from 'lucide-react-native';
import { useEffect } from 'react';
import { ProtectedRoute } from '@/components/ProtectedRoute';
import { useI18n } from '@/lib/i18n';

export default function SuccessScreen() {
    const router = useRouter();

    const { t } = useI18n();

    useEffect(() => {
        // Confetti simulation could go here
    }, []);

    return (
        <ProtectedRoute>
            <View className="flex-1 bg-[#050505] items-center justify-center p-[24px] relative">
                {/* Background effects - MECHANICAL REPLICATION */}
                <View
                    className="absolute top-1/4 left-1/2 -ml-[192px] w-[384px] h-[384px] bg-[#00FF80]/20 rounded-full opacity-20"
                    style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 120 }}
                />

                <View className="relative z-10 items-center w-full max-w-md">
                    {/* Success Icon */}
                    <View
                        className="w-[96px] h-[96px] bg-[#00FF80] rounded-full flex items-center justify-center mb-6"
                        style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 60 }}
                    >
                        <CheckCircle2 size={48} color="black" />
                    </View>

                    {/* Title */}
                    <Text className="text-[36px] font-black text-white font-heading uppercase mb-4 tracking-wider text-center">
                        {t('paymentSuccess.title')}
                    </Text>

                    {/* Subtitle */}
                    <Text className="text-gray-400 font-mono mb-8 text-sm text-center">
                        {t('paymentSuccess.subtitle')}
                    </Text>

                    {/* Info Box */}
                    <View className="bg-black/40 border border-white/10 p-[24px] rounded-[8px] mb-8 w-full">
                        <View className="flex-row items-start gap-3">
                            <ArrowRight size={20} color="#00FF80" className="mt-1" />
                            <View className="flex-1">
                                <Text className="text-sm text-gray-300 mb-2">{t('paymentSuccess.confirmed')}</Text>
                                <Text className="text-xs text-gray-500 font-mono">
                                    {t('paymentSuccess.checkVouchers')}
                                </Text>
                            </View>
                        </View>
                    </View>

                    {/* Action Buttons */}
                    <View className="w-full gap-3">
                        <TouchableOpacity
                            onPress={() => router.replace("/my-codes")}
                            className="w-full bg-[#00FF80] py-[16px] flex-row items-center justify-center gap-3 shadow-[0_0_40px_rgba(0,255,128,0.5)] active:scale-[0.98]"
                        >
                            <QrCode size={20} color="black" />
                            <Text className="text-black font-black text-[18px] font-heading uppercase tracking-wider">
                                {t('paymentSuccess.viewVouchers')}
                            </Text>
                        </TouchableOpacity>

                        <TouchableOpacity
                            onPress={() => router.replace("/")}
                            className="w-full bg-white/10 border border-white/20 py-[16px] flex-row items-center justify-center gap-3 active:scale-[0.98]"
                        >
                            <Zap size={20} color="white" />
                            <Text className="text-white font-black text-[18px] font-heading uppercase tracking-wider">
                                {t('paymentSuccess.backHome')}
                            </Text>
                        </TouchableOpacity>
                    </View>
                </View>
            </View>
        </ProtectedRoute>
    );
}
