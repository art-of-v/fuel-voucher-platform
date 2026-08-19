# Company Workers Feature — Implementation Plan

## Overview

Allow a user who owns a `LegalEntity` ("owner/company") to invite regular users as workers, gift fuel
vouchers to them, and recall vouchers if needed. Workers can use gifted vouchers the same way as their
own but cannot purchase vouchers on behalf of the company.

---

## Answered Design Decisions

| Question | Decision |
|---|---|
| Worker invite mechanism | Owner sends invite by phone number; worker must already be registered in the system |
| Worker in multiple companies | No — one company only |
| Who is the owner | The `User` who has a `LegalEntity` record (via `POST /api/legal-entity/profile`) |
| Recall → voucher destination | Back to company "pool" (`AssignedToUserId` stays as owner, `WorkerUserId` cleared, status `Assigned`) |
| Worker fired → voucher status | `Blocked` — new status; frozen until fuel station blocks them at their end and admin uploads replacement vouchers |
| Voucher replacement after firing | Admin imports new vouchers; admin manually unblocks or replaces `Blocked` vouchers |
| Worker purchase rights | Worker can still buy vouchers for personal use; only cannot buy on behalf of the company |
| Worker voucher screen | Same `GET /api/vouchers/my` — shows vouchers where `WorkerUserId = me` |
| QR generation for gifted voucher | Only the worker (`WorkerUserId`) can generate QR; owner is blocked from QR on gifted vouchers |
| Company vs personal purchase | Client explicitly sends `legalEntityId` at checkout to mark a purchase as company-owned; omitting it means personal |
| LegalEntityId on voucher | Voucher carries `LegalEntityId` FK — the authoritative company owner; `AssignedToUserId` is still the purchasing user |

---

## Data Model Changes

### 1. New fields on `FuelVoucher`

```
LegalEntityId   Guid?    FK → legal_entities (nullable, SetNull on delete)
WorkerUserId    Guid?    FK → users          (nullable, SetNull on delete)
```

- `AssignedToUserId` = the **purchasing user** (owner or regular user)
- `LegalEntityId` = the **company** this voucher belongs to (null = personal purchase)
- `WorkerUserId` = the **worker** the voucher was gifted to (null = in company pool or personal)

**Status semantics with the new fields:**

| AssignedToUserId | LegalEntityId | WorkerUserId | Status   | Meaning                                    |
|------------------|---------------|--------------|----------|--------------------------------------------||
| user             | null          | null         | Assigned | Personal voucher, bought for himself       |
| owner            | company       | null         | Assigned | Company voucher, in company pool           |
| owner            | company       | worker       | Assigned | Company voucher, gifted to worker          |
| owner            | company       | null         | Blocked  | Frozen after worker was fired              |
| any              | any           | any          | Used     | Used (by owner, worker, or personal user)  |

### 2. New entity: `CompanyInvitation`

```
Id                  Guid        PK
LegalEntityId       Guid        FK → legal_entities
OwnerUserId         Guid        FK → users (the sender)
WorkerPhoneNumber   string      phone number of the invited user
WorkerUserId        Guid        FK → users (resolved when found)
Status              enum        Pending | Accepted | Declined | Cancelled
CreatedAtUtc        DateTime
UpdatedAtUtc        DateTime
```

### 3. New entity: `CompanyMember`

```
Id                  Guid        PK
LegalEntityId       Guid        FK → legal_entities
WorkerUserId        Guid        FK → users (unique — one company per worker)
JoinedAtUtc         DateTime
```

### 4. New `VoucherStatus` value

```csharp
Blocked   // frozen after worker was fired; awaiting station-side cancellation + admin replacement
```

---

## Phase 1 — Company Membership

**Goal:** establish and manage the owner ↔ worker relationship.

### New entities
- `CompanyInvitation` (see model above)
- `CompanyMember` (see model above)

### EF migration
- `AddCompanyMembership` — adds `company_invitations` and `company_members` tables

### Endpoints (`/api/company/...`, `[Authorize]`)

| Method | Route | Actor | Description |
|---|---|---|---|
| `POST` | `/api/company/invitations` | Owner | Send invite by phone number; validates target user exists + not already a member |
| `GET` | `/api/company/invitations` | Owner | List all sent invitations with statuses |
| `DELETE` | `/api/company/invitations/{id}` | Owner | Cancel a pending invitation |
| `GET` | `/api/company/my-invitations` | Worker | List pending invitations received |
| `POST` | `/api/company/invitations/{id}/accept` | Worker | Accept → creates `CompanyMember` |
| `POST` | `/api/company/invitations/{id}/decline` | Worker | Decline invitation |
| `GET` | `/api/company/members` | Owner | List all active workers (with gifted voucher counts) |
| `DELETE` | `/api/company/members/{id}` | Owner | Fire worker → auto-recall all their vouchers → set `Blocked` status |

### Business rules
- A user can only be a member of **one** company at a time
- Owner cannot invite themselves
- Only `Pending` invitations can be accepted/declined/cancelled
- Firing a worker in one transaction: sets all their `WorkerUserId` vouchers → `Blocked`, clears `WorkerUserId`, saves `CompanyMember` removal

---

## Phase 2 — Voucher Gifting & Recall

**Goal:** owner sends vouchers to workers and can recall them.

### Model change
- Add `WorkerUserId` (nullable) to `FuelVoucher`
- Add `Blocked` to `VoucherStatus` enum

### EF migration
- `AddWorkerVoucherAssignment` — adds `worker_user_id` column to `fuel_vouchers`, adds `Blocked` (enum is stored as `int`, no column type change needed)

### Endpoints

| Method | Route | Actor | Description |
|---|---|---|---|
| `POST` | `/api/company/vouchers/gift` | Owner | Body: `{ workerUserId, voucherIds[] }` — assigns vouchers to a worker |
| `POST` | `/api/company/vouchers/recall/{voucherId}` | Owner | Recalls one voucher → clears `WorkerUserId`, keeps status `Assigned` |

### Business rules
- Owner can only gift vouchers where `LegalEntityId = ownerCompany` AND `WorkerUserId = null` AND `Status = Assigned`
- Owner cannot gift more than the worker's membership allows (no cap for now, revisit later)
- On recall: `WorkerUserId = null`, status stays `Assigned` — voucher returns to owner's pool
- Owner **cannot** generate QR for vouchers where `WorkerUserId` is set
- Worker **can** generate QR only for vouchers where `WorkerUserId = me`

---

## Phase 3 — Worker Voucher Screen

**Goal:** worker and owner see correct voucher views.

### Changes to existing endpoints

- `GET /api/vouchers/my` — extend to also return vouchers where `WorkerUserId = me` (in addition to `AssignedToUserId = me`)
  - Add a `source` field to the response DTO: `"own"` | `"gifted"`
- `GET /api/admin/vouchers` — add `workerUserId` filter
- Owner's voucher list response — add `workerUserId` + worker name fields so owner can see "Gifted to: Ivan Petrenko"

### QR generation guard
- `POST /api/vouchers/{id}/use` (MarkVoucherAsUsed) — if `WorkerUserId` is set, only the worker can mark it used; owner gets `403`

---

## Phase 4 — Blocked Vouchers & Admin Replacement Flow

**Goal:** handle the "fired worker made a photo of QR" risk.

### Behaviour
- `Blocked` vouchers are visible in admin panel with special badge
- Admin can manually **unblock** a voucher (set status back to `Assigned`, keep `AssignedToUserId`) if the station confirms it was not redeemed
- Admin imports replacement vouchers via the normal PDF import pipeline; admin can then **reassign** replacement vouchers to the original owner

### Endpoints (Admin)

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/admin/vouchers/{id}/unblock` | Manually unblock a `Blocked` voucher |
| `GET` | `/api/admin/vouchers?status=Blocked` | Already works via existing filter once `Blocked` is in the enum |

---

## Implementation Order

```
Phase 1  →  Phase 2  →  Phase 3  →  Phase 4
Members      Gifting      Screens      Admin tools
```

Each phase ends with:
- [ ] EF migration created and verified
- [ ] Build: 0 errors
- [ ] Unit tests for new command/query handlers
- [ ] Manual test checklist passed

---

## Files To Create / Modify (overview)

### Phase 1
| Action | Path |
|---|---|
| CREATE | `Features/Company/SharedModels/CompanyInvitation.cs` |
| CREATE | `Features/Company/SharedModels/CompanyMember.cs` |
| CREATE | `Features/Company/SharedModels/InvitationStatus.cs` |
| CREATE | `Features/Company/Configurations/CompanyInvitationConfiguration.cs` |
| CREATE | `Features/Company/Configurations/CompanyMemberConfiguration.cs` |
| CREATE | `Features/Company/SendInvitation/SendInvitationCommand.cs` + Handler |
| CREATE | `Features/Company/AcceptInvitation/AcceptInvitationCommand.cs` + Handler |
| CREATE | `Features/Company/DeclineInvitation/DeclineInvitationCommand.cs` + Handler |
| CREATE | `Features/Company/GetMembers/GetMembersQuery.cs` + Handler |
| CREATE | `Features/Company/FireWorker/FireWorkerCommand.cs` + Handler |
| CREATE | `Features/Company/CompanyController.cs` |
| MODIFY | `Persistence/ApplicationDbContext.cs` — add DbSets |
| CREATE | Migration `AddCompanyMembership` |

### Phase 2
| Action | Path |
|---|---|
| MODIFY | `Features/Vouchers/SharedModels/VoucherStatus.cs` — add `Blocked` |
| MODIFY | `Features/Vouchers/SharedModels/FuelVoucher.cs` — add `LegalEntityId` + `WorkerUserId` |
| MODIFY | `Features/Vouchers/Configurations/FuelVoucherConfiguration.cs` — add both FKs |
| MODIFY | `Features/Orders/CreateCheckout/BulkCheckoutCommand.cs` — add optional `LegalEntityId` field |
| MODIFY | `Features/Orders/CreateCheckout/BulkCheckoutCommandHandler.cs` — pass `LegalEntityId` to created voucher assignments |
| CREATE | `Features/Company/GiftVouchers/GiftVouchersCommand.cs` + Handler |
| CREATE | `Features/Company/RecallVoucher/RecallVoucherCommand.cs` + Handler |
| MODIFY | `Features/Company/CompanyController.cs` — new endpoints |
| CREATE | Migration `AddWorkerVoucherAssignment` |

### Phase 3
| Action | Path |
|---|---|
| MODIFY | `Features/Vouchers/GetUserVouchers/GetUserVouchersCommandHandler.cs` |
| MODIFY | `Features/Vouchers/GetUserVouchers/GetUserVouchersResponse.cs` — add `source` field |
| MODIFY | `Features/Vouchers/MarkVoucherAsUsed/MarkVoucherAsUsedCommandHandler.cs` — add worker guard |

### Phase 4
| Action | Path |
|---|---|
| CREATE | `Features/Vouchers/UnblockVoucher/UnblockVoucherCommand.cs` + Handler |
| MODIFY | `Features/Vouchers/AdminFuelVoucherController.cs` — add unblock endpoint |
