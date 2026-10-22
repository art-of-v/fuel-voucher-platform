import { Button } from "@/components/ui/button";
import { useAdminUserOptions } from "@/lib/admin-lookups";
import { useI18n } from "@/lib/i18n";
import { formatDate } from "@/lib/utils";
import type { AdminReport } from "@/lib/api-types";

/** One line of the act: a movement of money, in or out. */
interface ActRow {
  type: string;
  date: string;
  desc: string;
  debit: number | null;
  credit: number | null;
  id: string;
}

/**
 * The acquirer's statement act: what we received, what we delivered, what we gave back, and
 * what is left.
 *
 * <p>
 * It is the one document in this system that compares us against an independent source, so it is
 * printed rather than read: the table is the document, and the two rules at the bottom are where
 * the operator and the customer sign.
 */
export default function ReconciliationActModal({
  report,
  reportUserId,
  onClose,
}: {
  report: AdminReport;
  reportUserId: string;
  onClose: () => void;
}) {
  const { t } = useI18n();
  const { data: userOptions = [] } = useAdminUserOptions();

  const totalPaid = report.payments.reduce((s, p) => s + p.amount, 0);
  const totalLiters = report.payments.reduce((s, p) => s + p.liters, 0);
  const avgPrice = totalLiters > 0 ? totalPaid / totalLiters : 0;

  const rows: ActRow[] = [];
  report.payments.forEach((p) => {
    rows.push({
      type: "payment",
      id: p.orderId,
      date: p.createdAtUtc,
      desc: t(
        "reconciliation.paymentDesc",
        p.fuelName || t("reconciliation.fuel"),
        (p.liters / p.quantity).toString(),
        p.quantity.toString(),
      ),
      debit: p.amount,
      credit: null,
    });
    // Confirmed refund = money returned for undelivered vouchers.
    if (p.refundStatus === "Completed" && p.refundedKopecks > 0) {
      rows.push({
        type: "refund",
        id: p.orderId + "-refund",
        date: p.createdAtUtc,
        desc: t(
          "reconciliation.refundDesc",
          p.fuelName || t("reconciliation.fuel"),
        ),
        debit: null,
        credit: p.refundedKopecks / 100,
      });
    }
  });
  report.redemptions.forEach((r) => {
    rows.push({
      type: "redemption",
      id: r.voucherId,
      date: r.redeemedAt,
      desc: t(
        "reconciliation.redemptionDesc",
        r.fuelName || t("reconciliation.fuel"),
        r.liters.toString(),
      ),
      debit: null,
      credit: r.liters * avgPrice,
    });
  });
  rows.sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());

  const totalDebit = report.payments.reduce((s, p) => s + p.amount, 0);
  const totalCredit = report.redemptions.reduce(
    (s, r) => s + r.liters * avgPrice,
    0,
  );
  const totalRefunded = report.payments.reduce(
    (s, p) =>
      s + (p.refundStatus === "Completed" ? (p.refundedKopecks ?? 0) / 100 : 0),
    0,
  );
  const totalDelivered = report.payments.reduce(
    (s, p) => s + (p.fulfilledValueKopecks ?? 0) / 100,
    0,
  );
  const credit = totalCredit + totalRefunded;
  const balance = totalDebit - credit;

  const customer = reportUserId
    ? userOptions.find((u) => u.id === reportUserId)
    : null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-md p-4 overflow-auto">
      <div
        className="bg-white text-black rounded-xl p-8 max-w-4xl w-full animate-in zoom-in-50 duration-200 max-h-[95vh] overflow-y-auto print:shadow-none print:rounded-none print:p-4 print:max-h-none"
        onClick={(e: React.MouseEvent) => e.stopPropagation()}
      >
        <style>{`@media print { .no-print { display: none !important; } body { background: white; } th, tr { -webkit-print-color-adjust: exact; print-color-adjust: exact; } }`}</style>
        <div className="flex justify-between items-center mb-6 no-print">
          <h2 className="text-xl font-bold">
            {t("reconciliation.modalTitle")}
          </h2>
          <div className="flex gap-2">
            <Button
              onClick={() => window.print()}
              variant="default"
              size="sm"
              className="bg-blue-600 text-white hover:bg-blue-700"
            >
              {t("common.print")}
            </Button>
            <Button onClick={onClose} variant="outline" size="sm">
              {t("common.close")}
            </Button>
          </div>
        </div>

        <div className="text-center mb-8">
          <h1 className="text-2xl font-bold uppercase">
            {t("reconciliation.documentTitle")}
          </h1>
          <p className="text-muted-foreground mt-1">
            {t("reconciliation.periodLabel")}{" "}
            {report.period.from
              ? formatDate(report.period.from)
              : t("reconciliation.periodFrom")}
            {" — "}
            {report.period.to
              ? formatDate(report.period.to)
              : t("reconciliation.periodTo")}
          </p>
          {customer && (
            <p className="text-gray-700 mt-2 font-medium">
              {t("reconciliation.userLabel")} {customer.firstName || ""}{" "}
              {customer.lastName || ""} ({customer.phone || ""})
            </p>
          )}
        </div>

        <table className="w-full border-collapse mb-6">
          <thead>
            <tr className="bg-gray-100">
              <th className="border border-gray-300 p-2 text-left text-sm">
                {t("table.number")}
              </th>
              <th className="border border-gray-300 p-2 text-left text-sm">
                {t("table.date")}
              </th>
              <th className="border border-gray-300 p-2 text-left text-sm">
                {t("table.description")}
              </th>
              <th className="border border-gray-300 p-2 text-right text-sm">
                {t("table.debit")}
              </th>
              <th className="border border-gray-300 p-2 text-right text-sm">
                {t("table.credit")}
              </th>
            </tr>
          </thead>
          <tbody>
            {report.payments.length === 0 && report.redemptions.length === 0 ? (
              <tr>
                <td
                  colSpan={5}
                  className="border border-gray-300 p-4 text-center text-muted-foreground"
                >
                  {t("reconciliation.noOperations")}
                </td>
              </tr>
            ) : (
              rows.map((row, i) => (
                <tr key={row.id}>
                  <td className="border border-gray-300 p-2 text-sm font-mono">
                    {i + 1}
                  </td>
                  <td className="border border-gray-300 p-2 text-sm">
                    {formatDate(row.date)}
                  </td>
                  <td className="border border-gray-300 p-2 text-sm">
                    {row.desc}
                  </td>
                  <td className="border border-gray-300 p-2 text-sm text-right font-mono">
                    {row.debit !== null ? row.debit.toLocaleString() : "—"}
                  </td>
                  <td className="border border-gray-300 p-2 text-sm text-right font-mono">
                    {row.credit !== null ? row.credit.toLocaleString() : "—"}
                  </td>
                </tr>
              ))
            )}
          </tbody>
          {report.payments.length > 0 || report.redemptions.length > 0 ? (
            <tfoot>
              <tr className="font-bold bg-gray-50">
                <td
                  colSpan={3}
                  className="border border-gray-300 p-2 text-sm text-right"
                >
                  {t("reconciliation.total")}
                </td>
                <td className="border border-gray-300 p-2 text-sm text-right font-mono">
                  {totalDebit.toLocaleString()}
                </td>
                <td className="border border-gray-300 p-2 text-sm text-right font-mono">
                  {credit.toLocaleString()}
                </td>
              </tr>
              <tr className="text-muted-foreground">
                <td
                  colSpan={4}
                  className="border border-gray-300 p-2 text-sm text-right"
                >
                  {t("reconciliation.received")}
                </td>
                <td className="border border-gray-300 p-2 text-sm text-right font-mono">
                  {totalDebit.toLocaleString()} {t("table.uah")}
                </td>
              </tr>
              <tr className="text-muted-foreground">
                <td
                  colSpan={4}
                  className="border border-gray-300 p-2 text-sm text-right"
                >
                  {t("reconciliation.deliveredValue")}
                </td>
                <td className="border border-gray-300 p-2 text-sm text-right font-mono">
                  {totalDelivered.toLocaleString()} {t("table.uah")}
                </td>
              </tr>
              <tr className="text-muted-foreground">
                <td
                  colSpan={4}
                  className="border border-gray-300 p-2 text-sm text-right"
                >
                  {t("reconciliation.refundedLabel")}
                </td>
                <td className="border border-gray-300 p-2 text-sm text-right font-mono">
                  {totalRefunded.toLocaleString()} {t("table.uah")}
                </td>
              </tr>
              <tr className="font-bold bg-blue-50">
                <td
                  colSpan={4}
                  className="border border-gray-300 p-2 text-sm text-right"
                >
                  {t("reconciliation.balance")}
                </td>
                <td
                  className={`border border-gray-300 p-2 text-sm text-right font-mono ${balance >= 0 ? "text-green-700" : "text-red-700"}`}
                >
                  {balance.toLocaleString()} {t("table.uah")}
                </td>
              </tr>
            </tfoot>
          ) : null}
        </table>

        <div className="grid grid-cols-2 gap-8 mt-10 text-sm">
          <div>
            <p className="font-medium mb-8">
              {t("reconciliation.adminSignature")}
            </p>
            <div className="border-b border-gray-400 mt-10" />
          </div>
          <div>
            <p className="font-medium mb-8">
              {t("reconciliation.userSignature")}
            </p>
            <div className="border-b border-gray-400 mt-10" />
          </div>
        </div>
      </div>
    </div>
  );
}
