import { useEffect, useMemo, useState } from 'react';
import { Alert } from 'react-native';
import { getMyVouchers, getMyOrders, deleteMyOrder } from '../api/getVouchers';
import { markVoucherAsUsed, restoreVoucher, VoucherActionError } from '../api/updateVoucher';
import type { Voucher, Order } from '../../../core/types/api';
import { classifyVoucher } from '../../../core/types/api';
import { useI18n } from '../../../core/i18n';
import { useAuth } from '../../auth/hooks/useAuth';
import { useStore } from '../../../core/state/appStore';
import { reportError } from '../../../core/observability/sentry';
import { useAccountContext } from '../../company/hooks/useAccountContext';
import {
  filterVouchersByContext,
  filterOrdersByContext,
  groupCompanyStock,
} from '../../company/lib/stock';
import { splitByIssuanceReceipt, isLiveWalletVoucher } from '../lib/display';
import { useRefreshOnFocus } from '../../../core/hooks/useRefreshOnFocus';

/**
 * Data layer for the my-codes wallet screen: loads the user's vouchers and orders,
 * owns the optimistic mark-used / restore flow (with the ownership guards and the
 * VoucherActionError → message mapping), order deletion, and the order/voucher
 * groupings the screen renders. UI-only concerns — the pulse animation, expanded-
 * card set, pull-to-refresh spinner, brand colours, and confirmation dialogs —
 * stay in the screen.
 *
 * NOTE: intentionally not built on the older `useVouchers` hook. This screen's
 * behaviour is richer (error state, optimistic toggle, typed action errors,
 * selected-voucher preservation on refresh, order deletion), so folding it into
 * that hook would change behaviour. This is a faithful extraction of the screen.
 */
export function useMyCodes() {
  const { t } = useI18n();
  const { isAuthenticated: hookAuth, isLoading: authLoading, user } = useAuth();
  const storeAuth = useStore((state) => state.isAuthenticated);
  const isAuthenticated = storeAuth || hookAuth;

  // Active account context (multi-company epic #103, S2 + S5). Both lists are cached
  // (the switcher shares their keys), so arriving here they are usually warm.
  const context = useAccountContext();

  const [vouchers, setVouchers] = useState<Voucher[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedVoucher, setSelectedVoucher] = useState<Voucher | null>(null);

  useEffect(() => {
    if (isAuthenticated) {
      loadData();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated]);

  const loadData = async () => {
    try {
      setLoading(true);
      const [vouchersData, ordersData] = await Promise.all([getMyVouchers(), getMyOrders()]);
      setVouchers(Array.isArray(vouchersData) ? vouchersData : []);
      setOrders(Array.isArray(ordersData) ? ordersData : []);
      setError(null);
    } catch (error: any) {
      // Keep the raw cause in the logs and forward unexpected failures to Sentry;
      // the screen shows localized copy (codes.failedToLoad), never a raw string.
      console.log('Data fetch failed - likely connection or auth issue:', error?.message);
      if ((error?.status ?? 500) >= 500) {
        reportError(error);
      }
      setError(t('codes.failedToLoad'));
    } finally {
      setLoading(false);
    }
  };

  const refreshVouchers = async () => {
    try {
      const [vouchersData, ordersData] = await Promise.all([getMyVouchers(), getMyOrders()]);
      const newVouchers = Array.isArray(vouchersData) ? vouchersData : [];
      setVouchers(newVouchers);
      setOrders(Array.isArray(ordersData) ? ordersData : []);
      setSelectedVoucher((prev) => {
        if (!prev) return null;
        return newVouchers.find((v) => v.id === prev.id) || prev;
      });
    } catch (error: any) {
      // Silent background refresh: keep the previous data on screen. Log the cause
      // and forward unexpected (server/network) failures to Sentry.
      console.log('Background refresh failed:', error?.message);
      if ((error?.status ?? 500) >= 500) {
        reportError(error);
      }
    }
  };

  // Regenerating data on focus is silent by design (#164): the wallet keeps showing what it has
  // while it refetches, so tapping a tab never flashes the loader over data being read.
  useRefreshOnFocus(refreshVouchers, isAuthenticated);

  const toggleUsed = async (voucher: Voucher) => {
    // Owners viewing a voucher gifted to a worker — or any blocked voucher —
    // cannot redeem it. The UI hides the action, but guard here too (§6).
    const kind = classifyVoucher(voucher, user?.id);
    if (kind === 'blocked') {
      Alert.alert(t('common.error'), t('voucher.error.blocked'));
      return;
    }
    if (kind === 'gifted_to_worker') {
      Alert.alert(t('common.error'), t('voucher.error.workerOnly'));
      return;
    }
    const newStatus = voucher.status === 'used' ? 'active' : 'used';
    setSelectedVoucher((prev) => (prev?.id === voucher.id ? { ...prev, status: newStatus } : prev));
    setVouchers((prev) => prev.map((v) => (v.id === voucher.id ? { ...v, status: newStatus } : v)));
    try {
      if (voucher.status === 'used') {
        await restoreVoucher(voucher.id);
      } else {
        await markVoucherAsUsed(voucher.id);
      }
      await refreshVouchers();
    } catch (error: any) {
      await refreshVouchers();
      console.error('Failed to update status:', error);
      let message = t('codes.updateFailed');
      if (error instanceof VoucherActionError) {
        if (error.code === 'forbidden') message = t('voucher.error.workerOnly');
        else if (error.code === 'not_found') message = t('voucher.error.notFound');
        else if (error.code === 'invalid_state') message = t('voucher.error.invalidState');
        else if (error.code === 'unauthorized') message = t('voucher.error.unauthorized');
      } else if ((error?.status ?? 500) >= 500) {
        // Unexpected (non-typed) failure — forward the raw cause to Sentry.
        reportError(error);
      }
      Alert.alert(t('common.error'), message);
    }
  };

  /**
   * Deletes an unpaid order. Optimistically drops it from the list on success;
   * on failure reloads to resync and rethrows so the screen can alert. The
   * paid-order guard and the confirmation dialog live in the screen.
   */
  const deleteOrder = async (orderId: string) => {
    try {
      await deleteMyOrder(orderId);
      setOrders((prev) => prev.filter((o) => o.id !== orderId));
    } catch (err) {
      loadData();
      throw err;
    }
  };

  // Active account context (multi-company epic #103, S2 + S5): a stale/foreign id
  // resolves to personal (useAccountContext), so the wallet is scoped to what the user
  // actually sees — personal → personal vouchers only; an owner context → that
  // company's whole stock; a worker context → only the fuel issued to this worker.
  // No cross-context leak either way.
  const currentCompany = context.company;
  const isCompanyContext = context.kind !== 'personal';
  const isWorkerContext = context.kind === 'worker';

  const scopedVouchers = useMemo(
    () => filterVouchersByContext(vouchers, context, user?.id),
    [vouchers, context, user?.id],
  );
  const scopedOrders = useMemo(() => filterOrdersByContext(orders, context), [orders, context]);

  // The wallet is orders containing vouchers (see docs/DESIGN.md "wallet shape").
  // Renewal orders are NOT shown as their own cards: an extend/replace is an event in a
  // voucher's History, and the live voucher it produced is filed under the customer's
  // ORIGINAL purchase order via `originOrderId` (resolved on the server across replace swaps).
  const issuanceOrders = scopedOrders.filter((o) => o.kind === 'ReceivedFromCompany');
  const purchaseOrders = scopedOrders.filter(
    (o) => o.kind !== 'ReceivedFromCompany' && !o.isRenewal,
  );

  const pendingOrders = purchaseOrders.filter(
    (o) => o.status === 'PENDING_FULFILLMENT' || o.status === 'PENDING_PAYMENT',
  );

  // Live vouchers the customer currently holds, grouped under the purchase order they belong to.
  //
  // Built from the vouchers the sync response NESTS in each order, not from /api/vouchers/my: only
  // the nested VoucherDto carries `history` and `originOrderId`, and the wallet's whole point is the
  // per-voucher History (planning #180). `/api/vouchers/my` is still fetched — the context filter,
  // the company stock views and the issuance split all read it — but it is not the wallet's source.
  //
  // `originOrderId` re-parents a replacement onto the order the customer first bought, so one tank of
  // fuel stays one asset. That is why EVERY order is scanned, renewal orders included: a replace
  // delivers its stock voucher under the RENEWAL order's fulfillment, so scanning only purchase
  // orders would file it nowhere and the renewed fuel would vanish from the wallet (planning #180).
  // A retired/replaced original is Expired, so it drops out of the live filter below.
  const liveVouchersByOrder = useMemo(() => {
    const map = new Map<string, Voucher[]>();
    scopedOrders.forEach((order) => {
      (order.vouchers || []).forEach((voucher) => {
        if (!isLiveWalletVoucher(voucher)) return;
        const key = voucher.originOrderId ?? order.id;
        const list = map.get(key);
        if (list) list.push(voucher);
        else map.set(key, [voucher]);
      });
    });
    return map;
  }, [scopedOrders]);

  // Fulfilled purchase orders, each carrying its re-parented live vouchers. An order with no live
  // voucher left (everything used up and expired) is dropped from the wallet.
  const fulfilledOrders = useMemo(
    () =>
      purchaseOrders
        .filter((o) => o.status === 'FULFILLED' || o.status === 'PARTIALLY_REFUNDED')
        .map((o) => ({ ...o, vouchers: liveVouchersByOrder.get(o.id) ?? [] }))
        .filter((o) => o.vouchers.length > 0),
    [purchaseOrders, liveVouchersByOrder],
  );

  // Fuel the company handed over, split into the receipts it arrived in plus anything that
  // predates issuance orders and cannot honestly be filed under a handover.
  const { receipts, vouchersInReceipts, loose } = useMemo(
    () => splitByIssuanceReceipt(issuanceOrders, scopedVouchers),
    [issuanceOrders, scopedVouchers],
  );

  // Owner-context stock: undistributed pool + per-worker groups, computed over
  // every scoped company voucher (the stock view is not order-centric). Only the
  // owner sees it - for a worker it would be the employer's stock, and their own
  // vouchers are already filtered to what was issued to them.
  const companyStock = useMemo(
    () =>
      isWorkerContext
        ? { pool: [], poolLiters: 0, workers: [] }
        : groupCompanyStock(scopedVouchers),
    [scopedVouchers, isWorkerContext],
  );

  return {
    // auth
    isAuthenticated,
    authLoading,
    user,
    // data (scoped to the active account context)
    vouchers: scopedVouchers,
    orders: scopedOrders,
    loading,
    error,
    selectedVoucher,
    setSelectedVoucher,
    // context (multi-company epic #103, S2 + S5)
    context,
    currentCompany,
    isCompanyContext,
    isWorkerContext,
    companyStock,
    // derived: orders containing vouchers (renewals are folded into voucher History)
    pendingOrders,
    fulfilledOrders,
    issuanceOrders: receipts,
    issuanceVouchers: vouchersInReceipts,
    looseIssuanceVouchers: loose,
    // actions
    loadData,
    toggleUsed,
    deleteOrder,
  };
}
