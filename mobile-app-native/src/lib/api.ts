import { Platform } from "react-native";
import Constants from "expo-constants";

const DEFAULT_API_URL = Platform.OS === "android" ? "http://10.0.2.2:4000" : "http://localhost:4000";
export const BASE_URL =
  process.env.EXPO_PUBLIC_API_URL ||
  Constants.expoConfig?.extra?.apiUrl ||
  DEFAULT_API_URL;

console.log("API Base URL:", BASE_URL);

// --- Rest of interfaces (Purchases, Vouchers, etc.) ---

interface PurchaseData {
  packageId: string;
  stationId: string;
  stationName: string;
  fuelType: string;
  fuelName: string;
  liters: number;
  quantity: number;
  price: number;
  status?: string;
}

interface PurchaseResponse {
  id: number;
  sessionId: string;
  packageId: string;
  stationName: string;
  fuelName: string;
  liters: number;
  quantity: number;
  price: number;
  qrCodeId: number | null;
  status: string;
  monobankInvoiceId?: string | null;
  monobankStatus?: string | null;
  createdAt: string;
  qrCode?: {
    id: number;
    qrCodeUrl: string;
    qrCodeData?: string;
    stationId: string;
    fuelType: string;
    fuelName?: string; // Add fuelName if needed
    liters: number;
    status: string;
  };
}

export interface InventoryItem {
  provider: string;
  fuelType: string;
  liters: number;
  availableCount: number;
}

export interface Voucher {
  id: string;
  provider: string;
  fuelType: string;
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
  liters: number;
  quantity: number;
  price: number;
  status: "PENDING_FULFILLMENT" | "FULFILLED" | "REFUNDED";
  createdAt: string;
  fulfilledAt: string | null;
}

export interface SyncResponse {
  orders: Order[];
  vouchers: Voucher[];
  serverTimestamp: string;
}

// Helper for fetch with base URL
export async function apiFetch(endpoint: string, options: RequestInit = {}) {
  const url = `${BASE_URL}${endpoint}`;
  const response = await fetch(url, {
    credentials: "include", // Essential for cross-domain cookie persistence on mobile
    ...options,
  });
  return response;
}

export async function apiRequest(method: string, endpoint: string, data?: any) {
  const response = await apiFetch(endpoint, {
    method,
    headers: {
      "Content-Type": "application/json",
    },
    credentials: "include",
    body: data ? JSON.stringify(data) : undefined,
  });
  return response;
}

export async function getInventory(): Promise<InventoryItem[]> {
  const response = await apiFetch("/api/inventory");
  if (!response.ok) throw new Error("Failed to fetch inventory");
  return response.json();
}

export async function getStations(): Promise<Station[]> {
  const response = await apiFetch("/api/stations");
  if (!response.ok) throw new Error("Failed to fetch stations");
  return response.json();
}

export async function getStationNodes(): Promise<StationNode[]> {
  const response = await apiFetch("/api/station-nodes");
  if (!response.ok) throw new Error("Failed to fetch station nodes");
  return response.json();
}

export async function getFuelTypes(): Promise<FuelType[]> {
  const response = await apiFetch("/api/admin/fuel-types"); // Admin endpoint but public in current routes
  if (!response.ok) throw new Error("Failed to fetch fuel types");
  return response.json();
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

export async function getMyVouchers(): Promise<Voucher[]> {
  const response = await apiFetch("/api/vouchers/my", {
    credentials: "include",
  });
  if (!response.ok) {
    if (response.status === 401) return []; // Return empty if not logged in
    throw new Error("Failed to fetch user vouchers");
  }
  return response.json();
}

export async function getMyOrders(): Promise<Order[]> {
  const response = await apiFetch("/api/sync/orders", {
    credentials: "include",
  });
  if (!response.ok) {
    if (response.status === 401) return [];
    throw new Error("Failed to fetch orders");
  }
  return response.json();
}

export async function syncData(since?: string): Promise<SyncResponse> {
  const url = since
    ? `/api/sync?since=${encodeURIComponent(since)}`
    : "/api/sync";
  const response = await apiFetch(url, { credentials: "include" });
  if (!response.ok) {
    throw new Error("Failed to sync data");
  }
  return response.json();
}

export async function markVoucherAsUsed(
  voucherId: string,
): Promise<{ message: string; status: string }> {
  const response = await apiFetch(`/api/vouchers/${voucherId}/mark-used`, {
    method: "PATCH",
    credentials: "include",
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error("Unauthorized");
    if (response.status === 404) throw new Error("Voucher not found");
    throw new Error("Failed to mark voucher as used");
  }
  return response.json();
}

export async function restoreVoucher(
  voucherId: string,
): Promise<{ message: string; status: string }> {
  const response = await apiFetch(`/api/vouchers/${voucherId}/restore`, {
    method: "PATCH",
    credentials: "include",
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error("Unauthorized");
    if (response.status === 404) throw new Error("Voucher not found");
    throw new Error("Failed to restore voucher");
  }
  return response.json();
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

export async function getPackages(): Promise<FuelPackage[]> {
  const response = await apiFetch("/api/packages");
  if (!response.ok) throw new Error("Failed to fetch packages");
  return response.json();
}

export async function createMonobankInvoice(
  data: PurchaseData,
): Promise<{ purchaseId: number; invoiceId: string; pageUrl: string }> {
  const response = await apiFetch("/api/monobank/create-invoice", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ ...data, source: "mobile" }),
  });
  if (!response.ok) {
    if (response.status === 401) {
      throw new Error("401: Unauthorized - Please log in first");
    }
    const errorText = await response.text();
    let errorMsg = "Failed to create Monobank invoice";
    try {
      const errorJson = JSON.parse(errorText);
      errorMsg = errorJson.error?.message || errorJson.error || errorMsg;
    } catch (e) {
      errorMsg = errorText || errorMsg;
    }
    throw new Error(errorMsg);
  }
  return response.json();
}


export async function simulatePayment(
  purchaseId: number,
  scenario: "success" | "failure" = "success",
): Promise<{ status: string; purchase?: PurchaseResponse }> {
  const response = await apiFetch("/api/purchases/simulate", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify({ purchaseId, scenario }),
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error?.message || error.error || "Payment simulation failed");
  }
  return response.json();
}

export async function getMyPurchases(): Promise<PurchaseResponse[]> {
  const response = await apiFetch("/api/purchases/my", {
    credentials: "include",
  });
  if (!response.ok) {
    if (response.status === 401) {
      throw new Error("401: Unauthorized");
    }
    throw new Error("Failed to fetch purchases");
  }
  return response.json();
}



export async function createQrCode(data: {
  stationId: string;
  fuelType: string;
  liters: number;
  qrCodeUrl: string;
}) {
  const response = await apiFetch("/api/qr-codes", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  if (!response.ok) throw new Error("Failed to create QR code");
  return response.json();
}

export async function bulkCreateQrCodes(
  qrCodes: Array<{
    stationId: string;
    fuelType: string;
    liters: number;
    qrCodeUrl: string;
  }>,
) {
  const response = await apiFetch("/api/qr-codes/bulk", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ qrCodes }),
  });
  if (!response.ok) throw new Error("Failed to bulk create QR codes");
  return response.json();
}


