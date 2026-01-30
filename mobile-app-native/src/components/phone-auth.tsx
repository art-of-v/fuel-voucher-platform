
import { useState } from 'react';
import { View, Text, TouchableOpacity, TextInput, ActivityIndicator } from 'react-native';
import { Phone, ArrowRight, Lock, Check } from 'lucide-react-native';
import { useI18n } from '@/lib/i18n';
import { apiFetch } from '@/lib/api';

type AuthStep = "phone" | "code" | "success";

interface PhoneAuthProps {
    onSuccess: () => void;
}

export function PhoneAuth({ onSuccess }: PhoneAuthProps) {
    const { t } = useI18n();
    const [step, setStep] = useState<AuthStep>("phone");
    const [phone, setPhone] = useState("");
    const [code, setCode] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState("");

    const handleSendCode = async () => {
        if (!phone.trim()) {
            setError(t('phoneAuth.enterPhone'));
            return;
        }

        setLoading(true);
        setError("");

        try {
            const res = await apiFetch("/api/auth/phone/send-code", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ phone }),
            });

            const data = await res.json();

            if (res.status >= 400) {
                setError(data.error || t('phoneAuth.sendFailed'));
                return;
            }

            setStep("code");
        } catch (err) {
            setError(t('phoneAuth.networkError'));
        } finally {
            setLoading(false);
        }
    };

    const handleVerifyCode = async () => {
        if (!code.trim() || code.length !== 6) {
            setError(t('phoneAuth.enterCode'));
            return;
        }

        setLoading(true);
        setError("");

        try {
            const res = await apiFetch("/api/auth/phone/verify", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ phone, code }),
            });

            const data = await res.json();

            if (res.status >= 400) {
                setError(data.error || t('phoneAuth.verifyFailed'));
                return;
            }

            setStep("success");
            setTimeout(() => {
                onSuccess();
            }, 1500);
        } catch (err) {
            setError(t('phoneAuth.networkError'));
        } finally {
            setLoading(false);
        }
    };

    const handleCodeChange = (value: string) => {
        const digitsOnly = value.replace(/\D/g, "").slice(0, 6);
        setCode(digitsOnly);
    };

    return (
        <View className="w-full">
            {step === "phone" && (
                <View>
                    <View className="items-center mb-6">
                        <View className="w-[64px] h-[64px] bg-[#00FF80]/20 border-2 border-[#00FF80] items-center justify-center mb-4">
                            <Phone size={32} color="#00FF80" />
                        </View>
                        <Text className="text-[20px] font-black text-white font-heading uppercase tracking-wider text-center">
                            {t('phoneAuth.title')}
                        </Text>
                        <Text className="text-gray-400 text-sm mt-2 text-center">
                            {t('phoneAuth.subtitle')}
                        </Text>
                    </View>

                    <View className="gap-4">
                        <View>
                            <Text className="text-[10px] text-gray-500 font-mono uppercase tracking-wider mb-2">
                                {t('phoneAuth.phoneLabel')}
                            </Text>
                            <TextInput
                                value={phone}
                                onChangeText={setPhone}
                                placeholder="+380991234567"
                                placeholderTextColor="#333"
                                keyboardType="phone-pad"
                                maxLength={13}
                                className="w-full bg-black/50 border-2 border-white/20 px-[16px] py-[16px] text-white font-mono text-[18px]"
                            />
                        </View>

                        {error ? (
                            <Text className="text-[#EF4444] text-sm font-mono">{error}</Text>
                        ) : null}

                        <TouchableOpacity
                            onPress={handleSendCode}
                            disabled={loading}
                            className="w-full bg-[#00FF80] py-[16px] flex-row items-center justify-center gap-3 active:scale-[0.98] disabled:opacity-50"
                            style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 0.5, shadowRadius: 20 }}
                        >
                            {loading ? (
                                <>
                                    <ActivityIndicator size="small" color="black" />
                                    <Text className="text-black font-black text-[18px] font-heading uppercase">{t('phoneAuth.sending')}</Text>
                                </>
                            ) : (
                                <>
                                    <Text className="text-black font-black text-[18px] font-heading uppercase">{t('phoneAuth.sendCode')}</Text>
                                    <ArrowRight size={20} color="black" />
                                </>
                            )}
                        </TouchableOpacity>
                    </View>
                </View>
            )}

            {step === "code" && (
                <View>
                    <View className="items-center mb-6">
                        <View className="w-[64px] h-[64px] bg-[#00FF80]/20 border-2 border-[#00FF80] items-center justify-center mb-4">
                            <Lock size={32} color="#00FF80" />
                        </View>
                        <Text className="text-[20px] font-black text-white font-heading uppercase tracking-wider text-center">
                            {t('phoneAuth.enterCodeTitle')}
                        </Text>
                        <Text className="text-gray-400 text-sm mt-2 text-center">
                            {t('phoneAuth.codeSentTo')} <Text className="text-[#00FF80]">{phone}</Text>
                        </Text>
                    </View>

                    <View className="gap-4">
                        <View>
                            <Text className="text-[10px] text-gray-500 font-mono uppercase tracking-wider mb-2">
                                {t('phoneAuth.codeLabel')}
                            </Text>
                            <TextInput
                                value={code}
                                onChangeText={handleCodeChange}
                                placeholder="------"
                                placeholderTextColor="#333"
                                keyboardType="numeric"
                                maxLength={6}
                                className="w-full bg-black/50 border-2 border-white/20 px-[16px] py-[16px] text-white font-mono text-[30px] text-center tracking-[0.5em]"
                            />
                        </View>

                        {error ? (
                            <Text className="text-[#EF4444] text-sm font-mono">{error}</Text>
                        ) : null}

                        <TouchableOpacity
                            onPress={handleVerifyCode}
                            disabled={loading || code.length !== 6}
                            className="w-full bg-[#00FF80] py-[16px] flex-row items-center justify-center gap-3 active:scale-[0.98] disabled:opacity-50"
                            style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 0.5, shadowRadius: 20 }}
                        >
                            {loading ? (
                                <>
                                    <ActivityIndicator size="small" color="black" />
                                    <Text className="text-black font-black text-[18px] font-heading uppercase">{t('phoneAuth.verifying')}</Text>
                                </>
                            ) : (
                                <>
                                    <Text className="text-black font-black text-[18px] font-heading uppercase">{t('phoneAuth.verify')}</Text>
                                    <ArrowRight size={20} color="black" />
                                </>
                            )}
                        </TouchableOpacity>

                        <TouchableOpacity
                            onPress={() => {
                                setStep("phone");
                                setCode("");
                                setError("");
                            }}
                            className="w-full py-2 items-center active:scale-[0.98]"
                        >
                            <Text className="text-gray-400 text-sm">{t('phoneAuth.changePhone')}</Text>
                        </TouchableOpacity>
                    </View>
                </View>
            )}

            {step === "success" && (
                <View className="items-center py-8">
                    <View
                        className="w-[80px] h-[80px] bg-[#22C55E]/10 border-2 border-[#22C55E] items-center justify-center mb-4"
                        style={{ shadowColor: '#22C55E', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 0.5, shadowRadius: 20 }}
                    >
                        <Check size={40} color="#22C55E" />
                    </View>
                    <Text className="text-[24px] font-black text-white font-heading uppercase tracking-wider mb-2">
                        {t('phoneAuth.success')}
                    </Text>
                    <Text className="text-gray-400 text-sm text-center">
                        {t('phoneAuth.redirecting')}
                    </Text>
                </View>
            )}
        </View>
    );
}
