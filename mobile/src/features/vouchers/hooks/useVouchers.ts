import { useState, useEffect, useCallback } from 'react';
import { getMyVouchers, getMyOrders } from '../api/getVouchers';
import { markVoucherAsUsed, restoreVoucher } from '../api/updateVoucher';
import type { Voucher, Order } from '../../../core/types/api';

interface UseVouchersReturn {
  vouchers: Voucher[];
  orders: Order[];
  loading: boolean;
  selectedVoucher: Voucher | null;
  pendingOrders: Order[];
  setSelectedVoucher: (voucher: Voucher | null) => void;
  toggleUsed: (voucher: Voucher) => Promise<void>;
  refresh: () => Promise<void>;
}

export function useVouchers(): UseVouchersReturn {
  const [vouchers, setVouchers] = useState<Voucher[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedVoucher, setSelectedVoucher] = useState<Voucher | null>(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      const [vouchersData, ordersData] = await Promise.all([
        getMyVouchers(),
        getMyOrders(),
      ]);
      setVouchers(Array.isArray(vouchersData) ? vouchersData : []);
      setOrders(Array.isArray(ordersData) ? ordersData : []);
    } catch (error: any) {
      console.log('Data fetch failed:', error.message);
    } finally {
      setLoading(false);
    }
  }, []);

  const toggleUsed = async (voucher: Voucher) => {
    try {
      const isCurrentlyUsed = voucher.status === 'used';
      if (isCurrentlyUsed) {
        await restoreVoucher(voucher.id);
      } else {
        await markVoucherAsUsed(voucher.id);
      }
      await loadData();
    } catch (error: any) {
      console.error('Failed to update status:', error);
    }
  };

  const pendingOrders = orders.filter(
    (o) => o.status === 'PENDING_FULFILLMENT',
  );

  useEffect(() => {
    loadData();
  }, [loadData]);

  return {
    vouchers,
    orders,
    loading,
    selectedVoucher,
    pendingOrders,
    setSelectedVoucher,
    toggleUsed,
    refresh: loadData,
  };
}
