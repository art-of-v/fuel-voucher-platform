import { apiFetch } from '../../../core/api/apiClient';

// Typed error for voucher use/restore so the UI can map failures to localized
// copy. Note the backend does NOT return JSON on 4xx for mark-used: 403 has an
// empty body, 400/404 are plain strings — so we never parse the body here.
export class VoucherActionError extends Error {
  status: number;
  code: 'forbidden' | 'not_found' | 'invalid_state' | 'unauthorized' | 'unknown';
  constructor(status: number, code: VoucherActionError['code']) {
    super(code);
    this.name = 'VoucherActionError';
    this.status = status;
    this.code = code;
  }
}

function toActionError(status: number): VoucherActionError {
  if (status === 401) return new VoucherActionError(401, 'unauthorized');
  if (status === 403) return new VoucherActionError(403, 'forbidden');
  if (status === 404) return new VoucherActionError(404, 'not_found');
  if (status === 400) return new VoucherActionError(400, 'invalid_state');
  return new VoucherActionError(status, 'unknown');
}

export async function markVoucherAsUsed(
  voucherId: string,
): Promise<{ success?: boolean; message?: string; errorCode?: string }> {
  const response = await apiFetch(`/api/vouchers/${voucherId}/mark-used`, {
    method: 'PATCH',
  });
  if (!response.ok) {
    // 403 → gifted voucher belongs to another worker / owner is blocked.
    throw toActionError(response.status);
  }
  return response.json();
}

export async function restoreVoucher(
  voucherId: string,
): Promise<{ success?: boolean; message?: string; errorCode?: string }> {
  const response = await apiFetch(`/api/vouchers/${voucherId}/restore`, {
    method: 'PATCH',
  });
  if (!response.ok) {
    throw toActionError(response.status);
  }
  return response.json();
}
