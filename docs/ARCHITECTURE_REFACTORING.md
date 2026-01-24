# Fuel-Flow Architecture Refactoring Specification

## Executive Summary

This document outlines a comprehensive architectural refactoring of the Fuel-Flow solution, transforming it from its current monolithic structure into a clean, layered architecture that follows industry best practices while preserving all existing business logic and external behavior.

---

## Table of Contents

1. [Current Architecture Analysis](#1-current-architecture-analysis)
2. [Target Architecture Design](#2-target-architecture-design)
3. [Database Schema Refactoring](#3-database-schema-refactoring)
4. [Backend Refactoring Plan](#4-backend-refactoring-plan)
5. [Cross-Cutting Concerns](#5-cross-cutting-concerns)
6. [Migration Strategy](#6-migration-strategy)
7. [Verification Checklist](#7-verification-checklist)

---

## 1. Current Architecture Analysis

### 1.1 High-Level Topology

```
┌─────────────────────────────────────────────────────────────────┐
│                        Client Applications                       │
├─────────────────────────────────┬───────────────────────────────┤
│      Mobile App (React/Vite)    │   Admin Panel (React/Vite)    │
│        localhost:5001           │       localhost:5002          │
└────────────────┬────────────────┴───────────────┬───────────────┘
                 │                                 │
                 ▼                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                   Admin Backend (Express.js)                     │
│                         localhost:4000                           │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                    routes.ts (900+ lines)                 │   │
│  │  - Authentication, Purchases, Vouchers, Admin CRUD...    │   │
│  └──────────────────────────────────────────────────────────┘   │
└───────────┬─────────────────────────────────┬───────────────────┘
            │                                 │
            ▼                                 ▼
┌───────────────────────┐         ┌───────────────────────────────┐
│   PostgreSQL (5432)   │         │      Redis Streams (6379)     │
│   - 14 tables         │◄────────┤   - order-events              │
│   - Mixed concerns    │         │   - fulfillment-events        │
└───────────────────────┘         └───────────────────────────────┘
```

### 1.2 Identified Architectural Issues

#### **A. Presentation Layer Issues**
| Issue | Location | Impact |
|-------|----------|--------|
| God Router | `routes.ts` (900+ lines) | Unmaintainable, hard to test |
| Mixed concerns | Auth + Business logic in routes | Tight coupling |
| Inconsistent routing | Some routes in `/routes/`, some in `/interfaces/http/` | Confusion |
| Rate limiting embedded | `isRateLimited()` inline | Not reusable |

#### **B. Application Layer Issues**
| Issue | Location | Impact |
|-------|----------|--------|
| No service layer | Business logic in routes/repositories | No orchestration layer |
| Direct DB access from routes | `routes.ts` line 350+ | Violates separation |
| Transaction leakage | `fulfillment.consumer.ts` | Transaction logic in consumer |

#### **C. Domain Layer Issues**
| Issue | Location | Impact |
|-------|----------|--------|
| Anemic entities | All models are just DB types | No domain behavior |
| `getFuelAliases()` in repository | `vouchers.repository.ts` | Domain logic in persistence |
| No value objects | Prices as integers everywhere | Semantic loss |

#### **D. Infrastructure Layer Issues**
| Issue | Location | Impact |
|-------|----------|--------|
| Singleton anti-pattern | `getRedisClient()` | Testing difficulty |
| Missing abstractions | Direct Drizzle usage | ORM lock-in |
| Configuration scattered | `.env` + hardcoded values | Environment inconsistency |

#### **E. Database Schema Issues**
| Issue | Table(s) | Impact |
|-------|----------|--------|
| Redundant data | `purchases.stationName` + `stations.name` | Denormalization without reason |
| Missing FK constraints | `vouchers.assignedToUserId` → `users.id` | Referential integrity |
| Integer for boolean | `outbox.processed`, `phoneVerifications.verified` | Semantic confusion |
| Legacy table | `qrCodes` vs `vouchers` | Parallel inventory systems |
| No audit trail | All tables | Compliance risk |

---

## 2. Target Architecture Design

### 2.1 Architectural Style: Clean Architecture + Domain-Driven Design (Lite)

```
┌───────────────────────────────────────────────────────────────────────────┐
│                              PRESENTATION LAYER                            │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  Controllers/Routers         │  Middleware      │  DTOs/ViewModels  │  │
│  │  - auth.controller.ts        │  - auth.mw.ts    │  - purchase.dto   │  │
│  │  - purchase.controller.ts    │  - rate-limit    │  - voucher.dto    │  │
│  │  - voucher.controller.ts     │  - validation    │  - user.dto       │  │
│  │  - admin.controller.ts       │  - error-handler │                   │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │ Interface (DTOs)
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                              APPLICATION LAYER                             │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  Use Cases / Services                                                │  │
│  │  ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────────┐ │  │
│  │  │ PurchaseService  │ │ FulfillmentSvc   │ │ VoucherImportService │ │  │
│  │  │ - createPurchase │ │ - fulfillOrder   │ │ - processImport      │ │  │
│  │  │ - completePay    │ │ - backfillOrders │ │ - extractVouchers    │ │  │
│  │  └──────────────────┘ └──────────────────┘ └──────────────────────┘ │  │
│  │  ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────────┐ │  │
│  │  │ AuthService      │ │ UserService      │ │ NotificationService  │ │  │
│  │  │ - verifyPhone    │ │ - createUser     │ │ - sendPush           │ │  │
│  │  │ - login/logout   │ │ - updateProfile  │ │ - sendSMS            │ │  │
│  │  └──────────────────┘ └──────────────────┘ └──────────────────────┘ │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │ Interface (Domain Entities)
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                                DOMAIN LAYER                                │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  Entities                     │  Value Objects    │  Domain Services │  │
│  │  - User                       │  - Money          │  - FuelMatcher   │  │
│  │  - Order                      │  - PhoneNumber    │  - PriceCalc     │  │
│  │  - Voucher                    │  - FuelType       │                  │  │
│  │  - Fulfillment                │  - ExternalId     │                  │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  Repository Interfaces (Ports)                                       │  │
│  │  - IUserRepository            │  - IOrderRepository                  │  │
│  │  - IVoucherRepository         │  - IOutboxRepository                 │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────┬─────────────────────────────────────┘
                                      │ Interface (Repository Ports)
                                      ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                            INFRASTRUCTURE LAYER                            │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  Persistence (Adapters)       │  External Services │  Messaging     │  │
│  │  - DrizzleUserRepository      │  - StripePayment   │  - RedisStream │  │
│  │  - DrizzleOrderRepository     │  - TwilioSMS       │  - OutboxPoller│  │
│  │  - DrizzleVoucherRepository   │  - GeminiOCR       │                │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  Configuration                │  Logging           │  Security      │  │
│  │  - config.ts (centralized)    │  - logger.ts       │  - session     │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Proposed Directory Structure

```
admin-panel/backend/src/
├── index.ts                         # Application entry point
├── config/
│   ├── index.ts                     # Centralized configuration
│   ├── database.config.ts
│   ├── redis.config.ts
│   └── external-services.config.ts
│
├── presentation/                    # API Layer
│   ├── http/
│   │   ├── controllers/
│   │   │   ├── auth.controller.ts
│   │   │   ├── purchase.controller.ts
│   │   │   ├── voucher.controller.ts
│   │   │   ├── order.controller.ts
│   │   │   ├── admin/
│   │   │   │   ├── stations.controller.ts
│   │   │   │   ├── packages.controller.ts
│   │   │   │   ├── users.controller.ts
│   │   │   │   └── import.controller.ts
│   │   │   └── webhooks.controller.ts
│   │   ├── middleware/
│   │   │   ├── auth.middleware.ts
│   │   │   ├── rate-limit.middleware.ts
│   │   │   ├── validation.middleware.ts
│   │   │   └── error-handler.middleware.ts
│   │   ├── dto/
│   │   │   ├── purchase.dto.ts
│   │   │   ├── voucher.dto.ts
│   │   │   └── auth.dto.ts
│   │   └── router.ts                # Route aggregator
│   └── consumers/
│       └── fulfillment.consumer.ts
│
├── application/                     # Use Cases / Services
│   ├── services/
│   │   ├── auth.service.ts
│   │   ├── purchase.service.ts
│   │   ├── fulfillment.service.ts
│   │   ├── voucher-import.service.ts
│   │   ├── notification.service.ts
│   │   └── user.service.ts
│   ├── use-cases/                   # Optional: explicit use case classes
│   └── events/
│       ├── event-publisher.ts
│       └── event-types.ts
│
├── domain/                          # Core Business Logic
│   ├── entities/
│   │   ├── user.entity.ts
│   │   ├── order.entity.ts
│   │   ├── voucher.entity.ts
│   │   └── fulfillment.entity.ts
│   ├── value-objects/
│   │   ├── money.ts
│   │   ├── phone-number.ts
│   │   ├── fuel-type.ts
│   │   └── external-id.ts
│   ├── services/
│   │   └── fuel-matcher.service.ts  # getFuelAliases() logic
│   ├── repositories/                # Interfaces (Ports)
│   │   ├── user.repository.ts
│   │   ├── order.repository.ts
│   │   ├── voucher.repository.ts
│   │   └── outbox.repository.ts
│   └── events/
│       └── domain-events.ts
│
├── infrastructure/                  # External Adapters
│   ├── persistence/
│   │   ├── drizzle/
│   │   │   ├── schema.ts
│   │   │   ├── db.ts
│   │   │   ├── mappers/             # Entity <-> DB Type mappers
│   │   │   │   ├── user.mapper.ts
│   │   │   │   └── order.mapper.ts
│   │   │   └── repositories/
│   │   │       ├── drizzle-user.repository.ts
│   │   │       ├── drizzle-order.repository.ts
│   │   │       └── drizzle-voucher.repository.ts
│   │   └── migrations/
│   ├── messaging/
│   │   ├── redis/
│   │   │   ├── redis-client.ts
│   │   │   ├── redis-event-publisher.ts
│   │   │   └── redis-stream-reader.ts
│   │   └── outbox/
│   │       └── outbox-poller.ts
│   ├── external/
│   │   ├── stripe/
│   │   │   ├── stripe-client.ts
│   │   │   └── stripe-payment.adapter.ts
│   │   ├── twilio/
│   │   │   └── twilio-sms.adapter.ts
│   │   └── gemini/
│   │       └── gemini-ocr.adapter.ts
│   └── logging/
│       └── logger.ts
│
└── shared/                          # Shared Utilities
    ├── errors/
    │   ├── app-error.ts
    │   ├── validation-error.ts
    │   └── not-found-error.ts
    ├── types/
    │   └── common.types.ts
    └── utils/
        └── id-generator.ts
```

---

## 3. Database Schema Refactoring

### 3.1 Current Schema Issues & Fixes

#### **Table: users**
| Current Issue | Fix |
|---------------|-----|
| `id` is VARCHAR but should be UUID | Keep as VARCHAR for compatibility, add proper UUID generation |
| No `created_by` or `updated_by` audit | Add audit columns |
| Vehicle data in user table | Keep for now (bounded context: User Profile) |

#### **Table: purchases** → Deprecate in favor of `orders`
| Current Issue | Fix |
|---------------|-----|
| Parallel to `orders` table | Soft-deprecate, keep for legacy reads |
| `stationName` duplicates `stations.name` | Add proper FK, remove denormalized field in future |
| `qrCodeId` legacy field | Mark as deprecated |

#### **Table: vouchers**
| Current Issue | Fix |
|---------------|-----|
| `assignedToUserId` has no FK | Add FK to `users.id` |
| `status` is TEXT | Create enum type or check constraint |
| `purchaseId` FK to purchases | Consider changing to `orderId` |

#### **Table: orders**
| Current Issue | Fix |
|---------------|-----|
| Clean already | Add `currency` column for future |
| No FK to `users` | Add FK constraint |

#### **Table: outbox**
| Current Issue | Fix |
|---------------|-----|
| `processed` is INTEGER (0/1) | Change to BOOLEAN |
| No retry tracking | Add `attempts`, `last_error` columns |

### 3.2 Refactored Schema (Drizzle)

```typescript
// New schema.ts - Key Changes Highlighted

// ============ ENUMS ============
export const voucherStatusEnum = pgEnum('voucher_status', [
  'imported', 'available', 'reserved', 'sold', 'used', 'expired'
]);

export const orderStatusEnum = pgEnum('order_status', [
  'PENDING_FULFILLMENT', 'PARTIALLY_FULFILLED', 'FULFILLED', 'REFUNDED', 'CANCELLED'
]);

// ============ AUDIT MIXIN ============
const auditColumns = {
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
  createdBy: varchar("created_by"),
  updatedBy: varchar("updated_by"),
};

// ============ USERS ============
export const users = pgTable("users", {
  id: uuid("id").primaryKey().defaultRandom(),
  email: varchar("email", { length: 255 }).unique(),
  phone: varchar("phone", { length: 20 }).unique(),
  phoneVerified: boolean("phone_verified").default(false),
  // ... existing fields
  ...auditColumns,
});

// ============ VOUCHERS (Refactored) ============
export const vouchers = pgTable("vouchers", {
  id: uuid("id").primaryKey().defaultRandom(),
  provider: varchar("provider", { length: 50 }).notNull(),
  externalId: varchar("external_id", { length: 100 }),
  fuelTypeId: varchar("fuel_type_id").references(() => fuelTypes.id), // NEW FK
  amount: integer("amount").notNull(),
  unit: varchar("unit", { length: 20 }).notNull().default("liters"),
  currencyCode: varchar("currency_code", { length: 3 }).default("UAH"), // NEW
  expiresAt: timestamp("expires_at"),
  status: voucherStatusEnum("status").notNull().default("imported"),
  qrCodeData: text("qr_code_data"),
  assignedToUserId: uuid("assigned_to_user_id").references(() => users.id), // FK Added
  assignedAt: timestamp("assigned_at"),
  importJobId: uuid("import_job_id").references(() => importJobs.id),
  ...auditColumns,
}, (t) => ({
  idxProviderExternal: uniqueIndex("idx_voucher_provider_external")
    .on(t.provider, t.externalId)
    .where(sql`${t.externalId} IS NOT NULL`),
  idxStatus: index("idx_voucher_status").on(t.status),
  idxAssignedUser: index("idx_voucher_assigned_user").on(t.assignedToUserId),
}));

// ============ ORDERS (Enhanced) ============
export const orders = pgTable("orders", {
  id: uuid("id").primaryKey().defaultRandom(),
  userId: uuid("user_id").notNull().references(() => users.id), // FK Added
  productType: varchar("product_type", { length: 100 }).notNull(),
  providerId: varchar("provider_id").references(() => stations.id), // NEW FK
  fuelTypeId: varchar("fuel_type_id").references(() => fuelTypes.id), // NEW FK
  liters: integer("liters").notNull(),
  quantity: integer("quantity").notNull().default(1),
  unitPriceMinor: integer("unit_price_minor").notNull(), // Price in smallest unit
  totalPriceMinor: integer("total_price_minor").notNull(),
  currencyCode: varchar("currency_code", { length: 3 }).notNull().default("UAH"),
  status: orderStatusEnum("status").notNull().default("PENDING_FULFILLMENT"),
  paymentMethod: varchar("payment_method", { length: 50 }),
  stripePaymentId: varchar("stripe_payment_id", { length: 255 }),
  idempotencyKey: varchar("idempotency_key", { length: 255 }).unique(),
  fulfilledAt: timestamp("fulfilled_at"),
  ...auditColumns,
}, (t) => ({
  idxUserId: index("idx_order_user").on(t.userId),
  idxStatus: index("idx_order_status").on(t.status),
  idxCreatedAt: index("idx_order_created").on(t.createdAt),
}));

// ============ OUTBOX (Enhanced) ============
export const outbox = pgTable("outbox", {
  id: serial("id").primaryKey(),
  aggregateType: varchar("aggregate_type", { length: 50 }).notNull(), // NEW
  aggregateId: varchar("aggregate_id", { length: 100 }).notNull(), // NEW
  eventType: varchar("event_type", { length: 100 }).notNull(),
  payload: jsonb("payload").notNull(),
  processed: boolean("processed").notNull().default(false), // Changed to boolean
  processedAt: timestamp("processed_at"),
  attempts: integer("attempts").notNull().default(0), // NEW
  lastError: text("last_error"), // NEW
  scheduledFor: timestamp("scheduled_for").defaultNow(), // NEW: for delayed events
  ...auditColumns,
}, (t) => ({
  idxUnprocessed: index("idx_outbox_unprocessed")
    .on(t.processed, t.scheduledFor),
}));

// ============ NEW: FUEL_TYPE_ALIASES ============
// Moves fuel matching logic from code to database
export const fuelTypeAliases = pgTable("fuel_type_aliases", {
  id: serial("id").primaryKey(),
  fuelTypeId: varchar("fuel_type_id").notNull().references(() => fuelTypes.id),
  alias: varchar("alias", { length: 100 }).notNull(),
  locale: varchar("locale", { length: 5 }).default("uk"),
}, (t) => ({
  idxAlias: index("idx_fuel_alias").on(t.alias),
}));
```

### 3.3 Migration Path

```
Phase 1: Add new columns/tables (non-breaking)
  - Add FK columns (nullable)
  - Add new enum types
  - Add fuel_type_aliases table
  - Add audit columns

Phase 2: Data migration
  - Populate fuel_type_aliases from getFuelAliases()
  - Backfill FK references
  - Convert integer booleans to actual booleans

Phase 3: Add constraints (after all data is valid)
  - Enable FK constraints
  - Add NOT NULL where applicable
  - Add check constraints

Phase 4: Cleanup (optional, breaking)
  - Remove deprecated columns
  - Soft-delete purchases table
```

---

## 4. Backend Refactoring Plan

### 4.1 Controller Extraction

**Current:** `routes.ts` (900+ lines with 50+ endpoints)

**Target:** 7 focused controllers

| Controller | Endpoints | Lines (est.) |
|------------|-----------|--------------|
| `auth.controller.ts` | `/auth/*` | ~150 |
| `purchase.controller.ts` | `/purchases/*`, `/payments/*`, `/checkout` | ~200 |
| `voucher.controller.ts` | `/vouchers/*` | ~100 |
| `order.controller.ts` | `/sync/*` | ~80 |
| `admin/station.controller.ts` | `/admin/stations/*`, `/admin/fuel-types/*` | ~150 |
| `admin/package.controller.ts` | `/admin/packages/*` | ~80 |
| `admin/import.controller.ts` | `/admin/import/*`, `/vouchers/import` | ~100 |
| `webhooks.controller.ts` | `/webhooks/*` | ~100 |

### 4.2 Service Layer Introduction

**New Services:**

```typescript
// application/services/purchase.service.ts
export class PurchaseService {
  constructor(
    private orderRepository: IOrderRepository,
    private voucherRepository: IVoucherRepository,
    private eventPublisher: IEventPublisher,
    private paymentGateway: IPaymentGateway,
  ) {}

  async createPurchase(dto: CreatePurchaseDTO, userId: string): Promise<Order> {
    // 1. Validate inventory (optional, allow optimistic)
    // 2. Create order with PENDING_FULFILLMENT
    // 3. Publish ORDER_CREATED event
    // 4. Return order
  }

  async simulatePayment(orderId: string, scenario: 'success' | 'failure'): Promise<Order> {
    // Existing business logic, refactored
  }
}
```

### 4.3 Domain Service: FuelMatcher

Move `getFuelAliases()` from repository to domain:

```typescript
// domain/services/fuel-matcher.service.ts
export class FuelMatcherService {
  constructor(
    private aliasRepository: IFuelAliasRepository, // DB-backed
  ) {}

  async getAliases(fuelType: string): Promise<string[]> {
    // Check DB first
    const dbAliases = await this.aliasRepository.findByAlias(fuelType);
    if (dbAliases.length > 0) {
      return dbAliases.map(a => a.alias);
    }
    
    // Fallback to hardcoded for backwards compatibility
    return this.getHardcodedAliases(fuelType);
  }

  private getHardcodedAliases(type: string): string[] {
    // Existing getFuelAliases() logic
  }
}
```

---

## 5. Cross-Cutting Concerns

### 5.1 Centralized Configuration

```typescript
// config/index.ts
export const config = {
  app: {
    port: parseInt(process.env.PORT || '4000'),
    env: process.env.NODE_ENV || 'development',
    devUserId: process.env.DEV_USER_ID || 'd366f82a-e65c-4110-bf20-ab2f44750cfe',
  },
  database: {
    url: process.env.DATABASE_URL!,
    poolMin: 2,
    poolMax: 10,
  },
  redis: {
    url: process.env.REDIS_URL || 'redis://localhost:6379',
    streamBlockMs: 5000,
  },
  stripe: {
    secretKey: process.env.STRIPE_SECRET_KEY!,
    publishableKey: process.env.STRIPE_PUBLISHABLE_KEY!,
    webhookSecret: process.env.STRIPE_WEBHOOK_SECRET,
  },
  twilio: {
    accountSid: process.env.TWILIO_ACCOUNT_SID!,
    authToken: process.env.TWILIO_AUTH_TOKEN!,
    phoneNumber: process.env.TWILIO_PHONE_NUMBER!,
  },
  gemini: {
    apiKey: process.env.GEMINI_API_KEY!,
    model: 'gemini-1.5-flash',
  },
  session: {
    secret: process.env.SESSION_SECRET || 'change-in-production',
    maxAge: 24 * 60 * 60 * 1000,
  },
} as const;
```

### 5.2 Standardized Error Handling

```typescript
// shared/errors/app-error.ts
export class AppError extends Error {
  constructor(
    public readonly statusCode: number,
    public readonly code: string,
    message: string,
    public readonly details?: unknown,
  ) {
    super(message);
    this.name = 'AppError';
  }

  static badRequest(message: string, details?: unknown): AppError {
    return new AppError(400, 'BAD_REQUEST', message, details);
  }

  static unauthorized(message = 'Unauthorized'): AppError {
    return new AppError(401, 'UNAUTHORIZED', message);
  }

  static notFound(resource: string): AppError {
    return new AppError(404, 'NOT_FOUND', `${resource} not found`);
  }

  static conflict(message: string): AppError {
    return new AppError(409, 'CONFLICT', message);
  }
}

// Middleware
export function errorHandler(err: Error, req: Request, res: Response, next: NextFunction) {
  logger.error({ err, path: req.path }, 'Request error');

  if (err instanceof AppError) {
    return res.status(err.statusCode).json({
      error: { code: err.code, message: err.message, details: err.details },
    });
  }

  if (err instanceof ZodError) {
    return res.status(400).json({
      error: { code: 'VALIDATION_ERROR', message: 'Invalid request data', details: err.errors },
    });
  }

  // Generic 500
  return res.status(500).json({
    error: { code: 'INTERNAL_ERROR', message: 'An unexpected error occurred' },
  });
}
```

### 5.3 Structured Logging

```typescript
// infrastructure/logging/logger.ts
import pino from 'pino';

export const logger = pino({
  level: process.env.LOG_LEVEL || 'info',
  transport: process.env.NODE_ENV === 'development'
    ? { target: 'pino-pretty' }
    : undefined,
  base: {
    service: 'fuel-flow-api',
    version: process.env.npm_package_version,
  },
});

// Usage in services
logger.info({ orderId, userId }, 'Order created');
logger.warn({ attempts }, 'Outbox retry limit approaching');
```

---

## 6. Migration Strategy

### Phase 1: Foundation (Week 1)
- [ ] Create new directory structure (empty files)
- [ ] Implement `config/index.ts`
- [ ] Implement `logger.ts`
- [ ] Implement `AppError` and error middleware
- [ ] Add pino dependency

### Phase 2: Infrastructure Adapters (Week 2)
- [ ] Create repository interfaces in `domain/repositories/`
- [ ] Implement Drizzle adapters in `infrastructure/persistence/`
- [ ] Create mappers for entities
- [ ] Migrate Redis client to new structure

### Phase 3: Domain Layer (Week 3)
- [ ] Create entity classes with validation
- [ ] Create value objects
- [ ] Move `getFuelAliases()` to `FuelMatcherService`
- [ ] Create `fuel_type_aliases` table and seed data

### Phase 4: Application Services (Week 4)
- [ ] Create `PurchaseService`
- [ ] Create `FulfillmentService`
- [ ] Create `AuthService`
- [ ] Create `VoucherImportService`
- [ ] Wire up dependency injection

### Phase 5: Controller Extraction (Week 5)
- [ ] Extract `auth.controller.ts`
- [ ] Extract `purchase.controller.ts`
- [ ] Extract `voucher.controller.ts`
- [ ] Extract admin controllers
- [ ] Create new `router.ts` aggregator

### Phase 6: Cleanup (Week 6)
- [ ] Remove old `routes.ts`
- [ ] Add database FK constraints
- [ ] Update tests
- [ ] Documentation

---

## 7. Verification Checklist

### Business Logic Preservation
- [ ] All 50+ API endpoints return identical responses
- [ ] Rate limiting works identically
- [ ] Session/auth flow unchanged
- [ ] Voucher assignment FIFO logic unchanged
- [ ] Fuel alias matching unchanged
- [ ] Event-driven fulfillment unchanged

### External Behavior
- [ ] Mobile app works without changes
- [ ] Admin panel works without changes
- [ ] Stripe webhooks processed correctly
- [ ] Twilio SMS sent correctly
- [ ] Gemini OCR works correctly

### Database Integrity
- [ ] All 14 original tables exist
- [ ] All data preserved
- [ ] New constraints don't break existing data
- [ ] Migrations are reversible

---

## Appendix A: Architectural Improvements Summary

| Area | Before | After | Benefit |
|------|--------|-------|---------|
| Route Management | 900-line monolith | 7 focused controllers | Maintainability |
| Business Logic | Scattered in routes/repos | Application services | Testability |
| Domain Logic | In repositories | Domain layer | Clarity |
| Error Handling | Inconsistent try/catch | Centralized middleware | Consistency |
| Logging | `console.log` | Structured pino | Observability |
| Configuration | Scattered env reads | Centralized config | Discoverability |
| Fuel Matching | Hardcoded function | DB + fallback | Configurability |
| Database Types | Some missing FKs | Full referential integrity | Data quality |

---

*Document Version: 1.0*  
*Last Updated: 2026-01-24*
