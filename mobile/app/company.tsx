import { useState } from 'react';
import { View, Text, Pressable, ScrollView, StyleSheet, Alert, RefreshControl } from 'react-native';
import { Redirect } from 'expo-router';
import { AlertTriangle } from 'lucide-react-native';
import type { CompanyInvitationDto, CompanyMemberDto } from '../src/features/company/types';
import type { Voucher } from '../src/core/types/api';
import { useCompany } from '../src/features/company/hooks/useCompany';
import {
  CompanyStatsRow,
  InviteWorkerForm,
  PendingInvites,
  HubWorkersBranch,
  HubOrdersBranch,
  IssueVoucherModal,
  type GiftTarget,
} from '../src/features/company/components';
import { GridPageLayout, ScreenHeader, LoadingState, useContentInsets } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';

// The app is Ukrainian-only for now, so every invited worker's number starts
// with the same country code. Prefill it so the owner types only the subscriber
// digits instead of re-entering "+380" each time (and so a bare local number is
// never sent, which the server would otherwise mis-normalize to a +1 number).
const UA_DIAL_PREFIX = '+380';

export default function CompanyScreen() {
  const tokens = useDesignTokens();
  const contentInsets = useContentInsets();
  const { t } = useI18n();

  const [phone, setPhone] = useState(UA_DIAL_PREFIX);
  const [giftTarget, setGiftTarget] = useState<GiftTarget | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [refreshing, setRefreshing] = useState(false);

  const {
    isAuthenticated,
    authLoading,
    isLoading,
    hasQueryError,
    isOwnerContext,
    members,
    giftable,
    gifted,
    blocked,
    pendingInvites,
    giftGroups,
    companyOrders,
    invite,
    cancelInvite,
    fire,
    gift,
    recall,
    block,
    unblock,
    refreshAll,
    isInviting,
    isCancelling,
    isFiring,
    isGifting,
    isRecalling,
    isBlocking,
    isUnblocking,
  } = useCompany({
    onInviteSuccess: () => setPhone(UA_DIAL_PREFIX),
    onGiftSuccess: () => {
      setGiftTarget(null);
      setSelected(new Set());
    },
  });

  const allGiftableSelected = giftable.length > 0 && giftable.every((v) => selected.has(v.id));

  const memberName = (m: CompanyMemberDto) =>
    [m.workerFirstName, m.workerLastName].filter(Boolean).join(' ').trim() || m.workerPhoneNumber;

  const invitationName = (inv: CompanyInvitationDto) =>
    [inv.workerFirstName, inv.workerLastName].filter(Boolean).join(' ').trim() ||
    inv.workerPhoneNumber;

  const confirmFire = (m: CompanyMemberDto) => {
    Alert.alert(t('company.fire.confirmTitle'), t('company.fire.confirmDesc', memberName(m)), [
      { text: t('common.cancel'), style: 'cancel' },
      { text: t('company.fire.confirm'), style: 'destructive', onPress: () => fire(m.id) },
    ]);
  };

  const confirmRecall = (v: Voucher) => {
    Alert.alert(t('company.recall.confirmTitle'), t('company.recall.confirmDesc'), [
      { text: t('common.cancel'), style: 'cancel' },
      { text: t('company.recall.confirm'), style: 'destructive', onPress: () => recall(v.id) },
    ]);
  };

  // Freezing a worker's voucher is reversible (unblock thaws it) but still stops
  // the worker spending it, so confirm first — same as recall. Unblocking is a
  // plain restore, so it fires without a prompt.
  const confirmBlock = (v: Voucher) => {
    Alert.alert(t('company.block.confirmTitle'), t('company.block.confirmDesc'), [
      { text: t('common.cancel'), style: 'cancel' },
      { text: t('company.block.confirm'), style: 'destructive', onPress: () => block(v.id) },
    ]);
  };

  const toggleSelectAll = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setSelected(allGiftableSelected ? new Set() : new Set(giftable.map((v) => v.id)));
  };

  const toggleSelected = (id: string) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const openGift = (m: CompanyMemberDto) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    setSelected(new Set());
    setGiftTarget({ workerUserId: m.workerUserId, label: memberName(m) });
  };

  // The hub's orders branch points the same modal at fuel instead of at a person: the
  // vouchers come pre-selected and the recipient is asked for inside the modal, because
  // fuel nobody holds is the company's, not a particular worker's.
  const openGiftForFuel = (voucherIds: string[]) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    setSelected(new Set(voucherIds));
    setGiftTarget({ workerUserId: null, label: '' });
  };

  const Header = <ScreenHeader title={t('company.managementTitle')} />;

  if (!isAuthenticated && !authLoading) {
    return <Redirect href="/landing" />;
  }

  // Owner tools only (multi-company epic #103, S5). The profile row is gated on an
  // owner context, so reaching this screen in a worker/personal context means a stale
  // link or a fired worker — send them back to their own wallet rather than showing a
  // roster they have no rights over.
  if (!isLoading && !isOwnerContext) {
    return <Redirect href="/my-codes" />;
  }

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
        <CompanyStatsRow members={members} gifted={gifted} pendingInvites={pendingInvites} />

        {hasQueryError && (
          <View
            style={[
              styles.errorBanner,
              { backgroundColor: tokens.colors.card, borderColor: tokens.colors.error },
            ]}
          >
            <AlertTriangle size={16} color={tokens.colors.error} />
            <Text style={[styles.errorBannerText, { color: tokens.colors.error }]}>
              {t('company.loadError')}
            </Text>
            <Pressable
              onPress={refreshAll}
              style={[styles.retryBtn, { borderColor: tokens.colors.error }]}
            >
              <Text
                style={{
                  color: tokens.colors.error,
                  fontFamily: 'Inter-Black',
                  fontSize: 11,
                  letterSpacing: 0.8,
                }}
              >
                {t('common.retry')}
              </Text>
            </Pressable>
          </View>
        )}

        <InviteWorkerForm
          UA_DIAL_PREFIX={UA_DIAL_PREFIX}
          phone={phone}
          setPhone={setPhone}
          invite={invite}
          isInviting={isInviting}
        />

        <PendingInvites
          pendingInvites={pendingInvites}
          cancelInvite={cancelInvite}
          isCancelling={isCancelling}
          invitationName={invitationName}
        />

        <HubWorkersBranch
          members={members}
          gifted={gifted}
          blocked={blocked}
          unblock={unblock}
          isFiring={isFiring}
          isBlocking={isBlocking}
          isRecalling={isRecalling}
          isUnblocking={isUnblocking}
          memberName={memberName}
          openGift={openGift}
          confirmFire={confirmFire}
          confirmRecall={confirmRecall}
          confirmBlock={confirmBlock}
        />

        {/* The second hub branch: the company's own purchases by brand, and under each
            order the fuel it delivered that no worker holds — the stock the owner can
            still issue. The wallet keeps receipts only, so this is the one place the
            hub reconciles a purchase against what is left of it. */}
        <HubOrdersBranch
          companyOrders={companyOrders}
          giftable={giftable}
          onIssue={openGiftForFuel}
        />
      </ScrollView>

      <IssueVoucherModal
        members={members}
        giftable={giftable}
        giftGroups={giftGroups}
        gift={gift}
        isGifting={isGifting}
        allGiftableSelected={allGiftableSelected}
        giftTarget={giftTarget}
        setGiftTarget={setGiftTarget}
        selected={selected}
        toggleSelectAll={toggleSelectAll}
        toggleSelected={toggleSelected}
        memberName={memberName}
      />
    </GridPageLayout>
  );
}

const styles = StyleSheet.create({
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
});
