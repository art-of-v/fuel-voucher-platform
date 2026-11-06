import { apiFetch } from '../../../core/api/apiClient';

interface PurchaseData {
  packageId: string;
  /**
   * The fuel network, as a Brand id — `CheckoutItem.BrandId` on the server. It was `stationId`
   * until the 2026-10-18 rename, and `POST /api/purchases/bulk` answers 400
   * ("BrandId is required for every item") for any body that does not carry it.
   */
  brandId: string;
  brandName: string;
  fuelType: string;
  fuelName: string;
  liters: number;
  quantity: number;
  price: number;
  /** Validity term bought for; undefined = the voucher's full remaining term. */
  termCode?: string;
}

/**
 * Which of the two checkout buttons a tap came from, in the server's own words.
 *
 * `Declared` is the promotional one: the order is written, the bank is never asked, and the money that
 * pays for it is money a person on our side states arrived (planning #889). Sending it is a request and
 * not an entitlement — only an account a Manager has enrolled may ask, and the answer to any other
 * request is the refusal `promo_access_not_granted`.
 */
export type CheckoutSettlementRoute = 'Acquirer' | 'Declared';

interface AccountInactiveError extends Error {
  code: 'account_inactive';
}

function handleAccountInactiveError(
  response: Response,
  errorText: string,
): AccountInactiveError | null {
  if (response.status === 403) {
    try {
      const body = JSON.parse(errorText);
      if (body.code === 'account_inactive') {
        const error = new Error(
          body.detail ||
            'Account is not activated. Please contact an administrator to activate your account.',
        ) as AccountInactiveError;
        error.code = 'account_inactive';
        return error;
      }
    } catch {
      // If parsing fails, check if it's in the response text
      if (errorText.includes('account_inactive')) {
        const error = new Error(
          'Account is not activated. Please contact an administrator to activate your account.',
        ) as AccountInactiveError;
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
// detail/title is our API's own error and is trusted verbatim; any
// non-JSON body (HTML, plain text, empty) is suppressed in favour of the
// generic message, with the raw body logged for diagnostics only.
//
// A stable `code` is carried on the error when the server sent one, so a screen can render its own
// wording. The renewal side already does this (`renewalErrorKey`); the purchase side had none, so a
// rejection like `below_cost` reached the customer as the server's English sentence.
function buildPurchaseError(response: Response, bodyText: string): Error {
  const trimmed = bodyText.trim();
  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    try {
      const body = JSON.parse(trimmed);
      const code = typeof body?.code === 'string' && body.code ? body.code : undefined;
      const message = body?.detail ?? body?.title;
      if (typeof message === 'string' && message.trim()) {
        return code
          ? (Object.assign(new Error(message.trim()), { code }) as Error & { code: string })
          : new Error(message.trim());
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

// Maps a purchase-side {code} to a flat i18n key, so the customer reads their own language instead of
// the server's English. Falls back to the generic message for a code we have not seen, and the caller
// keeps the server's message when there is no code at all.
export function purchaseErrorKey(code?: string): string | null {
  switch (code) {
    // Our reason is a margin decision — this fuel is priced below what we pay — which the customer
    // can do nothing about. They are told only what they can act on.
    case 'below_cost':
      return 'purchase.error.belowCost';
    // The shelf emptied between opening the shop and pressing pay. Not our fault and not the
    // customer's either; it is the one refusal that resolves on its own.
    //
    // Deliberately NOT the same key as `below_cost`: they look identical to the customer and mean
    // different things to the operator, and one of them ("wait for stock") is actionable now.
    case 'package_out_of_stock':
      return 'purchase.error.outOfStock';
    // The acquirer would not open a payment at all - a terminal the bank switched off, a merchant
    // account out of order. Nothing was charged, the customer did nothing wrong, and the next tap
    // may work, which is the whole sentence they need. The bank's own words stay server-side: they
    // name our contract, not anything a customer can act on (planning #873).
    case 'payment_provider_unavailable':
      return 'purchase.error.providerUnavailable';
    // The basket is held by an order that may already have money moving on it, so the other button is
    // refused rather than quietly taking the basket over. The customer has one action: the earlier
    // order, which is waiting in the wallet's processing list — so the sentence points there instead
    // of saying "try again", which would fail the same way.
    case 'checkout_in_payment':
      return 'purchase.error.checkoutInPayment';
    // The offer was closed for this account between the button appearing and the tap. Says who decides,
    // because the customer cannot act on it and the only way forward is to ask us (planning #889).
    case 'promo_access_not_granted':
      return 'purchase.error.promoAccess';
    default:
      return null;
  }
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
): Promise<{
  purchaseId: number;
  invoiceId: string;
  pageUrl: string;
  appUrl?: string;
  alreadyPaid: boolean;
}> {
  const response = await apiFetch('/api/purchases', {
    method: 'POST',
    body: JSON.stringify({
      brand: 'MONOBANK',
      packageId: data.packageId,
      fuelTypeId: data.fuelType,
      liters: data.liters,
      quantity: data.quantity,
      price: data.price,
      brandId: data.brandId,
      brandName: data.brandName,
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
    // Only present when the backend asked Monobank for `withAppUrl`. It is NOT persisted on the
    // order, so an order re-opened from the wallet has none - callers must tolerate undefined.
    appUrl: result.appUrl ?? undefined,
    // Trigger 2 (planning #867): the money for this order was already declared and held, so the
    // checkout settled it and asked the bank for nothing. Read as a flag, never inferred from a
    // missing `pageUrl` — the two look identical in the body and mean the opposite.
    alreadyPaid: result.alreadyPaid === true,
  };
}

export async function createBulkMonobankInvoice(
  items: PurchaseData[],
  legalEntityId?: string | null,
  settlementRoute?: CheckoutSettlementRoute,
): Promise<{
  orderIds: string[];
  invoiceId: string;
  pageUrl: string;
  appUrl?: string;
  alreadyPaid: boolean;
  settlementRoute?: CheckoutSettlementRoute;
}> {
  const response = await apiFetch('/api/purchases/bulk', {
    method: 'POST',
    body: JSON.stringify({
      // legalEntityId is a command-level field for bulk checkout (applies to
      // every item), not per-item.
      ...(legalEntityId ? { legalEntityId } : {}),
      // Omitted for the pay button, which is what an absent field has always meant: the bank. Sent as
      // `Declared` by the promotional one, and only ever by that button — the server refuses it for an
      // account nobody enrolled, whatever the app's copy of the flag says.
      ...(settlementRoute ? { settlementRoute } : {}),
      items: items.map((data) => ({
        brand: 'MONOBANK',
        packageId: data.packageId,
        fuelTypeId: data.fuelType,
        liters: data.liters,
        quantity: data.quantity,
        price: data.price,
        brandId: data.brandId,
        brandName: data.brandName,
        // Undefined means the voucher's full remaining term. The server recomputes the price from its
        // own catalog, so this only chooses WHICH term, never how much it costs.
        ...(data.termCode ? { termCode: data.termCode } : {}),
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
    // Only present when the backend asked Monobank for `withAppUrl`. It is NOT persisted on the
    // order, so an order re-opened from the wallet has none - callers must tolerate undefined.
    appUrl: result.appUrl ?? undefined,
    // Trigger 2 (planning #867): money the platform was already holding for this customer paid for
    // the whole cart, so no invoice was asked for and there is no page to open. A flag, not an
    // inference from the empty `pageUrl` above: an older bundle reads that as a payment failure.
    alreadyPaid: result.alreadyPaid === true,
    // Which of the two routes the order was written on. It is read, not guessed, because an answer with
    // no payment URL means two opposite things: a promotional order that waits for the settlement
    // channel, or a bank payment that failed to start. Absent on a server older than the second button,
    // where the bank was the only route there was and an empty URL was only ever a failure.
    settlementRoute:
      result.settlementRoute === 'Declared' || result.settlementRoute === 'Acquirer'
        ? (result.settlementRoute as CheckoutSettlementRoute)
        : undefined,
  };
}
