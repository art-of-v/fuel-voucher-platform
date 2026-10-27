import { describe, expect, it } from "vitest";
import { ApiError } from "./api-client";
import { activationErrorMessage } from "./activation-error";

/**
 * The refusal the operator reads is written here, from the code the API sends — so the tests are
 * about the mapping, not about the wording. A Ukrainian panel that fell back to the server's English
 * paragraph is exactly the defect this file exists to prevent (planning #680 follow-up).
 */

// A translator that shows what was asked for and with what, instead of pinning the prose of four
// locale files to a unit test.
const t = (key: string, ...params: (string | number)[]) =>
  params.length ? `${key}[${params.join("|")}]` : key;

describe("activationErrorMessage", () => {
  it("names each fuel and translates the gap it is missing", () => {
    const error = new ApiError(
      "Cannot activate - a fuel without a price of its own cannot go on sale: ДП (no margin)",
      "fuel_not_priced",
      undefined,
      [
        { name: "ДП", gap: "margin" },
        { name: "А-95", gap: "cost_and_margin" },
      ],
    );

    expect(activationErrorMessage(error, t)).toBe(
      "imports.error.notPriced[ДП (imports.error.gapMargin), " +
        "А-95 (imports.error.gapCostAndMargin)]",
    );
  });

  it("interpolates the count of vouchers still without a cost", () => {
    const error = new ApiError(
      "Cannot activate - enter the cost first for 3 voucher(s): A, B, C",
      "voucher_cost_missing",
      { voucherCount: 3 },
    );

    expect(activationErrorMessage(error, t)).toBe(
      "imports.error.voucherCost[3]",
    );
  });

  it("says the catalogue case in its own words", () => {
    const error = new ApiError("Cannot activate - ...", "fuel_missing");

    expect(activationErrorMessage(error, t)).toBe("imports.error.fuelMissing");
  });

  it("keeps the server's sentence for a code it has not learnt", () => {
    const error = new ApiError("Some other refusal", "not_a_code_we_know");

    expect(activationErrorMessage(error, t)).toBe("Some other refusal");
  });

  it("keeps the server's sentence when the structured part did not arrive", () => {
    // An older backend sends the code and the prose and nothing between them. The prose is true,
    // so it is better than a localised sentence with a hole where the fuel list should be.
    const noFuels = new ApiError(
      "Cannot activate - the old words",
      "fuel_not_priced",
    );
    const noCount = new ApiError(
      "Cannot activate - the old words",
      "voucher_cost_missing",
    );

    expect(activationErrorMessage(noFuels, t)).toBe(
      "Cannot activate - the old words",
    );
    expect(activationErrorMessage(noCount, t)).toBe(
      "Cannot activate - the old words",
    );
  });

  it("does not invent a gap for a token it does not know", () => {
    const error = new ApiError(
      "Cannot activate",
      "fuel_not_priced",
      undefined,
      [{ name: "ДП", gap: "something_new_upstream" }],
    );

    expect(activationErrorMessage(error, t)).toBe(
      "imports.error.notPriced[ДП]",
    );
  });

  it("reads a plain error as its message", () => {
    expect(activationErrorMessage(new Error("Network is down"), t)).toBe(
      "Network is down",
    );
  });
});
