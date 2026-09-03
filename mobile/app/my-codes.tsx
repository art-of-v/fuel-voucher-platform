import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { View, RefreshControl, StyleSheet } from 'react-native';
import { Redirect } from 'expo-router';
import * as Linking from 'expo-linking';

import { getMyVouchers, getMyOrders } from '../src/features/vouchers/api/getVouchers';
import { markVoucherAsUsed, restoreVoucher, VoucherActionError } from '../src/features/vouchers/api/updateVoucher';
import type { Order, Voucher } from '../src/core/types/api';
import { classifyVoucher } from '../src/core/types/api';

import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  LoadingState,
  PageLayout,
  ScreenHeader,
  Text,
  toast,
} from '../src/core/ui';

import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { useStore } from '../src/core/state/appStore';

import { VoucherListItem } from '../src/components/VoucherListItem';
import { VoucherRedemptionSheet } from '../src/components/VoucherRedemptionSheet';

/**
 * The wallet — the user's home for everything they own at the pump.
 *
 * Phase 3 redesign. What this screen is now, and what it isn't:
 *
 *  - It is **a list of vouchers**, not a list of orders. Orders are receipts;
 *    vouchers are what you scan. Pending orders still surface — they are the
 *    fulfilment state of a not-yet-issued voucher — but they no longer
 *    dominate.
 *  - It is **organised by usability**, not by chronology. Usable vouchers
 *    lead. Used and expired vouchers collapse, so a customer with two live
 *    vouchers and thirty past ones still sees what they need in the first
 *    glance.
 *  - It **routes the user to the redemption sheet**, which is the only
 *    place the QR is rendered. Tapping a card opens the sheet directly — no
 *    "view details" intermediate screen.
 */
export default function MyCodesScreen() {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const { isAuthenticated: hookAuth, isLoading: authLoading, user } = useAuth();
  const storeAuth = useStore(state => state.isAuthenticated);
  const isAuthenticated = storeAuth || hookAuth;

  const [vouchers, setVouchers] = useState<Voucher[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [selectedVoucher, setSelectedVoucher] = useState<Voucher | null>(null);

  const refreshTokensRef = useRef(0);

  const loadData = useCallback(async () => {
    const token = ++refreshTokensRef.current;
    try {
      const [vouchersData, ordersData] = await Promise.all([
        getMyVouchers(),
        getMyOrders(),
      ]);
      if (token !== refreshTokensRef.current) return;
      setVouchers(Array.isArray(vouchersData) ? vouchersData : []);
      setOrders(Array.isArray(ordersData) ? ordersData : []);
      setLoadError(null);
    } catch (error: any) {
      if (token !== refreshTokensRef.current) return;
      console.log('Data fetch failed - likely connection or auth issue:', error?.message);
      setLoadError(error?.message || 'load_failed');
    } finally {
      if (token === refreshTokensRef.current) {
        setLoading(false);
        setRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    if (isAuthenticated) {
      loadData();
    }
  }, [isAuthenticated, loadData]);

  const refreshVouchers = useCallback(async () => {
    try {
      const [vouchersData, ordersData] = await Promise.all([
        getMyVouchers(),
        getMyOrders(),
      ]);
      const newVouchers = Array.isArray(vouchersData) ? vouchersData : [];
      setVouchers(newVouchers);
      setOrders(Array.isArray(ordersData) ? ordersData : []);
      setSelectedVoucher(prev => {
        if (!prev) return null;
        return newVouchers.find(v => v.id === prev.id) || prev;
      });
    } catch (error: any) {
      console.log('Background refresh failed:', error?.message);
    }
  }, []);

  const toggleUsed = useCallback(
    async (voucher: Voucher): Promise<boolean> => {
      const kind = classifyVoucher(voucher, user?.id);
      if (kind === 'blocked' || kind === 'gifted_to_worker') {
        toast.error(t('voucher.error.workerOnly'));
        return false;
      }

      const wasUsed = voucher.status === 'used';
      const optimistic = { ...voucher, status: wasUsed ? 'active' : 'used' };

      setSelectedVoucher(prev => (prev?.id === voucher.id ? optimistic : prev));
      setVouchers(prev => prev.map(v => (v.id === voucher.id ? optimistic : v)));

      try {
        if (wasUsed) {
          await restoreVoucher(voucher.id);
        } else {
          await markVoucherAsUsed(voucher.id);
        }
        await refreshVouchers();
        return true;
      } catch (error: any) {
        await refreshVouchers();
        let message = t('codes.updateFailed');
        if (error instanceof VoucherActionError) {
          if (error.code === 'forbidden') message = t('voucher.error.workerOnly');
          else if (error.code === 'not_found') message = t('voucher.error.notFound');
          else if (error.code === 'invalid_state') message = t('voucher.error.invalidState');
          else if (error.code === 'unauthorized') message = t('voucher.error.unauthorized');
        }
        toast.error(message);
        return false;
      }
    },
    [refreshVouchers, t, user?.id],
  );

  const handlePay = useCallback(async (order: Order) => {
    if (order.monobankPaymentUrl) {
      await Linking.openURL(order.monobankPaymentUrl);
    }
  }, []);

  const getBrandColor = useCallback(
    (provider: string = '') => {
      const p = provider.toLowerCase();
      const brandTokens = (tokens.colors.text as any).brand;
      if (p.includes('okko')) return brandTokens.okko;
      if (p.includes('wog')) return brandTokens.wog;
      if (p.includes('upg')) return brandTokens.upg;
      if (p.includes('klo')) return brandTokens.klo;
      if (p.includes('shell')) return brandTokens.shell;
      if (p.includes('socar')) return brandTokens.socar;
      return tokens.colors.primary;
    },
    [tokens],
  );

  // ─────────────────────────────────────────────────────────────────────────
  // Derived state
  // ─────────────────────────────────────────────────────────────────────────

  const { usableVouchers, usedVouchers, expiredVouchers, pendingOrders } = useMemo(() => {
    const now = Date.now();
    const usable: Voucher[] = [];
    const used: Voucher[] = [];
    const expired: Voucher[] = [];

    for (const v of vouchers) {
      const kind = classifyVoucher(v, user?.id);
      if (kind === 'blocked') {
        used.push(v);
        continue;
      }
      const isUsed = v.status === 'used';
      const exp = v.expirationDate ? new Date(v.expirationDate).getTime() < now : false;

      if (isUsed) used.push(v);
      else if (exp) expired.push(v);
      else usable.push(v);
    }

    const pending = orders.filter(
      o => o.status === 'PENDING_FULFILLMENT' || o.status === 'PENDING_PAYMENT',
    );

    return {
      usableVouchers: usable,
      usedVouchers: used,
      expiredVouchers: expired,
      pendingOrders: pending,
    };
  }, [vouchers, orders, user?.id]);

  const Header = (
    <ScreenHeader
      title={t('wallet.title')}
      subtitle={t('wallet.subtitle')}
      hideBack
    />
  );

  if (!isAuthenticated && !authLoading) {
    return <Redirect href="/landing" />;
  }

  if (loading && !refreshing) {
    return (
      <PageLayout header={Header} scroll={false}>
        <LoadingState fullScreen />
      </PageLayout>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  if (loadError && vouchers.length === 0 && orders.length === 0) {
    return (
      <PageLayout header={Header} scroll={false}>
        <ErrorState
          variant="offline"
          title={t('wallet.errorTitle')}
          description={t('wallet.errorDescription')}
          onRetry={() => {
            setLoading(true);
            loadData();
          }}
          fullScreen
        />
      </PageLayout>
    );
  }

  const hasNothing =
    usableVouchers.length === 0 &&
    usedVouchers.length === 0 &&
    expiredVouchers.length === 0 &&
    pendingOrders.length === 0;

  if (hasNothing) {
    return (
      <PageLayout header={Header} scroll={false}>
        <EmptyState
          title={t('wallet.emptyTitle')}
          description={t('wallet.emptyDescription')}
          style={{ flex: 1 }}
        />
      </PageLayout>
    );
  }

  const renderVoucher = (v: Voucher) => (
    <VoucherListItem
      key={v.id}
      voucher={v}
      brandColor={getBrandColor(v.provider)}
      currentUserId={user?.id}
      onPress={(voucher) => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
        setSelectedVoucher(voucher);
      }}
    />
  );

  return (
    <>
      <PageLayout
        header={Header}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => {
              setRefreshing(true);
              loadData();
            }}
            tintColor={tokens.colors.primary}
            colors={[tokens.colors.primary]}
          />
        }
        contentContainerStyle={{ gap: 16 }}
      >
        {/* Pending orders — the not-yet-issued vouchers */}
        {pendingOrders.length > 0 && (
          <Section title={t('codes.processingPurchases')}>
            {pendingOrders.map(order => (
              <PendingOrderCard
                key={order.id}
                order={order}
                onPay={handlePay}
              />
            ))}
          </Section>
        )}

        {/* Usable vouchers — the primary content */}
        {usableVouchers.length > 0 && (
          <Section
            title={t('wallet.sectionUsable')}
            trailing={
              <Text role="caption" tone="muted" style={{ letterSpacing: 1 }}>
                {String(usableVouchers.length)}
              </Text>
            }
          >
            {usableVouchers.map(renderVoucher)}
          </Section>
        )}

        {/* Used vouchers — collapsed after the first three. A "show all" link is
            reserved for a future history view; the wallet's job is the pump. */}
        {usedVouchers.length > 0 && (
          <Section
            title={t('wallet.sectionUsed')}
            trailing={
              <Text role="caption" tone="muted" style={{ letterSpacing: 1 }}>
                {String(usedVouchers.length)}
              </Text>
            }
          >
            {usedVouchers.slice(0, 3).map(renderVoucher)}
          </Section>
        )}

        {/* Expired vouchers — short list, dimmed */}
        {expiredVouchers.length > 0 && (
          <Section
            title={t('wallet.sectionExpired')}
            trailing={
              <Text role="caption" tone="muted" style={{ letterSpacing: 1 }}>
                {String(expiredVouchers.length)}
              </Text>
            }
          >
            {expiredVouchers.slice(0, 3).map(renderVoucher)}
          </Section>
        )}
      </PageLayout>

      <VoucherRedemptionSheet
        visible={!!selectedVoucher}
        voucher={selectedVoucher}
        user={user}
        brandColor={getBrandColor(selectedVoucher?.provider)}
        onClose={() => setSelectedVoucher(null)}
        onMarkUsed={toggleUsed}
        onRestore={toggleUsed}
      />
    </>
  );
}

function Section({
  title,
  trailing,
  children,
}: {
  title: string;
  trailing?: React.ReactNode;
  children: React.ReactNode;
}) {
  const tokens = useDesignTokens();
  return (
    <View style={{ gap: tokens.spacing.sm }}>
      <View style={styles.sectionHeader}>
        <Text
          role="caption"
          tone="muted"
          style={{
            textTransform: 'uppercase',
            letterSpacing: 1,
          }}
        >
          {title}
        </Text>
        {trailing}
      </View>
      <View style={{ gap: tokens.spacing.sm }}>{children}</View>
    </View>
  );
}

interface PendingOrderCardProps {
  order: Order;
  onPay: (order: Order) => void;
}

function PendingOrderCard({ order, onPay }: PendingOrderCardProps) {
  const { t } = useI18n();

  const liters = order.lineItems.reduce((sum, li) => sum + li.liters * li.quantity, 0);
  const isPayable = order.status === 'PENDING_PAYMENT' && !!order.monobankPaymentUrl;

  return (
    <Card padding="md">
      <View style={styles.pendingRow}>
        <View style={{ flex: 1, gap: 2 }}>
          <Text role="caption" tone="muted" style={{ textTransform: 'uppercase', letterSpacing: 1 }}>
            {order.provider}
          </Text>
          <Text role="bodyStrong">
            {liters} {t('packages.liters').toLowerCase()}
          </Text>
          <Text role="caption" tone="muted">
            {t('codes.pending')}
          </Text>
        </View>
        {isPayable && (
          <Button
            label={t('codes.payNow')}
            onPress={() => onPay(order)}
            variant="primary"
            size="md"
            fullWidth={false}
          />
        )}
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  sectionHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 4,
  },
  pendingRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
  },
});