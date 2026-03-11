import { Platform } from "react-native";
import Constants from "expo-constants";
import { SecurityService } from "./security.service";

const DEFAULT_API_URL = Platform.OS === "android" ? "http://10.0.2.2:4000" : "http://localhost:4000";
export const BASE_URL =
  process.env.EXPO_PUBLIC_API_URL ||
  Constants.expoConfig?.extra?.apiUrl ||
  DEFAULT_API_URL;

// --- Interfaces ---

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

export interface FuelPackage {
  id: string;
  stationId: string;
  fuelTypeId: string;
  fuelName: string;
  liters: number;
  price: number;
  originalPrice: number;
}

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
}

// --- API Client ---

export async function apiFetch(endpoint: string, options: RequestInit = {}) {
  const url = `${BASE_URL}${endpoint}`;
  const method = (options.method || "GET").toUpperCase();
  const timestamp = Date.now().toString();
  const deviceId = await SecurityService.getDeviceId();
 
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "x-device-id": deviceId,
    "x-timestamp": timestamp,
    ...(options.headers as Record<string, string>),
  };
 
  const bodyString = options.body ? (typeof options.body === 'string' ? options.body : JSON.stringify(options.body)) : '';
  const payloadToSign = `${method}${endpoint}${bodyString}${timestamp}`;
 
  // Public/Bootstrap endpoints don't need a signature yet (as the key isn't born)
  const isPublic = 
    endpoint.includes("/api/auth/phone/send-code") || 
    endpoint.includes("/api/auth/phone/verify") ||
    endpoint.includes("/api/auth/device/register");
 
  if (!isPublic) {
    // For app start /user/me, only prompt if we HAVE a key locally (registered user)
    // If no key exists, it's a new device - just send unsigned and let server fail gracefully
    const shouldSign = (endpoint !== "/api/auth/user/me") || (await SecurityService.hasKeys());
 
    if (shouldSign) {
      try {
        // Pure Biometric Signature — EVERY functional request prompts Face ID
        const signature = await SecurityService.signPayload(payloadToSign);
        headers["x-signature"] = signature;
      } catch (error) {
        console.error("Security/Signing error:", error);
        throw new Error("Біометрична перевірка не вдалася. Спробуйте ще раз.");
      }
    }
  }
 
  const response = await fetch(url, {
    ...options,
    headers,
    credentials: "include",
  });
 
  if (response.status === 401) {
    // Silence 401 logs as they are handled by logic/redirection
    // console.warn("Unauthorized:", endpoint);
  }

  return response;
}

export async function apiRequest(method: string, endpoint: string, data?: any) {
  return apiFetch(endpoint, {
    method,
    body: data ? JSON.stringify(data) : undefined,
  });
}

// --- Public Endpoints ---

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
  const response = await apiFetch("/api/admin/fuel-types");
  if (!response.ok) throw new Error("Failed to fetch fuel types");
  return response.json();
}

export async function getMyVouchers(): Promise<Voucher[]> {
  const response = await apiFetch("/api/vouchers/my");
  if (!response.ok) {
    if (response.status === 401) return [];
    throw new Error("Failed to fetch user vouchers");
  }
  return response.json();
}

export async function getMyOrders(): Promise<Order[]> {
  const response = await apiFetch("/api/sync/orders");
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
  const response = await apiFetch(url);
  if (!response.ok) {
    throw new Error("Failed to sync data");
  }
  return response.json();
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
    body: JSON.stringify({ ...data, source: "mobile" }),
  });
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || "Failed to create invoice");
  }
  return response.json();
}

export async function getMyPurchases(): Promise<PurchaseResponse[]> {
  const response = await apiFetch("/api/purchases/my");
  if (!response.ok) throw new Error("Failed to fetch purchases");
  return response.json();
}

export async function markVoucherAsUsed(
  voucherId: string,
): Promise<{ message: string; status: string }> {
  const response = await apiFetch(`/api/vouchers/${voucherId}/mark-used`, {
    method: "PATCH",
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
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error("Unauthorized");
    if (response.status === 404) throw new Error("Voucher not found");
    throw new Error("Failed to restore voucher");
  }
  return response.json();
}

export async function simulatePayment(
  purchaseId: number,
  scenario: "success" | "failure" = "success",
): Promise<{ status: string; purchase?: PurchaseResponse }> {
  const response = await apiFetch("/api/purchases/simulate", {
    method: "POST",
    body: JSON.stringify({ purchaseId, scenario }),
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error?.message || error.error || "Payment simulation failed");
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
    body: JSON.stringify({ qrCodes }),
  });
  if (!response.ok) throw new Error("Failed to bulk create QR codes");
  return response.json();
}

export async function logout(): Promise<void> {
  try {
    // 1. Notify server (requires Face ID confirmation to prove it's the user)
    await apiFetch("/api/auth/device/logout", {
      method: "POST"
    });
  } catch (e) {
    console.warn("Logout server notify failed:", e);
  }
 
  // 2. Wipe the hardware keys - The Safe is cleared!
  await SecurityService.revokeSecurity();
}
