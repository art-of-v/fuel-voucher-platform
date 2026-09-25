import { useState } from 'react';
import { z } from 'zod';
import { useI18n } from '../../../core/i18n';
import { useToastStore } from '../../../core/feedback/toastStore';
import { Haptics } from '../../../core/utils/haptics';
import { sendVerificationCode } from '../../auth/api/sendCode';
import { requestEmailChange } from '../api/changeEmail';

const emailSchema = z.string().email();

export type ChangeEmailStep = 'email' | 'code';

/**
 * Two-step state machine for the step-up-guarded self-service email change:
 *
 *   1. `email` — the user types the new address. We validate it locally, then ask the backend to
 *      send a fresh step-up OTP to the account's CURRENT channel (SMS, or email for staff — the
 *      server decides via `/api/auth/send-code`). Nothing is ever sent to the new address here.
 *   2. `code`  — the user enters that OTP. `requestEmailChange` stages the change and emails a
 *      CONFIRMATION LINK to the new address; the active email only changes once that link is
 *      opened, so success means "check your new inbox", not "done".
 *
 * A wrong OTP returns `INVALID_CODE` (backend 403, not 401 — see changeEmail.ts) and is shown
 * inline on the code field so the user can retry without being logged out.
 */
export function useChangeEmail(params: {
  currentEmail?: string | null;
  phoneNumber?: string | null;
  onConfirmed: () => void;
}) {
  const { currentEmail, phoneNumber, onConfirmed } = params;
  const { t } = useI18n();
  const showToast = useToastStore((s) => s.show);

  const [step, setStep] = useState<ChangeEmailStep>('email');
  const [newEmail, setNewEmail] = useState('');
  const [code, setCode] = useState('');
  const [emailError, setEmailError] = useState('');
  const [codeError, setCodeError] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [isConfirming, setIsConfirming] = useState(false);

  const reset = () => {
    setStep('email');
    setNewEmail('');
    setCode('');
    setEmailError('');
    setCodeError('');
    setIsSending(false);
    setIsConfirming(false);
  };

  const backToEmail = () => {
    setCode('');
    setCodeError('');
    setStep('email');
  };

  // Validate the new address and request a step-up code to the current channel. Also used as
  // "resend" from the code step (re-validates and re-sends; the address cannot have changed).
  const sendCode = async () => {
    const email = newEmail.trim();
    if (!emailSchema.safeParse(email).success) {
      setEmailError(t('profile.changeEmailInvalidFormat'));
      return;
    }
    if (currentEmail && email.toLowerCase() === currentEmail.trim().toLowerCase()) {
      setEmailError(t('profile.changeEmailSameEmail'));
      return;
    }
    if (!phoneNumber) {
      showToast({ kind: 'danger', message: t('common.error') });
      return;
    }
    setEmailError('');
    setIsSending(true);
    try {
      await sendVerificationCode(phoneNumber);
      setCode('');
      setCodeError('');
      setStep('code');
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (err: any) {
      showToast({
        kind: 'danger',
        message: err?.status === 429 ? t('profile.changeEmailRateLimited') : err?.message || t('common.error'),
      });
    } finally {
      setIsSending(false);
    }
  };

  const confirm = async () => {
    const trimmedCode = code.trim();
    if (trimmedCode.length < 6) {
      setCodeError(t('profile.changeEmailInvalidCode'));
      return;
    }
    setCodeError('');
    setIsConfirming(true);
    try {
      await requestEmailChange(newEmail.trim(), trimmedCode);
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      // Only staged: the server emailed a confirmation link to the new address and the active
      // email changes when it is opened. Reuse the existing "confirmation sent" copy.
      showToast({ kind: 'success', message: t('profile.emailConfirmSent') });
      onConfirmed();
      reset();
    } catch (err: any) {
      if (err?.code === 'INVALID_CODE') {
        setCodeError(t('profile.changeEmailInvalidCode'));
      } else if (err?.status === 429) {
        showToast({ kind: 'danger', message: t('profile.changeEmailRateLimited') });
      } else {
        showToast({ kind: 'danger', message: err?.message || t('common.error') });
      }
    } finally {
      setIsConfirming(false);
    }
  };

  return {
    step,
    newEmail,
    setNewEmail,
    code,
    setCode,
    emailError,
    setEmailError,
    codeError,
    setCodeError,
    isSending,
    isConfirming,
    sendCode,
    confirm,
    backToEmail,
    reset,
  };
}
