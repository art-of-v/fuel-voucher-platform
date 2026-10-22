import { BarChart } from "lucide-react";
import { useI18n } from "@/lib/i18n";
import { formatMoney } from "@/lib/money";
import type { ImportBatchPnl } from "@/lib/api-types";

/**
 * The batch's profit and loss, per fuel: what it sold, what it kept, and what it lost.
 *
 * <p>
 * The figures are null where the batch cannot know them: an uncosted fuel has no cost, and a
 * null there means "we do not know", not "zero". The panel says so rather than showing a zero
 * that reads like a loss.
 */
export default function BatchPnlPanel({
  batchPnl,
}: {
  batchPnl: ImportBatchPnl[];
}) {
  const { t } = useI18n();

  if (batchPnl.length === 0) return null;

  const money = (v: number | null | undefined) =>
    formatMoney(v == null ? null : Number(v));
  const perL = (v: number | null | undefined) =>
    v != null ? `${Number(v)} ₴` : "—";
  const loss = (v: number | null | undefined) =>
    v != null && Number(v) > 0 ? `−${money(v)}` : money(v);

  const totalRevenue = batchPnl.reduce(
    (s, b) => s + (b.realizedRevenue ?? 0),
    0,
  );
  const totalRealized = batchPnl.reduce(
    (s, b) => s + (b.realizedMargin ?? 0),
    0,
  );
  const totalExpiredLoss = batchPnl.reduce(
    (s, b) => s + (b.expiredLoss ?? 0),
    0,
  );
  const totalNet = batchPnl.reduce((s, b) => s + (b.netRealizedResult ?? 0), 0);
  const totalUnrealized = batchPnl.reduce(
    (s, b) => s + (b.unrealizedMargin ?? 0),
    0,
  );

  return (
    <div className="glass-panel p-6">
      <div className="flex items-center gap-2 mb-1">
        <BarChart className="w-5 h-5 text-primary" />
        <h3 className="text-lg font-bold">{t("imports.pnlTitle")}</h3>
      </div>
      <p className="text-sm text-muted-foreground mb-4">
        {t("imports.pnlHint")}
      </p>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-xs uppercase text-muted-foreground">
            <tr className="border-b border-border">
              <th className="text-left p-2">{t("imports.pnlFuel")}</th>
              <th className="text-right p-2">{t("imports.pnlSold")}</th>
              <th className="text-right p-2">{t("imports.pnlRemaining")}</th>
              <th className="text-right p-2">{t("imports.pnlCostL")}</th>
              <th className="text-right p-2">{t("imports.pnlAvgSaleL")}</th>
              <th className="text-right p-2">
                {t("imports.pnlRealizedRevenue")}
              </th>
              <th className="text-right p-2">
                {t("imports.pnlRealizedMargin")}
              </th>
              <th className="text-right p-2">{t("imports.pnlExpiredLoss")}</th>
              <th className="text-right p-2">{t("imports.pnlNetResult")}</th>
              <th className="text-right p-2">
                {t("imports.pnlUnrealizedMargin")}
              </th>
            </tr>
          </thead>
          <tbody>
            {batchPnl.map((b) => (
              <tr
                key={`${b.fuelTypeId}-${b.brand}`}
                className="border-b border-border/50"
              >
                <td className="p-2">
                  <div className="font-semibold">
                    {b.fuelTypeName || b.fuelTypeId}
                  </div>
                  <div className="text-xs uppercase text-muted-foreground">
                    {b.brand}
                  </div>
                </td>
                <td className="p-2 text-right font-mono">
                  {b.vouchersSold} · {b.litersSold} L
                </td>
                <td className="p-2 text-right font-mono">
                  {b.vouchersRemaining} · {b.litersRemaining} L
                </td>
                <td className="p-2 text-right font-mono">
                  {b.costPerLiter != null ? (
                    perL(b.costPerLiter)
                  ) : (
                    <span className="text-warning">
                      {t("imports.pnlUncosted")}
                    </span>
                  )}
                </td>
                <td className="p-2 text-right font-mono">
                  {perL(b.avgSalePricePerLiter)}
                </td>
                <td className="p-2 text-right font-mono">
                  {money(b.realizedRevenue)}
                </td>
                <td
                  className={`p-2 text-right font-mono ${b.realizedMargin != null && b.realizedMargin < 0 ? "text-destructive" : b.realizedMargin != null ? "text-success" : ""}`}
                >
                  {money(b.realizedMargin)}
                </td>
                <td className="p-2 text-right font-mono">
                  {b.vouchersExpired > 0 ? (
                    <>
                      <div className="text-destructive">
                        {loss(b.expiredLoss)}
                      </div>
                      <div className="text-xs text-muted-foreground">
                        {b.vouchersExpired} · {b.litersExpired} L
                      </div>
                    </>
                  ) : (
                    "—"
                  )}
                </td>
                <td
                  className={`p-2 text-right font-mono ${b.netRealizedResult != null && b.netRealizedResult < 0 ? "text-destructive" : b.netRealizedResult != null ? "text-success" : ""}`}
                >
                  {money(b.netRealizedResult)}
                </td>
                <td className="p-2 text-right font-mono">
                  {money(b.unrealizedMargin)}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t-2 border-border font-bold">
              <td className="p-2" colSpan={5}>
                {t("imports.pnlTotal")}
              </td>
              <td className="p-2 text-right font-mono">
                {money(totalRevenue)}
              </td>
              <td
                className={`p-2 text-right font-mono ${totalRealized < 0 ? "text-destructive" : "text-success"}`}
              >
                {money(totalRealized)}
              </td>
              <td className="p-2 text-right font-mono text-destructive">
                {loss(totalExpiredLoss)}
              </td>
              <td
                className={`p-2 text-right font-mono ${totalNet < 0 ? "text-destructive" : "text-success"}`}
              >
                {money(totalNet)}
              </td>
              <td className="p-2 text-right font-mono">
                {money(totalUnrealized)}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  );
}
