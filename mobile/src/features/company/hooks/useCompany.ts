import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert } from 'react-native';
import {
  blockWorkerVoucher,
  cancelInvitation,
  companyErrorKey,
  fireWorker,
  getMembers,
  getSentInvitations,
  giftVouchers,
  recallVoucher,
  sendInvitation,
  unblockWorkerVoucher,
} from '../api/companyApi';
import { getMyVouchers } from '../../vouchers/api/getVouchers';
import { classifyVoucher } from '../../../core/types/api';
import type { Voucher } from '../../../core/types/api';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { useAuth } from '../../auth/hooks/useAuth';
import { useStore } from '../../../core/state/appStore';
import { useLegalEntities } from './useLegalEntities';
import { resolveCurrentCompany } from '../lib/context';

/**
 * Narrows the user's vouchers to the active company context (#103 S3a). With a
 * resolved company id, only that entity's vouchers remain; with `null` (personal
 * context or an unowned/stale id that fell back to the owner's oldest entity on
 * the backend) the list is returned unchanged — classifyVoucher then keeps only
 * the company-pool/gifted ones, preserving pre-S3a single-company behaviour.
 */
function scopedVouchers(vouchers: Voucher[], ownedEntityId: string | null): Voucher[] {
  if (ownedEntityId == null) return vouchers;
  return vouchers.filter((v) => v.legalEntityId === ownedEntityId);
}

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

  // Multi-company (epic #103 S3a): every owner action is scoped to the active
  // company context. We resolve the stored context id against the entities the
  // user actually OWNS — a personal context, or a company the user is only a
  // member of, resolves to `null`, and the backend then falls back to the
  // owner's oldest entity (back-compat). Threading the id into the query keys
  // makes switching companies refetch the right roster/stock automatically.
  const currentLegalEntityId = useStore((state) => state.currentLegalEntityId);
  const { companies } = useLegalEntities();
  const ownedEntityId = resolveCurrentCompany(currentLegalEntityId, companies)?.id ?? null;

  const invitationsQuery = useQuery({
    queryKey: ['company', 'invitations', ownedEntityId],
    queryFn: () => getSentInvitations(ownedEntityId),
    enabled: isAuthenticated,
    retry: false,
  });
  const membersQuery = useQuery({
    queryKey: ['company', 'members', ownedEntityId],
    queryFn: () => getMembers(ownedEntityId),
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
  // Only the ACTIVE company's vouchers are giftable/recallable — scoping here
  // keeps the UI consistent with the backend, which rejects a gift whose voucher
  // belongs to a different entity than the one the action targets (#103 S3a).
  const contextVouchers = scopedVouchers(allVouchers, ownedEntityId);
  const giftable = contextVouchers.filter((v) => classifyVoucher(v, user?.id) === 'company_pool');
  const gifted = contextVouchers.filter((v) => classifyVoucher(v, user?.id) === 'gifted_to_worker');
  // Vouchers the owner froze on a worker (#103 S3b). The backend only surfaces a
  // Blocked voucher to the owner, so these are owner-side; classifyVoucher routes
  // them to 'blocked' (not 'gifted_to_worker') regardless of the worker link.
  const blocked = contextVouchers.filter((v) => classifyVoucher(v, user?.id) === 'blocked');
  const pendingInvites = invitations.filter((i) => (i.status || '').toLowerCase() === 'pending');
  const hasQueryError = invitationsQuery.isError || membersQuery.isError || vouchersQuery.isError;
  const giftGroups = groupGiftableByProvider(giftable);

  const showError = (err: unknown) => {
    Alert.alert(t('common.error'), t(companyErrorKey(err)));
  };

  const inviteMutation = useMutation({
    mutationFn: (workerPhone: string) => sendInvitation(workerPhone, ownedEntityId),
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
    mutationFn: (memberId: string) => fireWorker(memberId, ownedEntityId),
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
      giftVouchers(vars.workerUserId, vars.voucherIds, ownedEntityId),
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
    mutationFn: (voucherId: string) => recallVoucher(voucherId, ownedEntityId),
    onSuccess: () => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.invalidateQueries({ queryKey: ['company', 'members'] });
      queryClient.invalidateQueries({ queryKey: ['vouchers', 'my'] });
    },
    onError: showError,
  });

  // #103 S3b: freeze a worker's voucher (Assigned→Blocked) and thaw it back
  // (Blocked→Assigned), keeping the worker link both ways. Both refresh the
  // wallet so the badge/action flip immediately.
  const blockMutation = useMutation({
    mutationFn: (voucherId: string) => blockWorkerVoucher(voucherId, ownedEntityId),
    onSuccess: () => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      queryClient.invalidateQueries({ queryKey: ['vouchers', 'my'] });
    },
    onError: showError,
  });

  const unblockMutation = useMutation({
    mutationFn: (voucherId: string) => unblockWorkerVoucher(voucherId, ownedEntityId),
    onSuccess: () => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
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
    blocked,
    pendingInvites,
    giftGroups,
    // actions
    invite: (workerPhone: string) => inviteMutation.mutate(workerPhone),
    cancelInvite: (id: string) => cancelMutation.mutate(id),
    fire: (memberId: string) => fireMutation.mutate(memberId),
    gift: (workerUserId: string, voucherIds: string[]) =>
      giftMutation.mutate({ workerUserId, voucherIds }),
    recall: (voucherId: string) => recallMutation.mutate(voucherId),
    block: (voucherId: string) => blockMutation.mutate(voucherId),
    unblock: (voucherId: string) => unblockMutation.mutate(voucherId),
    refreshAll,
    // pending flags
    isInviting: inviteMutation.isPending,
    isCancelling: cancelMutation.isPending,
    isFiring: fireMutation.isPending,
    isGifting: giftMutation.isPending,
    isRecalling: recallMutation.isPending,
    isBlocking: blockMutation.isPending,
    isUnblocking: unblockMutation.isPending,
  };
}
