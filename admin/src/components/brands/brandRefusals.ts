import type { CodedSentence } from "@/lib/coded-error-message";

/**
 * The sentences for a refused brand or fuel write, in the panel's language.
 *
 * The codes come from `BrandsController` and `BrandFuelController`; a code this map has not learnt
 * keeps the server's English sentence, so a backend-only change still shows the operator
 * something true. A sentence whose payload did not arrive does the same: half a sentence with a
 * hole in it reads worse than the server's whole one.
 */
export const BRAND_REFUSALS: Record<string, CodedSentence> = {
  brand_slug_required: (_error, t) => t("brands.error.slugRequired"),
  brand_slug_taken: (_error, t) => t("brands.error.slugTaken"),
  brand_not_found: (_error, t) => t("brands.error.notFound"),
  fuel_not_found: (_error, t) => t("fuel.error.notFound"),
  fuel_test_data_required: (_error, t) => t("fuel.error.testDataRequired"),
  fuel_name_exists: (_error, t) => t("fuel.error.nameExists"),
  fuel_grade_exists: (error, t) => {
    const twin = text(error.extra?.twinName);
    return twin ? t("fuel.error.gradeExists", twin) : undefined;
  },
  fuel_code_taken: (_error, t) => t("fuel.error.codeTaken"),
  fuel_rehearsal_name: (error, t) => {
    const token = text(error.extra?.token);
    return token ? t("fuel.error.rehearsalName", token) : undefined;
  },
  fuel_pool_mismatch: (_error, t) => t("fuel.error.poolMismatch"),
  // Which half of the price is missing is the field the refusal names, so the operator is pointed
  // at the box that has to change rather than at both of them.
  fuel_live_stock_needs_price: (error, t) => {
    // Which half of the price is missing travels as the same token the activation refusal names
    // its fuels with, so "both" gets a sentence of its own rather than the first half only. The
    // field is the fallback for a backend that predates the token.
    const gap = text(error.extra?.gap);
    if (gap === "cost_and_margin") return t("fuel.error.liveStockNeedsBoth");
    if (gap === "margin") return t("fuel.error.liveStockNeedsMargin");
    if (gap === "cost") return t("fuel.error.liveStockNeedsCost");

    const field = text(error.extra?.field);
    if (field === "MarginUahPerLiter")
      return t("fuel.error.liveStockNeedsMargin");
    if (field === "SupplierPricePerLiter")
      return t("fuel.error.liveStockNeedsCost");
    return undefined;
  },
  fuel_archive_blocked: (error, t) => {
    const count = numberFrom(error.extra?.activeVoucherCount);
    if (count === undefined) return undefined;
    return t("fuel.error.archiveBlocked", count);
  },
  fuel_nominals_invalid: (error, t) => {
    const invalid = error.extra?.invalid;
    if (!Array.isArray(invalid) || invalid.length === 0) return undefined;
    return t("fuel.error.nominalsInvalid", invalid.join(", "));
  },
  fuel_cost_not_positive: (_error, t) => t("fuel.error.costNotPositive"),
  fuel_margin_negative: (_error, t) => t("fuel.error.marginNegative"),
  fuel_below_cost_needs_cost: (_error, t) => t("fuel.error.belowCostNeedsCost"),
};

function text(value: unknown): string | undefined {
  return typeof value === "string" && value !== "" ? value : undefined;
}

function numberFrom(value: unknown): number | undefined {
  return typeof value === "number" && Number.isFinite(value)
    ? value
    : undefined;
}
