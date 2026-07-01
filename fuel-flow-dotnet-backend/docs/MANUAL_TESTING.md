# FuelFlow API — Manual Testing Guide

## Prerequisites

- API running locally: `http://localhost:5202`
- Postman installed (import `docs/FuelFlow.postman_collection.json`)
- PostgreSQL running (see `appsettings.Development.json`)
- Environment: **Development** (SMS code is always `000000`)

---

## Flows

There are two separate flows depending on the role:

| Flow | Role | Description |
|------|------|-------------|
| **Admin Flow** | `Admin` | Set up catalog, import vouchers, manage inventory |
| **User Flow** | `User` | Browse stations, purchase vouchers, redeem |

---

## Admin Flow

### Step 1 — Login as Admin

**POST** `http://localhost:5202/api/auth/send-code`

```json
{
  "phoneNumber": "+10000000001"
}
```

> In Development, no real SMS is sent. Code is always `000000`.

---

### Step 2 — Verify Code (Admin)

**POST** `http://localhost:5202/api/auth/verify`

```json
{
  "phoneNumber": "+10000000001",
  "code": "000000"
}
```

**Response:**
```json
{
  "accessToken": "<jwt>",
  "refreshToken": "<token>",
  "expiresIn": 3600
}
```

> ⚠️ For the Admin role to be assigned, the user must already have their `role_id` set to the Admin role in the database. See the database note below.

> Save the `accessToken` — use it as `Bearer <accessToken>` in the `Authorization` header for all protected requests.

---

### Step 3 — Create a Station (Admin)

**POST** `http://localhost:5202/api/admin/stations`
`Authorization: Bearer <accessToken>`

```json
{
  "id": "okko",
  "name": "OKKO",
  "logoText": "OKKO",
  "color": "#e63946",
  "address": "Kyiv, Ukraine",
  "stationType": "fuel"
}
```

---

### Step 4 — Create a Fuel Type (Admin)

**POST** `http://localhost:5202/api/admin/fuel-types`
`Authorization: Bearer <accessToken>`

```json
{
  "id": "okko-a95",
  "stationId": "okko",
  "name": "A95",
  "basePrice": 6200,
  "discountPrice": 5800
}
```

> Prices are in **kopecks/cents** (integer). `6200` = 62.00 UAH.

---

### Step 5 — Create a Fuel Package (Admin)

**POST** `http://localhost:5202/api/admin/packages`
`Authorization: Bearer <accessToken>`

```json
{
  "id": "okko-a95-20l",
  "stationId": "okko",
  "fuelTypeId": "okko-a95",
  "fuelName": "A95",
  "liters": 20,
  "price": 11600,
  "originalPrice": 12400
}
```

---

### Step 6 — Import Vouchers (Admin)

**POST** `http://localhost:5202/api/vouchers/import`
`Authorization: Bearer <accessToken>`
`Content-Type: multipart/form-data`

- Field: `file` → select a `.pdf` voucher file

**Response:**
```json
{
  "importJobId": "<guid>",
  "imported": 5,
  "skipped": 0,
  "errors": []
}
```

---

### Step 7 — Check Inventory (Admin)

**GET** `http://localhost:5202/api/vouchers/inventory`
`Authorization: Bearer <accessToken>`

**Response:**
```json
{
  "inventory": [
	{
	  "provider": "OKKO",
	  "fuelType": "A95",
	  "liters": 20,
	  "available": 5,
	  "assigned": 0,
	  "used": 0,
	  "expired": 0,
	  "total": 5
	}
  ]
}
```

---

### Step 8 — Refresh Token

**POST** `http://localhost:5202/api/auth/refresh`

```json
{
  "refreshToken": "<refreshToken from Step 2>"
}
```

> Use this when the `accessToken` expires.

---

## User Flow

### Step 1 — Login as User

**POST** `http://localhost:5202/api/auth/send-code`

```json
{
  "phoneNumber": "+10000000002"
}
```

---

### Step 2 — Verify Code (User)

**POST** `http://localhost:5202/api/auth/verify`

```json
{
  "phoneNumber": "+10000000002",
  "code": "000000"
}
```

> Save the `accessToken`.

---

### Step 3 — Browse Stations

**GET** `http://localhost:5202/api/stations`

> No auth required. Returns the full station catalog.

---

### Step 4 — Browse Packages

**GET** `http://localhost:5202/api/packages`

> No auth required. Returns all fuel packages across all stations.

**GET** `http://localhost:5202/api/packages/station/okko`

> Filter packages by station ID.

---

### Step 5 — Create Purchase (Checkout)

**POST** `http://localhost:5202/api/purchases`
`Authorization: Bearer <accessToken>`

```json
{
  "provider": "OKKO",
  "fuelType": "A95",
  "liters": 20,
  "quantity": 1,
  "price": 11600,
  "stationId": "okko",
  "stationName": "OKKO"
}
```

**Response:**
```json
{
  "orderId": "<guid>",
  "status": "PendingPayment",
  "monobankInvoiceId": "<id>",
  "paymentUrl": "https://pay.monobank.ua/..."
}
```

> In Development, Monobank is stubbed. Copy the `orderId` for the next step.

---

### Step 6 — Simulate Payment (Admin or Dev only)

> This simulates a Monobank webhook callback. Requires **Admin** token.

**POST** `http://localhost:5202/api/purchases/simulate`
`Authorization: Bearer <adminAccessToken>`

```json
{
  "orderId": "<orderId from Step 5>",
  "scenario": "success"
}
```

> `scenario` can be `"success"` or `"failure"`.

**Response:**
```json
{
  "orderId": "<guid>",
  "status": "Fulfilled",
  "vouchersAssigned": 1
}
```

---

### Step 7 — Get My Vouchers

**GET** `http://localhost:5202/api/vouchers/my`
`Authorization: Bearer <userAccessToken>`

**Response:**
```json
{
  "vouchers": [
	{
	  "id": "<guid>",
	  "provider": "OKKO",
	  "fuelType": "A95",
	  "liters": 20,
	  "expirationDate": "2025-12-31",
	  "voucherNumber": "99999600000020368126",
	  "qrPayload": "9018$2000$;...",
	  "status": "Assigned",
	  "fuelSubtype": null,
	  "redemptionRules": null,
	  "imageUrl": null,
	  "createdAtUtc": "2025-06-18T10:00:00Z",
	  "updatedAtUtc": "2025-06-18T10:00:00Z"
	}
  ]
}
```

---

### Step 8 — Mark Voucher as Used

**PATCH** `http://localhost:5202/api/vouchers/<voucherId>/mark-used`
`Authorization: Bearer <userAccessToken>`

**Response:**
```json
{
  "success": true,
  "message": "Voucher marked as used"
}
```

---

### Step 9 — Sync (Orders + Voucher Summary)

**GET** `http://localhost:5202/api/sync`
`Authorization: Bearer <userAccessToken>`

**Response:**
```json
{
  "orders": [...],
  "totalOrders": 1,
  "totalVouchers": 1
}
```

---

### Step 10 — Get My Purchases

**GET** `http://localhost:5202/api/purchases/my`
`Authorization: Bearer <userAccessToken>`

> Returns purchase history with attached vouchers per order.

---

## Admin — Voucher Management

### Restore a Voucher (Admin)

If a voucher was incorrectly marked as used, an Admin can restore it.

**PATCH** `http://localhost:5202/api/vouchers/<voucherId>/restore`
`Authorization: Bearer <adminAccessToken>`

---

## Database Note — Assigning Admin Role

The Admin role is seeded with a fixed GUID. To make a user an Admin, run this SQL:

```sql
UPDATE users
SET role_id = 'a1111111-1111-1111-1111-111111111111'
WHERE phone_number = '+10000000001';
```

After updating, the user must re-login (call `send-code` + `verify` again) to get a new token with the role claim.

---

## Token Usage Summary

| Header | Value |
|--------|-------|
| `Authorization` | `Bearer <accessToken>` |

Tokens expire based on `JwtOptions.AccessTokenExpirationMinutes`. Use `/api/auth/refresh` to rotate.
