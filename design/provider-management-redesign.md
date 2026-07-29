# Fuel Provider Management — Redesign

## Business Model

### Core Entities

**Fuel Provider** (renamed from "Station")
- A company that sells fuel (OKKO, WOG, etc.)
- Has: name, logo text, color
- Price per fuel type is **global** — same at all locations
- Each provider defines its own **set of nominals** it offers

**Fuel Type** (per provider)
- A specific fuel product (e.g., "Pulls 95", "Diesel Euro")
- Has: name (provider-specific), supplier price per liter, margin per liter
- Final price per liter = supplier price + margin (auto-calculated)
- Margin can be UAH/L or percent

**Nominal**
- A package/liter amount offered by a provider (2, 3, 5, 10, 20, 50, 100 L)
- Each provider has their own list of available nominals
- Voucher price = final_price_per_liter × liters (auto-calculated, not stored)
- Creating/editing a fuel type auto-generates packages for all active nominals

**Voucher**
- Individual item purchased by customer
- Belongs to provider + fuel type + nominal
- Has: unique code, status, expiration

### Pricing Relationship

```
SupplierPricePerLiter + MarginUahPerLiter = FinalPricePerLiter
                               or
SupplierPricePerLiter × (1 + MarginPercent%) = FinalPricePerLiter

For each nominal (e.g. 10L):
  Voucher price = FinalPricePerLiter × 10
```

## Page Design

### Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│  FUEL FLOW  ADMIN  / Fuel Providers                                 │
│                                                                     │
│  [+ Add Provider]                                                   │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │ ● OKKO                                 [Edit] [Delete]  ▼    │   │
│  ├──────────────────────────────────────────────────────────────┤   │
│  │ Fuel            Supp/L    Margin    Final/L    Nominals       │   │
│  │ ───────────────────────────────────────────────────────────── │   │
│  │ Pulls 95       85.90 ₴   0.00 ₴    85.90 ₴    10L·859₴      │   │
│  │                                                20L·1718₴     │   │
│  │                                                50L·4295₴     │   │
│  │ ───────────────────────────────────────────────────────────── │   │
│  │ Diesel Euro    89.90 ₴   0.00 ₴    89.90 ₴    10L·899₴      │   │
│  │ ───────────────────────────────────────────────────────────── │   │
│  │ [+ Add Fuel]               [Nominals: 10, 20, 50]  [⚙]      │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │ ● WOG                                   [Edit] [Delete]  ▼    │   │
│  ├──────────────────────────────────────────────────────────────┤   │
│  │ ...                                                          │   │
│  └──────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### States

| State | Behavior |
|-------|----------|
| **Empty** | Show illustration + "Add your first fuel provider" CTA |
| **Loading** | Skeleton cards (3 placeholder cards with pulse animation) |
| **Error** | Red banner with retry button, message from server |
| **Loaded** | Expandable cards as shown above |
| **Saving** | Inline spinner on affected row, disable actions |
| **Deleted** | Slide-out animation, toast "Provider removed" |

### Interactions

| Action | Trigger | Behavior |
|--------|---------|----------|
| **Add Provider** | Click [+ Add Provider] | Opens modal: name, color picker, logo text. On save → appears as collapsed card |
| **Edit Provider** | Click [Edit] on card header | Opens same modal pre-filled. On save → updates header |
| **Delete Provider** | Click [Delete] | Confirmation dialog: "This will remove all fuels, nominals, and associated packages. Are you sure?" On confirm → slide-out + toast |
| **Expand/Collapse** | Click provider name/chevron | Smooth expand/collapse animation. Fuels table + nominals section slides in |
| **Edit Fuel Price** | Click price cell (Supp/L, Margin, Final/L) | Inline edit — cell becomes input. Tab/Enter to save, Esc to cancel. Auto-calculates dependent fields |
| **Add Fuel Type** | Click [+ Add Fuel] | Modal or inline row: fuel name, supplier price. Auto-creates packages for all active nominals |
| **Delete Fuel Type** | Click trash icon on fuel row | Confirm dialog, removes all associated packages |
| **Configure Nominals** | Click [⚙] on nominals line | Opens modal: checkboxes for [2L 3L 5L 10L 20L 50L 100L] + custom. On save, creates/removes packages for that provider's fuel types |
| **Voucher Inventory** | Click nominal badge | (Future) Show voucher list for that specific nominal |

### Responsive Behavior

- Desktop: full table layout with inline editing
- Tablet: same layout, slightly smaller cards
- Mobile: stack layout — each fuel becomes a card with stacked fields

## Data Layer

### API Endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| GET | /api/admin/providers | List all fuel providers (with fuels, prices, nominals) |
| POST | /api/admin/providers | Create provider |
| PUT | /api/admin/providers/{id} | Update provider details |
| DELETE | /api/admin/providers/{id} | Delete provider + all related data |
| POST | /api/admin/providers/{id}/fuels | Add fuel type with price |
| PUT | /api/admin/fuels/{id} | Update fuel price (supplier/margin/final) |
| DELETE | /api/admin/fuels/{id} | Remove fuel type |
| PUT | /api/admin/providers/{id}/nominals | Update provider's nominal set |
| GET | /api/admin/providers/{id}/history | Get audit trail (paginated, filterable by event type / date range) |

### Response Shape (GET /api/admin/providers)

```json
{
  "providers": [
    {
      "id": "okko",
      "name": "OKKO",
      "color": "#00ff80",
      "logoText": "OKKO",
      "nominals": [10, 20, 50],
      "fuels": [
        {
          "id": "okko-pulls95",
          "name": "Pulls 95",
          "supplierPricePerLiter": 85.90,
          "marginUahPerLiter": 0.00,
          "marginPercent": 0,
          "finalPricePerLiter": 85.90,
          "packages": [
            { "liters": 10, "price": 859 },
            { "liters": 20, "price": 1718 },
            { "liters": 50, "price": 4295 }
          ]
        }
      ]
    }
  ]
}
```

### Package Auto-Calculation

When a fuel type's price changes OR when nominals change:
1. Delete all existing packages for that fuel type
2. For each active nominal (e.g., 10, 20, 50):
   - Create package: liters=L, price=round(FinalPricePerLiter × L)
   - All other fields (supplierPricePerLiter, marginUahPerLiter, finalPricePerLiter) copied from fuel type

## Audit Trail & Outbox

### Every Change Must Be Recorded

Any price change, fuel addition/removal, nominal change, or provider update **must** produce an outbox event with full audit trail.

### Outbox Event Shape

| Field | Description |
|-------|-------------|
| EventId | Unique GUID |
| AggregateType | "Provider", "Fuel", "Nominal" |
| AggregateId | ID of the changed entity |
| EventType | "PriceChanged", "FuelAdded", "FuelRemoved", "NominalSetChanged", "ProviderCreated", "ProviderUpdated", "ProviderDeleted" |
| OldValue | Snapshot of values before change (JSON) |
| NewValue | Snapshot of values after change (JSON) |
| ChangedByUserId | Who made the change |
| ChangedByUserName | Display name of who made the change |
| ChangedAtUtc | Timestamp |
| Summary | Human-readable description, e.g. "OKKO / Pulls 95: supplier price 85.90 → 87.50" |

### Display in UI

A **History** section at the bottom of the provider card (collapsed by default):
- Shows last 50 events for that provider
- Each row: date, user, summary of what changed
- Expandable detail showing old/new JSON diff
- Color-coded severity: price change (yellow), creation (green), deletion (red)

### Outbox Processing

- Events written to `provider_event_outbox` table
- Background processor publishes them to a log/stream
- UI fetches history from `/api/admin/providers/{id}/history` endpoint
- History is queryable by provider, by date range, by event type

## Migration Strategy

### Phase 1 — New Page + Outbox (this task)
- Build the consolidated Fuel Providers page alongside existing pages
- New backend endpoint (`/api/admin/providers`) that joins data from stations + fuel types + packages
- Create `provider_event_outbox` table and background processor
- Implement audit trail for all mutations
- No data migration needed yet

### Phase 2 — Switch Over
- Once the new page is validated, remove old sidebar items
- Delete unused endpoints and frontend components

### Phase 3 — Seed Data
- Update seed with real prices provided by user
