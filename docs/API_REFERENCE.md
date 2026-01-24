# Admin Panel API Reference - Clean Architecture

## Base URL
```
Development: http://localhost:4000
Production: https://your-domain.com
```

## Authentication
Most admin endpoints require authentication. Include session cookie or auth token.

---

## 🏪 Stations API

### Public Endpoints (Mobile App)
```http
GET /api/stations
```
Returns all gas stations for map display.

### Admin Endpoints
```http
GET    /api/admin/stations           # List all stations
GET    /api/admin/stations/:id       # Get station by ID
POST   /api/admin/stations           # Create new station
PUT    /api/admin/stations/:id       # Update station
DELETE /api/admin/stations/:id       # Delete station
```

**Request Body (POST/PUT):**
```json
{
  "id": "okko",
  "name": "OKKO",
  "color": "#00ff80",
  "logoText": "OKKO",
  "lat": 49.8397,
  "lng": 24.0297
}
```

---

## 📦 Packages API

### Public Endpoints (Mobile App)
```http
GET /api/packages
```
Returns all fuel packages for purchase.

### Admin Endpoints
```http
GET    /api/admin/packages              # List all packages
GET    /api/admin/packages/:id          # Get package by ID
POST   /api/admin/packages              # Create new package
PUT    /api/admin/packages/:id          # Update package
DELETE /api/admin/packages/:id          # Delete package
GET    /api/admin/packages/suggestions  # Get AI package suggestions
```

**Request Body (POST/PUT):**
```json
{
  "station": "OKKO",
  "fuelType": "A-95",
  "liters": 50,
  "price": 2500,
  "discount": 5
}
```

---

## ⛽ Fuel Types API

### Admin Endpoints
```http
GET    /api/admin/fuel-types        # List all fuel types
GET    /api/admin/fuel-types/:id    # Get fuel type by ID
POST   /api/admin/fuel-types        # Create new fuel type
PUT    /api/admin/fuel-types/:id    # Update fuel type
DELETE /api/admin/fuel-types/:id    # Delete fuel type
```

**Request Body (POST/PUT):**
```json
{
  "id": "a95",
  "name": "A-95",
  "octaneRating": 95
}
```

---

## 🎫 QR Codes API

### Admin Endpoints
```http
GET    /api/admin/qr-codes        # List all QR codes
GET    /api/admin/qr-codes/:id    # Get QR code by ID
POST   /api/admin/qr-codes        # Create new QR code
PUT    /api/admin/qr-codes/:id    # Update QR code
DELETE /api/admin/qr-codes/:id    # Delete QR code
```

---

## 🎟️ Vouchers API

### Admin Endpoints
```http
GET    /api/admin/vouchers                    # List vouchers (with filters)
GET    /api/admin/vouchers/:id                # Get voucher by ID
PUT    /api/admin/vouchers/:id                # Update voucher
PATCH  /api/admin/vouchers/:id                # Partial update
DELETE /api/admin/vouchers/:id                # Delete voucher
DELETE /api/admin/vouchers                    # Delete all vouchers
PATCH  /api/admin/vouchers/:id/mark-used      # Mark as used
PATCH  /api/admin/vouchers/:id/restore        # Restore to available
POST   /api/admin/vouchers/bulk-action        # Bulk operations
```

**Query Parameters (GET /api/admin/vouchers):**
```
?status=available          # Filter by status
?provider=OKKO            # Filter by provider
?fuelType=A-95            # Filter by fuel type
?page=1                   # Page number
?limit=50                 # Items per page
?sortBy=createdAt         # Sort field
?sortDirection=desc       # Sort direction (asc/desc)
```

**Bulk Action Request:**
```json
{
  "action": "activate|expire|assign|delete|delete_all",
  "ids": ["voucher-id-1", "voucher-id-2"],
  "targetUserId": "user-123"  // Required for 'assign' action
}
```

---

## 📤 Import API

### Admin Endpoints
```http
POST /api/vouchers/import              # Upload PDF/images for import
GET  /api/vouchers/import-status/:id   # Check import job status
```

**Request (multipart/form-data):**
```
files: [File, File, ...]  // PDF or image files
```

**Response:**
```json
{
  "jobId": "job-uuid",
  "message": "Import Job Queued Successfully",
  "fileCount": 3
}
```

---

## 💳 Payments API

### Endpoints
```http
POST /api/payments/create-checkout-session   # Create Stripe checkout
POST /api/payments/create-payment-intent     # Create payment intent
GET  /api/payments/session/:sessionId        # Get session details
GET  /api/payments/config                    # Get Stripe public key
POST /api/payments/simulate-success-dev      # DEV ONLY: Simulate payment
```

**Create Checkout Session:**
```json
{
  "packageId": "okko-a95-50l",
  "packageName": "OKKO A-95 50L",
  "amount": 2500,
  "quantity": 1
}
```

**Create Payment Intent:**
```json
{
  "amount": 5000,
  "metadata": {
    "items": "OKKO - A95 50L x1, WOG - Diesel 30L x2"
  }
}
```

---

## 🔔 Webhooks API

### Stripe Webhook
```http
POST /api/webhooks/stripe
```

**Headers Required:**
```
stripe-signature: <signature>
```

**Handled Events:**
- `checkout.session.completed`
- `payment_intent.succeeded`
- `payment_intent.payment_failed`

---

## 👥 Users API

### Admin Endpoints
```http
GET    /api/admin/users           # List all users
GET    /api/admin/users/:id       # Get user by ID
PUT    /api/admin/users/:id       # Update user
DELETE /api/admin/users/:id       # Delete user
```

---

## 📊 Inventory API

```http
GET /api/inventory
```

Returns aggregated inventory counts by provider, fuel type, and liters.

**Response:**
```json
[
  {
    "provider": "OKKO",
    "fuelType": "A-95",
    "liters": 50,
    "availableCount": 120
  }
]
```

---

## 🔐 Authentication

### Endpoints
```http
POST /api/auth/request-otp      # Request OTP code
POST /api/auth/verify-otp       # Verify OTP and login
POST /api/auth/logout           # Logout
GET  /api/auth/me               # Get current user
```

---

## ⚡ Rate Limiting

All endpoints are rate-limited:
- **OTP Requests:** 3 per minute per IP
- **OTP Verification:** 5 per 5 minutes per phone
- **General API:** 100 per minute per IP

**Headers:**
```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 95
X-RateLimit-Reset: 1706097600
```

---

## 🚨 Error Responses

### Standard Error Format
```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human readable message",
    "details": {}  // Optional additional info
  }
}
```

### Common Error Codes
- `VALIDATION_ERROR` (400) - Invalid request data
- `UNAUTHORIZED` (401) - Not authenticated
- `FORBIDDEN` (403) - Not authorized
- `NOT_FOUND` (404) - Resource not found
- `RATE_LIMIT_EXCEEDED` (429) - Too many requests
- `INTERNAL_ERROR` (500) - Server error

---

## 📝 Notes

1. **Pagination:** Most list endpoints support `page` and `limit` query parameters
2. **Filtering:** Use query parameters for filtering (e.g., `?status=available`)
3. **Sorting:** Use `sortBy` and `sortDirection` query parameters
4. **Timestamps:** All timestamps are in ISO 8601 format (UTC)
5. **IDs:** Most resources use UUID v4 for IDs

---

## 🔄 Migration Status

✅ **Fully Migrated (Clean Architecture):**
- Stations API
- Packages API
- Fuel Types API
- QR Codes API
- Vouchers API (Admin)
- Import API
- Payments API
- Webhooks API

⏳ **Partially Migrated:**
- Vouchers API (some legacy endpoints remain)

🔜 **To Be Migrated:**
- Sync API
- Test Webhook API
