/// <reference types="nativewind/types" />
import { useState } from "react";
import { View, Text, Pressable, TextInput, ActivityIndicator, Image } from "react-native";
import { Phone, ArrowRight, Lock, Check } from "lucide-react-native";
import { apiRequest } from "../lib/utils";

type AuthStep = "phone" | "code" | "success";

interface PhoneAuthProps {
  onSuccess: () => void;
}

export function PhoneAuth({ onSuccess }: PhoneAuthProps) {
  const [step, setStep] = useState<AuthStep>("phone");
  const [phone, setPhone] = useState("+380");
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleSendCode = async () => {
    if (!phone.trim() || phone.length < 10) {
      setError("ENTER VALID PHONE");
      return;
    }

    setLoading(true);
    setError("");

    try {
      await apiRequest("POST", "/api/auth/phone/send-code", { phone });
      setStep("code");
    } catch (err: any) {
      setError(err.message || "NETWORK ERROR");
    } finally {
      setLoading(false);
    }
  };

  const handleVerifyCode = async () => {
    if (!code.trim() || code.length !== 6) {
      setError("ENTER 6-DIGIT CODE");
      return;
    }

    setLoading(true);
    setError("");

    try {
      await apiRequest("POST", "/api/auth/phone/verify", { phone, code });
      setStep("success");
      setTimeout(() => {
        onSuccess();
      }, 1000);
    } catch (err: any) {
      setError(err.message || "INVALID CODE");
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
          <View className="items-center mb-10">
            <View className="w-24 h-24 bg-black border-2 border-[#00FF80] p-1 items-center justify-center mb-6">
              <View className="w-full h-full bg-[#00FF8015] items-center justify-center">
                <Image
                  source={require("../../assets/original_lion_watermark.png")}
                  className="w-14 h-14"
                  resizeMode="contain"
                />
              </View>
            </View>
            <Text className="text-4xl font-black text-white uppercase tracking-tighter">LOGIN</Text>
            <Text className="text-white/40 text-[10px] items-center text-center font-bold uppercase tracking-[0.2em] mt-2">
              INITIATE SECURE HANDSHAKE
            </Text>
          </View>

          <View className="gap-6">
            <View>
              <Text className="text-[10px] text-white/30 font-bold uppercase tracking-widest mb-2">
                PHONE NUMBER
              </Text>
              <TextInput
                keyboardType="phone-pad"
                value={phone}
                onChangeText={setPhone}
                placeholder="+380"
                placeholderTextColor="#222"
                className="w-full bg-black/60 border border-white/10 px-6 py-5 text-white text-xl rounded font-bold"
              />
            </View>

            {error ? (
              <View className="bg-red-500/10 border border-red-500/30 p-3 rounded">
                <Text className="text-red-500 text-[10px] font-bold text-center uppercase tracking-widest">{error}</Text>
              </View>
            ) : null}

            <Pressable
              onPress={handleSendCode}
              disabled={loading}
              className={`w-full bg-[#00FF80] py-5 rounded flex-row items-center justify-center gap-3 active:scale-95 ${loading ? 'opacity-50' : ''}`}
            >
              {loading ? (
                <ActivityIndicator color="#000" />
              ) : (
                <>
                  <Text className="text-black font-black text-lg uppercase tracking-widest">SEND CODE</Text>
                  <ArrowRight size={20} color="#000" />
                </>
              )}
            </Pressable>
          </View>
        </View>
      )}

      {step === "code" && (
        <View>
          <View className="items-center mb-10">
            <View className="w-24 h-24 bg-black border-2 border-[#00FF80] p-1 items-center justify-center mb-6">
              <View className="w-full h-full bg-[#00FF8015] items-center justify-center">
                <Lock size={32} color="#00FF80" />
              </View>
            </View>
            <Text className="text-4xl font-black text-white uppercase tracking-tighter">VERIFY</Text>
            <Text className="text-white/40 text-[10px] text-center font-bold uppercase tracking-[0.2em] mt-2">
              SENT TO <Text className="text-[#00FF80]">{phone}</Text>
            </Text>
          </View>

          <View className="gap-6">
            <View>
              <Text className="text-[10px] text-white/30 font-bold uppercase tracking-widest mb-2">
                6-DIGIT CODE
              </Text>
              <TextInput
                keyboardType="number-pad"
                value={code}
                onChangeText={handleCodeChange}
                placeholder="000000"
                placeholderTextColor="#222"
                maxLength={6}
                className="w-full bg-black/60 border border-white/10 px-6 py-5 text-white text-4xl text-center tracking-[0.4em] rounded font-black"
                autoFocus
              />
            </View>

            {error ? (
              <View className="bg-red-500/10 border border-red-500/30 p-3 rounded">
                <Text className="text-red-500 text-[10px] font-bold text-center uppercase tracking-widest">{error}</Text>
              </View>
            ) : null}

            <Pressable
              onPress={handleVerifyCode}
              disabled={loading || code.length !== 6}
              className={`w-full bg-[#00FF80] py-5 rounded flex-row items-center justify-center gap-3 active:scale-95 ${loading ? 'opacity-50' : ''}`}
            >
              {loading ? (
                <ActivityIndicator color="#000" />
              ) : (
                <>
                  <Text className="text-black font-black text-lg uppercase tracking-widest">VERIFY</Text>
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
              <Text className="text-white/20 text-[10px] uppercase font-bold tracking-widest mt-2">CHANGE PHONE NUMBER</Text>
            </Pressable>
          </View>
        </View>
      )}

      {step === "success" && (
        <View className="items-center py-20">
          <View className="w-24 h-24 bg-black border-2 border-[#00FF80] p-1 items-center justify-center mb-6">
            <View className="w-full h-full bg-[#00FF80] items-center justify-center">
              <Check size={40} color="#000" />
            </View>
          </View>
          <Text className="text-4xl font-black text-white uppercase tracking-tighter">GRANTED</Text>
          <Text className="text-white/40 text-[10px] text-center font-bold uppercase tracking-[0.2em] mt-2">
            ACCESSING SECURE DATA
          </Text>
        </View>
      )}
    </View>
  );
}
