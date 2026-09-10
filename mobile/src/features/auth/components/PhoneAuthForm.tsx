import React from 'react';
import { View, StyleSheet, Linking } from 'react-native';
import { Phone, ArrowRight, Lock, Check } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import {
  Button,
  InlineFeedback,
  LoadingState,
  PressableScale,
  Text,
  TextField,
} from '../../../core/ui';
import { useLogin } from '../hooks/useLogin';

const PRIVACY_POLICY_URL = 'https://palne.shop/privacy/';

interface PhoneAuthFormProps {
  onSuccess: () => void;
  onBack?: () => void;
}

/**
 * Phone + SMS-code sign-in.
 *
 * Phase 2 migrated this form onto the design system without changing the flow:
 * the two hand-rolled `TextInput`s became `TextField` (which owns focus, error and
 * disabled states), the raw red error `<Text>` became `InlineFeedback`, the typography
 * went through `Text`, and the copy — previously hardcoded Ukrainian in a
 * four-language app — now uses the `phoneAuth.*` keys that already existed.
 */
export function PhoneAuthForm({ onSuccess, onBack }: PhoneAuthFormProps) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);
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

  // Explicit consent before an account is ever created (the first successful
  // code verification registers the user). The send button stays disabled
  // until it is checked, so no OTP can even be requested without it.
  const [consented, setConsented] = React.useState(false);

  const iconBox = (
    icon: React.ReactNode,
    { filled = false }: { filled?: boolean } = {},
  ) => (
    <View
      style={[
        styles.iconBox,
        {
          borderColor: tokens.colors.primary,
          borderRadius: tokens.radius.md,
          backgroundColor: filled ? tokens.colors.primary : 'transparent',
        },
      ]}
    >
      {icon}
    </View>
  );

  const heading = (title: string, subtitle: string, icon: React.ReactNode) => (
    <View style={[styles.header, { gap: tokens.spacing.sm }]}>
      {icon}
      <Text role="title" center>
        {title}
      </Text>
      <Text role="body" tone="secondary" center>
        {subtitle}
      </Text>
    </View>
  );

  return (
    <View style={[styles.container, { paddingHorizontal: tokens.spacing.xl }]}>
      {step === 'phone' && (
        <View style={styles.content}>
          {heading(
            t('phoneAuth.title'),
            t('phoneAuth.enterPhone'),
            iconBox(<Phone size={32} color={tokens.colors.primary} />),
          )}

          <View style={[styles.form, { gap: tokens.spacing.xl }]}>
            <TextField
              label={t('phoneAuth.phoneLabel')}
              value={phone}
              onChangeText={setPhone}
              placeholder="+380XXXXXXXXX"
              keyboardType="phone-pad"
              autoComplete="tel"
              textContentType="telephoneNumber"
              disabled={loading}
            />

            {/*
              The send step can fail for reasons that are not about the value the
              user typed (network, SMS gateway), so the message belongs to the
              form, not the field. The code step below is the opposite case.
            */}
            {error ? <InlineFeedback kind="danger" message={error} /> : null}

            <PressableScale
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                setConsented((v) => !v);
              }}
              style={styles.consentRow}
              accessibilityRole="checkbox"
              accessibilityState={{ checked: consented }}
            >
              <View
                style={[
                  styles.consentBox,
                  {
                    borderColor: consented
                      ? tokens.colors.primary
                      : tokens.colors.border ?? tokens.colors.text.muted,
                    backgroundColor: consented ? tokens.colors.primary : 'transparent',
                    borderRadius: tokens.radius.sm,
                  },
                ]}
              >
                {consented ? (
                  <Check size={16} color={tokens.colors.text.onPrimary} strokeWidth={3} />
                ) : null}
              </View>
              <Text role="caption" tone="secondary" style={styles.consentText}>
                {t('phoneAuth.consentPrefix')}{' '}
                <Text
                  role="caption"
                  style={{ color: tokens.colors.primary, textDecorationLine: 'underline' }}
                  onPress={() => {
                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                    void Linking.openURL(PRIVACY_POLICY_URL);
                  }}
                >
                  {t('phoneAuth.consentLink')}
                </Text>
              </Text>
            </PressableScale>

            <Button
              label={t('phoneAuth.sendCode')}
              onPress={handleSendCode}
              loading={loading}
              // No consent - no OTP request, so no account can be created
              // before the user has explicitly agreed to the policy.
              disabled={!consented}
              icon={<ArrowRight />}
            />

            {onBack ? (
              <Button
                label={t('common.back')}
                variant="ghost"
                size="md"
                hapticStyle="light"
                onPress={onBack}
              />
            ) : null}
          </View>
        </View>
      )}

      {step === 'code' && (
        <View style={styles.content}>
          {heading(
            t('phoneAuth.enterCodeTitle'),
            `${t('phoneAuth.enterCode')} ${phone}`,
            iconBox(<Lock size={32} color={tokens.colors.primary} />),
          )}

          <View style={[styles.form, { gap: tokens.spacing.xl }]}>
            <TextField
              label={t('phoneAuth.codeLabel')}
              value={code}
              onChangeText={setCode}
              placeholder="000000"
              keyboardType="number-pad"
              maxLength={6}
              codeStyle
              autoFocus
              // Lets both platforms offer the code from the SMS itself, which is
              // the difference between a two-tap and an eight-tap sign-in.
              textContentType="oneTimeCode"
              autoComplete="sms-otp"
              disabled={loading}
              // An invalid code *is* a problem with this field's value, so the
              // message is attached to the field.
              error={error ?? undefined}
            />

            <Button
              label={t('phoneAuth.verify')}
              onPress={handleVerifyCode}
              loading={loading}
              disabled={code.length !== 6}
              icon={<ArrowRight />}
            />

            <Button
              label={t('phoneAuth.changePhone')}
              variant="ghost"
              size="md"
              hapticStyle="light"
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                resetToPhone();
              }}
            />
          </View>
        </View>
      )}

      {step === 'security_setup' && (
        <LoadingState
          message={t('phoneAuth.securityDescription')}
          variant="block"
          size="large"
        />
      )}

      {step === 'success' && (
        <View style={[styles.content, { gap: tokens.spacing['2xl'] }]}>
          {iconBox(
            <Check size={40} color={tokens.colors.text.onPrimary} />,
            { filled: true },
          )}
          <Text role="title" center>
            {t('phoneAuth.success')}
          </Text>
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    width: '100%',
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
    marginBottom: 8,
  },
  form: {
    width: '100%',
  },
  consentRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 12,
  },
  consentBox: {
    width: 22,
    height: 22,
    borderWidth: 1.5,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 1,
  },
  consentText: {
    flex: 1,
    lineHeight: 20,
  },
});
