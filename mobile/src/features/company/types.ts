// Company owner / worker DTOs.
// See docs/MOBILE_CLIENT_COMPANY_WORKERS_CHANGES.md §10.

// Invitation as seen by the owner (sent invitations list).
export interface CompanyInvitationDto {
  id: string;
  legalEntityId: string;
  ownerUserId: string;
  workerUserId: string;
  workerPhoneNumber: string;
  workerFirstName?: string | null;
  workerLastName?: string | null;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

// Invitation as seen by the worker (my-invitations inbox). Pending only.
export interface MyCompanyInvitationDto {
  id: string;
  legalEntityId: string;
  legalEntityName: string;
  ownerUserId: string;
  ownerPhoneNumber: string;
  ownerFirstName?: string | null;
  ownerLastName?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

// A worker that belongs to the owner's company.
export interface CompanyMemberDto {
  id: string; // CompanyMember.Id — used to fire the worker
  workerUserId: string;
  workerPhoneNumber: string;
  workerFirstName?: string | null;
  workerLastName?: string | null;
  joinedAtUtc: string;
  giftedVoucherCount: number;
}
