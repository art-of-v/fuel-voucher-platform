import { useEffect, useMemo, useRef, useState } from 'react';
import { View, ScrollView, Animated, Alert, RefreshControl } from 'react-native';
import type { Order, Voucher } from '../src/core/types/api';
import { useMyCodes } from '../src/features/vouchers/hooks/useMyCodes';
import {
  GridBackground,
  GridPageLayout,
  LoadingState,
  ScreenHeader,
  useContentInsets,
} from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import {
  applyListControls,
  EMPTY_FILTERS,
  type ListControls,
} from '../src/features/vouchers/lib/listControls';
import {
  collectWorkerListVouchers,
  narrowIssuanceOrders,
} from '../src/features/vouchers/lib/workerList';
import { useWorkerUsage } from '../src/features/company/hooks/useWorkerUsage';

import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { Redirect, router, useLocalSearchParams } from 'expo-router';
import { shareOrderReceipt } from '../src/features/vouchers/api/getOrderReceipt';
import { paymentHref } from '../src/features/payments/paymentSession';
import { VoucherDetailModal } from '../src/features/vouchers/components/VoucherDetailModal';
import { getRenewalConfig, type RenewalConfig } from '../src/features/vouchers/renewal/api/renewal';
import { isRenewableVoucher, countRenewable } from '../src/features/vouchers/renewal/eligibility';
import {
  brandColorFor,
  isWalletEmpty,
  unusedLitres,
  countUsed,
  countPending,
  type WalletSection,
} from '../src/features/vouchers/lib/display';
import { CodesEmptyState } from '../src/features/vouchers/components/CodesEmptyState';
import { CodesErrorState } from '../src/features/vouchers/components/CodesErrorState';
import { CodesWalletHeader } from '../src/features/vouchers/components/CodesWalletHeader';
import { CompanyHubLink } from '../src/features/vouchers/components/CompanyHubLink';
import { PendingOrdersSection } from '../src/features/vouchers/components/PendingOrdersSection';
import { ReceiptsSection } from '../src/features/vouchers/components/ReceiptsSection';
import { RenewalCta } from '../src/features/vouchers/components/RenewalCta';
import { SpentOrdersFilter } from '../src/features/vouchers/components/SpentOrdersFilter';
import { WorkerIssuanceSection } from '../src/features/vouchers/components/WorkerIssuanceSection';

const GLOBAL_PADDING = 24;

export default function MyCodesScreen() {
  const tokens = useDesignTokens();
  const contentInsets = useContentInsets();
  const [refreshing, setRefreshing] = useState(false);
  const pulseAnim = useRef(new Animated.Value(1)).current;
  const { t } = useI18n();
  // Set by a tapped "order fulfilled" push (/my-codes?orderId=…). See the
  // deep-link focus effect below.
  const { orderId: focusOrderId } = useLocalSearchParams<{ orderId?: string }>();
  // The wallet opens on what can still be used. The filter chip reveals the rest: fully spent
  // orders and the spent vouchers inside every order (see `useMyCodes`).
  const [showSpent, setShowSpent] = useState(false);
  const {
    isAuthenticated,
    authLoading,
    user,
    vouchers,
    orders,
    loading,
    error,
    selectedVoucher,
    setSelectedVoucher,
    isCompanyContext,
    isWorkerContext,
    currentCompany,
    pendingOrders,
    fulfilledOrders,
    spentOrderCount,
    issuanceOrders,
    looseIssuanceVouchers,
    loadData,
    toggleUsed,
    deleteOrder,
  } = useMyCodes({ includeSpent: showSpent });

  // The worker's own usage report (epic #103 S5, #150). Fetched only in a worker context: an owner
  // already has the stock and P&L views, and the endpoint would answer with their own empty
  // issuance. The header above counts vouchers; this answers litres and dates.
  const workerUsage = useWorkerUsage(currentCompany?.id ?? null, isWorkerContext);

  // Sort/filter state for the worker's own list (#161 H0). Default: soonest expiry first, which is
  // the question a worker opens this screen to answer.
  const [workerListControls, setWorkerListControls] = useState<ListControls>({
    sortKey: 'expiry',
    sortDirection: 'asc',
    filters: EMPTY_FILTERS,
  });

  // Filter / sort for the worker's own list (#161 H0).
  //
  // The list is receipt-grouped, so the controls do not flatten it - that would throw away the
  // provenance the grouping exists for. Instead they narrow the vouchers and the receipts follow: a
  // receipt keeps only the vouchers that match, and one left with nothing disappears. The count in
  // the bar is what tells a worker that a filtered list is not an empty one.
  //
  // These sit with the other hooks, above the auth guard: a hook below an early return would run
  // conditionally and break hook order between renders.
  const workerListVouchers = useMemo(
    () => collectWorkerListVouchers(issuanceOrders, looseIssuanceVouchers),
    [issuanceOrders, looseIssuanceVouchers],
  );
  const visibleVoucherIds = useMemo(
    () => new Set(applyListControls(workerListVouchers, workerListControls).map((v) => v.id)),
    [workerListVouchers, workerListControls],
  );
  const filteredIssuanceOrders = useMemo(
    () => narrowIssuanceOrders(issuanceOrders, visibleVoucherIds),
    [issuanceOrders, visibleVoucherIds],
  );
  const filteredLooseIssued = useMemo(
    () => applyListControls(looseIssuanceVouchers, workerListControls),
    [looseIssuanceVouchers, workerListControls],
  );

  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(pulseAnim, { toValue: 0.6, duration: 2000, useNativeDriver: true }),
        Animated.timing(pulseAnim, { toValue: 1, duration: 2000, useNativeDriver: true }),
      ]),
    );
    loop.start();
    // Stopped on unmount: an endless loop keeps its frame callback alive after the screen is gone,
    // so it burned frames in the app and left this file's Jest teardown hanging on an open handle.
    return () => loop.stop();
  }, [pulseAnim]);

  // The lock handles empty state now

  // Renewal feature gate + threshold, fetched once. Drives whether the voucher
  // detail modal offers "renew" and for which vouchers (near-expiry / expired).
  // A read (no device signature); a failure just hides the entry point.
  const [renewalConfig, setRenewalConfig] = useState<RenewalConfig | null>(null);
  useEffect(() => {
    if (!isAuthenticated) return;
    let cancelled = false;
    getRenewalConfig()
      .then((cfg) => {
        if (!cancelled) setRenewalConfig(cfg);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [isAuthenticated]);

  // Entry gate for the currently open voucher: shared with the near-expiry
  // banner and the multi-select screen (see renewal/eligibility). The backend
  // quote/checkout stays the authority; this only decides what the UI offers.
  // Personal context only — renewing is a paid self-service checkout, and a
  // voucher issued by a company is not renewable (multi-company epic #103 S5).
  const selectedCanRenew =
    !isCompanyContext &&
    !!selectedVoucher &&
    isRenewableVoucher(selectedVoucher, user?.id, renewalConfig);

  // How many of the user's vouchers are near-expiry/expired and renewable —
  // drives whether the CTA banner shows and its count.
  const renewableCount = countRenewable(vouchers, user?.id, renewalConfig);

  const handlePay = async (order: Order) => {
    // Defence in depth. OrderCard already withholds the swipe pay action for an order that is no
    // longer PENDING_PAYMENT (canSwipe at OrderCard.tsx:96 gates renderRightActions), so this is
    // not reachable through the list today.
    //
    // It stays because that gate is UI-level and can be regressed by a card redesign, while this
    // handler is the single choke point where the invariant belongs - and it is the honest place
    // to record where the real exposure lives: a Monobank payment page the customer left open in
    // a browser stays payable for the invoice's whole validity window, and the app cannot close
    // it from here. That is fixed server-side by invalidating the invoice on failure.
    if (order.status !== 'PENDING_PAYMENT') {
      Alert.alert(t('codes.orderNotPayable'));
      return;
    }
    if (!order.monobankPaymentUrl) return;

    // In-app payment screen. appUrl is not persisted on the order, so it is absent here; /pay
    // shows the embeddable page and omits the "open the Monobank app" affordance.
    router.push(
      paymentHref({
        orderId: order.id,
        pageUrl: order.monobankPaymentUrl,
      }),
    );
  };

  const handleDeleteOrder = (order: Order) => {
    // Only allow deleting unpaid orders (PENDING_PAYMENT)
    // PENDING_FULFILLMENT orders have been paid and cannot be deleted by user
    if (order.status === 'PENDING_FULFILLMENT') {
      Alert.alert(t('codes.cannotDeletePaidOrder'));
      return;
    }
    Alert.alert(t('codes.deleteOrder'), t('codes.deleteOrderConfirm'), [
      { text: t('common.cancel'), style: 'cancel' },
      {
        text: t('common.delete'),
        style: 'destructive',
        onPress: async () => {
          try {
            await deleteOrder(order.id);
          } catch {
            Alert.alert(t('common.error'), t('codes.deleteFailed'));
          }
        },
      },
    ]);
  };

  /**
   * Downloads the order's PDF receipt and opens the system share sheet.
   *
   * <p>No confirmation dialog: this is a pure read that changes nothing on the server, and the
   * share sheet itself is the confirmation - dismissing it simply does not share.
   */
  const handleGetReceipt = async (order: Order) => {
    try {
      await shareOrderReceipt(order.id);
    } catch (error) {
      const detail = error instanceof Error ? error.message : '';
      // The alert first, haptics second and un-awaited: haptic feedback is decoration, and
      // awaiting it ahead of the message risks the customer seeing nothing at all when the
      // haptics module misbehaves on their device.
      Alert.alert(
        t('common.error'),
        detail ? `${t('codes.receiptFailed')}: ${detail}` : t('codes.receiptFailed'),
      );
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    }
  };

  // Tab root: no back affordance, because there is nothing to pop to.
  const Header = <ScreenHeader title={t('codes.title')} hideBack />;

  const [expandedOrders, setExpandedOrders] = useState<Set<string>>(new Set());

  // Deep-link focus: when arriving from a tapped "order fulfilled" push
  // (/my-codes?orderId=…, see notificationResponse.ts), expand that order so the
  // wallet opens on it rather than the generic list. Consumed once per distinct
  // orderId — a manual collapse afterwards stays collapsed.
  const consumedFocusRef = useRef<string | null>(null);
  useEffect(() => {
    if (focusOrderId && consumedFocusRef.current !== focusOrderId) {
      consumedFocusRef.current = focusOrderId;
      setExpandedOrders((prev) => new Set(prev).add(focusOrderId));
    }
  }, [focusOrderId]);

  const toggleOrderExpand = (orderId: string) => {
    setExpandedOrders((prev) => {
      const next = new Set(prev);
      if (next.has(orderId)) {
        next.delete(orderId);
      } else {
        next.add(orderId);
      }
      return next;
    });
  };

  // Opens the detail modal for a voucher tapped on a card.
  //
  // Two sources describe the same voucher and each carries something the other lacks, so they are
  // merged rather than swapped: the order-nested copy (from /api/sync/orders) is the only one with
  // `history` and `originOrderId`, while /api/vouchers/my carries the worker names, subtype and
  // redemption rules the modal shows. Preferring the /api/vouchers/my copy outright is what made the
  // History block unreachable (planning #180) — it has no `history` field at all.
  const openVoucher = (voucher: Voucher) => {
    const extras = vouchers.find((candidate) => candidate.id === voucher.id);
    setSelectedVoucher(
      extras
        ? { ...extras, history: voucher.history, originOrderId: voucher.originOrderId }
        : voucher,
    );
  };

  // Auth guard runs after all hooks (incl. useMyCodes and the effects above) so
  // hook order stays stable across renders (react-hooks/rules-of-hooks).
  if (!isAuthenticated && !authLoading) {
    return <Redirect href="/landing" />;
  }

  if (loading && !refreshing) {
    // Inside `PageLayout`, not instead of it: the previous bare centred `View`
    // dropped the header, the background and the safe-area handling for the
    // duration of the load, so the wallet visibly re-assembled itself.
    return (
      <GridPageLayout header={Header} background={<GridBackground />} disableScroll>
        <LoadingState fullScreen />
      </GridPageLayout>
    );
  }

  // Worker context (multi-company epic #103 S5): the fuel this company issued to
  // me — issued / used / remaining / pending counters, then the handover receipts. The
  // counters cover every voucher the worker holds, whether it sits inside a
  // receipt or predates them, so the header never disagrees with the list.
  //
  // A pending voucher is counted on its own: its fuel is already dispensed and waits for the
  // supplier's evidence, so it is neither used nor still in the worker's hands.
  const workerIssued = vouchers;
  const workerUsedCount = countUsed(workerIssued);
  const workerPendingCount = countPending(workerIssued);
  const workerLeftCount = workerIssued.length - workerUsedCount - workerPendingCount;
  const workerLitersLeft = unusedLitres(workerIssued);

  // Emptiness is context-dependent: a company context shows its receipts and any
  // in-flight purchases (#161 H4 — the hub owns the stock, so it is not counted here);
  // a worker context shows the fuel issued to them; personal shows orders + available vouchers.
  const walletSection: WalletSection = isWorkerContext
    ? 'worker'
    : isCompanyContext
      ? 'company'
      : 'personal';
  const isEmpty = isWalletEmpty(walletSection, {
    pendingOrders: pendingOrders.length,
    fulfilledOrders: fulfilledOrders.length,
    renewalOrders: 0,
    issuanceReceipts: issuanceOrders.length,
    unassignedVouchers: 0,
    issuedToMe: workerIssued.length,
  });

  // The spent-fuel filter. One switch covers both levels: it reveals the receipts whose every
  // voucher is spent, and the spent vouchers inside any receipt the wallet already shows. Offered
  // whenever such an order exists - including in the empty state, because "nothing to use" must
  // not mean "nothing to look at": the history is one tap away rather than gone.
  // A worker's list is not order-filtered the same way and carries its own status filter (#161 H0).
  const spentFilter =
    !isWorkerContext && spentOrderCount > 0 ? (
      <SpentOrdersFilter
        count={spentOrderCount}
        selected={showSpent}
        onToggle={() => setShowSpent((value) => !value)}
      />
    ) : null;

  // The filter hides receipts; it does not delete them from the account. Counting the summary
  // from `fulfilledOrders` alone made it report one fulfilled order fewer than the customer
  // owns as soon as one of them was fully spent, and zero when they all were.
  const fulfilledWalletCount = showSpent
    ? fulfilledOrders.length
    : fulfilledOrders.length + spentOrderCount;

  // "Nothing to use" is not "nothing bought": when the only receipts left are spent ones, the
  // default view has emptied the list, but the orders and their vouchers are still there.
  const onlySpentLeft = !isWorkerContext && spentOrderCount > 0;

  // Company context header: which company's stock this is + pool/distributed/worker counts.
  // Worker context header (epic #103 S5): who issued the fuel, and how much is
  // left. Buying stays in the personal context, so say so rather than hiding it.
  return (
    <GridPageLayout header={Header} background={<GridBackground />} disableScroll={true}>
      <ScrollView
        contentContainerStyle={{
          paddingHorizontal: GLOBAL_PADDING,
          paddingBottom: contentInsets.bottom,
        }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => {
              setRefreshing(true);
              loadData().finally(() => setRefreshing(false));
            }}
            tintColor={tokens.colors.primary}
            colors={[tokens.colors.primary]}
          />
        }
      >
        {!loading && error && vouchers.length === 0 && orders.length === 0 ? (
          <CodesErrorState onRetry={loadData} />
        ) : isEmpty ? (
          <CodesEmptyState
            onlySpentLeft={onlySpentLeft}
            isWorkerContext={isWorkerContext}
            isCompanyContext={isCompanyContext}
            pulseAnim={pulseAnim}
            ordersCount={orders.length}
            fulfilledCount={fulfilledWalletCount}
            pendingCount={pendingOrders.length}
            spentFilter={spentFilter}
            onRefresh={loadData}
          />
        ) : (
          <View style={{ gap: tokens.spacing['2xl'] }}>
            <CodesWalletHeader
              isWorkerContext={isWorkerContext}
              currentCompany={currentCompany}
              workerIssued={workerIssued}
              workerUsedCount={workerUsedCount}
              workerLeftCount={workerLeftCount}
              workerPendingCount={workerPendingCount}
              workerLitersLeft={workerLitersLeft}
              usageReport={workerUsage.report}
              usageLoading={workerUsage.isLoading}
              ordersCount={orders.length}
              fulfilledCount={fulfilledWalletCount}
              pendingCount={pendingOrders.length}
              spentFilter={spentFilter}
            />

            {!isCompanyContext && renewalConfig?.enabled && renewableCount > 0 && (
              <RenewalCta renewableCount={renewableCount} />
            )}

            {/* PENDING ORDERS */}
            {pendingOrders.length > 0 && (
              <PendingOrdersSection
                orders={pendingOrders}
                expandedOrders={expandedOrders}
                onToggle={toggleOrderExpand}
                onVoucherPress={openVoucher}
                onPay={handlePay}
                onDelete={handleDeleteOrder}
              />
            )}

            {/* RECEIPTS (personal + owner context). The wallet is orders containing vouchers.
                            Each card is a purchase the customer made, with its live vouchers
                            inside it (one if they bought one, many if they bought many).
                            A renewal is not its own card: it is an event in a voucher's History,
                            and a replacement is re-parented onto the original order server-side.
                            An owner sees the company's receipts too, and nothing else (#161 H4):
                            the hub owns who holds what, so this list and the hub cannot disagree. */}
            {!isWorkerContext && fulfilledOrders.length > 0 && (
              <ReceiptsSection
                orders={fulfilledOrders}
                expandedOrders={expandedOrders}
                onToggle={toggleOrderExpand}
                onVoucherPress={openVoucher}
                onReceipt={handleGetReceipt}
              />
            )}

            {/* WORKER CONTEXT — the fuel this company issued to me (epic #103 S5). Each
                            company handover is a real order now, so it renders as a receipt
                            with its vouchers inside it, exactly like the owner sees their
                            fulfilled orders. Only fuel predating issuance orders (or arriving
                            another way) stays a flat list below. */}
            {isWorkerContext && (
              <WorkerIssuanceSection
                vouchers={workerListVouchers}
                controls={workerListControls}
                onControlsChange={setWorkerListControls}
                issuanceOrders={filteredIssuanceOrders}
                looseIssued={filteredLooseIssued}
                expandedOrders={expandedOrders}
                onToggle={toggleOrderExpand}
                onVoucherPress={openVoucher}
                userId={user?.id}
                pulseAnim={pulseAnim}
                onSelectVoucher={setSelectedVoucher}
              />
            )}
          </View>
        )}

        {/* The pool and every worker's holdings now live in the company hub: the wallet keeps
            receipts only, so the same fuel is never listed in two places that can disagree.
            Rendered outside the empty-state branch on purpose - a company whose fuel is all used
            up has no receipts left, and that is exactly when it most needs the way into the hub. */}
        {isCompanyContext && !error && <CompanyHubLink isEmpty={isEmpty} />}
      </ScrollView>

      <VoucherDetailModal
        visible={!!selectedVoucher}
        voucher={selectedVoucher}
        user={user}
        onClose={() => setSelectedVoucher(null)}
        onToggleUsed={toggleUsed}
        brandColor={brandColorFor(selectedVoucher?.brand, tokens)}
        canRenew={selectedCanRenew}
        onRenew={(v) => {
          setSelectedVoucher(null);
          router.push(`/renew?voucherIds=${v.id}`);
        }}
      />
    </GridPageLayout>
  );
}
