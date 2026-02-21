/// <reference types="nativewind/types" />
import { useState } from "react";
import { View, Text, Pressable, TextInput, ActivityIndicator, Image, StyleSheet } from "react-native";
import { Phone, ArrowRight, Lock, Check } from "lucide-react-native";
import { apiRequest } from "../lib/utils";
import { tokens } from "../lib/design-tokens";

type AuthStep = "phone" | "code" | "success";

interface PhoneAuthProps {
  onSuccess: () => void;
  onBack?: () => void;
}

export function PhoneAuth({ onSuccess, onBack }: PhoneAuthProps) {
  const [step, setStep] = useState<AuthStep>("phone");
  const [phone, setPhone] = useState("+380");
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleSendCode = async () => {
    if (!phone.trim() || phone.length < 10) {
      setError("ВВЕДІТЬ КОРЕКТНИЙ НОМЕР");
      return;
    }

    setLoading(true);
    setError("");

    try {
      await apiRequest("POST", "/api/auth/phone/send-code", { phone });
      setStep("code");
    } catch (err: any) {
      setError(err.message || "ПОМИЛКА МЕРЕЖІ");
    } finally {
      setLoading(false);
    }
  };

  const handleVerifyCode = async () => {
    if (!code.trim() || code.length !== 6) {
      setError("ВВЕДІТЬ КОД");
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
      setError(err.message || "НЕВІРНИЙ КОД");
    } finally {
      setLoading(false);
    }
  };

  const handleCodeChange = (value: string) => {
    const digitsOnly = value.replace(/\D/g, "").slice(0, 6);
    setCode(digitsOnly);
  };

  return (
    <View style={styles.container}>
      {step === "phone" && (
        <View style={styles.content}>
          <View style={styles.header}>
            <View style={styles.iconBox}>
              <Phone size={32} color={tokens.colors.primary} />
            </View>
            <Text allowFontScaling={false} style={styles.title}>ВХІД ЗА ТЕЛЕФОНОМ</Text>
            <Text allowFontScaling={false} style={styles.subtitle}>Введіть номер телефону для отримання коду</Text>
          </View>

          <View style={styles.form}>
            <View style={styles.inputGroup}>
              <Text allowFontScaling={false} style={styles.label}>НОМЕР ТЕЛЕФОНУ</Text>
              <TextInput
                keyboardType="phone-pad"
                value={phone}
                onChangeText={setPhone}
                placeholder="+380XXXXXXXXX"
                placeholderTextColor="#333"
                style={styles.input}
              />
            </View>

            {error ? <Text style={styles.errorText}>{error}</Text> : null}

            <Pressable
              onPress={handleSendCode}
              disabled={loading}
              style={styles.primaryBtn}
            >
              {loading ? (
                <ActivityIndicator color="#000" />
              ) : (
                <>
                  <Text allowFontScaling={false} style={styles.btnText}>НАДІСЛАТИ КОД</Text>
                  <ArrowRight size={20} color="#000" />
                </>
              )}
            </Pressable>

            {onBack && (
              <Pressable onPress={onBack} style={styles.backBtn}>
                <Text allowFontScaling={false} style={styles.backBtnText}>Назад</Text>
              </Pressable>
            )}
          </View>
        </View>
      )}

      {step === "code" && (
        <View style={styles.content}>
          <View style={styles.header}>
            <View style={styles.iconBox}>
              <Lock size={32} color={tokens.colors.primary} />
            </View>
            <Text allowFontScaling={false} style={styles.title}>ПІДТВЕРДЖЕННЯ</Text>
            <Text allowFontScaling={false} style={styles.subtitle}>Введіть код, надісланий на {phone}</Text>
          </View>

          <View style={styles.form}>
            <TextInput
              keyboardType="number-pad"
              value={code}
              onChangeText={handleCodeChange}
              placeholder="000000"
              placeholderTextColor="#222"
              maxLength={6}
              style={[styles.input, styles.codeInput]}
              autoFocus
            />

            {error ? <Text style={styles.errorText}>{error}</Text> : null}

            <Pressable
              onPress={handleVerifyCode}
              disabled={loading || code.length !== 6}
              style={[styles.primaryBtn, (loading || code.length !== 6) && { opacity: 0.5 }]}
            >
              <Text allowFontScaling={false} style={styles.btnText}>ПІДТВЕРДИТИ</Text>
              <ArrowRight size={20} color="#000" />
            </Pressable>

            <Pressable onPress={() => setStep("phone")} style={styles.backBtn}>
              <Text allowFontScaling={false} style={styles.backBtnText}>Змінити номер</Text>
            </Pressable>
          </View>
        </View>
      )}

      {step === "success" && (
        <View style={styles.successBox}>
          <View style={[styles.iconBox, { backgroundColor: tokens.colors.primary }]}>
            <Check size={40} color="#000" />
          </View>
          <Text allowFontScaling={false} style={styles.title}>УСПІШНО</Text>
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    width: '100%',
    paddingHorizontal: 20,
  },
  content: {
    width: '100%',
    alignItems: 'center',
  },
  header: {
    alignItems: 'center',
    marginBottom: 40,
  },
  iconBox: {
    width: 64,
    height: 64,
    borderWidth: 1,
    borderColor: tokens.colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 24,
  },
  title: {
    fontSize: 24,
    fontWeight: '900',
    color: '#FFF',
    textAlign: 'center',
    textTransform: 'uppercase',
  },
  subtitle: {
    fontSize: 14,
    color: 'rgba(255,255,255,0.6)',
    textAlign: 'center',
    marginTop: 8,
  },
  form: {
    width: '100%',
    gap: 20,
  },
  inputGroup: {
    width: '100%',
  },
  label: {
    fontSize: 10,
    color: 'rgba(255,255,255,0.4)',
    fontWeight: 'bold',
    marginBottom: 8,
    letterSpacing: 1,
  },
  input: {
    width: '100%',
    backgroundColor: '#0A0A0A',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.1)',
    borderRadius: 2,
    padding: 18,
    color: '#FFF',
    fontSize: 18,
    fontWeight: 'bold',
  },
  codeInput: {
    fontSize: 32,
    textAlign: 'center',
    letterSpacing: 10,
  },
  primaryBtn: {
    width: '100%',
    backgroundColor: tokens.colors.primary,
    paddingVertical: 18,
    borderRadius: 2,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
  },
  btnText: {
    color: '#000',
    fontSize: 16,
    fontWeight: '900',
    textTransform: 'uppercase',
  },
  errorText: {
    color: '#FF4444',
    fontSize: 12,
    textAlign: 'center',
    fontWeight: 'bold',
  },
  backBtn: {
    alignItems: 'center',
    padding: 10,
  },
  backBtnText: {
    color: 'rgba(255,255,255,0.4)',
    fontSize: 14,
  },
  successBox: {
    alignItems: 'center',
    paddingVertical: 60,
  }
});
