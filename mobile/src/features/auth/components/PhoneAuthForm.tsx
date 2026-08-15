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
import { Button } from '../../../core/ui';
import { useLogin } from '../hooks/useLogin';

interface PhoneAuthFormProps {
  onSuccess: () => void;
  onBack?: () => void;
}

export function PhoneAuthForm({ onSuccess, onBack }: PhoneAuthFormProps) {
  const tokens = useDesignTokens();
  const filledFg = tokens.colors.isDark ? '#000' : '#FFF';
  const {
    step,
    phone,
    code,
    loading,
    error,
    diagResult,
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
              <Text
                style={[styles.errorText, { color: tokens.colors.error }]}
                numberOfLines={0}
              >
                {error}
              </Text>
            ) : null}

            <Button
              title="НАДІСЛАТИ КОД"
              onPress={handleSendCode}
              loading={loading}
              icon={<ArrowRight size={20} color={filledFg} />}
            />

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
              <Text
                style={[styles.errorText, { color: tokens.colors.error }]}
                numberOfLines={0}
              >
                {error}
              </Text>
            ) : null}

            <Button
              title="ПІДТВЕРДИТИ"
              onPress={handleVerifyCode}
              loading={loading}
              disabled={code.length !== 6}
              icon={<ArrowRight size={20} color={filledFg} />}
            />

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
    borderRadius: 16,
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
    borderRadius: 14,
    padding: 18,
    fontSize: 18,
    fontFamily: 'Inter-Black',
  },
  codeInput: {
    fontSize: 32,
    textAlign: 'center',
    letterSpacing: 10,
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
