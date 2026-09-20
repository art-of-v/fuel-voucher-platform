import { useEffect, useMemo, useState } from 'react';
import { Alert } from 'react-native';
import { getMyVouchers, getMyOrders, deleteMyOrder } from '../api/getVouchers';
import { markVoucherAsUsed, restoreVoucher, VoucherActionError } from '../api/updateVoucher';
import type { Voucher, Order } from '../../../core/types/api';
import { classifyVoucher } from '../../../core/types/api';
import { useI18n } from '../../../core/i18n';
import { useAuth } from '../../auth/hooks/useAuth';
import { useStore } from '../../../core/state/appStore';

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
      console.log('Data fetch failed - likely connection or auth issue:', error.message);
      setError(error?.message || 'Failed to load');
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
      console.log('Background refresh failed:', error.message);
    }
  };

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

  const pendingOrders = orders.filter(
    (o) => o.status === 'PENDING_FULFILLMENT' || o.status === 'PENDING_PAYMENT',
  );
  const fulfilledOrders = orders.filter(
    (o) => o.status === 'FULFILLED' || o.status === 'PARTIALLY_REFUNDED',
  );

  const assignedVoucherIds = useMemo(() => {
    const ids = new Set<string>();
    orders.forEach((order) => {
      (order.vouchers || []).forEach((v) => ids.add(v.id));
    });
    return ids;
  }, [orders]);

  const unassignedVouchers = vouchers.filter((v) => !assignedVoucherIds.has(v.id));

  return {
    // auth
    isAuthenticated,
    authLoading,
    user,
    // data
    vouchers,
    orders,
    loading,
    error,
    selectedVoucher,
    setSelectedVoucher,
    // derived
    pendingOrders,
    fulfilledOrders,
    unassignedVouchers,
    // actions
    loadData,
    toggleUsed,
    deleteOrder,
  };
}
