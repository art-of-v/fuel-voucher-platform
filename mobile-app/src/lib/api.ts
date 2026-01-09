
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
}

export async function getInventory(): Promise<InventoryItem[]> {
  const response = await fetch('/api/inventory');
  if (!response.ok) throw new Error('Failed to fetch inventory');
  return response.json();
}

export async function getMyVouchers(): Promise<Voucher[]> {
  const response = await fetch('/api/vouchers/my', { credentials: 'include' });
  if (!response.ok) {
    if (response.status === 401) return []; // Return empty if not logged in
    throw new Error('Failed to fetch user vouchers');
  }
  return response.json();
}

export async function markVoucherAsUsed(voucherId: string): Promise<{ message: string; status: string }> {
  const response = await fetch(`/api/vouchers/${voucherId}/mark-used`, {
    method: 'PATCH',
    credentials: 'include',
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error('Unauthorized');
    if (response.status === 404) throw new Error('Voucher not found');
    throw new Error('Failed to mark voucher as used');
  }
  return response.json();
}

export async function restoreVoucher(voucherId: string): Promise<{ message: string; status: string }> {
  const response = await fetch(`/api/vouchers/${voucherId}/restore`, {
    method: 'PATCH',
    credentials: 'include',
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error('Unauthorized');
    if (response.status === 404) throw new Error('Voucher not found');
    throw new Error('Failed to restore voucher');
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
  const response = await fetch('/api/packages');
  if (!response.ok) throw new Error('Failed to fetch packages');
  return response.json();
}

export async function createPurchase(data: PurchaseData): Promise<PurchaseResponse> {
  const response = await fetch('/api/purchases', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(data),
  });
  if (!response.ok) {
    if (response.status === 401) {
      throw new Error('401: Unauthorized - Please log in first');
    }
    throw new Error('Failed to create purchase');
  }
  return response.json();
}

export async function completePurchase(purchaseId: number): Promise<PurchaseResponse> {
  const response = await fetch(`/api/purchases/${purchaseId}/complete`, {
    method: 'POST',
  });
  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || 'Failed to complete purchase');
  }
  return response.json();
}

export async function simulatePayment(purchaseId: number, scenario: 'success' | 'failure' = 'success'): Promise<{ status: string; purchase?: PurchaseResponse }> {
  const response = await fetch('/api/payments/simulate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ purchaseId, scenario }),
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || 'Payment simulation failed');
  }
  return response.json();
}


export async function getMyPurchases(): Promise<PurchaseResponse[]> {
  const response = await fetch('/api/purchases/my', { credentials: 'include' });
  if (!response.ok) {
    if (response.status === 401) {
      throw new Error('401: Unauthorized');
    }
    throw new Error('Failed to fetch purchases');
  }
  return response.json();
}

export async function getPurchasesBySession(sessionId: string): Promise<PurchaseResponse[]> {
  const response = await fetch(`/api/purchases/session/${sessionId}`);
  if (!response.ok) throw new Error('Failed to fetch purchases');
  return response.json();
}

export async function createQrCode(data: {
  stationId: string;
  fuelType: string;
  liters: number;
  qrCodeUrl: string;
}) {
  const response = await fetch('/api/qr-codes', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  });
  if (!response.ok) throw new Error('Failed to create QR code');
  return response.json();
}

export async function bulkCreateQrCodes(qrCodes: Array<{
  stationId: string;
  fuelType: string;
  liters: number;
  qrCodeUrl: string;
}>) {
  const response = await fetch('/api/qr-codes/bulk', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ qrCodes }),
  });
  if (!response.ok) throw new Error('Failed to bulk create QR codes');
  return response.json();
}

// Helper to get or create session ID
export function getSessionId(): string {
  let sessionId = localStorage.getItem('fuel-session-id');
  if (!sessionId) {
    sessionId = 'session-' + Math.random().toString(36).substring(2, 15);
    localStorage.setItem('fuel-session-id', sessionId);
  }
  return sessionId;
}
