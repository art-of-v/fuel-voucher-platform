import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, CheckCircle, ChevronRight, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "sonner";
import { apiRequest } from "@/lib/api-client";
import { activationErrorMessage } from "@/lib/activation-error";
import { useI18n } from "@/lib/i18n";
import { useSession } from "@/lib/session-store";
import { formatDateTime } from "@/lib/utils";
import {
  VOUCHER_CONFIRMED_BADGE,
  VOUCHER_MARKED_BADGE,
} from "@/lib/voucher-status-style";
import type {
  ImportBatch,
  ImportBatchCost,
  ImportBatchPnl,
  ImportBatchVouchersPage,
} from "@/lib/api-types";
import BatchCostsPanel from "./BatchCostsPanel";
import BatchPnlPanel from "./BatchPnlPanel";

/** How many vouchers of a batch the table shows at a time. */
const BATCH_VOUCHER_PAGE_SIZE = 50;

/**
 * The import batches: every supplier delivery the platform has taken, and one of them open.
 *
 * <p>
 * A batch is one supplier's delivery. Its two whole-batch buttons act on the WHOLE batch and the
 * server decides which rows that is: the tab holds a page of it, so the ids in the browser would
 * be a page, and a button that says "activate all" and activates a page is a button that lies.
 */
export default function ImportBatchesTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const user = useSession((s) => s.user);

  const [selectedImportId, setSelectedImportId] = useState<string | null>(null);
  const [importVouchersPage, setImportVouchersPage] = useState(1);
  // Per-fuel cost/L drafts on the import-detail batch-costs panel (fuelTypeId → raw input text).
  const [batchCostInputs, setBatchCostInputs] = useState<
    Record<string, string>
  >({});

  const { data: importsList = [] } = useQuery<ImportBatch[]>({
    queryKey: ["/api/admin/voucher-imports"],
    enabled: !!user,
    staleTime: 30_000,
  });

  const selectedImport =
    importsList.find((i) => i.id === selectedImportId) ?? null;

  // The batch's vouchers, a page at a time. The endpoint answers with the page and the size of
  // the whole batch, because the two buttons below act on the whole batch and have to be able
  // to say what they are about to do.
  const { data: importVouchersPageData } = useQuery<ImportBatchVouchersPage>({
    queryKey: [
      "/api/admin/voucher-imports",
      selectedImportId,
      "vouchers",
      importVouchersPage,
    ],
    staleTime: 30_000,
    queryFn: async () => {
      if (!selectedImportId) {
        return {
          items: [],
          totalCount: 0,
          page: 1,
          pageSize: BATCH_VOUCHER_PAGE_SIZE,
          totalPages: 0,
          hasNextPage: false,
          hasPreviousPage: false,
        };
      }
      return await apiRequest<void, ImportBatchVouchersPage>(
        "GET",
        `/api/admin/voucher-imports/${selectedImportId}/vouchers?page=${importVouchersPage}&pageSize=${BATCH_VOUCHER_PAGE_SIZE}`,
      );
    },
    enabled: !!user && !!selectedImportId,
  });

  const importVouchers = importVouchersPageData?.items ?? [];
  const importVouchersTotal = importVouchersPageData?.totalCount ?? 0;
  const importVouchersTotalPages = importVouchersPageData?.totalPages ?? 0;

  const { data: batchCosts = [] } = useQuery<ImportBatchCost[]>({
    queryKey: ["/api/admin/voucher-imports", selectedImportId, "batch-costs"],
    queryFn: async () => {
      if (!selectedImportId) return [];
      const res = await apiRequest<void, ImportBatchCost[]>(
        "GET",
        `/api/admin/voucher-imports/${selectedImportId}/batch-costs`,
      );
      return res ?? [];
    },
    enabled: !!user && !!selectedImportId,
  });

  const { data: batchPnl = [] } = useQuery<ImportBatchPnl[]>({
    queryKey: ["/api/admin/voucher-imports", selectedImportId, "batch-pnl"],
    queryFn: async () => {
      if (!selectedImportId) return [];
      const res = await apiRequest<void, ImportBatchPnl[]>(
        "GET",
        `/api/admin/voucher-imports/${selectedImportId}/batch-pnl`,
      );
      return res ?? [];
    },
    enabled: !!user && !!selectedImportId,
  });

  // One voucher's own buttons, on a row: the id-based action, which acts on what it is given.
  const activateOneVoucherMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await apiRequest<{ action: string; ids: string[] }, unknown>(
        "POST",
        "/api/admin/vouchers/bulk-action",
        { action: "activate", ids },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/voucher-imports"],
      });
      if (selectedImportId) {
        queryClient.invalidateQueries({
          queryKey: [
            "/api/admin/voucher-imports",
            selectedImportId,
            "vouchers",
          ],
        });
      }
    },
    onError: (e: Error) => toast.error(activationErrorMessage(e, t)),
  });

  const deactivateOneVoucherMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await apiRequest<{ action: string; ids: string[] }, unknown>(
        "POST",
        "/api/admin/vouchers/bulk-action",
        { action: "deactivate", ids },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/voucher-imports"],
      });
      if (selectedImportId) {
        queryClient.invalidateQueries({
          queryKey: [
            "/api/admin/voucher-imports",
            selectedImportId,
            "vouchers",
          ],
        });
      }
    },
    onError: (e: Error) => toast.error(activationErrorMessage(e, t)),
  });

  // The whole batch, in one action, decided by the server.
  const activateVoucherMutation = useMutation({
    mutationFn: async ({ action }: { action: string }) => {
      await apiRequest<{ action: string }, unknown>(
        "POST",
        `/api/admin/voucher-imports/${selectedImportId}/bulk-action`,
        { action },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/voucher-imports"],
      });
      if (selectedImportId) {
        queryClient.invalidateQueries({
          queryKey: [
            "/api/admin/voucher-imports",
            selectedImportId,
            "vouchers",
          ],
        });
      }
    },
    onError: (e: Error) => toast.error(activationErrorMessage(e, t)),
  });

  const deactivateVoucherMutation = useMutation({
    mutationFn: async ({ action }: { action: string }) => {
      await apiRequest<{ action: string }, unknown>(
        "POST",
        `/api/admin/voucher-imports/${selectedImportId}/bulk-action`,
        { action },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/voucher-imports"],
      });
      if (selectedImportId) {
        queryClient.invalidateQueries({
          queryKey: [
            "/api/admin/voucher-imports",
            selectedImportId,
            "vouchers",
          ],
        });
      }
    },
    onError: (e: Error) => toast.error(activationErrorMessage(e, t)),
  });

  return (
    <div className="space-y-6 animate-in fade-in duration-300">
      {selectedImportId ? (
        <>
          {/* Detail View */}
          <div className="flex items-center justify-between">
            <Button
              variant="ghost"
              onClick={() => setSelectedImportId(null)}
              className="text-muted-foreground hover:text-foreground"
            >
              <ArrowLeft className="w-4 h-4 mr-2" /> {t("imports.back")}
            </Button>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={activateVoucherMutation.isPending}
                onClick={() => {
                  // The whole batch, decided by the server. The tab holds a page of it now, so
                  // the ids in the browser would be a page - and a button that says "activate
                  // all" and activates a page is a button that lies.
                  activateVoucherMutation.mutate({ action: "activate" });
                }}
              >
                <CheckCircle className="w-4 h-4 mr-1" />{" "}
                {t("imports.activateAll")}
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={deactivateVoucherMutation.isPending}
                onClick={() => {
                  deactivateVoucherMutation.mutate({ action: "deactivate" });
                }}
              >
                <XCircle className="w-4 h-4 mr-1" />{" "}
                {t("imports.deactivateAll")}
              </Button>
            </div>
          </div>

          {batchCosts.length > 0 && (
            <BatchCostsPanel
              importId={selectedImportId}
              batchCosts={batchCosts}
              batchCostInputs={batchCostInputs}
              onBatchCostInputsChange={setBatchCostInputs}
            />
          )}

          <BatchPnlPanel batchPnl={batchPnl} />

          <div className="space-y-1">
            <p className="text-sm text-muted-foreground">
              {t("imports.decodeIntegrityHint")}
            </p>
            <p className="text-sm">
              {t("imports.supplier")}:{" "}
              <span className="font-medium">
                {selectedImport?.supplierName ?? "—"}
              </span>
            </p>
          </div>

          <div className="glass-panel overflow-x-auto">
            <table className="w-full">
              <thead className="bg-muted">
                <tr>
                  <th className="text-left p-4">{t("vouchers.brand")}</th>
                  <th className="text-left p-4">{t("vouchers.fuelType")}</th>
                  <th className="text-left p-4">{t("vouchers.volume")}</th>
                  <th className="text-left p-4">{t("imports.number")}</th>
                  <th className="text-left p-4">{t("vouchers.expires")}</th>
                  <th className="text-left p-4">{t("common.status")}</th>
                  <th className="text-left p-4">
                    {t("imports.decodeIntegrity")}
                  </th>
                  <th className="text-left p-4">{t("common.actions")}</th>
                </tr>
              </thead>
              <tbody>
                {importVouchers.map((v) => {
                  const statusColors: Record<string, string> = {
                    Imported: "bg-warning/10 text-warning border-warning/20",
                    Available: "bg-success/10 text-success border-success/20",
                    DecodeIntegrityWarning:
                      "bg-warning/10 text-warning border-warning/20",
                    DecodeIntegrityFailed:
                      "bg-destructive/10 text-destructive border-destructive/20",
                    Assigned: "bg-info/10 text-info border-info/20",
                    Used: VOUCHER_CONFIRMED_BADGE,
                    PendingReconciliation: VOUCHER_MARKED_BADGE,
                    Expired:
                      "bg-destructive/10 text-destructive border-destructive/20",
                    Deactivated: "bg-muted text-muted-foreground border-border",
                  };
                  return (
                    <tr
                      key={v.id}
                      className="border-border hover:bg-foreground/5"
                    >
                      <td className="p-4 uppercase">{v.brand}</td>
                      <td className="p-4">{v.fuelTypeName || v.fuelTypeId}</td>
                      <td className="p-4">{v.liters}L</td>
                      <td className="p-4 font-mono text-xs">
                        {v.voucherNumber}
                      </td>
                      <td className="p-4 text-sm">
                        {v.supplierExpirationDate}
                        {v.customerExpirationDate !==
                          v.supplierExpirationDate && (
                          <div className="text-[10px] text-muted-foreground">
                            {t("vouchers.customerExpires")}:{" "}
                            {v.customerExpirationDate}
                          </div>
                        )}
                      </td>
                      <td className="p-4">
                        <span
                          className={`px-2 py-1 rounded text-xs font-bold uppercase border ${statusColors[v.status] || "bg-muted text-muted-foreground"}`}
                        >
                          {t(
                            "status." +
                              v.status.charAt(0).toLowerCase() +
                              v.status.slice(1),
                          )}
                        </span>
                      </td>
                      <td className="p-4">
                        {v.matrixMismatchPercent != null ? (
                          <span
                            className={`text-xs font-mono ${
                              v.matrixMismatchPercent <= 1
                                ? "text-success"
                                : v.matrixMismatchPercent <= 2
                                  ? "text-warning"
                                  : "text-destructive"
                            }`}
                          >
                            {v.matrixMismatchPercent.toFixed(2)}% (
                            {v.matrixMismatchedModules}/{v.matrixTotalModules})
                          </span>
                        ) : (
                          <span className="text-xs text-muted-foreground">
                            —
                          </span>
                        )}
                      </td>
                      <td className="p-4">
                        <div className="flex gap-1">
                          {(v.status === "Imported" ||
                            v.status === "DecodeIntegrityWarning" ||
                            v.status === "Expired" ||
                            v.status === "Deactivated") && (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="text-success hover:brightness-125"
                              disabled={activateVoucherMutation.isPending}
                              onClick={() =>
                                activateOneVoucherMutation.mutate([v.id])
                              }
                            >
                              <CheckCircle className="w-3.5 h-3.5" />
                            </Button>
                          )}
                          {(v.status === "Available" ||
                            v.status === "Imported" ||
                            v.status === "DecodeIntegrityWarning") && (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="text-destructive hover:brightness-125"
                              disabled={deactivateVoucherMutation.isPending}
                              onClick={() =>
                                deactivateOneVoucherMutation.mutate([v.id])
                              }
                            >
                              <XCircle className="w-3.5 h-3.5" />
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
                {importVouchers.length === 0 && (
                  <tr>
                    <td
                      colSpan={8}
                      className="p-16 text-center text-muted-foreground"
                    >
                      {t("imports.noVouchers")}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
            {importVouchersTotalPages > 1 && (
              <div className="flex items-center justify-between gap-2 mt-3">
                <span className="text-xs text-muted-foreground">
                  {importVouchersTotal} {t("imports.vouchers")}
                </span>
                <div className="flex items-center gap-2">
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={importVouchersPage <= 1}
                    onClick={() =>
                      setImportVouchersPage((p) => Math.max(1, p - 1))
                    }
                  >
                    {t("imports.page")}
                  </Button>
                  <span className="text-xs">
                    {t(
                      "imports.pageOf",
                      String(importVouchersPage),
                      String(importVouchersTotalPages),
                    )}
                  </span>
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={importVouchersPage >= importVouchersTotalPages}
                    onClick={() => setImportVouchersPage((p) => p + 1)}
                  >
                    {t("imports.page")}
                  </Button>
                </div>
              </div>
            )}
          </div>
        </>
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
