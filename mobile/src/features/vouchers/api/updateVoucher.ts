import { apiFetch } from '../../../core/api/apiClient';

export async function markVoucherAsUsed(
  voucherId: string,
): Promise<{ message: string; status: string }> {
  const response = await apiFetch(`/api/vouchers/${voucherId}/mark-used`, {
    method: 'PATCH',
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error('Unauthorized');
    if (response.status === 404) throw new Error('Voucher not found');
    throw new Error('Failed to mark voucher as used');
  }
  return response.json();
}

export async function restoreVoucher(
  voucherId: string,
): Promise<{ message: string; status: string }> {
  const response = await apiFetch(`/api/vouchers/${voucherId}/restore`, {
    method: 'PATCH',
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error('Unauthorized');
    if (response.status === 404) throw new Error('Voucher not found');
    throw new Error('Failed to restore voucher');
  }
  return response.json();
}
