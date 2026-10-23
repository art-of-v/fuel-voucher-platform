import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { BarChart, ChevronLeft, ChevronRight, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { apiRequest } from "@/lib/api-client";
import { useAdminFuelTypes } from "@/lib/admin-lookups";
import { useI18n } from "@/lib/i18n";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/lib/session-store";
import { formatDate } from "@/lib/utils";
import type { ReconciliationResponse } from "@/lib/api-types";
import { RedemptionReconciliationPanel } from "@/components/RedemptionReconciliationPanel";

/**
 * The reconciliation: what we received, what we delivered, and the orders where those two do
 * not agree.
 *
 * <p>
 * The three-way match is a PAGE: one row per order, and the table shows fifty of them. The count
 * behind the page travels with it, so the screen can say how many there are.
 */
export default function ReconciliationTab() {
  const { t } = useI18n();
  const user = useSession((s) => s.user);

  const [reconPage, setReconPage] = useState(1);
  const { data: fuelTypesList = [] } = useAdminFuelTypes();

  const { data: reconciliationData, isLoading: isReconLoading } =
    useQuery<ReconciliationResponse>({
      queryKey: ["/api/admin/reconciliation", reconPage],
      queryFn: () =>
        apiRequest<void, ReconciliationResponse>(
          "GET",
          `/api/admin/reconciliation?page=${reconPage}&limit=50`,
        ),
      enabled: !!user,
      staleTime: 30_000,
    });

  // The three-way match carries the fuel's ID, not its name. The catalog turns the id into the
  // name the operator knows; without it the column showed a raw UUID.
  const fuelNameById = new Map(
    fuelTypesList.map((f) => [f.id, f.name] as const),
  );

  return (
    <div className="space-y-6 animate-in fade-in duration-300">
      <h2 className="text-2xl font-bold flex items-center gap-2">
        <BarChart className="w-6 h-6 text-primary" />
        {t("reconciliation.title")}
      </h2>

      <RedemptionReconciliationPanel />

      {isReconLoading ? (
        <div className="flex items-center gap-2 text-muted-foreground">
          <Loader2 className="w-5 h-5 animate-spin" />
          {t("common.loading")}
        </div>
      ) : reconciliationData ? (
        <>
          {/* Money Ledger — received / delivered / refunded */}
          <div className="glass-panel p-4">
            <p className="text-sm text-muted-foreground mb-3">
              {t("reconciliation.moneyLedger")}
            </p>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div className="bg-success/10 border border-success/30 rounded-lg p-3">
                <p className="text-xs text-muted-foreground">
                  {t("reconciliation.received")}
                </p>
                <p className="text-xl font-bold text-success">
                  {(
                    (reconciliationData.summary.totalReceivedKopecks ?? 0) / 100
                  ).toLocaleString()}{" "}
                  ₴
                </p>
              </div>
              <div className="bg-info/10 border border-info/30 rounded-lg p-3">
                <p className="text-xs text-muted-foreground">
                  {t("reconciliation.deliveredValue")}
                </p>
                <p className="text-xl font-bold text-info">
                  {(
                    (reconciliationData.summary.totalFulfilledValueKopecks ??
                      0) / 100
                  ).toLocaleString()}{" "}
                  ₴
                </p>
              </div>
              <div className="bg-destructive/10 border border-destructive/30 rounded-lg p-3">
                <p className="text-xs text-muted-foreground">
                  {t("reconciliation.refundedLabel")}
                </p>
                <p className="text-xl font-bold text-destructive">
                  {(
                    (reconciliationData.summary.totalRefundedKopecks ?? 0) / 100
                  ).toLocaleString()}{" "}
                  ₴
                </p>
                <p className="text-xs text-muted-foreground">
                  {reconciliationData.summary.refundedOrders ?? 0}{" "}
                  {t("reconciliation.refundedOrders")}
                </p>
              </div>
            </div>
          </div>

          {/* Summary Cards — Key Reconciliation Metrics */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="glass-panel p-4">
              <p className="text-sm text-muted-foreground">
                {t("report.totalSpent")}
              </p>
              <p className="text-2xl font-bold text-success">
                {reconciliationData.summary.totalRevenueUah.toLocaleString()} ₴
              </p>
              <p className="text-xs text-muted-foreground">
                {reconciliationData.summary.fulfilled}{" "}
                {t("report.fulfilledOrders")}
              </p>
            </div>
            <div
              className={`border rounded-xl p-4 ${reconciliationData.summary.paidUnfulfilled > 0 ? "bg-destructive/20 border-destructive/50" : "bg-muted border-border"}`}
            >
              <p className="text-sm text-muted-foreground">
                {t("reconciliation.needAttention")}
              </p>
              <p
                className={`text-2xl font-bold ${reconciliationData.summary.paidUnfulfilled > 0 ? "text-destructive" : "text-success"}`}
              >
                {reconciliationData.summary.paidUnfulfilled +
                  reconciliationData.summary.partiallyFulfilled}
              </p>
              <p className="text-xs text-muted-foreground">
                {reconciliationData.summary.paidUnfulfilled}{" "}
                {t("reconciliation.unfulfilled")} ·{" "}
                {reconciliationData.summary.partiallyFulfilled}{" "}
                {t("reconciliation.partial")}
              </p>
            </div>
            <div
              className={`border rounded-xl p-4 ${reconciliationData.summary.orphanVouchers > 0 || reconciliationData.summary.unprocessedEvents > 10 ? "bg-warning/20 border-warning/50" : "bg-muted border-border"}`}
            >
              <p className="text-sm text-muted-foreground">
                {t("reconciliation.dataIntegrity")}
              </p>
              <p className="text-2xl font-bold text-warning">
                {reconciliationData.summary.orphanVouchers} /{" "}
                {reconciliationData.summary.unprocessedEvents}
              </p>
              <p className="text-xs text-muted-foreground">
                {t("reconciliation.orphanVouchers")}
              </p>
            </div>
            <div className="glass-panel p-4">
              <p className="text-sm text-muted-foreground">
                {t("reconciliation.providersDeficit")}
              </p>
              <p
                className={`text-2xl font-bold ${reconciliationData.summary.lowInventoryProviders > 0 ? "text-warning" : "text-success"}`}
              >
                {reconciliationData.summary.lowInventoryProviders}
              </p>
              <p className="text-xs text-muted-foreground">
                {reconciliationData.summary.importErrors7d}{" "}
                {t("reconciliation.importErrors7d")}
              </p>
            </div>
          </div>

          {/* Exceptions / Issues Section */}
          {reconciliationData.exceptions?.length > 0 && (
            <div className="glass-panel p-6">
              <div className="flex items-center gap-2 mb-4">
                <div className="w-2 h-2 rounded-full bg-destructive animate-pulse" />
                <h3 className="text-lg font-bold">
                  {t(
                    "reconciliation.issuesFound",
                    reconciliationData.exceptions.length.toString(),
                  )}
                </h3>
              </div>
              <div className="space-y-2">
                {reconciliationData.exceptions.map((ex, i) => (
                  <div
                    key={i}
                    className={`flex items-start gap-3 p-3 rounded-lg ${ex.severity === "critical" ? "bg-destructive/10 border border-destructive/30" : "bg-warning/10 border border-warning/30"}`}
                  >
                    <div
                      className={`w-2 h-2 rounded-full mt-1.5 shrink-0 ${ex.severity === "critical" ? "bg-destructive" : "bg-warning"}`}
                    />
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium text-foreground">
                        {ex.description}
                      </p>
                      <p className="text-xs text-muted-foreground mt-0.5">
                        {ex.type} · {formatDate(ex.createdAtUtc)}
                        {ex.id &&
                          ex.id !== "00000000-0000-0000-0000-000000000000" && (
                            <span className="ml-2 font-mono">
                              ID: {ex.id.slice(0, 8)}
                            </span>
                          )}
                      </p>
                    </div>
                    <span
                      className={`text-xs px-2 py-0.5 rounded font-medium ${ex.severity === "critical" ? "bg-destructive/20 text-destructive" : "bg-warning/20 text-warning"}`}
                    >
                      {ex.severity === "critical"
                        ? t("reconciliation.critical")
                        : t("reconciliation.warning")}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Three-Way Match Table: Order ↔ Payment ↔ Fulfillment */}
          <div className="glass-panel p-6">
            <h3 className="text-lg font-bold mb-4">
              {t("reconciliation.threeWayMatch")}
            </h3>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-muted">
                  <tr>
                    <th className="text-left p-3">
                      {t("reconciliation.threeOrder")}
                    </th>
                    <th className="text-left p-3">{t("report.brand")}</th>
                    <th className="text-left p-3">{t("report.fuel")}</th>
                    <th className="text-right p-3">
                      {t("report.amountHeader")}
                    </th>
                    <th className="text-left p-3">
                      {t("reconciliation.threePayment")}
                    </th>
                    <th className="text-left p-3">
                      {t("reconciliation.threeFulfillment")}
                    </th>
                    <th className="text-right p-3">
                      {t("reconciliation.threeVouchExp")}
                    </th>
                    <th className="text-right p-3">
                      {t("reconciliation.threeVouchDel")}
                    </th>
                    <th className="text-left p-3">
                      {t("reconciliation.threeMatch")}
                    </th>
                    <th className="text-left p-3">
                      {t("reconciliation.threeRefund")}
                    </th>
                    <th className="text-left p-3">
                      {t("reconciliation.threeAge")}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {reconciliationData.threeWayMatch?.items?.map((row) => {
                    const matchColor =
                      row.matchStatus === "OK"
                        ? "text-success bg-success/10"
                        : row.matchStatus === "PARTIAL"
                          ? "text-warning bg-warning/10"
                          : row.matchStatus === "UNFULFILLED"
                            ? "text-destructive bg-destructive/10"
                            : row.matchStatus === "CANCELLED"
                              ? "text-muted-foreground bg-muted"
                              : row.matchStatus === "REFUNDED"
                                ? "text-destructive bg-destructive/10"
                                : row.matchStatus === "PARTIAL_REFUNDED"
                                  ? "text-info bg-info/10"
                                  : "text-info bg-info/10";
                    return (
                      <tr key={row.orderId} className="border-border">
                        <td className="p-3 font-mono text-xs">
                          {row.orderId.slice(0, 8)}
                        </td>
                        <td className="p-3 uppercase">{row.brand}</td>
                        <td className="p-3">
                          {fuelNameById.get(row.fuelType) || row.fuelType}
                        </td>
                        <td className="p-3 text-right">
                          {formatMoney(row.totalPrice)}
                        </td>
                        <td className="p-3">
                          <span
                            className={`px-1.5 py-0.5 rounded text-xs ${row.monobankStatus === "Success" ? "bg-success/20 text-success" : row.monobankStatus === "Pending" ? "bg-warning/20 text-warning" : "bg-muted text-muted-foreground"}`}
                          >
                            {row.monobankStatus
                              ? t(
                                  "monobank.status." +
                                    row.monobankStatus.toLowerCase(),
                                )
                              : "—"}
                          </span>
                        </td>
                        <td className="p-3 text-xs">
                          {row.orderStatus
                            ? t(
                                "order.status." +
                                  row.orderStatus.charAt(0).toLowerCase() +
                                  row.orderStatus.slice(1),
                              )
                            : "—"}
                        </td>
                        <td className="p-3 text-right font-mono">
                          {row.vouchersExpected}
                        </td>
                        <td className="p-3 text-right font-mono">
                          {row.vouchersDelivered}
                        </td>
                        <td className="p-3">
                          <span
                            className={`px-1.5 py-0.5 rounded text-xs font-medium ${matchColor}`}
                          >
                            {t(
                              "reconciliation.match" +
                                row.matchStatus.charAt(0) +
                                row.matchStatus.slice(1).toLowerCase(),
                            )}
                          </span>
                        </td>
                        <td className="p-3">
                          {row.refundStatus ? (
                            <div className="flex flex-col gap-0.5">
                              <span
                                className={`px-1.5 py-0.5 rounded text-xs w-fit ${row.refundStatus === "Completed" ? "bg-success/20 text-success" : row.refundStatus === "Failed" ? "bg-destructive/20 text-destructive" : "bg-warning/20 text-warning"}`}
                              >
                                {t(
                                  "purchases.refundStatus." + row.refundStatus,
                                )}
                              </span>
                              {row.refundedKopecks > 0 && (
                                <span className="text-xs font-mono text-muted-foreground">
                                  {(row.refundedKopecks / 100).toLocaleString()}{" "}
                                  ₴
                                </span>
                              )}
                            </div>
                          ) : (
                            "—"
                          )}
                        </td>
                        <td className="p-3 text-xs text-muted-foreground">
                          {row.daysSinceCreated}d
                        </td>
                      </tr>
                    );
                  })}
                  {(!reconciliationData.threeWayMatch?.items ||
                    reconciliationData.threeWayMatch.items.length === 0) && (
                    <tr>
                      <td
                        colSpan={11}
                        className="p-8 text-center text-muted-foreground"
                      >
                        {t("reconciliation.noOrderData")}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {/* Voucher Funnel */}
          <div className="glass-panel p-6">
            <h3 className="text-lg font-bold mb-4">
              {t("reconciliation.voucherFunnel")}
            </h3>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              {reconciliationData.voucherFunnel?.map((item) => {
                const colors: Record<string, string> = {
                  Imported: "bg-info/10 border-info/30 text-info",
                  Available: "bg-success/10 border-success/30 text-success",
                  Assigned: "bg-warning/10 border-warning/30 text-warning",
                  Used: "bg-info/10 border-info/30 text-info",
                  Expired: "bg-muted border-border text-muted-foreground",
                  DecodeIntegrityFailed:
                    "bg-destructive/10 border-destructive/30 text-destructive",
                };
                const c =
                  colors[item.status] ||
                  "bg-muted border-border text-muted-foreground";
                return (
                  <div
                    key={item.status}
                    className={`border rounded-lg p-4 text-center ${c}`}
                  >
                    <p className="text-2xl font-bold">{item.count}</p>
                    <p className="text-xs mt-1">
                      {t(
                        "status." +
                          item.status.charAt(0).toLowerCase() +
                          item.status.slice(1),
                      )}
                    </p>
                    <p className="text-xs opacity-60">{item.totalLiters}L</p>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Profit Summary */}
          {reconciliationData.revenueSummary?.length > 0 && (
            <div className="glass-panel p-6">
              <h3 className="text-lg font-bold mb-4">
                {t("report.profitSummary")}
              </h3>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-muted">
                    <tr>
                      <th className="text-left p-3">{t("report.period")}</th>
                      <th className="text-right p-3">
                        {t("report.orders_short")}
                      </th>
                      <th className="text-right p-3">{t("report.profit")}</th>
                      <th className="text-right p-3">{t("report.avgOrder")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {reconciliationData.revenueSummary.map((m, i) => (
                      <tr key={i} className="border-border">
                        <td className="p-3">
                          {m.year}-{String(m.month).padStart(2, "0")}
                        </td>
                        <td className="p-3 text-right font-mono">
                          {m.orderCount}
                        </td>
                        <td className="p-3 text-right font-mono text-success">
                          {m.revenueUah.toLocaleString()} ₴
                        </td>
                        <td className="p-3 text-right font-mono text-muted-foreground">
                          {m.orderCount > 0
                            ? `${(m.revenueUah / m.orderCount).toLocaleString()} ₴`
                            : "—"}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {/* The page: one row per order, and the table shows fifty of them. */}
              {reconciliationData.threeWayMatch?.totalCount > 0 && (
                <div className="flex items-center justify-between pt-3">
                  <span className="text-sm text-muted-foreground">
                    {reconPage} /{" "}
                    {Math.max(
                      1,
                      Math.ceil(
                        (reconciliationData.threeWayMatch?.totalCount ?? 0) /
                          (reconciliationData.threeWayMatch?.pageSize || 50),
                      ),
                    )}{" "}
                    · {reconciliationData.threeWayMatch?.totalCount ?? 0}
                  </span>
                  <div className="flex items-center gap-1">
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={reconPage <= 1}
                      onClick={() => setReconPage((p) => Math.max(1, p - 1))}
                      className="h-8"
                    >
                      <ChevronLeft className="w-4 h-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={!reconciliationData.threeWayMatch?.hasNextPage}
                      onClick={() => setReconPage((p) => p + 1)}
                      className="h-8"
                    >
                      <ChevronRight className="w-4 h-4" />
                    </Button>
                  </div>
                </div>
              )}
            </div>
          )}
        </>
      ) : (
        <div className="text-center text-muted-foreground py-12">
          {t("reconciliation.loadFailed")}
        </div>
      )}
    </div>
  );
}
