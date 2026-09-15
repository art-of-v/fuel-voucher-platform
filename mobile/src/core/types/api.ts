export interface Station {
  id: string;
  name: string;
  color: string;
  logoText: string;
  address?: string;
  phone?: string;
  stationType?: string;
  lat?: string;
  lng?: string;
  /** Admin-managed display priority; lower = higher. 999 = end of the list. */
  sortOrder: number;
}

export interface StationNode {
  id: string;
  stationId: string;
  name: string;
  address?: string;
  phone?: string;
  city?: string;
  stationType?: string;
  lat?: string;
  lng?: string;
}

export interface FuelType {
  id: string;
  name: string;
  stationId: string;
  basePrice: number;
  discountPrice: number;
}

export interface StationWithFuels extends Station {
  fuels: FuelType[];
}

export interface FuelPackage {
  id: string;
  stationId: string;
  fuelTypeId: string;
  fuelName: string;
  liters: number;
  price: number;
  originalPrice: number;
}

export interface Voucher {
  id: string;
  provider: string;
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
  // Company / worker fields (see docs/COMPANY_WORKERS.md)
  source?: 'own' | 'gifted' | string;
  legalEntityId?: string | null;
  workerUserId?: string | null;
  workerFirstName?: string | null;
  workerLastName?: string | null;
  fuelSubtype?: string | null;
  redemptionRules?: string | null;
}

// Classification of a voucher relative to the current user. Derived from
// legalEntityId + workerUserId per the spec's suggested rules (§5).
export type VoucherKind =
  | 'personal'
  | 'company_pool'
  | 'gifted_to_me'
  | 'gifted_to_worker'
  | 'blocked';

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
  provider: string;
  fuelTypeId: string;
  liters: number;
  quantity: number;
}

export interface Order {
  id: string;
  provider: string;
  fuelType: string;
  fuelName?: string;
  liters: number;
  quantity: number;
  price: number;
  status: 'PENDING_PAYMENT' | 'PENDING_FULFILLMENT' | 'FULFILLED' | 'REFUNDED' | 'PARTIALLY_REFUNDED';
  createdAt: string;
  fulfilledAt: string | null;
  monobankPaymentUrl?: string;
  monobankInvoiceId?: string;
  legalEntityId?: string | null;
  vouchers?: Voucher[];
  lineItems: OrderLineItem[];
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
