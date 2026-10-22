import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { BarChart, FileSignature, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { apiRequest } from "@/lib/api-client";
import { useAdminUserOptions } from "@/lib/admin-lookups";
import { useI18n } from "@/lib/i18n";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/lib/session-store";
import { formatDate } from "@/lib/utils";
import type { AdminReport } from "@/lib/api-types";
import ReconciliationActModal from "./ReconciliationActModal";

/** The report's quick date presets. */
type QuickDate = "yesterday" | "week" | "month" | "30days";

const toDateInput = (d: Date) => {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
};

const formatDisplayDate = (iso: string) => {
  if (!iso) return "";
  const parts = iso.split("-");
  if (parts.length !== 3) return iso;
  return `${parts[2]}.${parts[1]}.${parts[0]}`;
};

/**
 * The report: what a customer (or the whole platform) bought, redeemed and still holds.
 *
 * <p>
 * The report is on demand: it runs only after the operator asks for it, and the acquirer's
 * statement act - the one document in this system that compares us against an independent
 * source - sits above it, so it does not depend on the form having been filled in.
 */
export default function ReportsTab() {
  const { t } = useI18n();
  const user = useSession((s) => s.user);

  const [reportUserId, setReportUserId] = useState("");
  const [reportFromDate, setReportFromDate] = useState("");
  const [reportToDate, setReportToDate] = useState("");
  const [reportTrigger, setReportTrigger] = useState(0);
  const [showReconciliationAct, setShowReconciliationAct] = useState(false);

  const { data: userOptions = [] } = useAdminUserOptions();

  const { data: reportData, isLoading: isReportLoading } =
    useQuery<AdminReport>({
      queryKey: [
        "/api/admin/report",
        reportUserId,
        reportFromDate,
        reportToDate,
        reportTrigger,
      ],
      enabled: !!user && reportTrigger > 0,
      // On-demand report (runs only after an explicit "generate" trigger); opt out of the global
      // background poll so it is not silently re-run every 15s.
      refetchInterval: false,
      queryFn: async () => {
        const params = new URLSearchParams();
        if (reportUserId) params.set("userId", reportUserId);
        if (reportFromDate) {
          params.set("fromDate", new Date(reportFromDate).toISOString());
        }
        if (reportToDate) {
          params.set(
            "toDate",
            new Date(reportToDate + "T23:59:59").toISOString(),
          );
        }
        const qs = params.toString();
        return await apiRequest<void, AdminReport>(
          "GET",
          `/api/admin/report${qs ? "?" + qs : ""}`,
        );
      },
    });

  const setReportQuickDate = (preset: QuickDate) => {
    const now = new Date();
    if (preset === "yesterday") {
      now.setDate(now.getDate() - 1);
      const s = toDateInput(now);
      setReportFromDate(s);
      setReportToDate(s);
    } else if (preset === "week") {
      const monday = new Date(now);
      monday.setDate(now.getDate() - ((now.getDay() + 6) % 7));
      setReportFromDate(toDateInput(monday));
      setReportToDate(toDateInput(now));
    } else if (preset === "30days") {
      const past = new Date(now);
      past.setDate(now.getDate() - 30);
      setReportFromDate(toDateInput(past));
      setReportToDate(toDateInput(now));
    } else {
      const first = new Date(now.getFullYear(), now.getMonth(), 1);
      setReportFromDate(toDateInput(first));
      setReportToDate(toDateInput(now));
    }
  };

  return (
    <div className="space-y-6 animate-in fade-in duration-300">
      <h2 className="text-2xl font-bold flex items-center gap-2">
        <BarChart className="w-6 h-6 text-primary" />
        {t("report.title")}
      </h2>

      {/* Filters */}
      <div className="glass-panel p-4 flex flex-wrap gap-4 items-end">
        <div>
          <label className="text-xs text-muted-foreground block mb-1">
            {t("report.allUsers")}
          </label>
          <div className="flex items-center gap-2">
            <select
              value={reportUserId}
              onChange={(e) => setReportUserId(e.target.value)}
              className="glass-input rounded-lg px-3 py-2 text-sm text-foreground w-64"
            >
              <option value="">{t("report.allUsers")}</option>
              {userOptions.map((u) => {
                const label =
                  u.firstName || u.lastName
                    ? `${u.firstName || ""} ${u.lastName || ""}`.trim()
                    : u.phone || u.id?.slice(0, 8) || u.id;
                return (
                  <option key={u.id} value={u.id}>
                    {label}
                  </option>
                );
              })}
            </select>
            <span className="text-xs text-muted-foreground">
              ({userOptions.length} users loaded)
            </span>
          </div>
        </div>
        <div>
          <label className="text-xs text-muted-foreground block mb-1">
            {t("report.from")}
          </label>
          <input
            type="text"
            placeholder="dd.mm.yyyy"
            value={reportFromDate ? formatDisplayDate(reportFromDate) : ""}
            onChange={(e) => {
              const v = e.target.value.replace(/[^\d]/g, "");
              if (v.length === 8) {
                setReportFromDate(
                  `${v.slice(4, 8)}-${v.slice(2, 4)}-${v.slice(0, 2)}`,
                );
              } else if (v.length < 8) setReportFromDate("");
            }}
            className="glass-input rounded-lg px-3 py-2 text-sm text-foreground"
          />
        </div>
        <div>
          <label className="text-xs text-muted-foreground block mb-1">
            {t("report.to")}
          </label>
          <input
            type="text"
            placeholder="dd.mm.yyyy"
            value={reportToDate ? formatDisplayDate(reportToDate) : ""}
            onChange={(e) => {
              const v = e.target.value.replace(/[^\d]/g, "");
              if (v.length === 8) {
                setReportToDate(
                  `${v.slice(4, 8)}-${v.slice(2, 4)}-${v.slice(0, 2)}`,
                );
              } else if (v.length < 8) setReportToDate("");
            }}
            className="glass-input rounded-lg px-3 py-2 text-sm text-foreground"
          />
        </div>
        <div className="flex gap-1 items-end">
          <Button
            onClick={() => setReportQuickDate("yesterday")}
            variant="outline"
            size="sm"
            className="h-9 text-xs"
          >
            {t("report.yesterday")}
          </Button>
          <Button
            onClick={() => setReportQuickDate("week")}
            variant="outline"
            size="sm"
            className="h-9 text-xs"
          >
            {t("report.week")}
          </Button>
          <Button
            onClick={() => setReportQuickDate("30days")}
            variant="outline"
            size="sm"
            className="h-9 text-xs"
          >
            {t("report.30days")}
          </Button>
          <Button
            onClick={() => setReportQuickDate("month")}
            variant="outline"
            size="sm"
            className="h-9 text-xs"
          >
            {t("report.month")}
          </Button>
        </div>
        <div className="flex gap-2 items-end">
          <Button
            onClick={() => setReportTrigger((t) => t + 1)}
            variant="default"
            size="sm"
            className="gap-2"
          >
            <BarChart className="w-4 h-4" />
            {t("report.generate")}
          </Button>
          {reportData && (
            <Button
              onClick={() => setShowReconciliationAct(true)}
              variant="outline"
              size="sm"
              className="gap-2"
            >
              <FileSignature className="w-4 h-4" />
              {t("report.reconciliationAct")}
            </Button>
          )}
        </div>
      </div>

      {isReportLoading ? (
        <div className="flex items-center gap-2 text-muted-foreground">
          <Loader2 className="w-5 h-5 animate-spin" />
          {t("report.loading")}
        </div>
      ) : reportData ? (
        <>
          {/* Summary Cards */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="glass-panel p-4">
              <p className="text-sm text-muted-foreground">
                {t("report.totalSpent")}
              </p>
              <p className="text-2xl font-bold text-success">
                {formatMoney(reportData.summary.totalSpent)}
              </p>
              <p className="text-xs text-muted-foreground">
                {reportData.summary.totalOrders} {t("report.orders")}
              </p>
            </div>
            <div className="glass-panel p-4">
              <p className="text-sm text-muted-foreground">
                {t("report.purchased")}
              </p>
              <p className="text-2xl font-bold text-warning">
                {reportData.summary.vouchersPurchased}
              </p>
              <p className="text-xs text-muted-foreground">
                {t(
                  "report.litersPurchased",
                  reportData.summary.totalLitersPurchased.toFixed(0),
                )}
              </p>
            </div>
            <div className="glass-panel p-4">
              <p className="text-sm text-muted-foreground">
                {t("report.used")}
              </p>
              <p className="text-2xl font-bold text-destructive">
                {reportData.summary.vouchersUsed}
              </p>
            </div>
            <div className="glass-panel p-4">
              <p className="text-sm text-muted-foreground">
                {t("report.litersUsed")}
              </p>
              <p className="text-2xl font-bold text-info">
                {reportData.summary.totalLitersUsed.toFixed(0)} L
              </p>
            </div>
          </div>

          {/* Monthly Breakdown */}
          {reportData.monthlyBreakdown?.length > 0 && (
            <div className="glass-panel p-6">
              <h3 className="text-lg font-bold mb-4">
                {t("report.monthlyBreakdown")}
              </h3>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-muted">
                    <tr>
                      <th className="text-left p-3">{t("report.month")}</th>
                      <th className="text-right p-3">{t("report.amount")}</th>
                      <th className="text-right p-3">
                        {t("report.purchased")}
                      </th>
                      <th className="text-right p-3">{t("report.used")}</th>
                      <th className="text-right p-3">
                        {t("report.litersUsed")}
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {reportData.monthlyBreakdown.map((mb) => (
                      <tr key={mb.month} className="border-border">
                        <td className="p-3 font-medium">{mb.month}</td>
                        <td className="p-3 text-right text-success">
                          {formatMoney(mb.totalSpent)}
                        </td>
                        <td className="p-3 text-right text-warning">
                          {mb.vouchersPurchased}
                        </td>
                        <td className="p-3 text-right text-destructive">
                          {mb.vouchersUsed}
                        </td>
                        <td className="p-3 text-right text-info">
                          {mb.totalLitersUsed.toFixed(0)}L
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* Payments Table */}
          <div className="glass-panel p-6">
            <h3 className="text-lg font-bold mb-4">
              {t("report.payments", reportData.payments.length)}
            </h3>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-muted">
                  <tr>
                    <th className="text-left p-3">{t("report.paymentId")}</th>
                    <th className="text-left p-3">{t("report.brand")}</th>
                    <th className="text-left p-3">{t("report.fuel")}</th>
                    <th className="text-right p-3">
                      {t("report.amountHeader")}
                    </th>
                    <th className="text-right p-3">{t("report.liters")}</th>
                    <th className="text-right p-3">{t("report.quantity")}</th>
                    <th className="text-left p-3">{t("report.bank")}</th>
                    <th className="text-left p-3">{t("report.status")}</th>
                    <th className="text-left p-3">{t("report.date")}</th>
                  </tr>
                </thead>
                <tbody>
                  {reportData.payments.map((p) => (
                    <tr key={p.orderId} className="border-border">
                      <td className="p-3 font-mono text-xs text-muted-foreground">
                        {p.orderId.slice(0, 8)}
                      </td>
                      <td className="p-3 uppercase text-xs">
                        {p.brand || "—"}
                      </td>
                      <td className="p-3">{p.fuelName || "—"}</td>
                      <td className="p-3 text-right font-mono">
                        {formatMoney(p.amount)}
                      </td>
                      <td className="p-3 text-right">
                        {p.liters / p.quantity}L
                      </td>
                      <td className="p-3 text-right">{p.quantity}</td>
                      <td className="p-3">
                        {p.monobankStatus ? (
                          <span
                            className={`px-1.5 py-0.5 rounded text-xs ${
                              p.monobankStatus === "Success"
                                ? "bg-success/20 text-success"
                                : "bg-muted text-muted-foreground"
                            }`}
                          >
                            {t(
                              "monobank.status." +
                                p.monobankStatus.toLowerCase(),
                            )}
                          </span>
                        ) : (
                          <span className="text-xs text-muted-foreground">
                            —
                          </span>
                        )}
                      </td>
                      <td className="p-3">
                        <span
                          className={`px-1.5 py-0.5 rounded text-xs ${
                            p.status === "Fulfilled"
                              ? "bg-success/20 text-success"
                              : p.status === "Cancelled" ||
                                  p.status === "Refunded"
                                ? "bg-destructive/20 text-destructive"
                                : "bg-warning/20 text-warning"
                          }`}
                        >
                          {t(
                            "order.status." +
                              p.status.charAt(0).toLowerCase() +
                              p.status.slice(1),
                          )}
                        </span>
                      </td>
                      <td className="p-3 text-xs text-muted-foreground">
                        {formatDate(p.createdAtUtc)}
                      </td>
                    </tr>
                  ))}
                  {reportData.payments.length === 0 && (
                    <tr>
                      <td
                        colSpan={9}
                        className="p-8 text-center text-muted-foreground"
                      >
                        {t("report.noPayments")}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
            {reportData.payments.length > 0 && (
              <div className="mt-4 text-xs text-muted-foreground">
                {reportData.payments.filter((p) => p.monobankInvoiceId).length >
                  0 && (
                  <span>
                    {t(
                      "report.monobankInvoices",
                      reportData.payments.filter((p) => p.monobankInvoiceId)
                        .length,
                    )}
                  </span>
                )}
              </div>
            )}
          </div>

          {/* Redemptions Table */}
          <div className="glass-panel p-6">
            <h3 className="text-lg font-bold mb-4">
              {t("report.redemptions", reportData.redemptions.length)}
            </h3>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-muted">
                  <tr>
                    <th className="text-left p-3">
                      {t("report.redemptionId")}
                    </th>
                    <th className="text-left p-3">{t("report.brand")}</th>
                    <th className="text-left p-3">{t("report.fuelName")}</th>
                    <th className="text-right p-3">{t("report.liters")}</th>
                    <th className="text-left p-3">{t("report.redeemedAt")}</th>
                  </tr>
                </thead>
                <tbody>
                  {reportData.redemptions.map((r) => (
                    <tr key={r.voucherId} className="border-border">
                      <td className="p-3 font-mono text-xs text-muted-foreground">
                        {r.voucherId.slice(0, 8)}
                      </td>
                      <td className="p-3 uppercase">{r.brand || "—"}</td>
                      <td className="p-3">{r.fuelName || "—"}</td>
                      <td className="p-3 text-right">{r.liters}L</td>
                      <td className="p-3 text-xs text-muted-foreground">
                        {formatDate(r.redeemedAt)}
                      </td>
                    </tr>
                  ))}
                  {reportData.redemptions.length === 0 && (
                    <tr>
                      <td
                        colSpan={5}
                        className="p-8 text-center text-muted-foreground"
                      >
                        {t("report.noRedemptions")}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      ) : (
        <div className="text-center text-muted-foreground py-12">
          {t("report.noData")}
        </div>
      )}

      {showReconciliationAct && reportData && (
        <ReconciliationActModal
          report={reportData}
          reportUserId={reportUserId}
          onClose={() => setShowReconciliationAct(false)}
        />
      )}
    </div>
  );
}
