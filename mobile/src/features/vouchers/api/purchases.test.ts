import { createBulkMonobankInvoice, createMonobankInvoice, purchaseErrorKey } from './purchases';
import { apiFetch } from '../../../core/api/apiClient';
import { translations, Language } from '../../../core/i18n/translations';

jest.mock('../../../core/api/apiClient', () => ({
  apiFetch: jest.fn(),
  BASE_URL: 'https://api.test',
}));

/**
 * `t()` of an unknown key returns the key, so a code mapped to a sentence nobody wrote shows up on
 * the customer's screen as `purchase.error.providerUnavailable` - in every language at once. This
 * checks the whole map against the four files, so the mapping cannot outrun the wording.
 */
describe('purchaseErrorKey', () => {
  it('gives the acquirer a refusal to open a payment its own wording', () => {
    // `MonobankInvoiceRejectedException.Code`. The customer cannot act on "this merchant has no
    // active terminal", so they are told the two things they can: nothing was charged, wait.
    expect(purchaseErrorKey('payment_provider_unavailable')).toBe(
      'purchase.error.providerUnavailable',
    );
  });

  it('keeps a below-cost rejection separate from an empty shelf', () => {
    expect(purchaseErrorKey('below_cost')).toBe('purchase.error.belowCost');
    expect(purchaseErrorKey('package_out_of_stock')).toBe('purchase.error.outOfStock');
  });

  it('gives the two checkout-route refusals their own wording', () => {
    // `CheckoutAlreadyInPaymentException.Code` and `PromoAccessException.Code`. Both are produced by
    // the promotional button, and the server's English sentence for either one names a rule the
    // customer cannot act on - which order holds the basket, and who decides the offer.
    expect(purchaseErrorKey('checkout_in_payment')).toBe('purchase.error.checkoutInPayment');
    expect(purchaseErrorKey('promo_access_not_granted')).toBe('purchase.error.promoAccess');
  });

  it('names no sentence that is missing from a language the app offers', () => {
    const codes = [
      'below_cost',
      'package_out_of_stock',
      'payment_provider_unavailable',
      'checkout_in_payment',
      'promo_access_not_granted',
    ];

    const missing = codes.flatMap((code) => {
      const key = purchaseErrorKey(code);
      if (!key) return [`${code} maps to nothing`];

      return (Object.keys(translations) as Language[])
        .filter((lang) => !(key in translations[lang]))
        .map((lang) => `${key} missing from ${lang}`);
    });

    expect(missing).toEqual([]);
  });

  it('says nothing for a code it has not seen, so the server message stands', () => {
    expect(purchaseErrorKey(undefined)).toBeNull();
    expect(purchaseErrorKey('some_future_code')).toBeNull();
  });
});

const mockedApiFetch = apiFetch as jest.MockedFunction<typeof apiFetch>;

const line = {
  packageId: 'pkg-1',
  brandId: 'brand-1',
  brandName: 'OKKO',
  fuelType: 'fuel-1',
  fuelName: 'A-95',
  liters: 10,
  quantity: 1,
  price: 900,
};

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: jest.fn().mockResolvedValue(JSON.stringify(body)),
    json: jest.fn().mockResolvedValue(body),
  } as unknown as Response;
}

/**
 * `alreadyPaid` is the only thing that separates a cart the platform's held money paid for from a
 * checkout that produced no invoice at all — both bodies carry an empty `paymentUrl`. Parsing it as
 * a flag, and defaulting it to false, is what keeps the screen from telling a customer who has paid
 * that the payment failed (planning #867 trigger 2).
 */
describe('the checkout response', () => {
  beforeEach(() => jest.clearAllMocks());

  it('surfaces the flag on the bulk path', async () => {
    mockedApiFetch.mockResolvedValue(jsonResponse({ orderIds: ['o-1'], alreadyPaid: true }));

    const result = await createBulkMonobankInvoice([line], null);

    expect(result.alreadyPaid).toBe(true);
    expect(result.pageUrl).toBe('');
  });

  it('surfaces the flag on the single-item path', async () => {
    mockedApiFetch.mockResolvedValue(jsonResponse({ orderId: 7, alreadyPaid: true }));

    const result = await createMonobankInvoice(line, null);

    expect(result.alreadyPaid).toBe(true);
  });

  it('reads a body without the flag as not paid, so an older server keeps failing loudly', async () => {
    mockedApiFetch.mockResolvedValue(
      jsonResponse({ orderIds: ['o-1'], invoiceId: '', paymentUrl: '' }),
    );

    const result = await createBulkMonobankInvoice([line], null);

    expect(result.alreadyPaid).toBe(false);
  });

  it('does not take a flag said in words for a flag set', async () => {
    // Anything that is not the literal `true` means "not paid": a string, a number, or a field some
    // future server adds for a different reason must not empty the customer's basket.
    mockedApiFetch.mockResolvedValue(jsonResponse({ orderIds: ['o-1'], alreadyPaid: 'true' }));

    const result = await createBulkMonobankInvoice([line], null);

    expect(result.alreadyPaid).toBe(false);
  });

  /**
   * The pay button sends no route at all, which is what an absent field has always meant on the
   * server (planning #889 resolves it to the bank). Only the promotional button puts `Declared` in the
   * body - so a body that carried it for an ordinary payment would order fuel outside the acquirer
   * from a screen that looks like it is taking the customer to the bank.
   */
  it('asks for the promotional route only when the second button pressed it', async () => {
    mockedApiFetch.mockResolvedValue(jsonResponse({ orderIds: ['o-1'] }));

    await createBulkMonobankInvoice([line], null);
    await createBulkMonobankInvoice([line], null, 'Declared');

    const bodyOf = (call: unknown[]) => JSON.parse((call[1] as { body: string }).body);
    expect(bodyOf(mockedApiFetch.mock.calls[0])).not.toHaveProperty('settlementRoute');
    expect(bodyOf(mockedApiFetch.mock.calls[1]).settlementRoute).toBe('Declared');
  });

  it('reads back the route the order was written on', async () => {
    // An answer with no payment URL is either a promotional order that waits or a payment that never
    // started; only this field tells them apart, and the screen branches on it.
    mockedApiFetch.mockResolvedValue(
      jsonResponse({ orderIds: ['o-1'], settlementRoute: 'Declared', paymentUrl: '' }),
    );

    const result = await createBulkMonobankInvoice([line], null, 'Declared');

    expect(result.settlementRoute).toBe('Declared');
    expect(result.pageUrl).toBe('');
    expect(result.alreadyPaid).toBe(false);
  });

  it('leaves the route un-set for a server that has never heard of it', async () => {
    mockedApiFetch.mockResolvedValue(
      jsonResponse({ orderIds: ['o-1'], paymentUrl: 'https://pay' }),
    );

    const result = await createBulkMonobankInvoice([line], null);

    expect(result.settlementRoute).toBeUndefined();
    expect(result.pageUrl).toBe('https://pay');
  });
});
