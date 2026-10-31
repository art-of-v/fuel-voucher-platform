import { describe, expect, it } from "vitest";
import { ApiError } from "./api-client";
import { BRAND_REFUSALS } from "@/components/brands/brandRefusals";
import { codedErrorMessage } from "./coded-error-message";

/**
 * The catalogue's refusals are the ones an operator meets most, and half of their sentences need
 * a fact from the refusal body — which twin holds the grade, which word reads as rehearsal, which
 * half of the price is missing, how many vouchers block an archive, which sizes are not litres.
 * The tests pin that mapping, and the fallback that keeps an unlearnt code from printing a
 * half-built sentence.
 */

// A translator that shows what was asked for and with what, instead of pinning the prose.
const t = (key: string, ...params: (string | number)[]) =>
  params.length ? `${key}[${params.join("|")}]` : key;

const say = (error: ApiError) => codedErrorMessage(error, t, BRAND_REFUSALS);

describe("brand refusals", () => {
  it("says a duplicate grade and names the row that already holds it", () => {
    const error = new ApiError(
      "A fuel with the same grade already exists",
      "fuel_grade_exists",
      undefined,
      undefined,
      { twinName: "А95 ЄВРО(QA)" },
    );

    expect(say(error)).toBe("fuel.error.gradeExists[А95 ЄВРО(QA)]");
  });

  it("names the word that reads as rehearsal data", () => {
    const error = new ApiError(
      'The name contains "QA", which reads as rehearsal data',
      "fuel_rehearsal_name",
      undefined,
      undefined,
      { token: "QA" },
    );

    expect(say(error)).toBe("fuel.error.rehearsalName[QA]");
  });

  it("points at whichever half of the price live stock must keep", () => {
    const needs = (extra: Record<string, unknown>) =>
      say(
        new ApiError(
          "This fuel has live stock and must keep a price",
          "fuel_live_stock_needs_price",
          undefined,
          undefined,
          extra,
        ),
      );

    expect(needs({ gap: "cost" })).toBe("fuel.error.liveStockNeedsCost");
    expect(needs({ gap: "margin" })).toBe("fuel.error.liveStockNeedsMargin");
    expect(needs({ gap: "cost_and_margin" })).toBe(
      "fuel.error.liveStockNeedsBoth",
    );
  });

  it("falls back to the named field when the refusal carries no gap token", () => {
    const needs = (extra: Record<string, unknown>) =>
      say(
        new ApiError(
          "This fuel has live stock and must keep a price",
          "fuel_live_stock_needs_price",
          undefined,
          undefined,
          extra,
        ),
      );

    expect(needs({ field: "SupplierPricePerLiter" })).toBe(
      "fuel.error.liveStockNeedsCost",
    );
    expect(needs({ field: "MarginUahPerLiter" })).toBe(
      "fuel.error.liveStockNeedsMargin",
    );
    // A gap token wins over the field, so a backend that names both cannot downgrade "both"
    // to whichever box it happens to point at.
    expect(
      needs({ gap: "cost_and_margin", field: "SupplierPricePerLiter" }),
    ).toBe("fuel.error.liveStockNeedsBoth");
    // Neither travelled: the server's whole sentence beats half a localised one.
    expect(needs({})).toBe("This fuel has live stock and must keep a price");
  });

  it("carries the count of vouchers that block an archive", () => {
    const error = new ApiError(
      "This fuel still has active vouchers",
      "fuel_archive_blocked",
      undefined,
      undefined,
      { activeVoucherCount: 4 },
    );

    expect(say(error)).toBe("fuel.error.archiveBlocked[4]");
  });

  it("names the sizes that are not positive litres", () => {
    const error = new ApiError(
      "A nominal volume must be a positive number of litres.",
      "fuel_nominals_invalid",
      undefined,
      undefined,
      { invalid: [-5, 0] },
    );

    expect(say(error)).toBe("fuel.error.nominalsInvalid[-5, 0]");
  });

  it("keeps the server's sentence when a sentence's payload did not arrive", () => {
    // An older backend sends the code and the English sentence and nothing between them. A
    // localised sentence with a hole where the name should be reads worse than the whole one.
    const noTwin = new ApiError(
      "A fuel with the same grade already exists",
      "fuel_grade_exists",
    );
    const noCount = new ApiError(
      "This fuel still has active vouchers",
      "fuel_archive_blocked",
    );

    expect(say(noTwin)).toBe("A fuel with the same grade already exists");
    expect(say(noCount)).toBe("This fuel still has active vouchers");
  });

  it("keeps the server's sentence for a code it has not learnt", () => {
    const error = new ApiError(
      "Something the panel does not know yet",
      "fuel_something_new",
    );

    expect(say(error)).toBe("Something the panel does not know yet");
  });
});
