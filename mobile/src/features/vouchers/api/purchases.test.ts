import { purchaseErrorKey } from './purchases';
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

  it('names no sentence that is missing from a language the app offers', () => {
    const codes = ['below_cost', 'package_out_of_stock', 'payment_provider_unavailable'];

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
