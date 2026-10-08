import { parseOrderNotification } from './orderRef';

/**
 * The notification message carries the full order GUID inline; this util is what
 * shortens it for display and hands back the full id for the deep-link
 * (planning #132). The order-fulfilled copy is the real input it has to handle.
 */

const ORDER_ID = 'ad9cf45f-1541-405c-95a9-41e2b2f9da72';
const FULFILLED = `Ваше замовлення #${ORDER_ID} виконано. Ваучери призначені та готові до використання.`;

describe('parseOrderNotification', () => {
  it('recovers the full order id from the message', () => {
    expect(parseOrderNotification(FULFILLED).orderId).toBe(ORDER_ID);
  });

  it('shortens the id to a trailing tail for display, keeping the copy around it', () => {
    expect(parseOrderNotification(FULFILLED).display).toBe(
      'Ваше замовлення #…f9da72 виконано. Ваучери призначені та готові до використання.',
    );
  });

  it('leaves no trace of the full id in the display string', () => {
    expect(parseOrderNotification(FULFILLED).display).not.toContain(ORDER_ID);
  });

  it('matches the id case-insensitively', () => {
    const upper = `Order #${ORDER_ID.toUpperCase()} done`;
    const { orderId, display } = parseOrderNotification(upper);
    expect(orderId).toBe(ORDER_ID.toUpperCase());
    expect(display).toBe('Order #…F9DA72 done');
  });

  it('returns the message untouched with a null id when it carries no order id', () => {
    const plain = 'Ваш акаунт підтверджено.';
    expect(parseOrderNotification(plain)).toEqual({
      orderId: null,
      display: plain,
      preview: plain,
    });
  });

  it('does not treat a short hex-ish fragment as an id', () => {
    const near = 'Код 1234-5678 активовано';
    expect(parseOrderNotification(near)).toEqual({ orderId: null, display: near, preview: near });
  });

  it('drops the whole id fragment from the preview, not just the guid', () => {
    const { preview } = parseOrderNotification(FULFILLED);

    // Leaving a bare `#` mid-sentence reads worse than the guid did.
    expect(preview).not.toContain('#');
    expect(preview).not.toContain(ORDER_ID);
    expect(preview).not.toContain('…f9da72');
    // And it does not leave a run of spaces where the fragment was.
    expect(preview).not.toMatch(/\s{2,}/);
    // The sentence itself survives.
    expect(preview.length).toBeGreaterThan(0);
  });
});
