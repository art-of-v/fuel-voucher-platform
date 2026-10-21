import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useI18n } from '../../../core/i18n';
import { useToastStore } from '../../../core/feedback/toastStore';
import { USER_QUERY_KEY, useAuth } from '../../auth/hooks/useAuth';
import { ReferralApiError, createReferralCode, redeemReferralCode } from '../api/referralApi';
import type { ReferralErrorCode } from '../types';

/**
 * The referral screen's data: the two codes, and the two writes that change them.
 *
 * The codes themselves are read from the signed-in customer
 * (`GET /api/auth/user/me` carries `referralCode` and `referredBy`), so there is
 * nothing extra to fetch — the screen's truth is the same truth the profile
 * screen shows. A write therefore invalidates that query rather than keeping a
 * second copy of the codes in local state, which is how a screen ends up
 * showing a code the database no longer holds.
 *
 * Both writes share one rate-limit budget — five an hour, create and redeem
 * together — so the hook fires them only when the customer asks.
 */

/** The failure of a referral write, in this client's vocabulary. */
function codeOf(error: unknown): ReferralErrorCode {
  return error instanceof ReferralApiError ? error.code : 'unknown';
}

/** How long the rate limiter asked the customer to wait, when it said. */
function retryAfterOf(error: unknown): number | undefined {
  return error instanceof ReferralApiError ? error.retryAfterSeconds : undefined;
}

export function useReferral() {
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const showToast = useToastStore((s) => s.show);
  const { user } = useAuth();

  const [createError, setCreateError] = useState<ReferralErrorCode | null>(null);
  const [redeemError, setRedeemError] = useState<ReferralErrorCode | null>(null);
  const [retryAfterSeconds, setRetryAfterSeconds] = useState<number | undefined>(undefined);

  const refreshCustomer = () => queryClient.invalidateQueries({ queryKey: [USER_QUERY_KEY] });

  const createMutation = useMutation({
    mutationFn: createReferralCode,
    onSuccess: async () => {
      setCreateError(null);
      setRetryAfterSeconds(undefined);
      await refreshCustomer();
      showToast({ kind: 'success', message: t('referral.created') });
    },
    onError: (error) => {
      setCreateError(codeOf(error));
      setRetryAfterSeconds(retryAfterOf(error));
    },
  });

  const redeemMutation = useMutation({
    mutationFn: redeemReferralCode,
    onSuccess: async () => {
      setRedeemError(null);
      setRetryAfterSeconds(undefined);
      await refreshCustomer();
      showToast({ kind: 'success', message: t('referral.redeemed') });
    },
    onError: (error) => {
      setRedeemError(codeOf(error));
      setRetryAfterSeconds(retryAfterOf(error));
    },
  });

  return {
    /** The code this customer hands out. `null` until they choose one. */
    myCode: user?.referralCode ?? null,
    /** The code of whoever referred this customer. `null` until they redeem one. */
    referredBy: user?.referredBy ?? null,
    createCode: (code: string) => createMutation.mutate(code),
    redeemCode: (code: string) => redeemMutation.mutate(code),
    isCreating: createMutation.isPending,
    isRedeeming: redeemMutation.isPending,
    createError,
    redeemError,
    retryAfterSeconds,
  };
}
