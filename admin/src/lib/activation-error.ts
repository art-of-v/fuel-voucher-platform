import { ApiError, type UnpricedFuel } from "@/lib/api-client";
import { errorMessage } from "@/lib/error-message";

/**
 * The sentence for a refused activation, in the language the panel is speaking.
 *
 * The gate refuses in three distinguishable ways, and each one sends the operator somewhere
 * different: a fuel with no margin is priced on the Brands tab, a batch with no cost is priced on
 * this one, and a voucher whose fuel left the catalogue is not a pricing problem at all. The
 * backend's own sentence says all of that in English, and the panel defaults to Ukrainian — so the
 * reason arrived as a paragraph the operator could not act on. The refusal therefore travels as a
 * machine code plus the fuels it names, and the words are made here.
 *
 * A code this file does not know falls back to the server's sentence, and a known code whose
 * structured part is missing falls back the same way: an older backend still says something true.
 */

type Translator = (key: string, ...params: (string | number)[]) => string;

const GAP_KEYS: Record<string, string> = {
  cost: "imports.error.gapCost",
  margin: "imports.error.gapMargin",
  cost_and_margin: "imports.error.gapCostAndMargin",
};

export function activationErrorMessage(error: unknown, t: Translator): string {
  if (!(error instanceof ApiError) || !error.code) return errorMessage(error);

  switch (error.code) {
    case "fuel_not_priced": {
      const named = describeFuels(error.unpricedFuels, t);
      return named ? t("imports.error.notPriced", named) : error.message;
    }
    case "fuel_missing":
      return t("imports.error.fuelMissing");
    case "voucher_cost_missing": {
      const count = error.data?.voucherCount;
      return typeof count === "number"
        ? t("imports.error.voucherCost", count)
        : error.message;
    }
    default:
      return error.message;
  }
}

/** "ДП (не задано маржу), А-95 (не задано собівартість)" — the names stay as the server wrote them. */
function describeFuels(
  fuels: UnpricedFuel[] | undefined,
  t: Translator,
): string {
  if (!fuels?.length) return "";
  return fuels
    .map((fuel) => {
      const gapKey = GAP_KEYS[fuel.gap];
      return gapKey ? `${fuel.name} (${t(gapKey)})` : fuel.name;
    })
    .join(", ");
}
