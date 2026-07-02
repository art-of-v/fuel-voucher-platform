import { QueryClient } from "@tanstack/react-query";

// TypeScript types for .NET API responses
export interface ApiResponse<T> {
  data?: T;
  total?: number;
  message?: string;
}

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
  createdAtUtc: Date;
}

export interface FuelType {
  id: string;
  name: string;
  stationId: string;
  basePrice: number;
  discountPrice: number;
  createdAtUtc: Date;
}

export interface FuelPackage {
  id: string;
  stationId: string;
  fuelTypeId: string;
  fuelName: string;
  liters: number;
  price: number;
  originalPrice: number;
  createdAtUtc: Date;
}

export interface QrCode {
  id: number;
  stationId: string;
  fuelType: string;
  liters: number;
  qrCodeUrl: string;
  status: "available" | "sold";
  purchaseId?: number;
  createdAtUtc: Date;
}

export interface Purchase {
  id: Guid;
  sessionId: string;
  packageId: string;
  stationName: string;
  fuelName: string;
  liters: number;
  quantity: number;
  price: number;
  qrCodeId?: number;
  voucherId?: string;
  status: string;
  monobankInvoiceId?: string;
  monobankStatus?: string;
  createdAtUtc: Date;
}

export interface FuelVoucher {
  id: Guid;
  provider: string;
  externalId?: string;
  fuelTypeId: string;
  amount: number;
  unit: string;
  expirationDate?: Date;
  status: string;
  monobankInvoiceId?: string;
  monobankStatus?: string;
  redemptionRules?: string;
  imageUrl?: string;
  qrCodeData?: string;
  originalFileName?: string;
  source: string;
  importJobId?: Guid;
  assignedToUserId?: string;
  createdAtUtc: Date;
  updatedAtUtc: Date;
}

export interface Contract {
  id: Guid;
  userId: string;
  companyId: Guid;
  contractUrl: string;
  status: string;
  signedAtUtc?: Date;
  createdAtUtc: Date;
}

export interface UserContract {
  id: Guid;
  userId: string;
  contractId: Guid;
  userName: string;
  email: string;
  companyName: string;
  signedAtUtc: Date;
}

// Admin API client functions
export const adminApi = {
  // Stations endpoints
  stations: {
    list: () => fetch('/api/admin/stations').then(r => r.json()) as Promise<Station[]>,
    get: (id: string) => fetch(`/api/admin/stations/${id}`).then(r => r.json()) as Promise<Station>,
    create: (data: Partial<Station>) => fetch('/api/admin/stations', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<Station>,
    update: (id: string, data: Partial<Station>) => fetch(`/api/admin/stations/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<Station>,
    delete: (id: string) => fetch(`/api/admin/stations/${id}`, { method: 'DELETE' }).then(r => r.json()) as Promise<{ success: boolean }>,
  },

  // Fuel types endpoints
  fuelTypes: {
    list: () => fetch('/api/admin/fuel-types').then(r => r.json()) as Promise<FuelType[]>,
    get: (id: string) => fetch(`/api/admin/fuel-types/${id}`).then(r => r.json()) as Promise<FuelType>,
    create: (data: Partial<FuelType>) => fetch('/api/admin/fuel-types', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<FuelType>,
    update: (id: string, data: Partial<FuelType>) => fetch(`/api/admin/fuel-types/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<FuelType>,
    delete: (id: string) => fetch(`/api/admin/fuel-types/${id}`, { method: 'DELETE' }).then(r => r.json()) as Promise<{ success: boolean }>,
  },

  // Packages endpoints
  packages: {
    list: () => fetch('/api/admin/packages').then(r => r.json()) as Promise<FuelPackage[]>,
    getByStation: (stationId: string) => fetch(`/api/admin/packages/station/${stationId}`).then(r => r.json()) as Promise<FuelPackage[]>,
    create: (data: Partial<FuelPackage>) => fetch('/api/admin/packages', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<FuelPackage>,
    update: (id: string, data: Partial<FuelPackage>) => fetch(`/api/admin/packages/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<FuelPackage>,
    delete: (id: string) => fetch(`/api/admin/packages/${id}`, { method: 'DELETE' }).then(r => r.json()) as Promise<{ success: boolean }>,
  },

  // QR Codes endpoints
  qrCodes: {
    list: () => fetch('/api/admin/qr-codes').then(r => r.json()) as Promise<QrCode[]>,
    create: (data: Partial<QrCode>) => fetch('/api/admin/qr-codes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<QrCode>,
    delete: (id: number) => fetch(`/api/admin/qr-codes/${id}`, { method: 'DELETE' }).then(r => r.json()) as Promise<{ success: boolean }>,
  },

  // Purchases endpoints
  purchases: {
    list: () => fetch('/api/admin/purchases').then(r => r.json()) as Promise<Purchase[]>,
    get: (id: Guid) => fetch(`/api/admin/purchases/${id}`).then(r => r.json()) as Promise<Purchase>,
    update: (id: Guid, data: { status: string }) => fetch(`/api/admin/purchases/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<Purchase>,
    delete: (id: Guid) => fetch(`/api/admin/purchases/${id}`, { method: 'DELETE' }).then(r => r.json()) as Promise<{ success: boolean }>,
  },

  // Vouchers endpoints (admin)
  vouchers: {
    list: (filters?: { search?: string; status?: string; page?: number; pageSize?: number }) => {
      const params = new URLSearchParams();
      if (filters?.search) params.append('search', filters.search);
      if (filters?.status) params.append('status', filters.status);
      if (filters?.page) params.append('page', filters.page.toString());
      if (filters?.pageSize) params.append('pageSize', filters.pageSize.toString());
      const url = `/api/admin/vouchers${params.toString() ? '?' + params.toString() : ''}`;
      return fetch(url).then(r => r.json()) as Promise<{ items: FuelVoucher[]; total: number }>;
    },
    get: (id: Guid) => fetch(`/api/admin/vouchers/${id}`).then(r => r.json()) as Promise<FuelVoucher>,
    update: (id: Guid, data: { status?: string; assignedToUserId?: Guid }) => fetch(`/api/admin/vouchers/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<FuelVoucher>,
    delete: (id: Guid) => fetch(`/api/admin/vouchers/${id}`, { method: 'DELETE' }).then(r => r.json()) as Promise<{ success: boolean }>,
  },

  // Orders endpoints
  orders: {
    list: () => fetch('/api/admin/orders').then(r => r.json()) as Promise<Order[]>,
    get: (id: Guid) => fetch(`/api/admin/orders/${id}`).then(r => r.json()) as Promise<Order>,
    update: (id: Guid, data: { status: string }) => fetch(`/api/admin/orders/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<Order>,
    delete: (id: Guid) => fetch(`/api/admin/orders/${id}`, { method: 'DELETE' }).then(r => r.json()) as Promise<{ success: boolean }>,
  },

  // Fuel Vouchers endpoints (admin)
  fuelVouchers: {
    list: (filters?: { search?: string; status?: string; page?: number; pageSize?: number }) => {
      const params = new URLSearchParams();
      if (filters?.search) params.append('search', filters.search);
      if (filters?.status) params.append('status', filters.status);
      if (filters?.page) params.append('page', filters.page.toString());
      if (filters?.pageSize) params.append('pageSize', filters.pageSize.toString());
      const url = `/api/admin/fuel-vouchers${params.toString() ? '?' + params.toString() : ''}`;
      return fetch(url).then(r => r.json()) as Promise<{ items: FuelVoucher[]; total: number }>;
    },
    get: (id: Guid) => fetch(`/api/admin/fuel-vouchers/${id}`).then(r => r.json()) as Promise<FuelVoucher>,
    update: (id: Guid, data: { status?: string; assignedToUserId?: Guid }) => fetch(`/api/admin/fuel-vouchers/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }).then(r => r.json()) as Promise<FuelVoucher>,
    delete: (id: Guid) => fetch(`/api/admin/fuel-vouchers/${id}`, { method: 'DELETE' }).then(r => r.json()) as Promise<{ success: boolean }>,
  },

  // Contracts endpoints
  contracts: {
    list: () => fetch('/api/admin/legal-entity/contracts').then(r => r.json()) as Promise<Contract[]>,
    getSignedContracts: () => fetch('/api/admin/legal-entity/signed-contracts').then(r => r.json()) as Promise<UserContract[]>,
  },
};
