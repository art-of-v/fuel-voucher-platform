import { useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { formatMoney } from "@/lib/money";

/**
 * One row of `GET /api/admin/brand-fuels/{fuelId}/price-history` (#1229).
 *
 * `finalPricePerLiter` is what a customer paid per litre at that instant, so it is the column that
 * answers "what did this cost them". The three numbers behind it - what we paid, what we added and the
 * column price the customer was shown against - are there because a price nobody can explain is a
 * price nobody trusts.
 */
export interface FuelPriceHistoryEntry {
  id: string;
  offerId: string;
  dataKind: string;
  supplierPricePerLiter: number | null;
  marginUahPerLiter: number | null;
  marginPercent: number | null;
  finalPricePerLiter: number | null;
  pumpPricePerLiter: number | null;
  minDiscountPerLiter: number | null;
  effectiveFromUtc: string;
  changedByUserId: string | null;
  reason: string;
}

/**
 * The single row of `GET /api/admin/brand-fuels/{fuelId}/price-at?asOf=...` - what one fuel was
 * priced at at a given instant, which a series clamped to the newest 200 rows cannot answer.
 */
export interface FuelPriceAt {
  id: string;
  effectiveFromUtc: string;
  supersededAtUtc: string | null;
  supplierPricePerLiter: number | null;
  marginUahPerLiter: number | null;
  finalPricePerLiter: number | null;
  pumpPricePerLiter: number | null;
  reason: string;
}

/**
 * `OfferPriceReasons`, which a CHECK constraint keeps in step with the write paths that exist. Kept as
 * a local map rather than fetched, because the list is closed and a missing entry would be a bug in
 * the backend's list rather than something an operator needs to see raw.
 */
const REASON_KEYS: Record<string, string> = {
  operator: "priceHistory.reasonOperator",
  "pump-import": "priceHistory.reasonPumpImport",
  "catalogue-import": "priceHistory.reasonCatalogueImport",
  "batch-cost": "priceHistory.reasonBatchCost",
  "exchange-release": "priceHistory.reasonExchangeRelease",
  backfill: "priceHistory.reasonBackfill",
};

/** The day, not the hour: an operator reading a price history is looking for "when did this change". */
function day(value: string | null | undefined): string {
  if (!value) return "—";
  return value.slice(0, 10);
}

/** A date input yields `YYYY-MM-DD`; the API takes an instant, so the end of that day is the safe reading. */
function endOfDayUtc(value: string): string {
  return `${value}T23:59:59.999Z`;
}

function perL(value: number | null | undefined): string {
  return value == null ? "—" : formatMoney(value, { decimals: 2, hideSymbol: true });
}

/**
 * The price history of one fuel.
 *
 * Two views, because they answer different questions. The **log** is "every change, newest first" -
 * what an operator reads to see who moved a price and which door it came through. The **price at** is
 * "what stood on this date", which is the one an order line or a complaint needs and which the log
 * alone cannot answer: the log is capped at the newest 200 rows, and reading it positionally is how
 * an operator picks the wrong row at the wrong moment.
 */
export function FuelPriceHistory({ fuelId }: { fuelId: string }) {
  const { t } = useI18n();
  const [asOfDate, setAsOfDate] = useState("");

  const log = useQuery<FuelPriceHistoryEntry[]>({
    queryKey: [`/api/admin/brand-fuels/${fuelId}`, "price-history"],
    queryFn: () =>
      apiRequest<any, FuelPriceHistoryEntry[]>(
        "GET",
        `/api/admin/brand-fuels/${fuelId}/price-history?limit=200`,
      ),
    staleTime: 30_000,
  });

  const asOf = useQuery<FuelPriceAt | null>({
    queryKey: [`/api/admin/brand-fuels/${fuelId}`, "price-at", asOfDate],
    queryFn: () =>
      apiRequest<any, FuelPriceAt | null>(
        "GET",
        `/api/admin/brand-fuels/${fuelId}/price-at?asOf=${encodeURIComponent(endOfDayUtc(asOfDate))}`,
      ),
    // Only asked for once a date is given: an empty box is not a question.
    enabled: asOfDate !== "",
    staleTime: 30_000,
  });

  const entries = log.data ?? [];

  return (
    <div className="rounded-md border border-border/60 bg-background/40">
      <div className="flex flex-wrap items-end gap-2 border-b border-border/60 p-3">
        <label className="flex flex-col gap-1">
          <span className="text-[11px] font-medium uppercase tracking-wider text-muted-foreground">
            {t("priceHistory.atTitle")}
          </span>
          <input
            type="date"
            value={asOfDate}
            onChange={(e) => setAsOfDate(e.target.value)}
            className="h-8 rounded-md border border-border bg-background px-2 text-xs"
          />
        </label>

        {asOfDate !== "" && (
          <div className="flex flex-col gap-1 text-xs">
            {asOf.isLoading ? (
              <span className="text-muted-foreground">{t("common.loading")}</span>
            ) : asOf.data ? (
              <>
                <span className="text-muted-foreground">{t("priceHistory.atResult")}</span>
                <span className="font-medium tabular-nums">
                  {perL(asOf.data.finalPricePerLiter)} {t("priceHistory.perLiter")}
                  {" · "}
                  {t("priceHistory.pump")} {perL(asOf.data.pumpPricePerLiter)}
                  {" · "}
                  {t(REASON_KEYS[asOf.data.reason] ?? "priceHistory.reasonUnknown")}
                </span>
                <span className="text-[10px] text-muted-foreground">
                  {t("priceHistory.stoodFrom", day(asOf.data.effectiveFromUtc))}
                  {asOf.data.supersededAtUtc
                    ? ` → ${t("priceHistory.stoodUntil", day(asOf.data.supersededAtUtc))}`
                    : ` → ${t("priceHistory.stoodUntilNow")}`}
                </span>
              </>
            ) : (
              <span className="text-muted-foreground">{t("priceHistory.atNone")}</span>
            )}
          </div>
        )}
      </div>

      {log.isLoading ? (
        <p className="p-3 text-xs text-muted-foreground">{t("common.loading")}</p>
      ) : entries.length === 0 ? (
        <p className="p-3 text-xs text-muted-foreground">{t("priceHistory.empty")}</p>
      ) : (
        <div className="max-h-72 overflow-y-auto">
          <table className="w-full text-xs">
            <thead className="sticky top-0 bg-muted/60">
              <tr className="text-left text-muted-foreground">
                <th className="p-2 font-medium">{t("priceHistory.colWhen")}</th>
                <th className="p-2 font-medium text-right">{t("priceHistory.colCustomer")}</th>
                <th className="p-2 font-medium text-right">{t("priceHistory.colBuy")}</th>
                <th className="p-2 font-medium text-right">{t("priceHistory.colMargin")}</th>
                <th className="p-2 font-medium text-right">{t("priceHistory.colPump")}</th>
                <th className="p-2 font-medium">{t("priceHistory.colReason")}</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={entry.id} className="border-t border-border/50">
                  <td className="p-2 font-mono whitespace-nowrap">{day(entry.effectiveFromUtc)}</td>
                  <td className="p-2 text-right font-medium tabular-nums">
                    {perL(entry.finalPricePerLiter)}
                  </td>
                  <td className="p-2 text-right tabular-nums text-muted-foreground">
                    {perL(entry.supplierPricePerLiter)}
                  </td>
                  <td className="p-2 text-right tabular-nums text-muted-foreground">
                    {perL(entry.marginUahPerLiter)}
                  </td>
                  <td className="p-2 text-right tabular-nums text-muted-foreground">
                    {perL(entry.pumpPricePerLiter)}
                  </td>
                  <td className="p-2">
                    {t(REASON_KEYS[entry.reason] ?? "priceHistory.reasonUnknown")}
                    {entry.dataKind !== "real" && (
                      <span className="ml-1 text-[10px] uppercase text-muted-foreground">
                        {entry.dataKind}
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
