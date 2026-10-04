import { apiFetch } from '../../../../core/api/apiClient';

/**
 * Client for the voucher renewal/replacement endpoints (planning #80, slice 4).
 *
 *  - GET  /api/purchases/renew/config  — feature gate + tier ladder, fetched once
 *                                        to decide whether to offer "renew" at all.
 *  - POST /api/purchases/renew/quote   — read-only per-voucher price + availability
 *                                        preview; drives the term picker so an
 *                                        unbuyable tier is disabled BEFORE payment.
 *  - POST /api/purchases/renew         — creates ONE Monobank invoice for the batch.
 *
 * config/quote are reads (no device signature — see apiClient). The checkout is a
 * money endpoint and IS device-signed, like the rest of /api/purchases.
 */

/** Stable tier codes returned by the backend (VoucherRenewalTerms.Code). */
export type RenewalTermCode = '1w' | '2w' | '1m' | '2m' | '3m' | '4m' | '5m' | '6m';

export interface RenewalTierInfo {
  term: string;
  ratePerLiterUah: number;
  /** Manager enabled the tier AND set a positive rate (ignores stock). */
  offerable: boolean;
}

export interface RenewalConfig {
  enabled: boolean;
  thresholdDays: number;
  tiers: RenewalTierInfo[];
}

export interface RenewalTermQuote {
  term: string;
  priceUah: number;
  available: boolean;
  /** 'not_offerable' | 'no_stock' when available is false; null otherwise. */
  unavailableReason?: string | null;
}

export interface RenewalVoucherQuote {
  voucherId: string;
  eligible: boolean;
  /** 'not_your_voucher' | 'not_renewable' when eligible is false. */
  ineligibleReason?: string | null;
  /** 'extend' | 'replace' when eligible. */
  branch?: string | null;
  provider: string;
  fuelTypeId: string;
  fuelName: string;
  liters: number;
  expirationDate: string;
  terms: RenewalTermQuote[];
}

export interface RenewalQuote {
  enabled: boolean;
  thresholdDays: number;
  vouchers: RenewalVoucherQuote[];
}

export interface RenewalCheckoutResult {
  orderId: string;
  monobankInvoiceId: string;
  paymentUrl: string;
  totalUah: number;
}

/** A failed renewal request carrying the backend's stable {code} where present. */
export class RenewalApiError extends Error {
  status: number;
  code?: string;
  constructor(message: string, status: number, code?: string) {
    super(message);
    this.name = 'RenewalApiError';
    this.status = status;
    this.code = code;
  }
}

// Never surface a raw HTML/proxy body in an Alert (planning #32). A JSON body
// carrying {code,message} is our API's own error; anything else collapses to a
// generic key the UI localises.
async function buildError(response: Response): Promise<RenewalApiError> {
  const bodyText = await response.text();
  const trimmed = bodyText.trim();
  if (trimmed.startsWith('{')) {
    try {
      const body = JSON.parse(trimmed);
      const message = typeof body?.message === 'string' ? body.message : '';
      const code = typeof body?.code === 'string' ? body.code : undefined;
      return new RenewalApiError(message, response.status, code);
    } catch {
      // fall through
    }
  }
  console.warn(
    `[renewal] request failed with status ${response.status}; suppressed non-JSON body (${trimmed.length} chars)`,
  );
  return new RenewalApiError('', response.status);
}

export async function getRenewalConfig(): Promise<RenewalConfig> {
  const response = await apiFetch('/api/purchases/renew/config');
  if (!response.ok) throw await buildError(response);
  const data = await response.json();
  return {
    enabled: !!data.enabled,
    thresholdDays: data.thresholdDays ?? 0,
    tiers: Array.isArray(data.tiers) ? data.tiers : [],
  };
}

export async function quoteRenewal(voucherIds: string[]): Promise<RenewalQuote> {
  const response = await apiFetch('/api/purchases/renew/quote', {
    method: 'POST',
    body: JSON.stringify({ voucherIds }),
  });
  if (!response.ok) throw await buildError(response);
  const data = await response.json();
  return {
    enabled: !!data.enabled,
    thresholdDays: data.thresholdDays ?? 0,
    vouchers: Array.isArray(data.vouchers) ? data.vouchers : [],
  };
}

export async function createRenewalCheckout(
  items: { voucherId: string; termCode: string }[],
): Promise<RenewalCheckoutResult> {
  const response = await apiFetch('/api/purchases/renew', {
    method: 'POST',
    body: JSON.stringify({ items }),
  });
  if (!response.ok) throw await buildError(response);
  const data = await response.json();
  return {
    orderId: data.orderId,
    monobankInvoiceId: data.monobankInvoiceId ?? '',
    paymentUrl: data.paymentUrl ?? '',
    totalUah: data.totalUah ?? 0,
  };
}

/**
 * Maps a backend {code} to a flat i18n key. Falls back to a generic message so
 * an unknown code never dead-ends. Covers both quote reasons and checkout codes.
 */
export function renewalErrorKey(code?: string): string {
  switch (code) {
    case 'account_inactive':
      return 'renew.error.accountInactive';
    case 'renewal_disabled':
      return 'renew.error.disabled';
    case 'not_your_voucher':
      return 'renew.error.notYours';
    case 'not_renewable':
      return 'renew.error.notRenewable';
    case 'tier_unavailable':
    case 'no_stock':
      return 'renew.error.unavailable';
    // The supplier's voucher does not have enough life left for this term, so we cannot
    // extend into it. Distinct from "temporarily unavailable": no restock fixes this one.
    case 'provider_term_exhausted':
      return 'renew.error.providerTermExhausted';
    case 'unknown_term':
      return 'renew.error.unknownTerm';
    case 'too_many_items':
      return 'renew.error.tooMany';
    case 'duplicate_voucher':
      return 'renew.error.duplicate';
    case 'empty_batch':
      return 'renew.error.emptyBatch';
    default:
      return 'renew.error.generic';
  }
}
