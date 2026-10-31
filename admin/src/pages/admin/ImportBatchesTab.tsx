import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronRight } from "lucide-react";
import { useI18n } from "@/lib/i18n";
import { useSession } from "@/lib/session-store";
import { formatDateTime } from "@/lib/utils";
import type { ImportBatch } from "@/lib/api-types";
import ImportBatchDetail from "./ImportBatchDetail";

/**
 * The import batches: every supplier delivery the platform has taken, and one of them open.
 *
 * <p>
 * A batch is one supplier's delivery. The list is this tab's own responsibility; the open batch -
 * its vouchers, costs, P&L and the two whole-batch buttons - is {@link ImportBatchDetail}.
 */
export default function ImportBatchesTab() {
  const { t } = useI18n();
  const user = useSession((s) => s.user);

  const [selectedImportId, setSelectedImportId] = useState<string | null>(null);

  const { data: importsList = [] } = useQuery<ImportBatch[]>({
    queryKey: ["/api/admin/voucher-imports"],
    enabled: !!user,
    staleTime: 30_000,
  });

  const selectedImport =
    importsList.find((i) => i.id === selectedImportId) ?? null;

  return (
    <div className="space-y-6 animate-in fade-in duration-300">
      {selectedImportId ? (
        <ImportBatchDetail
          importBatch={selectedImport}
          importId={selectedImportId}
          onBack={() => setSelectedImportId(null)}
        />
      ) : (
        <>
          {/* List View */}
          <div>
            <h2 className="text-xl font-bold">{t("imports.title")}</h2>
            <p className="text-sm text-muted-foreground mt-1">
              {t("imports.decodeIntegrityHint")}
            </p>
          </div>
          <div className="glass-panel overflow-x-auto">
            <table className="w-full">
              <thead className="bg-muted">
                <tr>
                  <th className="text-left p-4">{t("imports.file")}</th>
                  <th className="text-left p-4">{t("imports.supplier")}</th>
                  <th className="text-left p-4">{t("common.status")}</th>
                  <th className="text-left p-4">{t("imports.pages")}</th>
                  <th className="text-left p-4">{t("imports.vouchers")}</th>
                  <th className="text-left p-4">{t("imports.imported")}</th>
                  <th className="text-left p-4">{t("imports.duplicates")}</th>
                  <th className="text-left p-4">{t("imports.errors")}</th>
                  <th className="text-left p-4">{t("imports.warnings")}</th>
                  <th className="text-left p-4">{t("imports.created")}</th>
                  <th className="text-left p-4"></th>
                </tr>
              </thead>
              <tbody>
                {importsList.map((imp) => (
                  <tr
                    key={imp.id}
                    className="border-border hover:bg-foreground/5 cursor-pointer"
                    onClick={() => setSelectedImportId(imp.id)}
                  >
                    <td className="p-4 font-medium">{imp.fileName}</td>
                    <td className="p-4 text-sm">{imp.supplierName ?? "—"}</td>
                    <td className="p-4">
                      <span
                        className={`px-2 py-1 rounded text-xs font-bold uppercase border ${
                          imp.status === "Completed"
                            ? "bg-success/10 text-success border-success/20"
                            : imp.status === "Failed"
                              ? "bg-destructive/10 text-destructive border-destructive/20"
                              : "bg-warning/10 text-warning border-warning/20"
                        }`}
                      >
                        {t("status." + imp.status.toLowerCase())}
                      </span>
                    </td>
                    <td className="p-4">{imp.pageCount}</td>
                    <td className="p-4">{imp.voucherCount}</td>
                    <td className="p-4 text-success">{imp.importedCount}</td>
                    <td className="p-4 text-warning">{imp.duplicateCount}</td>
                    <td className="p-4 text-destructive">
                      {imp.failedCount + imp.decodeIntegrityFailedCount}
                    </td>
                    <td className="p-4 text-warning">
                      {imp.decodeIntegrityWarningCount}
                    </td>
                    <td className="p-4 text-sm text-muted-foreground">
                      {formatDateTime(imp.startedAtUtc)}
                    </td>
                    <td className="p-4">
                      <ChevronRight className="w-4 h-4 text-muted-foreground" />
                    </td>
                  </tr>
                ))}
                {importsList.length === 0 && (
                  <tr>
                    <td
                      colSpan={11}
                      className="p-16 text-center text-muted-foreground"
                    >
                      {t("imports.none")}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
