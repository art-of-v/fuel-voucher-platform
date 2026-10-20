export interface Station {
  /** Surrogate key (uuid). What the API joins on; nothing a human reads. */
  id: string;
  /**
   * The network's own code — `"okko"`, `"wog"`. It outlives a re-keying, so it is what the design
   * tokens (brand colours and logos) and a log line key on; a uuid would miss every entry.
   */
  slug: string;
  name: string;
  color: string;
  logoText: string;
  /** Admin-managed display priority; lower = higher. 999 = end of the list. */
  sortOrder: number;
}

export interface StationNode {
  id: string;
  /**
   * The fuel network this АЗК belongs to, as a Brand **uuid** — the wire name on
   * `GET /api/brand-stations`. It was `stationId` until the 2026-10-18 rename, when `stations`
   * became `brands` and `station_nodes` became `brand_stations`; nothing pointed at a filling
   * station in either name. It became a uuid with planning D-21.
   */
  brandId: string;
  /**
   * The same network's code, carried beside the id so a node can be labelled — and its logo found —
   * without a second round trip. `GET /api/brand-stations` has no id-by-slug route, so a client
   * holding only a node would otherwise print a hex string where "OKKO" belongs.
   */
  brandSlug: string;
  name: string;
  address?: string;
  phone?: string;
  city?: string;
  stationType?: string;
  /**
   * Decimal degrees, as JSON numbers. They were typed `string` for as long as the
   * API existed and the API never sent one — `PublicBrandStationResponse` maps
   * `Lat`/`Lng` as decimal — so every read went through `parseFloat` to bridge a
   * gap that was never on the wire. Caught by `wireContract.ts`, which assigns a
   * captured production response to this interface and is compiled by `tsc`.
   */
  lat?: number;
  lng?: number;
}

export interface FuelType {
  id: string;
  name: string;
  /** Brand uuid, matching {@link StationNode.brandId}. */
  brandId: string;
  /** The network's code. A fuel's own id is an opaque uuid, so this is what says which network a fuel belongs to. */
  brandSlug: string;
  basePrice: number;
  discountPrice: number;
  /**
   * What the network charges at the pump - the column price. Null when it is not known.
   *
   * This is the price a customer is shown for a fuel we cannot sell yet: it is what the market
   * charges, and the only honest number to show for a fuel nobody has costed.
   */
  pumpPricePerLiter?: number;
  /**
   * What WE charge - the cost plus the margin, capped at the pump price. Null when we have no
   * margin set, which is the state an imported fuel is in until an operator prices it.
   */
  finalPricePerLiter?: number;
  /**
   * Whether this fuel can be bought right now: at least one of its sizes has live stock behind it in
   * the caller's own pool.
   */
  isAvailable?: boolean;
}

export interface StationWithFuels extends Station {
  fuels: FuelType[];
}

export interface FuelPackage {
  id: string;
  /** Brand uuid, matching {@link StationNode.brandId}. */
  brandId: string;
  /** The network's code, matching {@link StationNode.brandSlug}. */
  brandSlug: string;
  fuelTypeId: string;
  fuelName: string;
  liters: number;
  price: number;
  originalPrice: number;
  /** Voucher price per litre (грн/л) from PublicPackageResponse; null on legacy rows. */
  finalPricePerLiter?: number;
  /**
   * Whether the shop can sell this exact package right now — true when at least one activated
   * (Available) voucher of the same brand + fuel + nominal exists for the caller.
   *
   * The catalog lists every package either way; this is what turns the buy button off rather than
   * hiding the card. It is scoped to the caller's own stock pool, so a QA account and a regular
   * customer can legitimately see different answers for the same package.
   *
   * Optional so a cached or older payload degrades to "buyable" — the server still refuses the sale.
   */
  isAvailable?: boolean;
}

export interface Voucher {
  id: string;
  brand: string;
  fuelType: string;
  fuelName?: string;
  amount: number;
  status: string;
  unit?: string;
  qrCodeUrl?: string;
  qrCodeData?: string;
  externalId?: string;
  imageUrl?: string | null;
  expirationDate?: string;
  // Company / worker fields (see docs/COMPANY_WORKERS.md (private docs repo))
  source?: 'own' | 'gifted' | string;
  legalEntityId?: string | null;
  workerUserId?: string | null;
  workerFirstName?: string | null;
  workerLastName?: string | null;
  fuelSubtype?: string | null;
  redemptionRules?: string | null;
  // The customer's original purchase order for this fuel, resolved across replace swaps
  // (see docs/VOUCHER_RENEWAL.md "Two histories"). The wallet files the live voucher under
  // this order so one tank of fuel stays one asset. Null when no purchase order backs it.
  originOrderId?: string | null;
  // The voucher's dated lifecycle: the purchase, then each renewal (oldest first).
  history?: VoucherHistoryEvent[];
}

// One dated entry in a voucher's life, from the sync response (VoucherDto.History).
export interface VoucherHistoryEvent {
  type: 'Purchase' | 'Renewal';
  date: string;
  liters: number;
  amount?: number | null;
  validFrom?: string | null;
  validTo?: string | null;
  termCode?: string | null;
  // True when a renewal REPLACED the voucher (a different one was issued, the old expired), so the
  // UI says "replaced" rather than "extended". False/absent for a purchase and for an extend.
  isReplacement?: boolean;
}

// Classification of a voucher relative to the current user. Derived from
// legalEntityId + workerUserId per the spec's suggested rules (§5).
export type VoucherKind =
  'personal' | 'company_pool' | 'gifted_to_me' | 'gifted_to_worker' | 'blocked';

export function classifyVoucher(
  voucher: Pick<Voucher, 'legalEntityId' | 'workerUserId' | 'status'>,
  currentUserId?: string | null,
): VoucherKind {
  if ((voucher.status ?? '').toLowerCase() === 'blocked') return 'blocked';
  if (!voucher.legalEntityId) return 'personal';
  if (voucher.workerUserId && voucher.workerUserId === currentUserId) return 'gifted_to_me';
  if (voucher.workerUserId) return 'gifted_to_worker';
  return 'company_pool';
}

export interface OrderLineItem {
  id: string;
  brand: string;
  fuelTypeId: string;
  liters: number;
  quantity: number;
}

export interface Order {
  id: string;
  brand: string;
  fuelType: string;
  fuelName?: string;
  liters: number;
  quantity: number;
  price: number;
  status:
    'PENDING_PAYMENT' | 'PENDING_FULFILLMENT' | 'FULFILLED' | 'REFUNDED' | 'PARTIALLY_REFUNDED';
  createdAt: string;
  fulfilledAt: string | null;
  monobankPaymentUrl?: string;
  monobankInvoiceId?: string;
  legalEntityId?: string | null;
  vouchers?: Voucher[];
  lineItems: OrderLineItem[];
  /** True when this order is a voucher renewal/replacement, not a fuel purchase. */
  isRenewal?: boolean;
  /**
   * What the order represents. `ReceivedFromCompany` is a handover of already-bought
   * fuel to a worker — price 0, and the worker's receipt rather than a purchase.
   * Defaults to a purchase so an older server response still renders.
   */
  kind?: 'Purchase' | 'Renewal' | 'ReceivedFromCompany';
}

export interface SyncResponse {
  orders: Order[];
  vouchers: Voucher[];
  serverTimestamp: string;
}

export interface Company {
  id: string;
  userId?: string;
  name: string;
  edrpou: string;
  vatNumber?: string;
  address?: string;
  directorName?: string;
  phone?: string;
  email?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface Contract {
  id: string;
  title: string;
  content: string;
  version: string;
  status: string;
}

export interface UserContract {
  id: string;
  signedAt: string;
  contract: Contract;
  station?: Station | null;
}

export interface User {
  id: string;
  phone?: string;
  firstName?: string;
  lastName?: string;
  email?: string;
  birthdate?: string;
  profileImageUrl?: string;
  userType?: 'INDIVIDUAL' | 'LEGAL_ENTITY';
}

export interface CartItem {
  id: string;
  package: FuelPackage;
  station: Station;
  fuel: FuelType;
  quantity: number;
}
