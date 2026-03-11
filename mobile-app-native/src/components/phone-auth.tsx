/// <reference types="nativewind/types" />
import { useState } from "react";
import { View, Text, Pressable, TextInput, ActivityIndicator, StyleSheet, Keyboard } from "react-native";
import { Phone, ArrowRight, Lock, Check } from "lucide-react-native";
import { apiRequest } from "../lib/api"; // Fixed import to use the enhanced api client
import { useDesignTokens } from "../lib/design-tokens";
import { Haptics } from "../lib/haptics";
import { SecurityService } from "../lib/security.service";
import { TokenStorage } from "../lib/token.storage";

type AuthStep = "phone" | "code" | "security_setup" | "success";

interface PhoneAuthProps {
  onSuccess: () => void;
  onBack?: () => void;
}

export function PhoneAuth({ onSuccess, onBack }: PhoneAuthProps) {
  const tokens = useDesignTokens();
  const [step, setStep] = useState<AuthStep>("phone");
  const [phone, setPhone] = useState("+380");
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const handleSendCode = async () => {
    if (!phone.trim() || phone.length < 10) {
      setError("ВВЕДІТЬ КОРЕКТНИЙ НОМЕР");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      return;
    }

    setLoading(true);
    setError("");
    Keyboard.dismiss();
 
    try {
      await apiRequest("POST", "/api/auth/phone/send-code", { phone });
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setStep("code");
    } catch (err: any) {
      setError(err.message || "ПОМИЛКА МЕРЕЖІ");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    } finally {
      setLoading(false);
    }
  };

  const handleVerifyCode = async () => {
    if (!code.trim() || code.length !== 6) {
      setError("ВВЕДІТЬ КОД");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      return;
    }

    setLoading(true);
    setError("");
    Keyboard.dismiss();

    try {
      // 1. Verify Phone OTP
      const response = await apiRequest("POST", "/api/auth/phone/verify", { phone, code });
      const { userId } = await response.json();

      setStep("security_setup");

      // 2. Setup Device Security (Key Generation)
      const { publicKey, deviceId } = await SecurityService.setupDeviceSecurity();
      const metadata = await SecurityService.getDeviceMetadata();

      // 3. Register Device with Backend
      const registerResponse = await apiRequest("POST", "/api/auth/device/register", {
        deviceId,
        userId,
        publicKey,
        ...metadata
      });
      if (!registerResponse.ok) {
        const err = await registerResponse.json().catch(() => ({}));
        throw new Error(err.error?.message || 'Помилка реєстрації пристрою');
      }

      // 4. Request Challenge for Initial Binding
      const challengeResponse = await apiRequest("POST", "/api/auth/device/challenge", { deviceId });
      if (!challengeResponse.ok) {
        const err = await challengeResponse.json().catch(() => ({}));
        throw new Error(err.error?.message || 'Помилка отримання challenge');
      }
      const { challenge } = await challengeResponse.json();

      // 5. Sign Challenge with Hardware Key
      const signature = await SecurityService.signPayload(challenge);

      // 6. Verify Signature and obtain tokens
      const verifyResponse = await apiRequest("POST", "/api/auth/device/verify", {
        deviceId,
        challenge,
        signature
      });
      if (!verifyResponse.ok) {
        const err = await verifyResponse.json().catch(() => ({}));
        throw new Error(err.error?.message || 'Помилка верифікації пристрою');
      }

      const { accessToken, refreshToken } = await verifyResponse.json();

      // 7. Save Session
      await TokenStorage.saveTokens(accessToken, refreshToken);

      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setStep("success");
      setTimeout(() => {
        onSuccess();
      }, 1000);
    } catch (err: any) {
      setError(err.message || "ПОМИЛКА АВТОРИЗАЦІЇ");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setStep("code"); // Fallback to code entry if binding fails
    } finally {
      setLoading(false);
    }
  };

  const handlePhoneChange = (value: string) => {
    setPhone(value);
    // Auto-dismiss if full number is entered (+380 XX XXX XX XX format or raw)
    if (value.length >= 13) {
      Keyboard.dismiss();
    }
  };

  const handleCodeChange = (value: string) => {
    const digitsOnly = value.replace(/\D/g, "").slice(0, 6);
    setCode(digitsOnly);
    // Auto-dismiss if full code reached
    if (digitsOnly.length === 6) {
      Keyboard.dismiss();
    }
  };

  return (
    <View style={styles.container}>
      {step === "phone" && (
        <View style={styles.content}>
          <View style={styles.header}>
            <View style={[styles.iconBox, { borderColor: tokens.colors.primary }]}>
              <Phone size={32} color={tokens.colors.primary} />
            </View>
            <Text allowFontScaling={false} style={[styles.title, { color: tokens.colors.text.primary }]}>ВХІД ЗА ТЕЛЕФОНОМ</Text>
            <Text allowFontScaling={false} style={[styles.subtitle, { color: tokens.colors.text.dim }]}>Введіть номер телефону для отримання коду</Text>
          </View>

          <View style={styles.form}>
            <View style={styles.inputGroup}>
              <Text allowFontScaling={false} style={[styles.label, { color: tokens.colors.text.dim }]}>НОМЕР ТЕЛЕФОНУ</Text>
              <TextInput
                keyboardType="phone-pad"
                value={phone}
                onChangeText={handlePhoneChange}
                placeholder="+380XXXXXXXXX"
                placeholderTextColor={tokens.colors.text.dim}
                style={[styles.input, { backgroundColor: tokens.colors.background, borderColor: tokens.colors.borderLight, color: tokens.colors.text.primary }]}
              />
            </View>

            {error ? <Text style={[styles.errorText, { color: tokens.colors.error }]}>{error}</Text> : null}

            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                handleSendCode();
              }}
              disabled={loading}
              style={[styles.primaryBtn, { backgroundColor: tokens.colors.primary }]}
            >
              {loading ? (
                <ActivityIndicator color={tokens.colors.isDark ? "#000" : "#FFF"} />
              ) : (
                <>
                  <Text allowFontScaling={false} style={[styles.btnText, { color: tokens.colors.isDark ? "#000" : "#FFF" }]}>НАДІСЛАТИ КОД</Text>
                  <ArrowRight size={20} color={tokens.colors.isDark ? "#000" : "#FFF"} />
                </>
              )}
            </Pressable>

            {onBack && (
              <Pressable onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onBack();
              }} style={styles.backBtn}>
                <Text allowFontScaling={false} style={[styles.backBtnText, { color: tokens.colors.text.dim }]}>Назад</Text>
              </Pressable>
            )}
          </View>
        </View>
      )}

      {step === "code" && (
        <View style={styles.content}>
          <View style={styles.header}>
            <View style={[styles.iconBox, { borderColor: tokens.colors.primary }]}>
              <Lock size={32} color={tokens.colors.primary} />
            </View>
            <Text allowFontScaling={false} style={[styles.title, { color: tokens.colors.text.primary }]}>ПІДТВЕРДЖЕННЯ</Text>
            <Text allowFontScaling={false} style={[styles.subtitle, { color: tokens.colors.text.dim }]}>Введіть код, надісланий на {phone}</Text>
          </View>

          <View style={styles.form}>
            <TextInput
              keyboardType="number-pad"
              value={code}
              onChangeText={handleCodeChange}
              placeholder="000000"
              placeholderTextColor={tokens.colors.text.dim}
              maxLength={6}
              style={[styles.input, styles.codeInput, { backgroundColor: tokens.colors.background, borderColor: tokens.colors.borderLight, color: tokens.colors.text.primary }]}
              autoFocus
            />

            {error ? <Text style={[styles.errorText, { color: tokens.colors.error }]}>{error}</Text> : null}

            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                handleVerifyCode();
              }}
              disabled={loading || code.length !== 6}
              style={[styles.primaryBtn, { backgroundColor: tokens.colors.primary }, (loading || code.length !== 6) && { opacity: 0.5 }]}
            >
              {loading ? (
                <ActivityIndicator color={tokens.colors.isDark ? "#000" : "#FFF"} />
              ) : (
                <>
                  <Text allowFontScaling={false} style={[styles.btnText, { color: tokens.colors.isDark ? "#000" : "#FFF" }]}>ПІДТВЕРДИТИ</Text>
                  <ArrowRight size={20} color={tokens.colors.isDark ? "#000" : "#FFF"} />
                </>
              )}
            </Pressable>

            <Pressable onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
              setStep("phone");
            }} style={styles.backBtn}>
              <Text allowFontScaling={false} style={[styles.backBtnText, { color: tokens.colors.text.dim }]}>Змінити номер</Text>
            </Pressable>
          </View>
        </View>
      )}

      {step === "security_setup" && (
        <View style={styles.content}>
          <View style={styles.header}>
            <View style={[styles.iconBox, { borderColor: tokens.colors.primary }]}>
              <ActivityIndicator color={tokens.colors.primary} size="large" />
            </View>
            <Text allowFontScaling={false} style={[styles.title, { color: tokens.colors.text.primary }]}>БЕЗПЕКА</Text>
            <Text allowFontScaling={false} style={[styles.subtitle, { color: tokens.colors.text.dim }]}>Прив'язка пристрою та налаштування біометрії...</Text>
          </View>
        </View>
      )}

      {step === "success" && (
        <View style={styles.successBox}>
          <View style={[styles.iconBox, { backgroundColor: tokens.colors.primary, borderColor: tokens.colors.primary }]}>
            <Check size={40} color={tokens.colors.isDark ? "#000" : "#FFF"} />
          </View>
          <Text allowFontScaling={false} style={[styles.title, { color: tokens.colors.text.primary }]}>УСПІШНО</Text>
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
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 24,
  },
  title: {
    fontSize: 24,
    fontFamily: 'Rajdhani-Bold',
    textAlign: 'center',
    textTransform: 'uppercase',
  },
  subtitle: {
    fontSize: 14,
    fontFamily: 'Inter-Medium',
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
    fontFamily: 'Inter-Bold',
    marginBottom: 8,
    letterSpacing: 1,
    textTransform: 'uppercase',
  },
  input: {
    width: '100%',
    borderWidth: 1,
    borderRadius: 2,
    padding: 18,
    fontSize: 18,
    fontFamily: 'Inter-Black',
  },
  codeInput: {
    fontSize: 32,
    textAlign: 'center',
    letterSpacing: 10,
  },
  primaryBtn: {
    width: '100%',
    paddingVertical: 18,
    borderRadius: 2,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
  },
  btnText: {
    fontSize: 16,
    fontFamily: 'Inter-Black',
    textTransform: 'uppercase',
  },
  errorText: {
    fontSize: 12,
    textAlign: 'center',
    fontFamily: 'Inter-Bold',
  },
  backBtn: {
    alignItems: 'center',
    padding: 10,
  },
  backBtnText: {
    fontSize: 14,
    fontFamily: 'Inter-Medium',
  },
  successBox: {
    alignItems: 'center',
    paddingVertical: 60,
  }
});
