import { describe, expect, it } from "vitest";
import { computeFinal } from "./finalPrice";

describe("computeFinal", () => {
  // The regression: the preview computed min(cost + profit, pump) - the formula the server
  // stopped using - so while an operator was typing a cost and a margin, the admin showed the
  // cheaper of the two whenever the column price sat below the cost plus the margin. That is a
  // price the server does not charge.
  it("is the cost plus the margin when our price sits below the column price", () => {
    expect(computeFinal(49, 2, 55)).toBe(51);
  });

  it("is the cost plus the margin when our price sits above the column price", () => {
    // 56 is above the column price of 55, and that is the model: the column price is a
    // reference, not a cap. The customer pays 56 and sees the 55 struck through.
    expect(computeFinal(49, 7, 55)).toBe(56);
  });

  it("shows the column price for a fuel nobody has priced yet", () => {
    // No cost or no margin means no price of ours: the shelf shows the column price, marked
    // "not in stock", and the fuel cannot be bought.
    expect(computeFinal(null, null, 55)).toBe(55);
    expect(computeFinal(null, 2, 55)).toBe(55);
    expect(computeFinal(49, null, 55)).toBe(55);
  });

  it("is zero when there is neither a cost nor a column price", () => {
    expect(computeFinal(null, null, null)).toBe(0);
  });
});
