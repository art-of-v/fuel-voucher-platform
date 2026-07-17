import { Platform } from "react-native";
import Constants from "expo-constants";
import { SecurityService } from "./security.service";
import { TokenStorage } from "./token.storage";

let isRefreshing = false;
let pendingRefreshPromise: Promise<boolean> | null = null;

const DEFAULT_API_URL = Platform.OS === "android" ? "http://10.0.2.2:5000" : "http://localhost:5000";
export const BASE_URL =
  process.env.EXPO_PUBLIC_API_URL ||
  Constants.expoConfig?.extra?.apiUrl ||
  DEFAULT_API_URL;

async function tryRefreshToken(): Promise<boolean> {
  if (isRefreshing && pendingRefreshPromise) {
    return pendingRefreshPromise;
  }

  isRefreshing = true;
  pendingRefreshPromise = (async () => {
    try {
      const refreshToken = await TokenStorage.getRefreshToken();
      if (!refreshToken) return false;

      const response = await fetch(`${BASE_URL}/api/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken }),
      });

      if (!response.ok) {
        await TokenStorage.clearTokens();
        return false;
      }

      const data = await response.json();
      await TokenStorage.saveTokens(data.accessToken, data.refreshToken);
      return true;
    } catch {
      await TokenStorage.clearTokens();
      return false;
    } finally {
      isRefreshing = false;
      pendingRefreshPromise = null;
    }
  })();

  return pendingRefreshPromise;
}

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

  // Attach stored JWT if available and not already overridden
  if (!headers["Authorization"]) {
    const storedToken = await TokenStorage.getAccessToken();
    if (storedToken) {
      headers["Authorization"] = `Bearer ${storedToken}`;
    }
  }

  const bodyString = options.body ? (typeof options.body === 'string' ? options.body : JSON.stringify(options.body)) : '';
  const payloadToSign = `${method}${endpoint}${bodyString}${timestamp}`;
 
  // Public/Bootstrap endpoints don't need a signature yet (as the key isn't born)
  const isPublic = 
    endpoint.includes("/api/auth/send-code") || 
    endpoint.includes("/api/auth/verify") ||
    endpoint.includes("/api/auth/device/register") ||
    endpoint.includes("/api/stations") ||
    endpoint.includes("/api/packages") ||
    endpoint.includes("/api/admin/fuel-types") ||
    endpoint.includes("/api/logs");

  // Biometric signature is only required for transactions and sensitive state changes
  // or explicitly requested via the 'x-force-signature' header.
  const isSensitiveAction = 
    endpoint.includes("/mark-used") || 
    endpoint.includes("/restore") ||
    endpoint.includes("/api/legal-entity/profile") ||
    (endpoint === "/api/purchases" && method === "POST") ||
    endpoint.includes("/purchases/simulate") ||
    headers['x-force-signature'] === 'true';

  // Clean up internal control header before sending
  if (headers['x-force-signature']) {
    delete headers['x-force-signature'];
  }
 
  if (!isPublic && isSensitiveAction) {
    const hasKeys = await SecurityService.hasKeys();
 
    if (hasKeys) {
      try {
        // Pure Biometric Signature — only for transactions/sensitive state changes
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
 
  if (response.status === 401 && !endpoint.includes("/api/auth/refresh")) {
    const refreshed = await tryRefreshToken();
    if (refreshed) {
      const storedToken = await TokenStorage.getAccessToken();
      if (storedToken) {
        headers["Authorization"] = `Bearer ${storedToken}`;
      }
      const retryResponse = await fetch(url, {
        ...options,
        headers,
        credentials: "include",
      });
      if (retryResponse.status === 401) {
        await TokenStorage.clearTokens();
      }
      return retryResponse;
    }
  }

  return response;
}

export async function apiRequest(method: string, endpoint: string, data?: any, extraHeaders?: Record<string, string>) {
  return apiFetch(endpoint, {
    method,
    body: data ? JSON.stringify(data) : undefined,
    headers: extraHeaders,
  });
}

// --- Public Endpoints ---

export async function getInventory(): Promise<InventoryItem[]> {
  const response = await apiFetch("/api/vouchers/inventory");
  if (!response.ok) throw new Error("Failed to fetch inventory");
  const data = await response.json();
  const items = Array.isArray(data) ? data : (data.inventory ?? []);
  return items.map((i: any) => ({
    provider: i.provider,
    fuelType: i.fuelTypeName ?? i.fuelType,
    liters: i.liters,
    availableCount: i.available ?? i.availableCount,
  }));
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
  const data = await response.json();
  const statusMap: Record<string, Order["status"]> = {
    PendingPayment: "PENDING_FULFILLMENT",
    Paid: "PENDING_FULFILLMENT",
    PendingFulfillment: "PENDING_FULFILLMENT",
    PartiallyFulfilled: "PENDING_FULFILLMENT",
    Fulfilled: "FULFILLED",
    Refunded: "REFUNDED",
    Cancelled: "REFUNDED",
  };
  return data.map((o: any) => ({
    ...o,
    status: statusMap[o.status] ?? "PENDING_FULFILLMENT",
    createdAt: o.createdAtUtc ?? o.createdAt,
    fulfilledAt: o.fulfilledAtUtc ?? o.fulfilledAt ?? null,
    fuelType: o.fuelType ?? o.fuelTypeId ?? "",
  }));
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
  const response = await apiFetch("/api/purchases", {
    method: "POST",
    body: JSON.stringify({
      provider: data.provider || "MONOBANK",
      fuelTypeId: data.fuelType,
      liters: data.liters,
      quantity: data.quantity,
      price: data.price,
      stationId: data.stationId,
      stationName: data.stationName,
    }),
  });
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || "Failed to create invoice");
  }
  const result = await response.json();
  return {
    purchaseId: result.orderId,
    invoiceId: result.monobankInvoiceId ?? "",
    pageUrl: result.paymentUrl ?? "",
  };
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

export async function sendAppLog(level: 'info' | 'error' | 'crash', message: string, details?: any): Promise<void> {
    try {
        await apiFetch("/api/logs", {
            method: "POST",
            body: JSON.stringify({
                level,
                message,
                details,
                platform: Platform.OS,
                timestamp: new Date().toISOString()
            })
        });
    } catch (e) {
        console.warn("Failed to send log to server:", e);
    }
}

// --- Legal Entity Endpoints ---

export async function getLegalProfile(): Promise<{ company: Company | null }> {
  const response = await apiFetch("/api/legal-entity/profile");
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    const msg = errorData.error?.message || errorData.error || errorData.message || "Failed to fetch legal profile";
    throw new Error(msg);
  }
  return response.json();
}

export async function updateLegalProfile(data: Partial<Company>): Promise<Company> {
  const response = await apiFetch("/api/legal-entity/profile", {
    method: "POST",
    body: JSON.stringify(data),
  });
  if (!response.ok) throw new Error("Failed to update legal profile");
  return response.json();
}

export async function getAvailableContracts(): Promise<Contract[]> {
  const response = await apiFetch("/api/legal-entity/contracts");
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    const msg = errorData.error?.message || errorData.error || errorData.message || "Failed to fetch contracts";
    throw new Error(msg);
  }
  return response.json();
}

export async function signContracts(contractIds: string[], signatureData: string, stationId?: string): Promise<UserContract[]> {
  const response = await apiFetch("/api/legal-entity/sign", {
    method: "POST",
    body: JSON.stringify({ contractIds, signatureData, stationId }),
    headers: {
      'x-force-signature': 'true' // Require biometric/faceid for signing
    }
  });
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    const msg = errorData.error?.message || errorData.error || errorData.message || "Failed to sign contracts";
    throw new Error(msg);
  }
  return response.json();
}

export async function getSignedContracts(): Promise<UserContract[]> {
  const response = await apiFetch("/api/legal-entity/contracts/signed");
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    const msg = errorData.error?.message || errorData.error || errorData.message || "Failed to fetch signed contracts";
    throw new Error(msg);
  }
  return response.json();
}
