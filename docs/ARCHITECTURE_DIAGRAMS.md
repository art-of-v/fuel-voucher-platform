# Fuel-Flow Target Architecture Diagram

## System Context Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                   EXTERNAL SYSTEMS                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐                  │
│  │   Stripe API    │  │   Twilio API    │  │  Google Gemini  │                  │
│  │   (Payments)    │  │     (SMS)       │  │     (OCR)       │                  │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘                  │
│           │                    │                    │                            │
└───────────┼────────────────────┼────────────────────┼────────────────────────────┘
            │                    │                    │
            ▼                    ▼                    ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              FUEL-FLOW PLATFORM                                  │
│                                                                                  │
│  ┌───────────────────────────────────────────────────────────────────────────┐  │
│  │                          API Gateway Layer                                 │  │
│  │                       (Express.js - Port 4000)                             │  │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────────────┐  │  │
│  │  │   Auth      │ │  Purchase   │ │  Voucher    │ │   Admin            │  │  │
│  │  │ Controller  │ │ Controller  │ │ Controller  │ │   Controllers      │  │  │
│  │  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └─────────┬───────────┘  │  │
│  └─────────┼───────────────┼───────────────┼───────────────────┼─────────────┘  │
│            │               │               │                   │                 │
│            ▼               ▼               ▼                   ▼                 │
│  ┌───────────────────────────────────────────────────────────────────────────┐  │
│  │                        Application Services                                │  │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────────────┐  │  │
│  │  │   Auth      │ │  Purchase   │ │ Fulfillment │ │   VoucherImport     │  │  │
│  │  │  Service    │ │  Service    │ │  Service    │ │     Service         │  │  │
│  │  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └─────────┬───────────┘  │  │
│  └─────────┼───────────────┼───────────────┼───────────────────┼─────────────┘  │
│            │               │               │                   │                 │
│            ▼               ▼               ▼                   ▼                 │
│  ┌───────────────────────────────────────────────────────────────────────────┐  │
│  │                           Domain Layer                                     │  │
│  │  ┌───────────────────────────────────────────────────────────────────┐    │  │
│  │  │  Entities: User | Order | Voucher | Fulfillment | Station         │    │  │
│  │  │  Value Objects: Money | PhoneNumber | FuelType | ExternalId       │    │  │
│  │  │  Domain Services: FuelMatcherService                              │    │  │
│  │  │  Repository Interfaces: IUserRepo | IOrderRepo | IVoucherRepo     │    │  │
│  │  └───────────────────────────────────────────────────────────────────┘    │  │
│  └───────────────────────────────────────────────────────────────────────────┘  │
│                                      │                                           │
│                                      ▼                                           │
│  ┌───────────────────────────────────────────────────────────────────────────┐  │
│  │                        Infrastructure Layer                                │  │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────────────┐  │  │
│  │  │  Drizzle    │ │   Redis     │ │   Stripe    │ │   Twilio/Gemini     │  │  │
│  │  │ Repositories│ │  Messaging  │ │  Adapter    │ │     Adapters        │  │  │
│  │  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └─────────┬───────────┘  │  │
│  └─────────┼───────────────┼───────────────┼───────────────────┼─────────────┘  │
│            │               │               │                   │                 │
└────────────┼───────────────┼───────────────┼───────────────────┼─────────────────┘
             │               │               │                   │
             ▼               ▼               │                   │
┌────────────────────┐ ┌────────────────────┐│                   │
│    PostgreSQL      │ │      Redis         ││                   │
│    (Port 5432)     │ │    (Port 6379)     ││                   │
│  ┌──────────────┐  │ │  ┌──────────────┐  ││                   │
│  │ users        │  │ │  │ order-events │  ││       ┌───────────┴───────────┐
│  │ orders       │  │ │  │ (Stream)     │  ││       │   External Services   │
│  │ vouchers     │  │ │  └──────────────┘  ││       │   (Stripe/Twilio/     │
│  │ fulfillments │  │ │  ┌──────────────┐  ││       │    Gemini)            │
│  │ stations     │  │ │  │ fulfillment- │  ││       └───────────────────────┘
│  │ fuel_types   │  │ │  │ events       │  ││
│  │ packages     │  │ │  │ (Stream)     │  ││
│  │ outbox       │  │ │  └──────────────┘  ││
│  │ import_jobs  │  │ └────────────────────┘│
│  └──────────────┘  │                       │
└────────────────────┘                       │
                                             │
                     ┌───────────────────────┴───────────────────────┐
                     │            CLIENT APPLICATIONS                 │
                     │  ┌─────────────────┐  ┌─────────────────────┐ │
                     │  │   Mobile App    │  │    Admin Panel      │ │
                     │  │ (React/Capacitor│  │  (React/Vite)       │ │
                     │  │  Port 5001)     │  │   Port 5002         │ │
                     │  └─────────────────┘  └─────────────────────┘ │
                     └───────────────────────────────────────────────┘
```

## Event Flow Diagram

```
┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│   Mobile     │    │   Purchase   │    │    Order     │    │   Redis      │
│   Client     │    │   Service    │    │  Repository  │    │   Streams    │
└──────┬───────┘    └──────┬───────┘    └──────┬───────┘    └──────┬───────┘
       │                   │                   │                   │
       │  POST /purchases  │                   │                   │
       │──────────────────>│                   │                   │
       │                   │                   │                   │
       │                   │  createOrder()    │                   │
       │                   │──────────────────>│                   │
       │                   │                   │                   │
       │                   │                   │ BEGIN TRANSACTION │
       │                   │                   │──────────────────>│
       │                   │                   │                   │
       │                   │                   │ INSERT order      │
       │                   │                   │ INSERT outbox     │
       │                   │                   │                   │
       │                   │                   │ COMMIT            │
       │                   │                   │──────────────────>│
       │                   │                   │                   │
       │                   │                   │ XADD order-events │
       │                   │                   │──────────────────>│
       │                   │                   │                   │
       │   { orderId }     │                   │                   │
       │<──────────────────│                   │                   │
       │                   │                   │                   │
       │                   │                   │                   │
┌──────┴───────┐    ┌──────┴───────┐    ┌──────┴───────┐    ┌──────┴───────┐
│   Mobile     │    │   Purchase   │    │    Order     │    │   Redis      │
│   Client     │    │   Service    │    │  Repository  │    │   Streams    │
└──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘

                                        ... Later ...

┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│  Fulfillment │    │   Voucher    │    │    Order     │    │   Redis      │
│   Consumer   │    │  Repository  │    │  Repository  │    │   Streams    │
└──────┬───────┘    └──────┬───────┘    └──────┬───────┘    └──────┬───────┘
       │                   │                   │                   │
       │  XREADGROUP       │                   │                   │
       │<──────────────────────────────────────────────────────────│
       │                   │                   │                   │
       │  ORDER_CREATED    │                   │                   │
       │  { orderId }      │                   │                   │
       │                   │                   │                   │
       │  findAvailable()  │                   │                   │
       │──────────────────>│                   │                   │
       │                   │                   │                   │
       │                   │ BEGIN TX          │                   │
       │                   │ SELECT FOR UPDATE │                   │
       │                   │ UPDATE voucher    │                   │
       │                   │ INSERT fulfillment│                   │
       │                   │ COMMIT            │                   │
       │                   │                   │                   │
       │   voucher         │                   │                   │
       │<──────────────────│                   │                   │
       │                   │                   │                   │
       │  updateStatus()   │                   │                   │
       │─────────────────────────────────────>│                   │
       │                   │                   │                   │
       │                   │                   │ UPDATE FULFILLED  │
       │                   │                   │                   │
       │  XACK             │                   │                   │
       │──────────────────────────────────────────────────────────>│
       │                   │                   │                   │
```

## Database Entity Relationship Diagram

```
┌─────────────────────┐       ┌─────────────────────┐
│       users         │       │      stations       │
├─────────────────────┤       ├─────────────────────┤
│ id (PK, UUID)       │       │ id (PK)             │
│ email               │       │ name                │
│ phone               │       │ color               │
│ first_name          │       │ logo_text           │
│ last_name           │       │ lat, lng            │
│ vehicle_*           │       │ created_at          │
│ referral_code       │       └──────────┬──────────┘
│ bonus_balance       │                  │
│ created_at          │                  │ 1:N
└──────────┬──────────┘                  ▼
           │              ┌─────────────────────────┐
           │              │      fuel_types         │
           │              ├─────────────────────────┤
           │              │ id (PK)                 │
           │              │ name                    │
           │              │ station_id (FK)─────────┘
           │              │ base_price              │
           │              │ discount_price          │
           │              └──────────┬──────────────┘
           │                         │
           │                         │ 1:N
           │                         ▼
           │              ┌─────────────────────────┐
           │              │     fuel_packages       │
           │              ├─────────────────────────┤
           │              │ id (PK)                 │
           │              │ station_id (FK)         │
           │              │ fuel_type_id (FK)───────┘
           │              │ liters                  │
           │              │ price                   │
           │              │ original_price          │
           │              └─────────────────────────┘
           │
           │ 1:N
           ▼
┌─────────────────────┐       ┌─────────────────────┐
│       orders        │       │     import_jobs     │
├─────────────────────┤       ├─────────────────────┤
│ id (PK, UUID)       │       │ id (PK, UUID)       │
│ user_id (FK)────────┘       │ admin_id            │
│ product_type        │       │ total_files         │
│ provider            │       │ processed_files     │
│ fuel_type           │       │ status              │
│ liters              │       │ model_used          │
│ quantity            │       │ created_at          │
│ price               │       └──────────┬──────────┘
│ status              │                  │
│ stripe_payment_id   │                  │ 1:N
│ created_at          │                  ▼
│ fulfilled_at        │       ┌─────────────────────┐
└──────────┬──────────┘       │      vouchers       │
           │                  ├─────────────────────┤
           │                  │ id (PK, UUID)       │
           │ 1:N              │ provider            │
           ▼                  │ external_id         │
┌─────────────────────┐       │ fuel_type           │
│    fulfillments     │       │ amount              │
├─────────────────────┤       │ status              │
│ id (PK, SERIAL)     │       │ qr_code_data        │
│ order_id (FK)───────┘       │ assigned_to_user_id │──┐
│ voucher_id (FK)─────────────│ import_job_id (FK)──┘  │
│ fulfilled_at        │       │ created_at          │  │
└─────────────────────┘       └─────────────────────┘  │
                                        ▲              │
                                        │              │
                                        └──────────────┘
                                         (FK to users)

┌─────────────────────┐       ┌─────────────────────┐
│       outbox        │       │  phone_verifications│
├─────────────────────┤       ├─────────────────────┤
│ id (PK, SERIAL)     │       │ id (PK, SERIAL)     │
│ event_type          │       │ phone               │
│ payload (JSONB)     │       │ code                │
│ processed           │       │ expires_at          │
│ processed_at        │       │ verified            │
│ created_at          │       │ created_at          │
└─────────────────────┘       └─────────────────────┘
```

## Layer Dependency Rules

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                  │
│   PRESENTATION ─────────────────────────────────────────────┐   │
│       │                                                      │   │
│       │ depends on                                           │   │
│       ▼                                                      │   │
│   APPLICATION ─────────────────────────────────────────┐     │   │
│       │                                                 │     │   │
│       │ depends on                                      │     │   │
│       ▼                                                 │     │   │
│   DOMAIN ─────────────────────────────────────────┐     │     │   │
│       │                                            │     │     │   │
│       │ defines interfaces for                     │     │     │   │
│       ▼                                            │     │     │   │
│   INFRASTRUCTURE ◄─────────────────────────────────┘     │     │   │
│       │                                                   │     │   │
│       │ implements                                        │     │   │
│       └───────────────────────────────────────────────────┘     │   │
│                                                                  │
│   RULE: Inner layers NEVER depend on outer layers               │
│   RULE: Domain layer has NO external dependencies               │
│   RULE: All cross-layer communication via interfaces            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```
