# About Solution

This document provides a detailed overview of the Fuel-Flow architecture, technology stack, and implementation details.

## Topology

The system follows a microservices-inspired architecture managed via Docker Compose:

```mermaid
graph TD
    User([User App]) -- HTTP/REST --> API[Admin Backend]
    Admin([Admin Panel]) -- HTTP/REST --> API
    API -- SQL --> DB[(PostgreSQL)]
    API -- Streams --> Redis[(Redis)]
    Redis -- Events --> Consumer[Fulfillment Consumer]
    Consumer -- SQL --> DB
    API -- API --> Stripe[Stripe Payments]
    API -- API --> Gemini[Gemini AI OCR]
    API -- API --> Twilio[Twilio SMS]
```

- **PostgreSQL**: Stores users, vouchers, orders, fulfillments, and session data.
- **Redis**: Event streaming for real-time order fulfillment with consumer groups.
- **Admin Backend**: Orchestrates business logic, handles OCR processing for voucher PDFs, and manages integrations.
- **Fulfillment Consumer**: Async service that assigns vouchers to orders in FIFO order.
- **Frontend Applications**: React-based SPAs for both administrators and end-users.

## File Structure

```text
Fuel-Flow/
├── admin-panel/
│   ├── backend/        # Node.js/Express API & OCR Logic
│   │   └── src/
│   │       ├── features/orders/     # Order-based purchasing
│   │       ├── services/            # Fulfillment consumer
│   │       └── shared/infrastructure/  # Redis, DB clients
│   ├── frontend/       # React Admin Dashboard
│   └── database/       # Drizzle ORM Schema & Migrations
├── mobile-app/         # Capacitor/React Mobile Application
├── docker-compose.yml  # Container Orchestration
└── .env                # Environment Variables
```

## Tech Stack

### Backend
- **Framework**: Express.js
- **Language**: TypeScript
- **ORM**: Drizzle ORM
- **Event Streaming**: Redis Streams (with database outbox fallback)
- **Authentication**: Passport.js (Local Strategy, Session-based)
- **Storage**: Multer for file uploads
- **OCR**: Google Gemini AI & `pdf-lib`, `pdfjs-dist`

### Frontend & Mobile
- **Core**: React 19
- **Build Tool**: Vite
- **Styling**: Tailwind CSS 4
- **State Management**: Zustand
- **Data Fetching**: TanStack Query (React Query)
- **Navigation**: Wouter
- **Mobile Bridge**: Capacitor 8

## Implementation Highlights

### Event-Driven Order Fulfillment
The system decouples order creation from voucher assignment using an **Optimistic Purchase** model:
1. **User purchases** → Order created with `PENDING_FULFILLMENT` status
2. **Event published** → Redis Streams (or database outbox)
3. **Consumer processes** → Assigns voucher using FIFO ordering with row-level locking
4. **Order fulfilled** → Status updated, user sees voucher in app

This architecture allows purchases to succeed even when voucher inventory is empty, with automatic backfill when vouchers are imported.

### OCR Processing
The system uses a sophisticated OCR pipeline to extract voucher data from PDF files. It leverages `pdfjs-dist` to render PDF pages into images, which are then processed by Google's Gemini AI to identify fuel types, dosages, and QR codes.

### Payment Integration
Stripe is integrated to handle secure payments for fuel vouchers. The backend manages Stripe customers, payment intents, and webhooks to ensure synchronization between payment status and order creation.

### Mobile Experience
The `mobile-app` is built as a Progressive Web App (PWA) wrapped with Capacitor, allowing for native features and distribution across Android and iOS platforms while maintaining a shared codebase with the web.
