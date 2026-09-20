import { useState } from 'react';
import {
  View,
  Text,
  Pressable,
  TextInput,
  ScrollView,
  ActivityIndicator,
  StyleSheet,
  Alert,
  Modal,
  RefreshControl,
} from 'react-native';
import { Redirect } from 'expo-router';
import {
  UserPlus,
  Users,
  Send,
  Gift,
  Trash2,
  X,
  Check,
  RotateCcw,
  Clock,
  AlertTriangle,
  CheckSquare,
  Square,
} from 'lucide-react-native';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getSentInvitations,
  sendInvitation,
  cancelInvitation,
  getMembers,
  fireWorker,
  giftVouchers,
  recallVoucher,
  companyErrorKey,
} from '../src/features/company/api/companyApi';
import type { CompanyInvitationDto, CompanyMemberDto } from '../src/features/company/types';
import { getMyVouchers } from '../src/features/vouchers/api/getVouchers';
import { classifyVoucher } from '../src/core/types/api';
import type { Voucher } from '../src/core/types/api';
import { GridPageLayout, ScreenHeader, LoadingState, useContentInsets } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { formatExpirationDate } from '../src/core/utils/formatters';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { useStore } from '../src/core/state/appStore';

function invitationStatusKey(status: string): string {
  switch ((status || '').toLowerCase()) {
    case 'pending':
      return 'company.status.pending';
    case 'accepted':
      return 'company.status.accepted';
    case 'declined':
      return 'company.status.declined';
    case 'cancelled':
    case 'canceled':
      return 'company.status.cancelled';
    default:
      return 'company.status.pending';
  }
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

export default function CompanyScreen() {
  const tokens = useDesignTokens();
  const contentInsets = useContentInsets();
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const { isAuthenticated: hookAuth, isLoading: authLoading, user } = useAuth();
  const storeAuth = useStore(state => state.isAuthenticated);
  const isAuthenticated = storeAuth || hookAuth;

  const [phone, setPhone] = useState('');
  const [giftTarget, setGiftTarget] = useState<CompanyMemberDto | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [refreshing, setRefreshing] = useState(false);

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
  const giftable = allVouchers.filter(v => classifyVoucher(v, user?.id) === 'company_pool');
  const gifted = allVouchers.filter(v => classifyVoucher(v, user?.id) === 'gifted_to_worker');
  const pendingInvites = invitations.filter(i => (i.status || '').toLowerCase() === 'pending');
  const hasQueryError = invitationsQuery.isError || membersQuery.isError || vouchersQuery.isError;
  const giftGroups = groupGiftableByProvider(giftable);
  const allGiftableSelected = giftable.length > 0 && giftable.every(v => selected.has(v.id));

  const showError = (err: unknown) => {
    Alert.alert(t('common.error'), t(companyErrorKey(err)));
  };

  const inviteMutation = useMutation({
    mutationFn: (workerPhone: string) => sendInvitation(workerPhone),
    onSuccess: () => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setPhone('');
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
      Alert.alert(t('company.fire.doneTitle'), t('company.fire.doneDesc', String(res.blockedVoucherCount ?? 0)));
    },
    onError: showError,
  });

  const giftMutation = useMutation({
    mutationFn: (vars: { workerUserId: string; voucherIds: string[] }) =>
      giftVouchers(vars.workerUserId, vars.voucherIds),
    onSuccess: (res) => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setGiftTarget(null);
      setSelected(new Set());
      queryClient.invalidateQueries({ queryKey: ['company', 'members'] });
      queryClient.invalidateQueries({ queryKey: ['vouchers', 'my'] });
      Alert.alert(t('company.gift.doneTitle'), t('company.gift.doneDesc', String(res.giftedCount ?? 0)));
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

  const memberName = (m: CompanyMemberDto) =>
    [m.workerFirstName, m.workerLastName].filter(Boolean).join(' ').trim() || m.workerPhoneNumber;

  const invitationName = (inv: CompanyInvitationDto) =>
    [inv.workerFirstName, inv.workerLastName].filter(Boolean).join(' ').trim() || inv.workerPhoneNumber;

  const confirmFire = (m: CompanyMemberDto) => {
    Alert.alert(
      t('company.fire.confirmTitle'),
      t('company.fire.confirmDesc', memberName(m)),
      [
        { text: t('common.cancel'), style: 'cancel' },
        { text: t('company.fire.confirm'), style: 'destructive', onPress: () => fireMutation.mutate(m.id) },
      ],
    );
  };

  const confirmRecall = (v: Voucher) => {
    Alert.alert(
      t('company.recall.confirmTitle'),
      t('company.recall.confirmDesc'),
      [
        { text: t('common.cancel'), style: 'cancel' },
        { text: t('company.recall.confirm'), style: 'destructive', onPress: () => recallMutation.mutate(v.id) },
      ],
    );
  };

  const toggleSelectAll = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setSelected(allGiftableSelected ? new Set() : new Set(giftable.map(v => v.id)));
  };

  const refreshAll = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['company', 'invitations'] }),
      queryClient.invalidateQueries({ queryKey: ['company', 'members'] }),
      queryClient.invalidateQueries({ queryKey: ['vouchers', 'my'] }),
    ]);
  };

  const toggleSelected = (id: string) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const openGift = (m: CompanyMemberDto) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    setSelected(new Set());
    setGiftTarget(m);
  };

  const Header = <ScreenHeader title={t('company.managementTitle')} />;

  if (!isAuthenticated && !authLoading) {
    return <Redirect href="/landing" />;
  }

  const isLoading = invitationsQuery.isLoading || membersQuery.isLoading;
  if (isLoading) {
    // Inside `PageLayout`, not instead of it: the previous bare centred `View`
    // dropped the header, the safe-area handling and the background for the
    // duration of the load, so the screen visibly re-assembled itself.
    return (
      <GridPageLayout header={Header} disableScroll>
        <LoadingState fullScreen />
      </GridPageLayout>
    );
  }

  const workerLabel = giftTarget ? memberName(giftTarget) : '';

  return (
    <GridPageLayout header={Header} disableScroll>
      <ScrollView
        style={{ flex: 1 }}
        // This screen owns its scroller (it needs the refresh control), so it
        // reads the same derived clearance PageLayout would have applied.
        contentContainerStyle={{
          paddingHorizontal: 20,
          paddingTop: 10,
          paddingBottom: contentInsets.bottom,
        }}
        keyboardShouldPersistTaps="handled"
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={async () => {
              setRefreshing(true);
              await refreshAll();
              setRefreshing(false);
            }}
            tintColor={tokens.colors.primary}
            colors={[tokens.colors.primary]}
          />
        }
      >
        {/* Stats header */}
        <View style={styles.statsRow}>
          <View style={[styles.statCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
            <Users size={16} color={tokens.colors.primary} />
            <Text style={[styles.statValue, { color: tokens.colors.text.primary }]}>{members.length}</Text>
            <Text style={[styles.statLabel, { color: tokens.colors.text.dim }]}>{t('company.stats.members')}</Text>
          </View>
          <View style={[styles.statCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
            <Clock size={16} color={tokens.colors.primary} />
            <Text style={[styles.statValue, { color: tokens.colors.text.primary }]}>{pendingInvites.length}</Text>
            <Text style={[styles.statLabel, { color: tokens.colors.text.dim }]}>{t('company.stats.pending')}</Text>
          </View>
          <View style={[styles.statCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
            <Gift size={16} color={tokens.colors.primary} />
            <Text style={[styles.statValue, { color: tokens.colors.text.primary }]}>{gifted.length}</Text>
            <Text style={[styles.statLabel, { color: tokens.colors.text.dim }]}>{t('company.stats.gifted')}</Text>
          </View>
        </View>

        {hasQueryError && (
          <View style={[styles.errorBanner, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.error }]}>
            <AlertTriangle size={16} color={tokens.colors.error} />
            <Text style={[styles.errorBannerText, { color: tokens.colors.error }]}>{t('company.loadError')}</Text>
            <Pressable onPress={refreshAll} style={[styles.retryBtn, { borderColor: tokens.colors.error }]}>
              <Text style={{ color: tokens.colors.error, fontFamily: 'Inter-Black', fontSize: 11, letterSpacing: 0.8 }}>
                {t('common.retry')}
              </Text>
            </Pressable>
          </View>
        )}

        {/* Invite a worker */}
        <View style={[styles.card, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
          <View style={styles.sectionHeader}>
            <UserPlus size={18} color={tokens.colors.primary} />
            <Text style={[styles.sectionTitle, { color: tokens.colors.primary }]}>{t('company.invite.section')}</Text>
          </View>
          <View style={{ flexDirection: 'row', gap: 10 }}>
            <TextInput
              value={phone}
              onChangeText={setPhone}
              placeholder={t('company.invite.placeholder')}
              placeholderTextColor={tokens.colors.text.dim}
              keyboardType="phone-pad"
              autoCapitalize="none"
              style={[styles.input, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
            />
            <Pressable
              disabled={!phone.trim() || inviteMutation.isPending}
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                inviteMutation.mutate(phone.trim());
              }}
              style={[styles.iconBtn, { backgroundColor: tokens.colors.primary }, (!phone.trim() || inviteMutation.isPending) && { opacity: 0.4 }]}
            >
              {inviteMutation.isPending ? (
                <ActivityIndicator size="small" color={tokens.colors.text.onPrimary} />
              ) : (
                <Send size={18} color={tokens.colors.text.onPrimary} />
              )}
            </Pressable>
          </View>
        </View>

        {/* Pending / sent invitations */}
        <View style={[styles.card, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
          <View style={styles.sectionHeader}>
            <Clock size={18} color={tokens.colors.primary} />
            <Text style={[styles.sectionTitle, { color: tokens.colors.primary }]}>{t('company.sent.section')}</Text>
          </View>
          {invitations.length === 0 ? (
            <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>{t('company.sent.empty')}</Text>
          ) : (
            <View style={{ gap: 12 }}>
              {invitations.map((inv) => {
                const isPending = (inv.status || '').toLowerCase() === 'pending';
                return (
                  <View key={inv.id} style={[styles.row, { borderColor: tokens.colors.borderLight }]}>
                    <View style={{ flex: 1 }}>
                      <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16 }} numberOfLines={1}>
                        {invitationName(inv)}
                      </Text>
                      <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>{t(invitationStatusKey(inv.status))}</Text>
                    </View>
                    {isPending && (
                      <Pressable
                        disabled={cancelMutation.isPending}
                        onPress={() => cancelMutation.mutate(inv.id)}
                        style={[styles.smallBtn, { borderColor: tokens.colors.error }, cancelMutation.isPending && { opacity: 0.5 }]}
                      >
                        <X size={14} color={tokens.colors.error} />
                        <Text style={{ color: tokens.colors.error, fontFamily: 'Inter-Black', fontSize: 11, letterSpacing: 0.8 }}>
                          {t('company.sent.cancel')}
                        </Text>
                      </Pressable>
                    )}
                  </View>
                );
              })}
            </View>
          )}
        </View>

        {/* Workers */}
        <View style={[styles.card, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
          <View style={styles.sectionHeader}>
            <Users size={18} color={tokens.colors.primary} />
            <Text style={[styles.sectionTitle, { color: tokens.colors.primary }]}>{t('company.members.section')}</Text>
          </View>
          {members.length === 0 ? (
            <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>{t('company.members.empty')}</Text>
          ) : (
            <View style={{ gap: 14 }}>
              {members.map((m) => (
                <View key={m.id} style={[styles.memberRow, { borderColor: tokens.colors.borderLight }]}>
                  <View style={{ flex: 1 }}>
                    <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16 }} numberOfLines={1}>
                      {memberName(m)}
                    </Text>
                    <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>
                      {t('company.members.joined', formatExpirationDate(m.joinedAtUtc))} · {t('company.members.giftedCount', String(m.giftedVoucherCount ?? 0))}
                    </Text>
                  </View>
                  <View style={{ flexDirection: 'row', gap: 8 }}>
                    <Pressable
                      onPress={() => openGift(m)}
                      style={[styles.smallBtn, { borderColor: tokens.colors.primary }]}
                    >
                      <Gift size={14} color={tokens.colors.primary} />
                      <Text style={{ color: tokens.colors.primary, fontFamily: 'Inter-Black', fontSize: 11, letterSpacing: 0.8 }}>
                        {t('company.members.gift')}
                      </Text>
                    </Pressable>
                    <Pressable
                      disabled={fireMutation.isPending}
                      onPress={() => confirmFire(m)}
                      style={[styles.smallBtn, { borderColor: tokens.colors.error }, fireMutation.isPending && { opacity: 0.5 }]}
                    >
                      <Trash2 size={14} color={tokens.colors.error} />
                      <Text style={{ color: tokens.colors.error, fontFamily: 'Inter-Black', fontSize: 11, letterSpacing: 0.8 }}>
                        {t('company.members.fire')}
                      </Text>
                    </Pressable>
                  </View>
                </View>
              ))}
            </View>
          )}
        </View>

        {/* Gifted vouchers (recall) */}
        <View style={[styles.card, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
          <View style={styles.sectionHeader}>
            <Gift size={18} color={tokens.colors.primary} />
            <Text style={[styles.sectionTitle, { color: tokens.colors.primary }]}>{t('company.recall.section')}</Text>
          </View>
          {gifted.length === 0 ? (
            <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>{t('company.recall.empty')}</Text>
          ) : (
            <View style={{ gap: 12 }}>
              {gifted.map((v) => {
                const workerName = [v.workerFirstName, v.workerLastName].filter(Boolean).join(' ').trim();
                return (
                  <View key={v.id} style={[styles.row, { borderColor: tokens.colors.borderLight }]}>
                    <View style={{ flex: 1 }}>
                      <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16 }} numberOfLines={1}>
                        {v.provider} · {v.amount} {v.unit || t('common.liter')}
                      </Text>
                      <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }} numberOfLines={1}>
                        {v.fuelName || v.fuelType}{workerName ? ` → ${workerName}` : ''}
                      </Text>
                    </View>
                    <Pressable
                      disabled={recallMutation.isPending}
                      onPress={() => confirmRecall(v)}
                      style={[styles.smallBtn, { borderColor: tokens.colors.primary }, recallMutation.isPending && { opacity: 0.5 }]}
                    >
                      <RotateCcw size={14} color={tokens.colors.primary} />
                      <Text style={{ color: tokens.colors.primary, fontFamily: 'Inter-Black', fontSize: 11, letterSpacing: 0.8 }}>
                        {t('company.recall.action')}
                      </Text>
                    </Pressable>
                  </View>
                );
              })}
            </View>
          )}
        </View>
      </ScrollView>

      {/* Gift modal */}
      <Modal visible={!!giftTarget} transparent animationType="slide" onRequestClose={() => setGiftTarget(null)}>
        <View style={[styles.modalOverlay, { backgroundColor: tokens.colors.overlay }]}>
          <View style={[styles.modalSheet, { backgroundColor: tokens.colors.background, borderColor: tokens.colors.borderLight }]}>
            <View style={styles.modalHeader}>
              <View style={{ flex: 1 }}>
                <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 20 }}>
                  {t('company.gift.title')}
                </Text>
                <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>
                  {t('company.gift.subtitle', workerLabel)}
                </Text>
              </View>
              <Pressable onPress={() => setGiftTarget(null)} style={{ padding: 6 }}>
                <X size={22} color={tokens.colors.text.muted} />
              </Pressable>
            </View>

            <ScrollView style={{ maxHeight: 360 }} contentContainerStyle={{ gap: 10, paddingVertical: 4 }}>
              {giftable.length === 0 ? (
                <Text style={[styles.emptyText, { color: tokens.colors.text.dim, paddingVertical: 20 }]}>
                  {t('company.gift.empty')}
                </Text>
              ) : (
                <>
                  <Pressable
                    onPress={toggleSelectAll}
                    style={[styles.voucherPick, { borderColor: tokens.colors.borderLight, backgroundColor: tokens.colors.card }]}
                  >
                    {allGiftableSelected ? (
                      <CheckSquare size={20} color={tokens.colors.primary} />
                    ) : (
                      <Square size={20} color={tokens.colors.primary} />
                    )}
                    <Text style={{ color: tokens.colors.primary, fontFamily: 'Inter-Black', fontSize: 12, letterSpacing: 1 }}>
                      {allGiftableSelected ? t('company.gift.clear') : t('company.gift.selectAll')}
                    </Text>
                  </Pressable>
                  {giftGroups.map((group) => (
                    <View key={group.provider} style={{ gap: 10 }}>
                      <Text style={[styles.groupHeader, { color: tokens.colors.text.dim }]}>{group.provider}</Text>
                      {group.items.map((v) => {
                        const isSel = selected.has(v.id);
                        return (
                          <Pressable
                            key={v.id}
                            onPress={() => toggleSelected(v.id)}
                            style={[
                              styles.voucherPick,
                              {
                                borderColor: isSel ? tokens.colors.primary : tokens.colors.borderLight,
                                backgroundColor: isSel ? `${tokens.colors.primary}14` : tokens.colors.card,
                              },
                            ]}
                          >
                            <View style={[styles.checkbox, { borderColor: isSel ? tokens.colors.primary : tokens.colors.borderLight, backgroundColor: isSel ? tokens.colors.primary : 'transparent' }]}>
                              {isSel && <Check size={14} color={tokens.colors.text.onPrimary} strokeWidth={3} />}
                            </View>
                            <View style={{ flex: 1 }}>
                              <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 15 }} numberOfLines={1}>
                                {v.provider} · {v.amount} {v.unit || t('common.liter')}
                              </Text>
                              <Text style={{ color: tokens.colors.text.dim, fontSize: 11 }} numberOfLines={1}>
                                {v.fuelName || v.fuelType}
                                {v.expirationDate ? ` · ${t('codes.expires')}: ${formatExpirationDate(v.expirationDate)}` : ''}
                              </Text>
                            </View>
                          </Pressable>
                        );
                      })}
                    </View>
                  ))}
                </>
              )}
            </ScrollView>

            <Pressable
              disabled={selected.size === 0 || giftMutation.isPending}
              onPress={() => {
                if (!giftTarget) return;
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
                giftMutation.mutate({ workerUserId: giftTarget.workerUserId, voucherIds: [...selected] });
              }}
              style={[styles.confirmBtn, { backgroundColor: tokens.colors.primary }, (selected.size === 0 || giftMutation.isPending) && { opacity: 0.4 }]}
            >
              {giftMutation.isPending ? (
                <ActivityIndicator size="small" color={tokens.colors.text.onPrimary} />
              ) : (
                <>
                  <Gift size={18} color={tokens.colors.text.onPrimary} />
                  <Text style={{ color: tokens.colors.text.onPrimary, fontFamily: 'Inter-Black', fontSize: 13, letterSpacing: 1 }}>
                    {selected.size === 0 ? t('company.gift.confirmZero') : t('company.gift.confirm', String(selected.size))}
                  </Text>
                </>
              )}
            </Pressable>
          </View>
        </View>
      </Modal>
    </GridPageLayout>
  );
}

const styles = StyleSheet.create({
  card: {
    padding: 18,
    borderRadius: 12,
    borderWidth: 1,
    marginBottom: 18,
  },
  sectionHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    marginBottom: 16,
  },
  sectionTitle: {
    fontFamily: 'Rajdhani-SemiBold',
    fontSize: 12,
    letterSpacing: 3,
    textTransform: 'uppercase',
  },
  statsRow: {
    flexDirection: 'row',
    gap: 10,
    marginBottom: 18,
  },
  statCard: {
    flex: 1,
    alignItems: 'center',
    paddingVertical: 14,
    borderRadius: 12,
    borderWidth: 1,
    gap: 4,
  },
  statValue: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 22,
  },
  statLabel: {
    fontFamily: 'Inter-Medium',
    fontSize: 10,
    letterSpacing: 1,
    textTransform: 'uppercase',
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingHorizontal: 14,
    paddingVertical: 12,
    borderRadius: 10,
    borderWidth: 1,
    marginBottom: 18,
  },
  errorBannerText: {
    flex: 1,
    fontFamily: 'Inter-Bold',
    fontSize: 13,
  },
  retryBtn: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 8,
    borderWidth: 1,
  },
  groupHeader: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 13,
    letterSpacing: 2,
    textTransform: 'uppercase',
    marginTop: 4,
  },
  input: {
    flex: 1,
    borderRadius: 10,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontFamily: 'Inter-Bold',
    fontSize: 14,
    borderWidth: 1,
  },
  iconBtn: {
    width: 48,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  emptyText: {
    fontFamily: 'Inter-Medium',
    fontSize: 13,
    textAlign: 'center',
    paddingVertical: 8,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    paddingVertical: 10,
    paddingHorizontal: 12,
    borderRadius: 10,
    borderWidth: 1,
  },
  memberRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingVertical: 12,
    paddingHorizontal: 12,
    borderRadius: 10,
    borderWidth: 1,
    flexWrap: 'wrap',
  },
  smallBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    paddingHorizontal: 10,
    paddingVertical: 7,
    borderRadius: 8,
    borderWidth: 1,
  },
  modalOverlay: {
    flex: 1,
    justifyContent: 'flex-end',
    // Scrim colour comes from `tokens.colors.overlay` at the call site. Five
    // screens each picked their own black alpha (0.6 / 0.7 / 0.8 / 0.92); the
    // scrim is now one value that also lightens correctly on the light themes.
  },
  modalSheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    padding: 20,
    paddingBottom: 40,
    gap: 16,
  },
  modalHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
  },
  voucherPick: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    padding: 12,
    borderRadius: 10,
    borderWidth: 1,
  },
  checkbox: {
    width: 22,
    height: 22,
    borderRadius: 6,
    borderWidth: 2,
    alignItems: 'center',
    justifyContent: 'center',
  },
  confirmBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 16,
    borderRadius: 12,
  },
});
