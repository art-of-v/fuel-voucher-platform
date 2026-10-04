import React from 'react';
import { Pressable, View } from 'react-native';

import { BottomSheet, Button, Text, TextField } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { useChangeEmail } from '../hooks/useChangeEmail';

/**
 * The change-email sheet — a two-step, step-up-guarded flow: a fresh OTP to the
 * current channel confirms the request, then the new address is confirmed by an
 * emailed link.
 *
 * The flow state lives in this component rather than in the screen, which is safe
 * for a different reason than in the other two sheets: a `Modal` does not mount
 * its children while hidden, so the hook unmounts on every dismiss and starts
 * fresh next time. That is exactly what the screen's explicit
 * `changeEmail.reset()` call used to achieve, so the reset becomes unnecessary
 * rather than merely relocated — see the note in `app/profile.tsx`.
 */
export interface ChangeEmailSheetProps {
  visible: boolean;
  /** The address the OTP is sent to, if known. */
  currentEmail?: string;
  /** The phone the OTP is sent to, if known. */
  phoneNumber?: string;
  onClose: () => void;
  /** Called once the emailed link confirms the new address. */
  onConfirmed: () => void;
}

export function ChangeEmailSheet({
  visible,
  currentEmail,
  phoneNumber,
  onClose,
  onConfirmed,
}: ChangeEmailSheetProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const changeEmail = useChangeEmail({ currentEmail, phoneNumber, onConfirmed });

  return (
    <BottomSheet
      visible={visible}
      onClose={onClose}
      title={t('profile.changeEmail')}
      footer={
        changeEmail.step === 'email' ? (
          <Button
            label={t('profile.changeEmailSendCode')}
            variant="primary"
            size="lg"
            fullWidth
            loading={changeEmail.isSending}
            onPress={changeEmail.sendCode}
          />
        ) : (
          <Button
            label={t('profile.changeEmailConfirm')}
            variant="primary"
            size="lg"
            fullWidth
            loading={changeEmail.isConfirming}
            onPress={changeEmail.confirm}
          />
        )
      }
    >
      <View style={{ gap: tokens.spacing.lg, paddingBottom: tokens.spacing.lg }}>
        {changeEmail.step === 'email' ? (
          <>
            <Text role="secondary" tone="muted">
              {t('profile.changeEmailStepInfo')}
            </Text>
            <TextField
              label={t('profile.changeEmailNew')}
              value={changeEmail.newEmail}
              onChangeText={(text) => {
                changeEmail.setNewEmail(text);
                if (changeEmail.emailError) changeEmail.setEmailError('');
              }}
              keyboardType="email-address"
              autoCapitalize="none"
              autoFocus
              error={changeEmail.emailError || undefined}
            />
          </>
        ) : (
          <>
            <Text role="secondary" tone="muted">
              {t('profile.changeEmailCodeInfo')}
            </Text>
            <TextField
              label={t('phoneAuth.codeLabel')}
              value={changeEmail.code}
              onChangeText={(text) => {
                changeEmail.setCode(text);
                if (changeEmail.codeError) changeEmail.setCodeError('');
              }}
              placeholder="000000"
              keyboardType="number-pad"
              maxLength={6}
              codeStyle
              autoFocus
              textContentType="oneTimeCode"
              autoComplete="sms-otp"
              error={changeEmail.codeError || undefined}
            />
            <View
              style={{
                flexDirection: 'row',
                justifyContent: 'space-between',
                alignItems: 'center',
              }}
            >
              <Pressable onPress={changeEmail.backToEmail} hitSlop={8}>
                <Text role="bodyStrong" tone="muted">
                  {t('profile.changeEmailBack')}
                </Text>
              </Pressable>
              <Pressable
                onPress={changeEmail.sendCode}
                hitSlop={8}
                disabled={changeEmail.isSending}
              >
                <Text role="bodyStrong" tone="accent">
                  {t('profile.changeEmailResend')}
                </Text>
              </Pressable>
            </View>
          </>
        )}
      </View>
    </BottomSheet>
  );
}
