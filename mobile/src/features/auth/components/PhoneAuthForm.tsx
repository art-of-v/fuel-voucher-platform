import React from 'react';
import { View, StyleSheet, Linking, TextInput } from 'react-native';
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
import { TwoFactorSecretField } from './TwoFactorSecretField';

const PRIVACY_POLICY_URL = 'https://palne.shop/privacy/';

interface PhoneAuthFormProps {
  onSuccess: () => void;
  onBack?: () => void;
  /** The phone the code was requested for - pre-filled when the code arrived by push. */
  initialPhone?: string;
  /** The code itself, when it arrived by push instead of over SMS. */
  initialCode?: string;
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
export function PhoneAuthForm({
  onSuccess,
  onBack,
  initialPhone,
  initialCode,
}: PhoneAuthFormProps) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);
  const {
    step,
    phone,
    code,
    twoFactorCode,
    twoFactor,
    loading,
    error,
    setPhone,
    setCode,
    setTwoFactorCode,
    handleSendCode,
    handleVerifyCode,
    handleVerifyTwoFactor,
    resetToPhone,
  } = useLogin(onSuccess, { initialPhone, initialCode });

  // Explicit consent before an account is ever created (the first successful
  // code verification registers the user). The send button stays disabled
  // until it is checked, so no OTP can even be requested without it.
  const [consented, setConsented] = React.useState(false);

  // The code field is focused when a code step appears rather than by `autoFocus` at
  // mount: the two code steps share one field, so the mount happens once and the focus
  // has to follow the step. Doing it in an effect runs it after the commit - by which
  // time the previous keyboard has been dismissed - instead of inside the commit, which
  // is where the swap used to collide.
  const codeInputRef = React.useRef<TextInput>(null);
  React.useEffect(() => {
    if (step === 'code' || step === 'two_factor') {
      codeInputRef.current?.focus();
    }
  }, [step]);

  const iconBox = (icon: React.ReactNode, { filled = false }: { filled?: boolean } = {}) => (
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
              onPress={() => setConsented((v) => !v)}
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
                      : (tokens.colors.border ?? tokens.colors.text.muted),
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

      {/*
        Both code steps are ONE tree, not two. The SMS code and the authenticator code are
        the same interaction - six digits in the same place - and rendering them as two
        separate screens threw the mounted field away at the exact moment the sign-in
        needed it: the swap unmounted a focused input and mounted another one that took
        focus during the same frame, which is the shape of the 2026-10-29 crash reports
        (a native exception on the screen change, then expo-updates error recovery
        re-raising it). Here the field is never replaced, so there is nothing to tear
        down: it clears, re-labels, and takes the keyboard again.
      */}
      {(step === 'code' || step === 'two_factor') && (
        <View style={styles.content}>
          {heading(
            step === 'code' ? t('phoneAuth.enterCodeTitle') : t('phoneAuth.twoFactorTitle'),
            step === 'code' ? `${t('phoneAuth.enterCode')} ${phone}` : t('phoneAuth.twoFactorHint'),
            iconBox(<Lock size={32} color={tokens.colors.primary} />),
          )}

          <View style={[styles.form, { gap: tokens.spacing.xl }]}>
            {/*
              Always rendered, so the input below keeps its position in the tree and is
              never remounted when the enrolment card appears.
            */}
            <View
              style={
                twoFactor?.needsEnrollment && twoFactor.enrollmentSecret
                  ? undefined
                  : styles.hiddenSlot
              }
            >
              {twoFactor?.needsEnrollment && twoFactor.enrollmentSecret ? (
                <TwoFactorSecretField secret={twoFactor.enrollmentSecret} />
              ) : null}
            </View>

            <TextField
              ref={codeInputRef}
              testID="auth-code-field"
              label={step === 'code' ? t('phoneAuth.codeLabel') : t('phoneAuth.twoFactorCodeLabel')}
              value={step === 'code' ? code : twoFactorCode}
              onChangeText={step === 'code' ? setCode : setTwoFactorCode}
              placeholder="000000"
              keyboardType="number-pad"
              maxLength={6}
              codeStyle
              // Lets both platforms offer the SMS code from the message itself, which is
              // the difference between a two-tap and an eight-tap sign-in. The
              // authenticator code comes from another app, not from a message.
              textContentType={step === 'code' ? 'oneTimeCode' : undefined}
              autoComplete={step === 'code' ? 'sms-otp' : undefined}
              disabled={loading}
              // An invalid code *is* a problem with this field's value, so the message is
              // attached to the field on both steps.
              error={error ?? undefined}
            />

            <Button
              label={t('phoneAuth.verify')}
              onPress={step === 'code' ? handleVerifyCode : handleVerifyTwoFactor}
              loading={loading}
              disabled={(step === 'code' ? code : twoFactorCode).length !== 6}
              icon={<ArrowRight />}
            />

            <Button
              // On the code step this changes the number; on the second factor it takes
              // the person back to the start of the sign-in, because a challenge that was
              // spent or expired cannot be redeemed with any code. Same action either
              // way - the label is what says which one it is.
              label={step === 'code' ? t('phoneAuth.changePhone') : t('phoneAuth.startOver')}
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
        <LoadingState message={t('phoneAuth.securityDescription')} variant="block" size="large" />
      )}

      {step === 'success' && (
        <View style={[styles.content, { gap: tokens.spacing['2xl'] }]}>
          {iconBox(<Check size={40} color={tokens.colors.text.onPrimary} />, { filled: true })}
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
  // An empty slot in a `gap` column still consumes a gap, and the enrollment block only
  // exists on the authenticator step. Hiding the empty slot keeps the layout identical
  // while leaving the field at a stable child index - which is what keeps it mounted.
  hiddenSlot: {
    display: 'none',
  },
  consentRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 12,
    // The box is 22pt; the row is the target, so it carries the 44pt minimum.
    minHeight: 44,
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
