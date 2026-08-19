# Company Workers

Lets a user who owns a `LegalEntity` (the **owner**) invite registered users as **workers**,
gift them company fuel vouchers, recall those vouchers, and fire workers. Workers redeem
gifted vouchers like their own but cannot purchase on behalf of the company.

This is a backend feature reference. All routes require an authenticated user token
(`[Authorize]`); "owner" vs "worker" is determined by the caller's relationship to the
`LegalEntity`, not by a separate role.

---

## Design decisions

| Question | Decision |
|---|---|
| Worker invite mechanism | Owner invites by phone number; the worker must already be registered |
| Worker in multiple companies | No — one company only |
| Who is the owner | The `User` who created a `LegalEntity` (via `POST /api/legal-entity/profile`) |
| Recall destination | Back to the company pool: `AssignedToUserId` stays the owner, `WorkerUserId` cleared, status stays `Assigned` |
| Fired worker → voucher status | `Blocked` — frozen until the station cancels them and admin uploads replacements |
| Voucher replacement after firing | Admin imports new vouchers and manually unblocks/replaces `Blocked` ones |
| Worker purchase rights | Worker may still buy for personal use; cannot buy on behalf of the company |
| Company vs personal purchase | Client sends `legalEntityId` at checkout to mark a purchase company-owned; omitting it = personal |
| `LegalEntityId` on voucher | Authoritative company owner; `AssignedToUserId` remains the purchasing user |

---

## Data model

New fields on `FuelVoucher`:

```
LegalEntityId   Guid?   FK → legal_entities (SetNull on delete) — the company (null = personal)
WorkerUserId    Guid?   FK → users          (SetNull on delete) — worker gifted to (null = pool/personal)
```

`AssignedToUserId` is always the **purchasing** user (owner or personal buyer).

**Status semantics:**

| AssignedToUserId | LegalEntityId | WorkerUserId | Status | Meaning |
|---|---|---|---|---|
| user | null | null | Assigned | Personal voucher |
| owner | company | null | Assigned | Company voucher, in the pool |
| owner | company | worker | Assigned | Company voucher, gifted to a worker |
| owner | company | null | Blocked | Frozen after the worker was fired |
| any | any | any | Used | Redeemed |

New entities:

```
CompanyInvitation { Id, LegalEntityId, OwnerUserId, WorkerPhoneNumber, WorkerUserId,
                    Status (Pending|Accepted|Declined|Cancelled), CreatedAtUtc, UpdatedAtUtc }

CompanyMember     { Id, LegalEntityId, WorkerUserId (unique — one company per worker), JoinedAtUtc }
```

New `VoucherStatus` value: **`Blocked`** — frozen after a worker is fired; awaiting
station-side cancellation + admin replacement.

---

## API — `/api/company`

### Owner

| Method | Route | Success | Failures |
|---|---|---|---|
| `POST` | `/invitations` | `200 { invitationId, status: "Pending" }` | `400` no company / invalid phone / worker not found / self-invite; `409` worker already a member / already pending |
| `GET` | `/invitations` | `200 [ CompanyInvitation… ]` | — |
| `DELETE` | `/invitations/{id}` | `200 { success: true }` | `404` not found; `409` not pending |
| `GET` | `/members` | `200 [ { …, giftedVoucherCount } ]` | — |
| `DELETE` | `/members/{id}` | `200 { success: true, blockedVoucherCount }` | `400` no company; `404` not found |
| `POST` | `/vouchers/gift` | `200 { success: true, giftedCount }` | `400` no company / not a member / empty list; `404` voucher not found; `409` not eligible |
| `POST` | `/vouchers/recall/{voucherId}` | `200 { success: true }` | `400` no company; `404` not found; `403` not this owner's; `409` only gifted-assigned can be recalled |

`POST /vouchers/gift` body: `{ workerUserId, voucherIds: [] }`. Eligible vouchers are
company-owned, assigned to the owner, not already gifted, and in `Assigned` status.

Firing a worker (`DELETE /members/{id}`) removes membership and, in one transaction, sets
every voucher gifted to that worker to `Blocked` and clears its `WorkerUserId`.

### Worker

| Method | Route | Success | Failures |
|---|---|---|---|
| `GET` | `/my-invitations` | `200 [ { …, legalEntityName, ownerPhoneNumber… } ]` | — |
| `POST` | `/invitations/{id}/accept` | `200 { success: true, memberId }` | `404` not found; `409` not pending / already a member |
| `POST` | `/invitations/{id}/decline` | `200 { success: true }` | `404` not found; `409` not pending |

---

## Effects on existing endpoints

### Checkout — `POST /api/purchases` (and `/bulk`)

Optional `legalEntityId: Guid?`. If present, the purchase is company-owned (the created
vouchers carry that `LegalEntityId`); if omitted, it is personal. A `legalEntityId` that does
not belong to the caller is rejected with `400`.

### `GET /api/vouchers/my`

Returns a **plain array** (not an object wrapper). Each voucher includes company fields:

- `source`: `"own"` | `"gifted"`
- `legalEntityId`: `Guid | null`
- `workerUserId`, `workerFirstName`, `workerLastName`: `null` unless gifted

A voucher appears for a worker when `WorkerUserId == me` (`source = "gifted"`). The owner
still sees company vouchers via `AssignedToUserId == owner`; the owner detects a gifted
voucher by `workerUserId != null`. Classification:

| `legalEntityId` | `workerUserId` | Meaning |
|---|---|---|
| `null` | `null` | Personal |
| set | `null` | Company pool (not yet gifted) |
| set | set | Company voucher gifted to a worker |

`GET /api/purchases/my` also carries `legalEntityId` on each order (and nested voucher):
`null` = personal purchase, set = company purchase.

### `PATCH /api/vouchers/{id}/mark-used`

If `WorkerUserId` is set, only that worker may mark the voucher used; others get `403`.
Worker-facing `GET /api/vouchers/my` currently filters to `Assigned`/`Used`, so workers
generally do not receive `Blocked` vouchers in that list.

---

## Admin support

- `GET /api/admin/vouchers?workerUserId=…` — filter by worker.
- `POST /api/admin/vouchers/{id}/unblock` — manually unblock a `Blocked` voucher (e.g. after
  the station confirms it was not redeemed). `GET /api/admin/vouchers?status=Blocked` lists them.

---

## Known limitation

`GET /api/voucher-catalog/{id}/qr` still authorizes QR access by `AssignedToUserId` (the owner)
only — it does **not** check `WorkerUserId`. So a worker cannot yet fetch the QR for a voucher
gifted to them; the owner still can, because company vouchers remain assigned to the owner.
Treat gifted-worker QR access as unsupported until this guard is updated.
