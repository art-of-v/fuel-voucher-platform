
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
    liters: number;
    status: string;
  };
}

export async function getPackages() {
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
