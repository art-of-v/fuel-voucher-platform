import { describe, expect, it } from "vitest";
import { orderStatusKey, voucherStatusKey } from "./status-keys";

/**
 * The translation keys for the states the API reports.
 *
 * <p>
 * The API spells its states in PascalCase and the translation file keys them in camelCase, and
 * these two functions are the only place that knows the two spellings agree.
 */
describe("the status keys", () => {
  it("turns an order's state into its translation key", () => {
    expect(orderStatusKey("Fulfilled")).toBe("fulfilled");
    expect(orderStatusKey("PartiallyRefunded")).toBe("partiallyRefunded");
  });

  it("keeps the two pending order states apart", () => {
    // An order waiting for its money and an order waiting for its fuel are two different
    // things, and the translations carry two different words for them. The function used to
    // collapse them into one `pending` key - and never did, because the comparison was
    // against a lowercase literal while the value was camelCase.
    expect(orderStatusKey("PendingPayment")).toBe("pendingPayment");
    expect(orderStatusKey("PendingFulfillment")).toBe("pendingFulfillment");
  });

  it("turns a voucher's state into its translation key", () => {
    expect(voucherStatusKey("Available")).toBe("available");
    expect(voucherStatusKey("DecodeIntegrityWarning")).toBe(
      "decodeIntegrityWarning",
    );
  });
});
