import React from 'react';
import {
  View,
  Text,
  Pressable,
  TextInput,
  ActivityIndicator,
  StyleSheet,
} from 'react-native';
import {
  Phone,
  ArrowRight,
  Lock,
  Check,
} from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { Haptics } from '../../../core/utils/haptics';
import { useLogin } from '../hooks/useLogin';

interface PhoneAuthFormProps {
  onSuccess: () => void;
  onBack?: () => void;
}

export function PhoneAuthForm({ onSuccess, onBack }: PhoneAuthFormProps) {
  const tokens = useDesignTokens();
  const {
    step,
    phone,
    code,
    loading,
    error,
    setPhone,
    setCode,
    handleSendCode,
    handleVerifyCode,
    resetToPhone,
  } = useLogin(onSuccess);

  return (
    <View style={styles.container}>
      {step === 'phone' && (
        <View style={styles.content}>
          <View style={styles.header}>
            <View style={[styles.iconBox, { borderColor: tokens.colors.primary }]}>
              <Phone size={32} color={tokens.colors.primary} />
            </View>
            <Text
              allowFontScaling={false}
              style={[styles.title, { color: tokens.colors.text.primary }]}
            >
              ВХІД ЗА ТЕЛЕФОНОМ
            </Text>
            <Text
              allowFontScaling={false}
              style={[styles.subtitle, { color: tokens.colors.text.dim }]}
            >
              Введіть номер телефону для отримання коду
            </Text>
          </View>

          <View style={styles.form}>
            <View style={styles.inputGroup}>
              <Text
                allowFontScaling={false}
                style={[styles.label, { color: tokens.colors.text.dim }]}
              >
                НОМЕР ТЕЛЕФОНУ
              </Text>
              <TextInput
                keyboardType="phone-pad"
                value={phone}
                onChangeText={setPhone}
                placeholder="+380XXXXXXXXX"
                placeholderTextColor={tokens.colors.text.dim}
                style={[
                  styles.input,
                  {
                    backgroundColor: tokens.colors.background,
                    borderColor: tokens.colors.borderLight,
                    color: tokens.colors.text.primary,
                  },
                ]}
              />
            </View>

            {error ? (
              <Text style={[styles.errorText, { color: tokens.colors.error }]}>
                {error}
              </Text>
            ) : null}

            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                handleSendCode();
              }}
              disabled={loading}
              style={[styles.primaryBtn, { backgroundColor: tokens.colors.primary }]}
            >
              {loading ? (
                <ActivityIndicator
                  color={tokens.colors.isDark ? '#000' : '#FFF'}
                />
              ) : (
                <>
                  <Text
                    allowFontScaling={false}
                    style={[
                      styles.btnText,
                      { color: tokens.colors.isDark ? '#000' : '#FFF' },
                    ]}
                  >
                    НАДІСЛАТИ КОД
                  </Text>
                  <ArrowRight
                    size={20}
                    color={tokens.colors.isDark ? '#000' : '#FFF'}
                  />
                </>
              )}
            </Pressable>

            {onBack && (
              <Pressable
                onPress={() => {
                  Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                  onBack();
                }}
                style={styles.backBtn}
              >
                <Text
                  allowFontScaling={false}
                  style={[styles.backBtnText, { color: tokens.colors.text.dim }]}
                >
                  Назад
                </Text>
              </Pressable>
            )}
          </View>
        </View>
      )}

      {step === 'code' && (
        <View style={styles.content}>
          <View style={styles.header}>
            <View style={[styles.iconBox, { borderColor: tokens.colors.primary }]}>
              <Lock size={32} color={tokens.colors.primary} />
            </View>
            <Text
              allowFontScaling={false}
              style={[styles.title, { color: tokens.colors.text.primary }]}
            >
              ПІДТВЕРДЖЕННЯ
            </Text>
            <Text
              allowFontScaling={false}
              style={[styles.subtitle, { color: tokens.colors.text.dim }]}
            >
              Введіть код, надісланий на {phone}
            </Text>
          </View>

          <View style={styles.form}>
            <TextInput
              keyboardType="number-pad"
              value={code}
              onChangeText={setCode}
              placeholder="000000"
              placeholderTextColor={tokens.colors.text.dim}
              maxLength={6}
              style={[
                styles.input,
                styles.codeInput,
                {
                  backgroundColor: tokens.colors.background,
                  borderColor: tokens.colors.borderLight,
                  color: tokens.colors.text.primary,
                },
              ]}
              autoFocus
            />

            {error ? (
              <Text style={[styles.errorText, { color: tokens.colors.error }]}>
                {error}
              </Text>
            ) : null}

            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                handleVerifyCode();
              }}
              disabled={loading || code.length !== 6}
              style={[
                styles.primaryBtn,
                { backgroundColor: tokens.colors.primary },
                (loading || code.length !== 6) && { opacity: 0.5 },
              ]}
            >
              {loading ? (
                <ActivityIndicator
                  color={tokens.colors.isDark ? '#000' : '#FFF'}
                />
              ) : (
                <>
                  <Text
                    allowFontScaling={false}
                    style={[
                      styles.btnText,
                      { color: tokens.colors.isDark ? '#000' : '#FFF' },
                    ]}
                  >
                    ПІДТВЕРДИТИ
                  </Text>
                  <ArrowRight
                    size={20}
                    color={tokens.colors.isDark ? '#000' : '#FFF'}
                  />
                </>
              )}
            </Pressable>

            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                resetToPhone();
              }}
              style={styles.backBtn}
            >
              <Text
                allowFontScaling={false}
                style={[styles.backBtnText, { color: tokens.colors.text.dim }]}
              >
                Змінити номер
              </Text>
            </Pressable>
          </View>
        </View>
      )}

      {step === 'security_setup' && (
        <View style={styles.content}>
          <View style={styles.header}>
            <View style={[styles.iconBox, { borderColor: tokens.colors.primary }]}>
              <ActivityIndicator color={tokens.colors.primary} size="large" />
            </View>
            <Text
              allowFontScaling={false}
              style={[styles.title, { color: tokens.colors.text.primary }]}
            >
              БЕЗПЕКА
            </Text>
            <Text
              allowFontScaling={false}
              style={[styles.subtitle, { color: tokens.colors.text.dim }]}
            >
              Прив'язка пристрою та налаштування біометрії...
            </Text>
          </View>
        </View>
      )}

      {step === 'success' && (
        <View style={styles.successBox}>
          <View
            style={[
              styles.iconBox,
              {
                backgroundColor: tokens.colors.primary,
                borderColor: tokens.colors.primary,
              },
            ]}
          >
            <Check size={40} color={tokens.colors.isDark ? '#000' : '#FFF'} />
          </View>
          <Text
            allowFontScaling={false}
            style={[styles.title, { color: tokens.colors.text.primary }]}
          >
            УСПІШНО
          </Text>
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
  },
});
