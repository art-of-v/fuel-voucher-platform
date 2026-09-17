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

interface AccountInactiveError extends Error {
  code: 'account_inactive';
}

function handleAccountInactiveError(response: Response, errorText: string): AccountInactiveError | null {
  if (response.status === 403) {
    try {
      const body = JSON.parse(errorText);
      if (body.code === 'account_inactive') {
        const error = new Error(body.message || 'Account is not activated. Please contact an administrator to activate your account.') as AccountInactiveError;
        error.code = 'account_inactive';
        return error;
      }
    } catch {
      // If parsing fails, check if it's in the response text
      if (errorText.includes('account_inactive')) {
        const error = new Error('Account is not activated. Please contact an administrator to activate your account.') as AccountInactiveError;
        error.code = 'account_inactive';
        return error;
      }
    }
  }
  return null;
}

export async function createMonobankInvoice(
  data: PurchaseData,
  legalEntityId?: string | null,
): Promise<{ purchaseId: number; invoiceId: string; pageUrl: string }> {
  const response = await apiFetch('/api/purchases', {
    method: 'POST',
      body: JSON.stringify({
        provider: 'MONOBANK',
        packageId: data.packageId,
        fuelTypeId: data.fuelType,
        liters: data.liters,
        quantity: data.quantity,
        price: data.price,
        stationId: data.stationId,
        stationName: data.stationName,
        ...(legalEntityId ? { legalEntityId } : {}),
      }),
  });
  const errorText = await response.text();
  if (!response.ok) {
    const accountInactiveError = handleAccountInactiveError(response, errorText);
    if (accountInactiveError) throw accountInactiveError;
    throw new Error(errorText || 'Failed to create invoice');
  }
  const result = JSON.parse(errorText);
  return {
    purchaseId: result.orderId,
    invoiceId: result.monobankInvoiceId ?? '',
    pageUrl: result.paymentUrl ?? '',
  };
}

export async function createBulkMonobankInvoice(
  items: PurchaseData[],
  legalEntityId?: string | null,
): Promise<{ orderIds: string[]; invoiceId: string; pageUrl: string }> {
  const response = await apiFetch('/api/purchases/bulk', {
    method: 'POST',
      body: JSON.stringify({
        // legalEntityId is a command-level field for bulk checkout (applies to
        // every item), not per-item.
        ...(legalEntityId ? { legalEntityId } : {}),
        items: items.map((data) => ({
          provider: 'MONOBANK',
          packageId: data.packageId,
          fuelTypeId: data.fuelType,
        liters: data.liters,
        quantity: data.quantity,
        price: data.price,
        stationId: data.stationId,
        stationName: data.stationName,
      })),
    }),
  });
  const errorText = await response.text();
  if (!response.ok) {
    const accountInactiveError = handleAccountInactiveError(response, errorText);
    if (accountInactiveError) throw accountInactiveError;
    throw new Error(errorText || 'Failed to create bulk invoice');
  }
  const result = JSON.parse(errorText);
  return {
    orderIds: result.orderIds ?? [],
    invoiceId: result.monobankInvoiceId ?? '',
    pageUrl: result.paymentUrl ?? '',
  };
}
