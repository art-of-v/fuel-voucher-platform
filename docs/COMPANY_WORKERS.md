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
| Worker in multiple companies | No — one company only (relaxing this is planned, epic #103 S5/W1) |
| Who is the owner | The `User` who created a `LegalEntity` (via `POST /api/legal-entity/profile`) |
| Recall destination | Back to the company pool **and back under the purchase it arrived with**: `AssignedToUserId` stays the owner, `WorkerUserId` cleared, status stays `Assigned`, `OrderId` repointed off the issuance order |
| Fired worker → voucher status | `Blocked` — frozen until the station cancels them and admin uploads replacements |
| Voucher replacement after firing | Admin imports new vouchers and manually unblocks/replaces `Blocked` ones |
| Gift → the voucher's owning order | Gifting creates **one `ReceivedFromCompany` order per gift action** and repoints every gifted voucher's `OrderId` to it, so the worker gets a receipt instead of a flat list |
| Fuel coming back to the company | Recall, firing and the admin unblock all put the voucher back under its **purchase**; the owner's freeze/unblock never moves it — a frozen voucher stays the worker's so they can see why it is unusable |
| Handover in money views | `ReceivedFromCompany` carries `Price = 0` and is excluded from every revenue / margin / savings / refundable-amount view. Renewals still count — they are real customer payments |
| Worker purchase rights | Worker may still buy for personal use; cannot buy on behalf of the company |
| Worker context (mobile) | A member may switch into a company they work for: redeem the fuel issued to them, no owner tools, no buying. Owner rights win if the same person also owns the company |
| Company vs personal purchase | Client sends `legalEntityId` at checkout to mark a purchase company-owned; omitting it = personal |
| `LegalEntityId` on voucher | Authoritative company owner; `AssignedToUserId` remains the purchasing user |

---

## Data model

New fields on `FuelVoucher`:

```
LegalEntityId   Guid?   FK → legal_entities (SetNull on delete) — the company (null = personal)
WorkerUserId    Guid?   FK → users          (SetNull on delete) — worker gifted to (null = pool/personal)
OrderId         Guid?   FK → orders         (Restrict on delete) — the order that put this voucher in
                              someone's hands (null = still operator stock, belongs to no order)
```

`OrderId` is part of the wider voucher/order model rather than of company workers, but gifting is
what first repoints it, so it is documented here.

`AssignedToUserId` is always the **purchasing** user (owner or personal buyer).

**A voucher in somebody's hands always belongs to an order.** The database enforces it with
`ck_voucher_held_has_order`: `status NOT IN ('Assigned','Used','Blocked') OR order_id IS NOT NULL`.
Warehouse stock (`Available`, `Imported`, …) keeps `OrderId` null — stock belongs to no order
until it is handed over. `OrderId` is written in the same statement that flips the status, so a
voucher is never in a hand with an unknown origin.

`OrderId` is `Restrict` on purpose: an order that delivered fuel is the only record of where that
fuel came from, so deleting it must never silently strip it. `DELETE /api/admin/orders/{id}`
refuses with an explanatory message while any voucher still points at the order (counted with
query filters ignored, so a soft-deleted voucher still blocks it), and the nightly abandoned-order
cleanup (`OrderCleanupService`) skips such orders — otherwise one of them would abort the whole
batch on the FK and be re-selected forever.

**Status semantics:**

| AssignedToUserId | LegalEntityId | WorkerUserId | Status | OrderId | Meaning |
|---|---|---|---|---|---|
| user | null | null | Assigned | the purchase | Personal voucher |
| owner | company | null | Assigned | the company's purchase | Company voucher, in the pool |
| owner | company | worker | Assigned | the issuance order | Company voucher, gifted to a worker |
| owner | company | worker | Blocked | the issuance order | Frozen by the owner; still the worker's |
| owner | company | null | Blocked | the purchase | Frozen after the worker was fired |
| any | any | any | Used | the order that delivered it | Redeemed |

New entities:

```
CompanyInvitation { Id, LegalEntityId, OwnerUserId, WorkerPhoneNumber, WorkerUserId,
                    Status (Pending|Accepted|Declined|Cancelled), CreatedAtUtc, UpdatedAtUtc }

CompanyMember     { Id, LegalEntityId, WorkerUserId (unique — one company per worker), JoinedAtUtc }
```

New `VoucherStatus` value: **`Blocked`** — frozen after a worker is fired; awaiting
station-side cancellation + admin replacement.

### Order kinds and the issuance order

`Order` gains two columns:

```
Kind            OrderKind  Purchase (default) | Renewal | ReceivedFromCompany
SourceOrderId   Guid?      FK → orders (SetNull on delete) — for an issuance, the purchase its fuel came from
```

- **`Purchase`** — fuel bought for money (the default; every pre-existing order is one).
- **`Renewal`** — a customer's own voucher extended or replaced, paid for by that customer. Both
  the self-serve renewal checkout and the operator-initiated renewal create one, priced at the
  surcharge actually collected, so a renewal is revenue like any other customer payment.
- **`ReceivedFromCompany`** — fuel a company handed to one of its workers. **Not a sale**: it
  moves fuel the company already bought from its pool into someone's hands. `Price` is `0`,
  `Status` is `Fulfilled`, `UserId` is the **worker** (so the order is the worker's receipt),
  `LegalEntityId` is the company, and `SourceOrderId` names the purchase the fuel came from —
  set only when the whole batch came from one purchase, since an order can name a single parent.

An issuance carries one `OrderLineItem` per voucher (`Quantity = 1`) at the **company's own unit
price**, for information only, plus one `Fulfillment` per voucher. Every money view excludes
`ReceivedFromCompany`: report/revenue, the admin dashboard, reconciliation revenue, customer
savings, import-batch P&L revenue and refundable amount. The batch P&L is deliberately asymmetric:
a handover still counts the voucher as "sold" (the fuel really left the operator, so its later
expiry is not an operator loss) but books no revenue, no COGS and no margin.

**Returning to the purchase.** Fuel that goes back to the company belongs to the purchase again,
not to the handover — otherwise the company's own history would keep claiming fuel it no longer
owns. Every path that moves a voucher changes its order in exactly one of these ways:

| Action | `OrderId` after | Why |
|---|---|---|
| Gift (`POST /api/company/vouchers/gift`) | the new issuance order | the handover is what put the fuel in this worker's hands |
| Recall (`POST /api/company/vouchers/recall/{id}`) | the purchase | fuel returns to the pool |
| Fire a worker (`DELETE /api/company/members/{id}`) — voucher still `Assigned` | the purchase | same as recall |
| Fire a worker — voucher already `Blocked` | unchanged (the issuance) | a frozen voucher stays the worker's, so the worker sees why it is unusable |
| Owner freeze / unfreeze (`POST /api/company/vouchers/{block,unblock}/{id}`) | unchanged | the worker keeps the voucher, so it keeps the handover |
| Admin unblock (`POST /api/admin/vouchers/{id}/unblock`) | the purchase | the admin thaw clears `WorkerUserId`, so the voucher is company stock again |
| Renewal **extend** (`FulfillmentService.ProcessRenewalOrderAsync`) | unchanged (the purchase) | the same voucher is extended in place, so it keeps the order it was bought under |
| Renewal **replace** | the renewal order | the delivered voucher is a different stock voucher; its `OrderId` is the renewal order. The customer wallet still files it under the ORIGINAL purchase for display (one asset), resolving the root via the `voucher_renewal_items` chain - a display concern, not a column change |
| Expiry trimmer returns it to stock | cleared (`null`) | the fulfillment that tied it to an order is gone, so it belongs to no order |

"the purchase" resolves to the issuance's `SourceOrderId`, falling back to the voucher's earliest
fulfillment (the delivery it originally arrived under) and, failing both, leaving the issuance in
place — a null would break `ck_voucher_held_has_order`. A voucher that was not under an issuance
keeps its own order untouched, so these paths are safe to run over any set of vouchers.

The expiry trimmer in `FulfillmentService` skips issuance orders entirely, as it already skipped
renewals.

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

Gifting is one transaction and it creates **one issuance order per gift action** (`Kind =
ReceivedFromCompany`) that becomes the owner of every voucher in that call: one `OrderLineItem`
per voucher at the company's own unit price (information only — the order's `Price` is `0`), one
`Fulfillment` per voucher, and each gifted voucher's `OrderId` repointed from the purchase to the
issuance. The vouchers may have been bought across several orders, so `SourceOrderId` is set only
when the whole batch came from one purchase. The handler's result also carries the new order's id
(`IssuanceOrderId`); the endpoint does not put it on the wire yet.

Firing a worker (`DELETE /members/{id}`) removes membership and, in one transaction, sets
every voucher gifted to that worker to `Blocked` and clears its `WorkerUserId`. Vouchers that were
still `Assigned` go back under the purchase they arrived with; ones the owner had already frozen
keep the worker and the issuance order, so the worker can see why the fuel is unusable.

### Worker

| Method | Route | Success | Failures |
|---|---|---|---|
| `GET` | `/my-invitations` | `200 [ { …, legalEntityName, ownerPhoneNumber… } ]` | — |
| `GET` | `/my-memberships` | `200 [ { memberId, legalEntityId, name, edrpou, ownerUserId, isOwner, joinedAtUtc } ]` | — |
| `POST` | `/invitations/{id}/accept` | `200 { success: true, memberId }` | `404` not found; `409` not pending / already a member |
| `POST` | `/invitations/{id}/decline` | `200 { success: true }` | `404` not found; `409` not pending |

`GET /my-memberships` lists the companies the caller **works for** — the worker-side counterpart
of `GET /api/legal-entity/mine`, which lists companies the caller **owns**. Without it a member has
no context for the fuel issued to them, because the wallet scopes by context and
`/api/legal-entity/mine` never contains a company the caller does not own. Membership rows exist only
while the membership lives (firing deletes the row), so no status filter is needed; `isOwner` is true
when the caller also owns the entity, so a client can show one row with owner rights instead of a
duplicate. A pending invitation alone yields no membership and therefore no context.

**Owner endpoints and a worker.** Being a member is not ownership. The owner list endpoints
(`GET /members`, `GET /invitations`) resolve to "no company" for an entity the caller does not own and
answer `200` with an empty body; every owner write (`gift`, `recall`, `block`, `unblock`, `fire`,
`invite`, `cancel`) is rejected with `404`. Nothing about the company leaks to a member.

---

## Effects on existing endpoints

### Checkout — `POST /api/purchases` (and `/bulk`)

Optional `legalEntityId: Guid?`. If present, the purchase is company-owned (the created
vouchers carry that `LegalEntityId`); if omitted, it is personal. A `legalEntityId` that does
not belong to the caller is rejected with `400`. The order is `Kind = Purchase`, and each voucher
it delivers records it as its `OrderId` — that is the order a gifted voucher returns to when it
comes back to the company.

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

A worker's own issued vouchers carry the **company's** `legalEntityId`, so a client that scopes the
wallet by `legalEntityId` alone hides them — the personal filter keeps only `legalEntityId == null`,
and the company filter requires owning the entity. A worker context must therefore narrow to
`LegalEntityId == <membership> && WorkerUserId == me`; see the context model in the planning repo
(`docs/MULTI_COMPANY.md`, epic #103 S5).

The list carries no order fields — a client's view of "which receipt is this voucher from?" comes
from the order list, not from here.

`GET /api/purchases/my` also carries `legalEntityId` on each order (and nested voucher):
`null` = personal purchase, set = company purchase. It returns every order whose `UserId` is the
caller, so the **worker's issuance order shows up there** as a `Fulfilled` order with `price = 0`,
`legalEntityId` = the company and the gifted vouchers nested — that order is the worker's receipt
for the fuel, and `isRenewal` is `false` for it (it is not a renewal).

### `PATCH /api/vouchers/{id}/mark-used`

If `WorkerUserId` is set, only that worker may mark the voucher used; others get `403`.
Worker-facing `GET /api/vouchers/my` currently filters to `Assigned`/`Used`, so workers
generally do not receive `Blocked` vouchers in that list.

---

## Admin support

- `GET /api/admin/vouchers?workerUserId=…` — filter by worker.
- `POST /api/admin/vouchers/{id}/unblock` — manually unblock a `Blocked` voucher (e.g. after
  the station confirms it was not redeemed). `GET /api/admin/vouchers?status=Blocked` lists them.
  Unlike the owner's own freeze/thaw (`POST /api/company/vouchers/unblock/{id}`, which keeps the
  worker and the handover), the admin thaw clears `WorkerUserId` and returns the voucher to the
  company pool, so it also goes back under its purchase.

An order that still owns vouchers cannot be deleted: `DELETE /api/admin/orders/{id}` refuses with
the number of vouchers recorded against it, and the nightly abandoned-order cleanup skips it.

---

## QR access for gifted vouchers

`GET /api/voucher-catalog/{id}/qr` authorizes the **holder**: `WorkerUserId` when the voucher
is gifted, otherwise `AssignedToUserId` (the owner). Admins can fetch any QR. The check agrees
with `MarkVoucherAsUsedCommandHandler`, so whoever can redeem a gifted voucher can also pull
its QR — and nobody else can.
