import { apiFetch } from '../../../core/api/apiClient';

interface PurchaseData {
  packageId: string;
  stationId: string;
  stationName: string;
  fuelType: string;
  fuelName: string;
  liters: number;
  quantity: number;
  price: number;
}

export async function createMonobankInvoice(
  data: PurchaseData,
): Promise<{ purchaseId: number; invoiceId: string; pageUrl: string }> {
  const response = await apiFetch('/api/purchases', {
    method: 'POST',
    body: JSON.stringify({
      provider: 'MONOBANK',
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
    throw new Error(errorText || 'Failed to create invoice');
  }
  const result = await response.json();
  return {
    purchaseId: result.orderId,
    invoiceId: result.monobankInvoiceId ?? '',
    pageUrl: result.paymentUrl ?? '',
  };
}
