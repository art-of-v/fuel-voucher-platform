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
  unit: string;
  qrCodeUrl?: string;
  qrCodeData?: string;
  externalId?: string;
  imageUrl?: string | null;
}

export interface Order {
  id: string;
  productType: string;
  provider: string;
  fuelType: string;
  fuelName?: string;
  liters: number;
  quantity: number;
  price: number;
  status: 'PENDING_FULFILLMENT' | 'FULFILLED' | 'REFUNDED';
  createdAt: string;
  fulfilledAt: string | null;
  vouchers?: Voucher[];
}

export interface SyncResponse {
  orders: Order[];
  vouchers: Voucher[];
  serverTimestamp: string;
}

export interface Company {
  id: string;
  userId: string;
  name: string;
  edrpou: string;
  vatNumber?: string;
  address?: string;
  directorName?: string;
  phone?: string;
  email?: string;
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
