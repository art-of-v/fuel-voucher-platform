import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, History } from "lucide-react";

import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { formatMoney } from "@/lib/money";

/**
 * One row of `GET /api/admin/voucher-imports/{id}/cost-timeline` (#178).
 *
 * The point of the document is auditability: not "this batch costs 45" but "it cost 40, then
 * 300 UAH of exchange surcharge moved it to 45 on the 7th". So every row leads with what changed
 * and when, and the money is secondary.
 */
export interface BatchCostTimelineEntry {
  exchangeBatchId: string;
  atUtc: string;
  fuelTypeId: string;
  brand: string;
  vouchersExchangedOut: number;
  litersExchangedOut: number;
  surchargeUah: number;
  costPerLiterBefore: number | null;
  costPerLiterAfter: number | null;
}

export interface BatchCostTimelineResponse {
  importId: string;
  entries: BatchCostTimelineEntry[];
  currentCosts: {
    fuelTypeId: string;
    fuelTypeName: string | null;
    brand: string;
    voucherCount: number;
    totalLiters: number;
    costPerLiter: number | null;
    blendedCostPerLiter: number | null;
  }[];
}

function perL(value: number | null | undefined): string {
  return value == null ? "—" : `${value.toFixed(2)} ₴`;
}

function date(value: string | null | undefined): string {
  if (!value) return "—";
  // Rendered from an ISO instant; the day is what an operator is reading, not the hour.
  return value.slice(0, 10);
}

/**
 * The cost history of one import batch, behind a toggle.
 *
 * Fetched lazily on first expand: the timeline is an audit view, not something the imports tab
 * needs on load, and an operator looking at batch costs should not pay for it.
 */
export function BatchCostTimeline({
  importId,
  enabled,
}: {
  importId: string | null;
  enabled: boolean;
}) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);

  const { data, isLoading } = useQuery<BatchCostTimelineResponse>({
    queryKey: ["/api/admin/voucher-imports", importId, "cost-timeline"],
    queryFn: async () => {
      if (!importId) return { importId: "", entries: [], currentCosts: [] };
      return await apiRequest<void, BatchCostTimelineResponse>(
        "GET",
        `/api/admin/voucher-imports/${importId}/cost-timeline`,
      );
    },
    // Both gates: the tab must be open AND the operator must have asked for the history.
    enabled: enabled && !!importId && open,
    staleTime: 30_000,
  });

  const entries = data?.entries ?? [];

  return (
    <div className="mt-2">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground"
      >
        {open ? (
          <ChevronDown className="w-3.5 h-3.5" />
        ) : (
          <ChevronRight className="w-3.5 h-3.5" />
        )}
        <History className="w-3.5 h-3.5" />
        <span>{t("imports.costTimeline")}</span>
      </button>

      {open && (
        <div className="mt-2 border border-border/50 rounded-md overflow-hidden">
          {isLoading ? (
            <p className="p-3 text-xs text-muted-foreground">
              {t("common.loading")}
            </p>
          ) : entries.length === 0 ? (
            <p className="p-3 text-xs text-muted-foreground">
              {t("imports.costTimelineEmpty")}
            </p>
          ) : (
            <table className="w-full text-xs">
              <thead>
                <tr className="bg-muted/40 text-left text-muted-foreground">
                  <th className="p-2 font-medium">
                    {t("imports.timelineDate")}
                  </th>
                  <th className="p-2 font-medium">
                    {t("imports.timelineFuel")}
                  </th>
                  <th className="p-2 font-medium text-right">
                    {t("imports.timelineOut")}
                  </th>
                  <th className="p-2 font-medium text-right">
                    {t("imports.timelineSurcharge")}
                  </th>
                  <th className="p-2 font-medium text-right">
                    {t("imports.timelineCostMove")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr
                    key={`${entry.exchangeBatchId}-${entry.fuelTypeId}`}
                    className="border-t border-border/50"
                  >
                    <td className="p-2 font-mono">{date(entry.atUtc)}</td>
                    <td className="p-2">
                      <div>{entry.fuelTypeId}</div>
                      <div className="text-muted-foreground uppercase text-[10px]">
                        {entry.brand}
                      </div>
                    </td>
                    <td className="p-2 text-right font-mono">
                      {entry.vouchersExchangedOut} · {entry.litersExchangedOut}{" "}
                      L
                    </td>
                    <td className="p-2 text-right font-mono">
                      {formatMoney(entry.surchargeUah)}
                    </td>
                    <td className="p-2 text-right font-mono">
                      {perL(entry.costPerLiterBefore)} →{" "}
                      {perL(entry.costPerLiterAfter)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {/*
            Where the batch stands now, read from the same response rather than from the panel
            above it. Two sources showing two numbers for the same cost is the problem #178 was
            filed about, so this deliberately renders the timeline's own figure.
          */}
          {data && data.currentCosts.length > 0 && (
            <div className="border-t border-border/50 p-2 text-xs text-muted-foreground">
              {data.currentCosts.map((cost) => (
                <div
                  key={`${cost.fuelTypeId}-${cost.brand}`}
                  className="font-mono"
                >
                  {cost.fuelTypeName || cost.fuelTypeId} ·{" "}
                  {perL(cost.costPerLiter)}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
