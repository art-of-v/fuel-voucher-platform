import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert } from 'react-native';
import {
  cancelInvitation,
  companyErrorKey,
  fireWorker,
  getMembers,
  getSentInvitations,
  giftVouchers,
  recallVoucher,
  sendInvitation,
} from '../api/companyApi';
import { getMyVouchers } from '../../vouchers/api/getVouchers';
import { classifyVoucher } from '../../../core/types/api';
import type { Voucher } from '../../../core/types/api';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { useAuth } from '../../auth/hooks/useAuth';
import { useStore } from '../../../core/state/appStore';

function groupGiftableByProvider(vouchers: Voucher[]): { provider: string; items: Voucher[] }[] {
  const groups: { provider: string; items: Voucher[] }[] = [];
  const byProvider = new Map<string, { provider: string; items: Voucher[] }>();
  for (const v of vouchers) {
    const key = (v.provider || '').toUpperCase() || '—';
    let group = byProvider.get(key);
    if (!group) {
      group = { provider: key, items: [] };
      byProvider.set(key, group);
      groups.push(group);
    }
    group.items.push(v);
  }
  return groups;
}

/**
 * Data layer for the company-management screen: the three list queries, the five
 * worker/voucher mutations, and the values derived from them. UI state (the invite
 * form, the gift modal target, the checkbox selection, the pull-to-refresh spinner)
 * stays in the screen. The two mutations that clear that UI state on success take
 * callbacks so the hook stays free of screen concerns.
 */
export function useCompany(callbacks?: {
  onInviteSuccess?: () => void;
  onGiftSuccess?: () => void;
}) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const { isAuthenticated: hookAuth, isLoading: authLoading, user } = useAuth();
  const storeAuth = useStore((state) => state.isAuthenticated);
  const isAuthenticated = storeAuth || hookAuth;

  const invitationsQuery = useQuery({
    queryKey: ['company', 'invitations'],
    queryFn: getSentInvitations,
    enabled: isAuthenticated,
    retry: false,
  });
  const membersQuery = useQuery({
    queryKey: ['company', 'members'],
    queryFn: getMembers,
    enabled: isAuthenticated,
    retry: false,
  });
  const vouchersQuery = useQuery({
    queryKey: ['vouchers', 'my'],
    queryFn: getMyVouchers,
    enabled: isAuthenticated,
    retry: false,
  });

  const invitations = invitationsQuery.data ?? [];
  const members = membersQuery.data ?? [];
  const allVouchers = vouchersQuery.data ?? [];
  const giftable = allVouchers.filter((v) => classifyVoucher(v, user?.id) === 'company_pool');
  const gifted = allVouchers.filter((v) => classifyVoucher(v, user?.id) === 'gifted_to_worker');
  const pendingInvites = invitations.filter((i) => (i.status || '').toLowerCase() === 'pending');
  const hasQueryError = invitationsQuery.isError || membersQuery.isError || vouchersQuery.isError;
  const giftGroups = groupGiftableByProvider(giftable);

  const showError = (err: unknown) => {
    Alert.alert(t('common.error'), t(companyErrorKey(err)));
  };

  const inviteMutation = useMutation({
    mutationFn: (workerPhone: string) => sendInvitation(workerPhone),
    onSuccess: () => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      callbacks?.onInviteSuccess?.();
      queryClient.invalidateQueries({ queryKey: ['company', 'invitations'] });
      Alert.alert(t('company.invite.sentTitle'), t('company.invite.sentDesc'));
    },
    onError: showError,
  });

  const cancelMutation = useMutation({
    mutationFn: (id: string) => cancelInvitation(id),
    onSuccess: () => {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
      queryClient.invalidateQueries({ queryKey: ['company', 'invitations'] });
    },
    onError: showError,
  });

  const fireMutation = useMutation({
    mutationFn: (memberId: string) => fireWorker(memberId),
    onSuccess: (res) => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.invalidateQueries({ queryKey: ['company', 'members'] });
      queryClient.invalidateQueries({ queryKey: ['vouchers', 'my'] });
      Alert.alert(
        t('company.fire.doneTitle'),
        t('company.fire.doneDesc', String(res.blockedVoucherCount ?? 0)),
      );
    },
    onError: showError,
  });

  const giftMutation = useMutation({
    mutationFn: (vars: { workerUserId: string; voucherIds: string[] }) =>
      giftVouchers(vars.workerUserId, vars.voucherIds),
    onSuccess: (res) => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      callbacks?.onGiftSuccess?.();
      queryClient.invalidateQueries({ queryKey: ['company', 'members'] });
      queryClient.invalidateQueries({ queryKey: ['vouchers', 'my'] });
      Alert.alert(
        t('company.gift.doneTitle'),
        t('company.gift.doneDesc', String(res.giftedCount ?? 0)),
      );
    },
    onError: showError,
  });

  const recallMutation = useMutation({
    mutationFn: (voucherId: string) => recallVoucher(voucherId),
    onSuccess: () => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.invalidateQueries({ queryKey: ['company', 'members'] });
      queryClient.invalidateQueries({ queryKey: ['vouchers', 'my'] });
    },
    onError: showError,
  });

  const refreshAll = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['company', 'invitations'] }),
      queryClient.invalidateQueries({ queryKey: ['company', 'members'] }),
      queryClient.invalidateQueries({ queryKey: ['vouchers', 'my'] }),
    ]);
  };

  const isLoading = invitationsQuery.isLoading || membersQuery.isLoading;

  return {
    // auth
    isAuthenticated,
    authLoading,
    // status
    isLoading,
    hasQueryError,
    // data
    invitations,
    members,
    giftable,
    gifted,
    pendingInvites,
    giftGroups,
    // actions
    invite: (workerPhone: string) => inviteMutation.mutate(workerPhone),
    cancelInvite: (id: string) => cancelMutation.mutate(id),
    fire: (memberId: string) => fireMutation.mutate(memberId),
    gift: (workerUserId: string, voucherIds: string[]) =>
      giftMutation.mutate({ workerUserId, voucherIds }),
    recall: (voucherId: string) => recallMutation.mutate(voucherId),
    refreshAll,
    // pending flags
    isInviting: inviteMutation.isPending,
    isCancelling: cancelMutation.isPending,
    isFiring: fireMutation.isPending,
    isGifting: giftMutation.isPending,
    isRecalling: recallMutation.isPending,
  };
}
