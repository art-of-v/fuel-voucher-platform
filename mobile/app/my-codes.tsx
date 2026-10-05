import { useState, useEffect, useRef } from 'react';
import {
  View,
  Text,
  Pressable,
  StyleSheet,
  ScrollView,
  Animated,
  Alert,
  RefreshControl,
} from 'react-native';
import {
  QrCode as QrIcon,
  Clock,
  CheckCircle,
  AlertTriangle,
  RefreshCw,
  Users,
  Fuel,
  ChevronRight,
} from 'lucide-react-native';
import type { Order } from '../src/core/types/api';
import { useMyCodes } from '../src/features/vouchers/hooks/useMyCodes';
import {
  GridBackground,
  GridPageLayout,
  LoadingState,
  ScreenHeader,
  useContentInsets,
} from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { VoucherCard, WalletSummaryBar } from '../src/features/vouchers/components';
import { CompanyStockHeader, WorkerFuelHeader } from '../src/features/company/components';

import * as Linking from 'expo-linking';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { GlowText } from '../src/components/glow-text';
import { Redirect, router, useLocalSearchParams } from 'expo-router';
import { OrderCard } from '../src/components/OrderCard';
import { VoucherDetailModal } from '../src/components/VoucherDetailModal';
import { getRenewalConfig, type RenewalConfig } from '../src/features/vouchers/renewal/api/renewal';
import { isRenewableVoucher, countRenewable } from '../src/features/vouchers/renewal/eligibility';
import {
  brandColorFor,
  isWalletEmpty,
  unusedLitres,
  countUsed,
  type WalletSection,
} from '../src/features/vouchers/lib/display';

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
    companyStock,
    pendingOrders,
    fulfilledOrders,
    renewalOrders,
    issuanceOrders,
    looseIssuanceVouchers,
    unassignedVouchers,
    loadData,
    toggleUsed,
    deleteOrder,
  } = useMyCodes();

  useEffect(() => {
    Animated.loop(
      Animated.sequence([
        Animated.timing(pulseAnim, { toValue: 0.6, duration: 2000, useNativeDriver: true }),
        Animated.timing(pulseAnim, { toValue: 1, duration: 2000, useNativeDriver: true }),
      ]),
    ).start();
  }, []);

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
    if (order.monobankPaymentUrl) {
      await Linking.openURL(order.monobankPaymentUrl);
    }
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

  const distributedCount = companyStock.workers.reduce((sum, w) => sum + w.vouchers.length, 0);

  // Worker context (multi-company epic #103 S5): the fuel this company issued to
  // me — issued / used / remaining counters, then the handover receipts. The
  // counters cover every voucher the worker holds, whether it sits inside a
  // receipt or predates them, so the header never disagrees with the list.
  const workerIssued = vouchers;
  const workerUsedCount = countUsed(workerIssued);
  const workerLeftCount = workerIssued.length - workerUsedCount;
  const workerLitersLeft = unusedLitres(workerIssued);

  // Fuel that belongs to no handover receipt: it predates issuance orders or came
  // another way, and cannot honestly be filed under one.
  const looseIssued = looseIssuanceVouchers;

  // Emptiness is context-dependent: a company context shows its stock (pool +
  // per-worker) plus any in-flight purchases; a worker context shows the fuel
  // issued to them; personal shows orders + available vouchers. Company fulfilled
  // vouchers are assigned to orders, so they live in the pool — not in
  // `unassignedVouchers` — hence the dedicated company check.
  const walletSection: WalletSection = isWorkerContext
    ? 'worker'
    : isCompanyContext
      ? 'company'
      : 'personal';
  const isEmpty = isWalletEmpty(walletSection, {
    pendingOrders: pendingOrders.length,
    fulfilledOrders: fulfilledOrders.length,
    renewalOrders: renewalOrders.length,
    issuanceReceipts: issuanceOrders.length,
    unassignedVouchers: unassignedVouchers.length,
    poolVouchers: companyStock.pool.length,
    workersWithStock: companyStock.workers.length,
    issuedToMe: workerIssued.length,
  });

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
          <View style={styles.errorContainer}>
            <AlertTriangle size={40} color={tokens.colors.error} />
            <Text
              allowFontScaling={false}
              style={[styles.errorTitle, { color: tokens.colors.text.primary }]}
            >
              {t('codes.failedToLoad')}
            </Text>
            <Pressable
              onPress={loadData}
              style={({ pressed }) => [
                {
                  marginTop: 24,
                  paddingHorizontal: 24,
                  paddingVertical: 12,
                  borderWidth: 1,
                  borderColor: tokens.colors.primary,
                  backgroundColor: pressed ? tokens.colors.primaryDim : 'transparent',
                },
              ]}
            >
              <Text style={{ color: tokens.colors.primary, fontSize: 12, letterSpacing: 1.5 }}>
                {t('codes.retry')}
              </Text>
            </Pressable>
          </View>
        ) : isEmpty ? (
          <View style={styles.emptyContainer}>
            <View style={styles.emptyIconBox}>
              <QrIcon size={40} color={tokens.colors.primary} />
            </View>
            <GlowText
              intensity="low"
              align="center"
              animation="pulse"
              animatedValue={pulseAnim}
              style={[styles.emptyTitle, { color: tokens.colors.text.primary }]}
            >
              {isWorkerContext
                ? t('codes.stock.workerEmpty')
                : isCompanyContext
                  ? t('codes.stock.companyEmpty')
                  : t('codes.noAssets')}
            </GlowText>
            <Text
              allowFontScaling={false}
              style={[styles.emptySubtitle, { color: tokens.colors.text.muted }]}
            >
              {isWorkerContext
                ? t('codes.stock.workerEmptySub')
                : isCompanyContext
                  ? t('codes.stock.companyEmptySub')
                  : t('codes.purchaseFuel')}
            </Text>
            <Pressable
              onPress={loadData}
              style={{
                marginTop: 24,
                padding: 12,
                borderWidth: 1,
                borderColor: tokens.colors.primary,
              }}
            >
              <Text style={{ color: tokens.colors.primary, fontSize: 12 }}>⟳ REFRESH</Text>
            </Pressable>
          </View>
        ) : (
          <View style={{ gap: 24 }}>
            {isWorkerContext ? (
              <WorkerFuelHeader
                currentCompany={currentCompany}
                workerIssued={workerIssued}
                workerUsedCount={workerUsedCount}
                workerLeftCount={workerLeftCount}
                workerLitersLeft={workerLitersLeft}
              />
            ) : isCompanyContext ? (
              <CompanyStockHeader
                companyStock={companyStock}
                currentCompany={currentCompany}
                distributedCount={distributedCount}
              />
            ) : (
              <WalletSummaryBar
                orders={orders}
                fulfilledOrders={fulfilledOrders}
                pendingOrders={pendingOrders}
              />
            )}

            {/* NEAR-EXPIRY RENEWAL CTA — the discoverable entry point.
                            Shown only when the feature is on and the user actually has
                            renewable (near-expiry / expired) vouchers. Taps into the
                            dedicated multi-select screen. Personal context only: renewal is
                            a self-service checkout, and buying into a company context is S4
                            (multi-company epic #103). */}
            {!isCompanyContext && renewalConfig?.enabled && renewableCount > 0 && (
              <Pressable
                onPress={() => {
                  Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                  router.push('/renew-select');
                }}
                style={({ pressed }) => [
                  {
                    flexDirection: 'row',
                    alignItems: 'center',
                    gap: 14,
                    padding: 16,
                    borderRadius: 14,
                    borderWidth: 1,
                    borderColor: pressed
                      ? tokens.colors.warning
                      : tokens.colors.status.warning.border,
                    backgroundColor: tokens.colors.status.warning.subtle,
                    transform: pressed ? [{ scale: 0.98 }] : [],
                  },
                ]}
              >
                <View
                  style={{
                    width: 42,
                    height: 42,
                    borderRadius: 21,
                    alignItems: 'center',
                    justifyContent: 'center',
                    backgroundColor: `${tokens.colors.warning}22`,
                  }}
                >
                  <RefreshCw size={20} color={tokens.colors.warning} />
                </View>
                <View style={{ flex: 1, gap: 3 }}>
                  <Text
                    allowFontScaling={false}
                    style={{
                      fontSize: 14,
                      fontFamily: 'Rajdhani-Bold',
                      letterSpacing: 0.5,
                      color: tokens.colors.text.primary,
                    }}
                  >
                    {t('codes.renewCta.title')}
                  </Text>
                  <Text
                    allowFontScaling={false}
                    style={{ fontSize: 12, fontFamily: 'Inter', color: tokens.colors.text.muted }}
                  >
                    {t('codes.renewCta.subtitle', String(renewableCount))}
                  </Text>
                </View>
                <View
                  style={{
                    paddingHorizontal: 14,
                    paddingVertical: 8,
                    borderRadius: 10,
                    backgroundColor: tokens.colors.warning,
                  }}
                >
                  <Text
                    allowFontScaling={false}
                    style={{
                      fontSize: 11,
                      fontFamily: 'Inter-Black',
                      letterSpacing: 1,
                      textTransform: 'uppercase',
                      color: tokens.colors.status.warning.onBase,
                    }}
                  >
                    {t('codes.renewCta.action')}
                  </Text>
                </View>
              </Pressable>
            )}

            {/* PENDING ORDERS */}
            {pendingOrders.length > 0 && (
              <View style={{ gap: 12 }}>
                <View style={styles.sectionHeader}>
                  <Clock size={14} color={tokens.colors.accent} />
                  <View
                    style={{
                      flex: 1,
                      height: 1,
                      backgroundColor: `${tokens.colors.accent}1A`,
                      marginLeft: 8,
                    }}
                  />
                  <Text
                    allowFontScaling={false}
                    style={[styles.sectionLabel, { color: tokens.colors.accent, marginBottom: 0 }]}
                  >
                    {t('codes.processingPurchases')}
                  </Text>
                </View>
                {pendingOrders.map((order) => (
                  <OrderCard
                    key={order.id}
                    order={order}
                    isExpanded={expandedOrders.has(order.id)}
                    onToggle={toggleOrderExpand}
                    onVoucherPress={(v) => {
                      const fullVoucher = vouchers.find((v2) => v2.id === v.id) || v;
                      setSelectedVoucher(fullVoucher);
                    }}
                    onVoucherLongPress={(v) => {
                      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                      const fullVoucher = vouchers.find((v2) => v2.id === v.id) || v;
                      setSelectedVoucher(fullVoucher);
                    }}
                    onPay={handlePay}
                    onDelete={handleDeleteOrder}
                    brandColor={brandColorFor(order.provider, tokens)}
                  />
                ))}
              </View>
            )}

            {/* FULFILLED ORDERS WITH VOUCHERS — personal context only. In a
                            company context these vouchers are shown as stock (pool / per
                            worker) below, not as order receipts. */}
            {!isCompanyContext && fulfilledOrders.length > 0 && (
              <View style={{ gap: 12 }}>
                <View style={styles.sectionHeader}>
                  <CheckCircle size={14} color={tokens.colors.primary} />
                  <View
                    style={{
                      flex: 1,
                      height: 1,
                      backgroundColor: `${tokens.colors.primary}1A`,
                      marginLeft: 8,
                    }}
                  />
                  <Text
                    allowFontScaling={false}
                    style={[styles.sectionLabel, { color: tokens.colors.primary, marginBottom: 0 }]}
                  >
                    {t('codes.fulfilledOrders')}
                  </Text>
                </View>
                {fulfilledOrders.map((order) => (
                  <OrderCard
                    key={order.id}
                    order={order}
                    isExpanded={expandedOrders.has(order.id)}
                    onToggle={toggleOrderExpand}
                    onVoucherPress={(v) => {
                      const fullVoucher = vouchers.find((v2) => v2.id === v.id) || v;
                      setSelectedVoucher(fullVoucher);
                    }}
                    onVoucherLongPress={(v) => {
                      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                      const fullVoucher = vouchers.find((v2) => v2.id === v.id) || v;
                      setSelectedVoucher(fullVoucher);
                    }}
                    brandColor={brandColorFor(order.provider, tokens)}
                  />
                ))}
              </View>
            )}

            {/* RENEWAL ORDERS ("Продовження") — kept out of the purchase
                            sections; the renewed/extended voucher itself lives under
                            "Доступні" with its new expiry, not nested here. Personal
                            context only — company stock is shown below. */}
            {!isCompanyContext && renewalOrders.length > 0 && (
              <View style={{ gap: 12 }}>
                <View style={styles.sectionHeader}>
                  <RefreshCw size={14} color={tokens.colors.warning} />
                  <View
                    style={{
                      flex: 1,
                      height: 1,
                      backgroundColor: `${tokens.colors.warning}1A`,
                      marginLeft: 8,
                    }}
                  />
                  <Text
                    allowFontScaling={false}
                    style={[styles.sectionLabel, { color: tokens.colors.warning, marginBottom: 0 }]}
                  >
                    {t('codes.renewalOrders')}
                  </Text>
                </View>
                {renewalOrders.map((order) => (
                  <OrderCard
                    key={order.id}
                    order={order}
                    isExpanded={expandedOrders.has(order.id)}
                    onToggle={toggleOrderExpand}
                    onVoucherPress={(v) => {
                      const fullVoucher = vouchers.find((v2) => v2.id === v.id) || v;
                      setSelectedVoucher(fullVoucher);
                    }}
                    onVoucherLongPress={(v) => {
                      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                      const fullVoucher = vouchers.find((v2) => v2.id === v.id) || v;
                      setSelectedVoucher(fullVoucher);
                    }}
                    onPay={handlePay}
                    onDelete={handleDeleteOrder}
                    brandColor={brandColorFor(order.provider, tokens)}
                  />
                ))}
              </View>
            )}

            {/* AVAILABLE VOUCHERS (personal context) — not linked to any order.
                            In a company context these same vouchers surface below as stock
                            (pool + per-worker) instead (multi-company epic #103, S2). */}
            {!isCompanyContext && unassignedVouchers.length > 0 && (
              <View style={{ gap: 12 }}>
                <View style={styles.sectionHeader}>
                  <View
                    style={{
                      flex: 1,
                      height: 1,
                      backgroundColor: `${tokens.colors.text.neon}1A`,
                      marginRight: 8,
                    }}
                  />
                  <Text
                    allowFontScaling={false}
                    style={[
                      styles.sectionLabel,
                      { color: tokens.colors.text.neon, marginBottom: 0 },
                    ]}
                  >
                    {t('codes.availablePayloads')}
                  </Text>
                </View>
                {unassignedVouchers.map((voucher) => (
                  <VoucherCard
                    key={voucher.id}
                    voucher={voucher}
                    userId={user?.id}
                    pulseAnim={pulseAnim}
                    onSelect={setSelectedVoucher}
                  />
                ))}
              </View>
            )}

            {/* WORKER CONTEXT — the fuel this company issued to me (epic #103 S5). Each
                            company handover is a real order now, so it renders as a receipt
                            with its vouchers inside it, exactly like the owner sees their
                            fulfilled orders. Only fuel predating issuance orders (or arriving
                            another way) stays a flat list below. */}
            {isWorkerContext && (
              <View style={{ gap: 12 }}>
                {issuanceOrders.length > 0 && (
                  <>
                    <View style={styles.sectionHeader}>
                      <Fuel size={14} color={tokens.colors.accent} />
                      <View
                        style={{
                          flex: 1,
                          height: 1,
                          backgroundColor: `${tokens.colors.accent}1A`,
                          marginHorizontal: 8,
                        }}
                      />
                      <Text
                        allowFontScaling={false}
                        style={[
                          styles.sectionLabel,
                          { color: tokens.colors.accent, marginBottom: 0 },
                        ]}
                      >
                        {t('codes.stock.issuedReceipts')} · {issuanceOrders.length}
                      </Text>
                    </View>
                    {issuanceOrders.map((order) => (
                      <OrderCard
                        key={order.id}
                        order={order}
                        isExpanded={expandedOrders.has(order.id)}
                        onToggle={toggleOrderExpand}
                        onVoucherPress={(v) => {
                          const fullVoucher = vouchers.find((v2) => v2.id === v.id) || v;
                          setSelectedVoucher(fullVoucher);
                        }}
                        onVoucherLongPress={(v) => {
                          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                          const fullVoucher = vouchers.find((v2) => v2.id === v.id) || v;
                          setSelectedVoucher(fullVoucher);
                        }}
                        brandColor={brandColorFor(order.provider, tokens)}
                      />
                    ))}
                  </>
                )}

                {looseIssued.length > 0 && (
                  <>
                    {issuanceOrders.length > 0 && (
                      <View style={styles.sectionHeader}>
                        <Fuel size={14} color={tokens.colors.accent} />
                        <View
                          style={{
                            flex: 1,
                            height: 1,
                            backgroundColor: `${tokens.colors.accent}1A`,
                            marginHorizontal: 8,
                          }}
                        />
                        <Text
                          allowFontScaling={false}
                          style={[
                            styles.sectionLabel,
                            { color: tokens.colors.accent, marginBottom: 0 },
                          ]}
                        >
                          {t('codes.stock.issuedToYou')} · {looseIssued.length}
                        </Text>
                      </View>
                    )}
                    {looseIssued.map((voucher) => (
                      <VoucherCard
                        key={voucher.id}
                        voucher={voucher}
                        userId={user?.id}
                        pulseAnim={pulseAnim}
                        onSelect={setSelectedVoucher}
                      />
                    ))}
                  </>
                )}
              </View>
            )}
            {/* The pool and every worker's holdings now live in the company hub:
                            the wallet keeps receipts only, so the same fuel is never
                            listed in two places that can disagree. */}
            {isCompanyContext && (
              <Pressable
                accessibilityRole="button"
                onPress={() => router.push('/company')}
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  paddingVertical: 14,
                  paddingHorizontal: 16,
                  borderRadius: 14,
                  borderWidth: 1,
                  borderColor: `${tokens.colors.primary}33`,
                  backgroundColor: `${tokens.colors.primary}0F`,
                }}
              >
                <Text
                  allowFontScaling={false}
                  style={{
                    fontSize: 13,
                    fontFamily: 'Inter',
                    color: tokens.colors.text.muted,
                  }}
                >
                  {t('company.managementTitle')}
                </Text>
                <ChevronRight size={18} color={tokens.colors.primary} />
              </Pressable>
            )}
          </View>
        )}
      </ScrollView>

      <VoucherDetailModal
        visible={!!selectedVoucher}
        voucher={selectedVoucher}
        user={user}
        onClose={() => setSelectedVoucher(null)}
        onToggleUsed={toggleUsed}
        brandColor={brandColorFor(selectedVoucher?.provider, tokens)}
        canRenew={selectedCanRenew}
        onRenew={(v) => {
          setSelectedVoucher(null);
          router.push(`/renew?voucherIds=${v.id}`);
        }}
      />
    </GridPageLayout>
  );
}

const styles = StyleSheet.create({
  sectionHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  sectionLabel: {
    fontFamily: 'Rajdhani-SemiBold',
    fontSize: 12,
    letterSpacing: 6,
    textTransform: 'uppercase',
    marginBottom: 8,
  },
  diagonalStampContainer: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    left: 0,
    right: 0,
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
  },
  diagonalStamp: {
    borderWidth: 1,
    padding: 2,
    transform: [{ rotate: '-12deg' }],
  },
  diagonalStampInner: {
    borderWidth: 2,
    paddingHorizontal: 20,
    paddingVertical: 6,
  },
  diagonalStampText: {
    fontSize: 22,
    fontFamily: 'Rajdhani-Bold',
    letterSpacing: 6,
    textTransform: 'uppercase',
  },
  emptyContainer: {
    marginTop: 80,
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 80,
    borderRadius: 4,
  },
  emptyIconBox: {
    width: 80,
    height: 80,
    borderRadius: 40,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 24,
  },
  emptyTitle: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 24,
    letterSpacing: 4,
    textTransform: 'uppercase',
  },
  emptySubtitle: {
    fontSize: 12,
    fontFamily: 'Inter-Bold',
    marginTop: 8,
    letterSpacing: 1,
  },
  errorContainer: {
    marginTop: 80,
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 80,
    borderRadius: 4,
    gap: 16,
  },
  errorTitle: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 20,
    letterSpacing: 3,
    textTransform: 'uppercase',
    textAlign: 'center',
  },
});
