# Mobile Client Changes for Company Workers

## Goal

Update the mobile app to support the new **company owner / worker** flow in the backend.

This document focuses on:
- new mobile features to add
- existing mobile flows that must change
- request / response shapes the app should use
- role-based UI behavior for owner vs worker
- known backend caveats discovered during implementation

---

## High-level feature summary

A regular user can now also act as a **company owner** if they created a `LegalEntity` profile.

A company owner can:
- buy vouchers for the company by sending `legalEntityId` in checkout
- invite a registered user to become a worker
- see current workers
- gift company vouchers to a worker
- recall gifted vouchers
- fire a worker

A worker can:
- receive invitations
- accept or decline an invitation
- see gifted vouchers inside the same `GET /api/vouchers/my` flow
- mark their gifted vouchers as used

A worker cannot:
- buy on behalf of the company unless they own that company
- use owner-only company management endpoints

---

## Backend changes that affect mobile

## 1. Existing auth flow stays the same

No new auth endpoints were added.

Owner and worker both continue to use the same login flow:
- `POST /api/auth/send-code`
- `POST /api/auth/verify`

The difference is only **which user account** is authenticated.

---

## 2. Legal entity profile becomes important for mobile

The legal entity endpoints already existed, but now they directly affect checkout and company features.

### Get legal entity profile
`GET /api/legal-entity/profile`

Possible responses:
- `200 OK` with profile object
- `404 Not Found` if the user is not a company owner yet

Response shape:

```json
{
  "id": "guid",
  "name": "My Company LLC",
  "edrpou": "12345678",
  "vatNumber": "987654321",
  "address": "Kyiv, Khreshchatyk 1",
  "directorName": "Ivan Petrenko",
  "phone": "+380991234567",
  "email": "company@example.com",
  "createdAt": "2026-08-17T00:00:00Z",
  "updatedAt": "2026-08-17T00:00:00Z"
}
```

### Create / update legal entity profile
`POST /api/legal-entity/profile`

Request body:

```json
{
  "name": "My Company LLC",
  "edrpou": "12345678",
  "vatNumber": "987654321",
  "address": "Kyiv, Khreshchatyk 1",
  "directorName": "Ivan Petrenko",
  "phone": "+380991234567",
  "email": "company@example.com"
}
```

Use the returned `id` as the company identifier for company purchases.

---

## 3. Checkout changed: company purchase vs personal purchase

## Existing endpoint changed
`POST /api/purchases`

### New optional field
- `legalEntityId: guid | null`

### Personal purchase
If `legalEntityId` is omitted, the purchase is personal.

```json
{
  "provider": "okko",
  "fuelTypeId": "okko-a95",
  "liters": 20,
  "quantity": 1,
  "price": 11600,
  "stationId": "okko",
  "stationName": "OKKO"
}
```

### Company purchase
If `legalEntityId` is sent, the purchase becomes company-owned.

```json
{
  "legalEntityId": "guid",
  "provider": "okko",
  "fuelTypeId": "okko-a95",
  "liters": 20,
  "quantity": 1,
  "price": 11600,
  "stationId": "okko",
  "stationName": "OKKO"
}
```

### Mobile changes needed
Add a clear checkout choice:
- **Personal purchase**
- **Buy for company**

Recommended UX:
- show the choice only if `GET /api/legal-entity/profile` returns `200`
- default to personal
- when company is selected, send `legalEntityId`
- when personal is selected, omit `legalEntityId`

### Validation behavior
If the client sends a `legalEntityId` that does not belong to the current user, backend returns a bad request.

---

## 4. New company endpoints

All routes below require authenticated user token.

Base route:
`/api/company`

---

## Owner endpoints

### Send invitation
`POST /api/company/invitations`

Request:

```json
{
  "workerPhoneNumber": "+10000000002"
}
```

Success response:

```json
{
  "invitationId": "guid",
  "status": "Pending"
}
```

Possible failures:
- `400` if owner has no legal entity
- `400` if phone is invalid
- `400` if worker does not exist
- `400` if owner invites self
- `409` if worker already belongs to a company
- `409` if pending invite already exists

### Get sent invitations
`GET /api/company/invitations`

Response: array

```json
[
  {
	"id": "guid",
	"legalEntityId": "guid",
	"ownerUserId": "guid",
	"workerUserId": "guid",
	"workerPhoneNumber": "+10000000002",
	"workerFirstName": "Ivan",
	"workerLastName": "Petrenko",
	"status": "Pending",
	"createdAtUtc": "2026-08-17T00:00:00Z",
	"updatedAtUtc": "2026-08-17T00:00:00Z"
  }
]
```

### Cancel invitation
`DELETE /api/company/invitations/{id}`

Success:

```json
{ "success": true }
```

Possible failures:
- `404` invitation not found
- `409` if invitation is no longer pending

### Get members
`GET /api/company/members`

Response: array

```json
[
  {
	"id": "member-guid",
	"workerUserId": "user-guid",
	"workerPhoneNumber": "+10000000002",
	"workerFirstName": "Ivan",
	"workerLastName": "Petrenko",
	"joinedAtUtc": "2026-08-17T00:00:00Z",
	"giftedVoucherCount": 3
  }
]
```

### Fire worker
`DELETE /api/company/members/{id}`

Success:

```json
{
  "success": true,
  "blockedVoucherCount": 2
}
```

Behavior:
- worker membership is removed
- all currently gifted assigned vouchers for that worker become `Blocked`
- `workerUserId` is cleared on those vouchers

### Gift vouchers to worker
`POST /api/company/vouchers/gift`

Request:

```json
{
  "workerUserId": "guid",
  "voucherIds": ["guid-1", "guid-2"]
}
```

Success:

```json
{
  "success": true,
  "giftedCount": 2
}
```

Possible failures:
- `400` owner has no company
- `400` worker is not a member
- `400` empty voucher list
- `404` voucher not found
- `409` voucher not eligible for gifting

Eligible vouchers are only those that are:
- company-owned
- assigned to owner
- not currently gifted to another worker
- in `Assigned` status

### Recall voucher from worker
`POST /api/company/vouchers/recall/{voucherId}`

Success:

```json
{ "success": true }
```

Possible failures:
- `400` owner has no company
- `404` voucher not found
- `403` voucher does not belong to that company owner
- `409` only gifted assigned vouchers can be recalled

---

## Worker endpoints

### Get my invitations
`GET /api/company/my-invitations`

Response: array

```json
[
  {
	"id": "guid",
	"legalEntityId": "guid",
	"legalEntityName": "My Company LLC",
	"ownerUserId": "guid",
	"ownerPhoneNumber": "+10000000001",
	"ownerFirstName": "Owner",
	"ownerLastName": "User",
	"createdAtUtc": "2026-08-17T00:00:00Z",
	"updatedAtUtc": "2026-08-17T00:00:00Z"
  }
]
```

### Accept invitation
`POST /api/company/invitations/{id}/accept`

Success:

```json
{
  "success": true,
  "memberId": "guid"
}
```

Possible failures:
- `404` invitation not found
- `409` invitation no longer pending
- `409` worker already belongs to a company

### Decline invitation
`POST /api/company/invitations/{id}/decline`

Success:

```json
{ "success": true }
```

Possible failures:
- `404` invitation not found
- `409` invitation no longer pending

---

## 5. My vouchers response changed

## Endpoint
`GET /api/vouchers/my`

## Important response shape
The controller currently returns a **plain array**, not an object wrapper.

Actual response shape:

```json
[
  {
	"id": "guid",
	"provider": "OKKO",
	"fuelType": "okko-95",
	"liters": 20,
	"amount": 20,
	"expirationDate": "2026-12-31",
	"voucherNumber": "OKKO-123",
	"externalId": "OKKO-123",
	"qrPayload": "...",
	"qrCodeData": "...",
	"status": "Assigned",
	"source": "own",
	"legalEntityId": null,
	"workerUserId": null,
	"workerFirstName": null,
	"workerLastName": null,
	"fuelSubtype": null,
	"redemptionRules": null,
	"imageUrl": "data:image/png;base64,...",
	"createdAtUtc": "2026-08-17T00:00:00Z",
	"updatedAtUtc": "2026-08-17T00:00:00Z"
  }
]
```

## New / changed fields
- `source`: string
  - `"own"`
  - `"gifted"`
- `legalEntityId`: `guid | null` — **new field**
- `workerUserId`: `guid | null`
- `workerFirstName`: `string | null`
- `workerLastName`: `string | null`

## Meaning for worker user
If a voucher belongs to a worker through `WorkerUserId == currentUser`, then:
- it appears in `GET /api/vouchers/my`
- `source = "gifted"`

## Meaning for company owner
The owner still sees company vouchers through `AssignedToUserId == owner`.
That includes:
- company pool vouchers
- vouchers currently gifted to workers

Important detail:
- owner may still get `source = "own"`
- to detect that the voucher is gifted to a worker, check `workerUserId != null`

## How to distinguish personal vs company voucher
Use `legalEntityId` as the definitive company indicator:

| `legalEntityId` | `workerUserId` | Meaning |
|---|---|---|
| `null` | `null` | Personal voucher |
| `guid` | `null` | Company pool voucher (not yet gifted) |
| `guid` | `guid` | Company voucher gifted to a worker |

## Mobile changes needed
Update voucher domain model and UI logic.

Recommended classification on the client:
- personal / company pool / gifted-to-worker / gifted-to-me

Suggested rules:
- if `legalEntityId == null` → personal
- else if current user equals `workerUserId` → gifted to me
- else if `workerUserId != null` → gifted to worker
- else → company pool item (owned by the legal entity, not yet assigned to a worker)

---

## 5b. My purchases response changed

## Endpoint
`GET /api/purchases/my`

## New fields

Two new `legalEntityId` fields were added — one on the **purchase/order** level, and one on each **nested voucher**.

### Purchase (order) shape — changed fields

```json
{
  "id": "order-guid",
  "provider": "WOG",
  "fuelType": "wog-95-euro",
  "fuelName": "WOG 95 Euro",
  "liters": 20,
  "quantity": 1,
  "price": 11600,
  "status": "Fulfilled",
  "monobankInvoiceId": "...",
  "monobankPaymentUrl": "...",
  "monobankStatus": "success",
  "legalEntityId": "guid-or-null",
  "createdAtUtc": "2026-08-18T12:00:00Z",
  "fulfilledAtUtc": "2026-08-18T12:05:00Z",
  "lineItems": [...],
  "vouchers": [...]
}
```

- `legalEntityId` on the order: `guid` if this was a company purchase, `null` if personal.

### Nested voucher shape — changed fields

Each voucher inside the `vouchers` array now also includes `legalEntityId`:

```json
{
  "id": "voucher-guid",
  "provider": "WOG",
  "fuelType": "wog-95-euro",
  "fuelName": "WOG 95 Euro",
  "liters": 20,
  "amount": 20,
  "expirationDate": "2026-12-31",
  "voucherNumber": "...",
  "externalId": "...",
  "qrPayload": "...",
  "qrCodeData": "...",
  "status": "Assigned",
  "legalEntityId": "guid-or-null",
  "imageUrl": "data:image/png;base64,..."
}
```

## How to distinguish personal vs company purchase

| `legalEntityId` on order | Meaning |
|---|---|
| `null` | Personal purchase |
| `guid` | Company purchase made on behalf of a legal entity |

## Mobile changes needed
- Update the purchases domain model (`PurchaseDto`) to include `legalEntityId`.
- Update the nested voucher model (`VoucherDto` inside purchases) to include `legalEntityId`.
- In the purchases list UI, show a company badge / indicator when `legalEntityId != null`.
- In purchase detail, show which company this purchase was made for (you can cross-reference with `GET /api/legal-entity/profile`).

---

## 6. Mark voucher as used behavior changed

## Endpoint
`PATCH /api/vouchers/{id}/mark-used`

## New behavior
If `workerUserId` is set on a voucher, only that worker can mark it used.

### Possible outcomes
- `200 OK` success
- `404 Not Found` voucher missing
- `403 Forbidden` when trying to use a gifted voucher that belongs to another worker / owner is blocked
- `400 Bad Request` invalid state

### Mobile changes needed
- if user is a company owner looking at a voucher gifted to a worker, hide or disable the “Use” action
- if API returns `403`, show a specific message like:
  - “Only the assigned worker can use this voucher”

---

## 7. Voucher QR behavior relevant for mobile

QR endpoint still exists at:
`GET /api/voucher-catalog/{id}/qr`

## Important backend caveat
Current backend implementation still authorizes QR download only by `AssignedToUserId`.
That means gifted worker QR access is **not yet aligned** with the intended business rule.

Practical effect right now:
- owner can still fetch QR for company-owned vouchers because they remain `AssignedToUserId = owner`
- worker QR fetch for gifted vouchers is not fully supported by the current backend guard

## Mobile recommendation
Do not rely on QR ownership behavior for gifted vouchers until backend QR authorization is corrected.
For now, treat this as a backend limitation.

---

## 8. Status handling updates

Voucher statuses now include:
- `Imported`
- `Available`
- `Assigned`
- `Used`
- `Expired`
- `Blocked`
- plus existing verification-related statuses if already used in admin flows

## `Blocked` meaning for mobile
A `Blocked` voucher is frozen after a worker was fired.

Mobile recommendations:
- show a distinct blocked badge
- hide “Use” / QR actions for blocked vouchers
- blocked vouchers should not be treated as active/giftable

Note: worker-facing `GET /api/vouchers/my` currently filters to `Assigned` and `Used`, so workers usually will not receive blocked vouchers in that list.

---

## 9. Suggested mobile screens / features

## A. Profile / Company setup
Add company section in profile:
- show company profile if it exists
- allow create/edit legal entity profile
- store `legalEntityId` locally in profile state after fetch/upsert

## B. Checkout
Add purchase mode selector:
- Personal
- Company

When Company is selected:
- send `legalEntityId`
- show label like “This voucher will belong to company”

## C. Company owner screens
Add a new “Company” area for owners:
- company workers list
- sent invitations list
- invite worker by phone
- gift vouchers to worker
- recall voucher from worker
- fire worker

## D. Worker screens
Add worker invitation inbox:
- list incoming invitations
- accept / decline

## E. My vouchers screen
Update to support:
- own vouchers
- gifted-to-me vouchers
- owner-visible gifted-to-worker vouchers
- blocked state visuals if needed later

Suggested UI tags:
- “Own”
- “Gifted to me”
- “Gifted to worker”
- “Company pool”
- “Blocked”

---

## 10. Client models to add / update

## Legal entity model
```ts
interface LegalEntityProfile {
  id: string;
  name: string;
  edrpou: string;
  vatNumber?: string | null;
  address?: string | null;
  directorName?: string | null;
  phone?: string | null;
  email?: string | null;
  createdAt: string;
  updatedAt: string;
}
```

## Checkout payload update
```ts
interface CreatePurchaseRequest {
  legalEntityId?: string;
  provider: string;
  fuelTypeId: string;
  liters: number;
  quantity: number;
  price: number;
  stationId?: string;
  stationName?: string;
}
```

## Owner invitation item
```ts
interface CompanyInvitationDto {
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
```

## Worker invitation item
```ts
interface MyCompanyInvitationDto {
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
```

## Company member item
```ts
interface CompanyMemberDto {
  id: string;
  workerUserId: string;
  workerPhoneNumber: string;
  workerFirstName?: string | null;
  workerLastName?: string | null;
  joinedAtUtc: string;
  giftedVoucherCount: number;
}
```

## Voucher model update
```ts
interface VoucherDto {
  id: string;
  provider: string;
  fuelType: string;
  liters: number;
  amount: number;
  expirationDate: string;
  voucherNumber: string;
  externalId: string;
  qrPayload: string;
  qrCodeData: string;
  status: string;
  source: "own" | "gifted" | string;
  workerUserId?: string | null;
  workerFirstName?: string | null;
  workerLastName?: string | null;
  fuelSubtype?: string | null;
  redemptionRules?: string | null;
  imageUrl?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}
```

---

## 11. Error handling expectations

## Invitation / company management
Handle:
- `400` validation or business rule violation
- `404` missing resource
- `409` state conflict

## Voucher use
Handle:
- `403` forbidden for gifted voucher owned by another actor
- `400` invalid voucher state

## Checkout
Handle:
- `400` invalid `legalEntityId`

Recommended UX:
- show backend error text when present
- for `409`, prefer user-friendly copy like “This invitation is no longer pending”

---

## 12. Recommended implementation order for mobile

1. Update API client types
   - legal entity profile
   - voucher DTO
   - company DTOs
   - checkout request with `legalEntityId`

2. Update checkout flow
   - personal vs company purchase selector

3. Update my vouchers screen
   - parse array response
   - support `source`, `workerUserId`, worker names
   - disable use actions where appropriate

4. Add worker invitation inbox
   - `GET /api/company/my-invitations`
   - accept / decline actions

5. Add owner company section
   - invite worker
   - list invitations
   - list members
   - gift / recall / fire actions

6. Add blocked / gifted states to UI

---

## 13. Out of scope for mobile

These exist but are admin-focused and usually not needed in the mobile app:
- `GET /api/admin/vouchers?workerUserId=...`
- `POST /api/admin/vouchers/{id}/unblock`

---

## 14. Important caveats summary

1. `GET /api/vouchers/my` currently returns a plain array.
2. Owner can see gifted vouchers because `AssignedToUserId` stays owner.
3. Owner must inspect `workerUserId` to know a voucher is gifted.
4. `PATCH /api/vouchers/{id}/mark-used` now enforces worker-only usage for gifted vouchers.
5. QR route access for gifted vouchers is not fully aligned yet in backend.

---

## 15. Minimal mobile checklist

- [ ] Fetch and store `legalEntityId`
- [ ] Add “buy for company” checkout option
- [ ] Send `legalEntityId` when company purchase is selected
- [ ] Add company invitation inbox for worker
- [ ] Add owner company management screens
- [ ] Update voucher list model and rendering
- [ ] Handle `source`, `workerUserId`, `workerFirstName`, `workerLastName`
- [ ] Hide/disable invalid use actions for gifted vouchers
- [ ] Add blocked status visuals
- [ ] Handle array response from `GET /api/vouchers/my`
