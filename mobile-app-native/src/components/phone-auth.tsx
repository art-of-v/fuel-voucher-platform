import { useState } from "react";
import { View, Text, Pressable, TextInput, ActivityIndicator } from "react-native";
import { Phone, ArrowRight, Lock, Check } from "lucide-react-native";
import { useI18n } from "../lib/i18n";
import { apiRequest } from "../lib/utils";

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
      await apiRequest("POST", "/api/auth/phone/send-code", { phone });
      setStep("code");
    } catch (err: any) {
      setError(err.message || t('phoneAuth.networkError'));
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
      await apiRequest("POST", "/api/auth/phone/verify", { phone, code });
      setStep("success");
      setTimeout(() => {
        onSuccess();
      }, 1500);
    } catch (err: any) {
      setError(err.message || t('phoneAuth.networkError'));
    } finally {
      setLoading(false);
    }
  };

  const handleCodeChange = (value: string) => {
    const digitsOnly = value.replace(/\D/g, "").slice(0, 6);
    setCode(digitsOnly);
  };

  return (
    <View className="space-y-6">
      {step === "phone" && (
        <View>
          <View className="items-center mb-6">
            <View className="w-16 h-16 bg-[#00FF80] border-2 border-[#00FF80] items-center justify-center mb-4 rounded">
              <Phone size={32} color="#000" />
            </View>
            <Text className="text-xl font-black text-white uppercase">{t('phoneAuth.title')}</Text>
            <Text className="text-gray-400 text-sm mt-2 text-center">{t('phoneAuth.subtitle')}</Text>
          </View>

          <View className="space-y-4">
            <View>
              <Text className="text-[10px] text-gray-500 font-bold uppercase tracking-widest mb-2">
                {t('phoneAuth.phoneLabel')}
              </Text>
              <TextInput
                keyboardType="phone-pad"
                value={phone}
                onChangeText={setPhone}
                placeholder="+380XXXXXXXXX"
                placeholderTextColor="#444"
                className="w-full bg-zinc-900 border border-white/20 px-4 py-4 text-white text-lg rounded"
              />
            </View>

            {error ? <Text className="text-red-500 text-sm">{error}</Text> : null}

            <Pressable
              onPress={handleSendCode}
              disabled={loading}
              className={`w-full bg-[#00FF80] py-4 rounded flex-row items-center justify-center gap-3 active:scale-95 ${loading ? 'opacity-50' : ''}`}
            >
              {loading ? (
                <ActivityIndicator color="#000" />
              ) : (
                <>
                  <Text className="text-black font-black text-lg uppercase">{t('phoneAuth.sendCode')}</Text>
                  <ArrowRight size={20} color="#000" />
                </>
              )}
            </Pressable>
          </View>
        </View>
      )}

      {step === "code" && (
        <View>
          <View className="items-center mb-6">
            <View className="w-16 h-16 bg-[#00FF80] border-2 border-[#00FF80] items-center justify-center mb-4 rounded">
              <Lock size={32} color="#000" />
            </View>
            <Text className="text-xl font-black text-white uppercase">{t('phoneAuth.enterCodeTitle')}</Text>
            <Text className="text-gray-400 text-sm mt-2 text-center">
              {t('phoneAuth.codeSentTo')} <Text className="text-[#00FF80]">{phone}</Text>
            </Text>
          </View>

          <View className="space-y-4">
            <View>
              <Text className="text-[10px] text-gray-500 font-bold uppercase tracking-widest mb-2">
                {t('phoneAuth.codeLabel')}
              </Text>
              <TextInput
                keyboardType="number-pad"
                value={code}
                onChangeText={handleCodeChange}
                placeholder="------"
                placeholderTextColor="#444"
                maxLength={6}
                className="w-full bg-zinc-900 border border-white/20 px-4 py-4 text-white text-3xl text-center tracking-[0.5em] rounded"
              />
            </View>

            {error ? <Text className="text-red-500 text-sm">{error}</Text> : null}

            <Pressable
              onPress={handleVerifyCode}
              disabled={loading || code.length !== 6}
              className={`w-full bg-[#00FF80] py-4 rounded flex-row items-center justify-center gap-3 active:scale-95 ${loading ? 'opacity-50' : ''}`}
            >
              {loading ? (
                <ActivityIndicator color="#000" />
              ) : (
                <>
                  <Text className="text-black font-black text-lg uppercase">{t('phoneAuth.verify')}</Text>
                  <ArrowRight size={20} color="#000" />
                </>
              )}
            </Pressable>

            <Pressable
              onPress={() => {
                setStep("phone");
                setCode("");
                setError("");
              }}
              className="w-full items-center"
            >
              <Text className="text-gray-400 text-sm uppercase font-bold tracking-widest mt-2">{t('phoneAuth.changePhone')}</Text>
            </Pressable>
          </View>
        </View>
      )}

      {step === "success" && (
        <View className="items-center py-8">
          <View className="w-20 h-20 bg-green-500/20 border-2 border-green-500 items-center justify-center mb-4 rounded-full">
            <Check size={40} color="#22C55E" />
          </View>
          <Text className="text-2xl font-black text-white uppercase">{t('phoneAuth.success')}</Text>
          <Text className="text-gray-400 text-sm mt-2 uppercase font-bold tracking-widest">{t('phoneAuth.redirecting')}</Text>
        </View>
      )}
    </View>
  );
}
