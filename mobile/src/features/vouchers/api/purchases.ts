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

// User-facing fallback shown when the server or an intermediary returns a body
// we can't turn into a specific message — e.g. an HTML "Bad Request"/"Invalid
// Hostname" page injected by an intercepting proxy on the device's network
// (planning #32). We must never dump that raw body into the checkout Alert.
const GENERIC_CHECKOUT_ERROR =
  'Не вдалося створити платіж. Перевірте підключення до інтернету та спробуйте ще раз.';

// Turns a failed purchase response into a clean Error. A JSON body carrying a
// message/detail/title is our API's own error and is trusted verbatim; any
// non-JSON body (HTML, plain text, empty) is suppressed in favour of the
// generic message, with the raw body logged for diagnostics only.
function buildPurchaseError(response: Response, bodyText: string): Error {
  const trimmed = bodyText.trim();
  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    try {
      const body = JSON.parse(trimmed);
      const message = body?.message ?? body?.detail ?? body?.title;
      if (typeof message === 'string' && message.trim()) {
        return new Error(message.trim());
      }
    } catch {
      // Not JSON after all — fall through to the generic message.
    }
  }
  console.warn(
    `[purchases] request failed with status ${response.status}; suppressed non-JSON body (${trimmed.length} chars)`,
  );
  return new Error(GENERIC_CHECKOUT_ERROR);
}

// Parses a successful (2xx) response body, guarding against a non-JSON success
// (e.g. a proxy returning an HTML page with a 200) so a raw SyntaxError with the
// HTML in it never reaches the UI.
function parseSuccessBody(response: Response, bodyText: string) {
  try {
    return JSON.parse(bodyText);
  } catch {
    throw buildPurchaseError(response, bodyText);
  }
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
  const bodyText = await response.text();
  if (!response.ok) {
    const accountInactiveError = handleAccountInactiveError(response, bodyText);
    if (accountInactiveError) throw accountInactiveError;
    throw buildPurchaseError(response, bodyText);
  }
  const result = parseSuccessBody(response, bodyText);
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
  const bodyText = await response.text();
  if (!response.ok) {
    const accountInactiveError = handleAccountInactiveError(response, bodyText);
    if (accountInactiveError) throw accountInactiveError;
    throw buildPurchaseError(response, bodyText);
  }
  const result = parseSuccessBody(response, bodyText);
  return {
    orderIds: result.orderIds ?? [],
    invoiceId: result.monobankInvoiceId ?? '',
    pageUrl: result.paymentUrl ?? '',
  };
}
