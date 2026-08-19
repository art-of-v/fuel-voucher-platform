import { apiFetch } from '../../../core/api/apiClient';
import type {
  CompanyInvitationDto,
  MyCompanyInvitationDto,
  CompanyMemberDto,
} from '../types';

// Backend company endpoints return camelCase `{ error: "<Code>" }` on failure,
// bare arrays on list success, and some 4xx (e.g. recall 403) with an empty
// body. This error type preserves both the HTTP status and the backend code so
// the UI can map them to friendly, localized copy.
export class CompanyApiError extends Error {
  status: number;
  code: string;
  constructor(status: number, code: string) {
    super(code || `HTTP_${status}`);
    this.name = 'CompanyApiError';
    this.status = status;
    this.code = code;
  }
}

async function parseError(response: Response): Promise<CompanyApiError> {
  let code = '';
  try {
    const data = await response.json();
    code = data?.error ?? data?.message ?? '';
  } catch {
    // Empty body (e.g. 403 on recall) — fall back to a status-based code.
  }
  return new CompanyApiError(response.status, typeof code === 'string' ? code : '');
}

// Maps a CompanyApiError to an i18n key. Known backend codes get specific
// copy; anything else falls back to a status-based generic message.
export function companyErrorKey(error: unknown): string {
  if (error instanceof CompanyApiError) {
    switch (error.code) {
      case 'OwnerCompanyNotFound':
        return 'company.error.noCompany';
      case 'InvalidPhone':
        return 'company.error.invalidPhone';
      case 'WorkerNotFound':
        return 'company.error.workerNotFound';
      case 'CannotInviteSelf':
        return 'company.error.cannotInviteSelf';
      case 'WorkerAlreadyMember':
        return 'company.error.alreadyMember';
      case 'AlreadyPending':
        return 'company.error.alreadyPending';
      case 'WorkerNotMember':
        return 'company.error.workerNotMember';
      case 'EmptyVoucherList':
        return 'company.error.emptyVoucherList';
      case 'VoucherNotFound':
        return 'company.error.voucherNotFound';
      case 'VoucherNotEligible':
        return 'company.error.voucherNotEligible';
      case 'InvalidStatus':
        return 'company.error.invalidStatus';
      case 'InvalidState':
        return 'company.error.invalidState';
      case 'AlreadyMember':
        return 'company.error.alreadyMember';
      case 'NotFound':
        return 'company.error.notFound';
    }
    switch (error.status) {
      case 403:
        return 'company.error.forbidden';
      case 404:
        return 'company.error.notFound';
      case 409:
        return 'company.error.conflict';
      case 400:
        return 'company.error.badRequest';
    }
  }
  return 'company.error.generic';
}

// ---------------------------------------------------------------------------
// Worker endpoints
// ---------------------------------------------------------------------------

export async function getMyInvitations(): Promise<MyCompanyInvitationDto[]> {
  const response = await apiFetch('/api/company/my-invitations');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw await parseError(response);
  }
  const data = await response.json();
  return Array.isArray(data) ? data : [];
}

export async function acceptInvitation(
  invitationId: string,
): Promise<{ success: boolean; memberId?: string }> {
  const response = await apiFetch(
    `/api/company/invitations/${invitationId}/accept`,
    { method: 'POST' },
  );
  if (!response.ok) throw await parseError(response);
  return response.json();
}

export async function declineInvitation(
  invitationId: string,
): Promise<{ success: boolean }> {
  const response = await apiFetch(
    `/api/company/invitations/${invitationId}/decline`,
    { method: 'POST' },
  );
  if (!response.ok) throw await parseError(response);
  return response.json();
}

// ---------------------------------------------------------------------------
// Owner endpoints
// ---------------------------------------------------------------------------

export async function getSentInvitations(): Promise<CompanyInvitationDto[]> {
  const response = await apiFetch('/api/company/invitations');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw await parseError(response);
  }
  const data = await response.json();
  return Array.isArray(data) ? data : [];
}

export async function sendInvitation(
  workerPhoneNumber: string,
): Promise<{ invitationId: string; status: string }> {
  const response = await apiFetch('/api/company/invitations', {
    method: 'POST',
    body: JSON.stringify({ workerPhoneNumber }),
  });
  if (!response.ok) throw await parseError(response);
  return response.json();
}

export async function cancelInvitation(
  invitationId: string,
): Promise<{ success: boolean }> {
  const response = await apiFetch(
    `/api/company/invitations/${invitationId}`,
    { method: 'DELETE' },
  );
  if (!response.ok) throw await parseError(response);
  return response.json();
}

export async function getMembers(): Promise<CompanyMemberDto[]> {
  const response = await apiFetch('/api/company/members');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw await parseError(response);
  }
  const data = await response.json();
  return Array.isArray(data) ? data : [];
}

export async function fireWorker(
  memberId: string,
): Promise<{ success: boolean; blockedVoucherCount: number }> {
  const response = await apiFetch(`/api/company/members/${memberId}`, {
    method: 'DELETE',
  });
  if (!response.ok) throw await parseError(response);
  return response.json();
}

export async function giftVouchers(
  workerUserId: string,
  voucherIds: string[],
): Promise<{ success: boolean; giftedCount: number }> {
  const response = await apiFetch('/api/company/vouchers/gift', {
    method: 'POST',
    body: JSON.stringify({ workerUserId, voucherIds }),
  });
  if (!response.ok) throw await parseError(response);
  return response.json();
}

export async function recallVoucher(
  voucherId: string,
): Promise<{ success: boolean }> {
  const response = await apiFetch(
    `/api/company/vouchers/recall/${voucherId}`,
    { method: 'POST' },
  );
  if (!response.ok) throw await parseError(response);
  return response.json();
}
