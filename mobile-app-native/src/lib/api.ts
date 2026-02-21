import { Platform } from "react-native";
import Constants from "expo-constants";

// Determine Base URL
// 1. If VITE_API_URL is set in .env (via expo-constants), use it.
// 2. If Android Emulator: 10.0.2.2 is the host loopback.
// 3. If iOS Simulator or Web: localhost.
// 4. If Physical Device: You must set EXPO_PUBLIC_API_URL in .env to your PC IP (e.g. 192.168.0.103)
// const ENV_API_URL = process.env.EXPO_PUBLIC_API_URL;
const ENV_API_URL = process.env.EXPO_PUBLIC_API_URL || "https://fuel-flow-admin-panel-bac.onrender.com";
const LOCALHOST = Platform.OS === "android" ? "10.0.2.2" : "localhost";
export const BASE_URL =
  Platform.OS === "web"
    ? "" // Absolute on web causes CORS issues with Nginx proxy, use relative
    : ENV_API_URL || `http://${LOCALHOST}:4000`;

// const BASE_URL = "https://fuel-flow-admin-panel-bac.onrender.com";
console.log("API Base URL:", BASE_URL);

// --- Rest of interfaces (Purchases, Vouchers, etc.) ---

interface PurchaseData {
  packageId: string;
  stationId: string;
  stationName: string;
  fuelType: string;
  fuelName: string;
  liters: number;
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
  price: number;
  qrCodeId: number | null;
  status: string;
  stripeSessionId: string | null;
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

export async function createPurchase(
  data: PurchaseData,
): Promise<PurchaseResponse> {
  const response = await apiFetch("/api/purchases", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "include",
    body: JSON.stringify(data),
  });
  if (!response.ok) {
    if (response.status === 401) {
      throw new Error("401: Unauthorized - Please log in first");
    }
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error?.message || error.error || "Failed to create purchase");
  }
  return response.json();
}

export async function completePurchase(
  purchaseId: number,
): Promise<PurchaseResponse> {
  const response = await apiFetch(`/api/purchases/${purchaseId}/complete`, {
    method: "POST",
  });
  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || "Failed to complete purchase");
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

export async function getPurchasesBySession(
  sessionId: string,
): Promise<PurchaseResponse[]> {
  const response = await apiFetch(`/api/purchases/session/${sessionId}`);
  if (!response.ok) throw new Error("Failed to fetch purchases");
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

// Helper to get or create session ID
// Note: In React Native, localStorage is not available.
// We should use AsyncStorage, but for this first port pass, we'll use a simple in-memory fallback or no-op.
// TODO: Replace with AsyncStorage
let memorySessionId = "";

export function getSessionId(): string {
  if (memorySessionId) return memorySessionId;
  memorySessionId = "session-" + Math.random().toString(36).substring(2, 15);
  return memorySessionId;
}
