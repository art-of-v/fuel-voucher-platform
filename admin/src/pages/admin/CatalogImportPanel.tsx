import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { FileUp, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { useSession } from "@/lib/session-store";
import { errorMessage } from "@/lib/error-message";
import type { AdminSupplier, CatalogImportResult } from "@/lib/api-types";

/**
 * The catalog import: a supplier's PDF of vouchers, uploaded and parsed into stock.
 *
 * <p>
 * A PDF is one supplier's delivery, and the vouchers it creates have to be settled with that
 * supplier - the brand printed on them cannot say who issued them. So the supplier is chosen
 * before the upload, not guessed per voucher.
 */
export default function CatalogImportPanel() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const user = useSession((s) => s.user);

  const [importFiles, setImportFiles] = useState<File[]>([]);
  const [importSupplierId, setImportSupplierId] = useState<string>("");
  const [isDragging, setIsDragging] = useState(false);
  const [isImporting, setIsImporting] = useState(false);
  const [importStatus, setImportStatus] = useState<
    "idle" | "processing" | "completed" | "error"
  >("idle");
  const [importResult, setImportResult] = useState({
    success: 0,
    errors: 0,
    existing: 0,
    modelUsed: "",
  });
  const [importErrorLines, setImportErrorLines] = useState<
    { pageNumber: number; voucherNumber: string | null; reason: string }[]
  >([]);
  const [importProgress, setImportProgress] = useState({
    processed: 0,
    total: 0,
  });
  const [importErrorMsg, setImportErrorMsg] = useState("");

  const { data: suppliers = [] } = useQuery<AdminSupplier[]>({
    queryKey: ["suppliers"],
    queryFn: () =>
      apiRequest<void, AdminSupplier[]>("GET", "/api/admin/suppliers"),
    enabled: !!user,
  });

  const activeSuppliers = suppliers.filter((s) => s.isActive);

  const startImport = async () => {
    if (importFiles.length === 0) return;
    // Refuse before the upload rather than letting the backend reject the whole file
    // after a 300s parse: the supplier decides who the stock is settled with.
    if (!importSupplierId) {
      setImportStatus("error");
      setImportErrorMsg(t("import.supplierRequired"));
      return;
    }
    setIsImporting(true);
    setImportStatus("processing");
    setImportErrorMsg("");
    setImportResult({ success: 0, errors: 0, existing: 0, modelUsed: "" });
    setImportErrorLines([]);
    const formData = new FormData();
    importFiles.forEach((file) => formData.append("file", file));
    formData.append("supplierId", importSupplierId);
    try {
      const result = await apiRequest<FormData, CatalogImportResult>(
        "POST",
        "/api/voucher-catalog/import",
        formData,
        undefined,
        300_000,
        0,
      );

      setImportProgress({ processed: 1, total: 1 });
      setImportStatus(result.failed > 0 ? "error" : "completed");
      setImportResult({
        success: result.imported || 0,
        errors: result.failed || 0,
        existing: result.duplicates || 0,
        modelUsed: "",
      });
      setImportErrorLines(result.errors || []);
    } catch (e: unknown) {
      console.error("Import failed:", e, {
        url: "/api/voucher-catalog/import",
        method: "POST",
        timeoutMs: 300_000,
        retries: 0,
        files: importFiles.map((f) => ({
          name: f.name,
          size: f.size,
          type: f.type,
        })),
      });
      setImportStatus("error");
      setImportErrorMsg(errorMessage(e));
      // Don't overwrite if we already set partial results above
      setImportResult((prev) => {
        if (prev.success > 0 || prev.existing > 0) return prev;
        return {
          success: 0,
          errors: importFiles.length,
          existing: 0,
          modelUsed: "",
        };
      });
    }
    setIsImporting(false);
    queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
    setImportFiles([]);
  };

  return (
    <>
      <div className="glass-panel p-8 mb-8">
        <div
          className={`flex flex-col items-center justify-center border-2 border-dashed rounded-lg p-12 transition-colors ${isDragging ? "border-primary bg-primary/10" : "border-border hover:bg-foreground/5"}`}
          onDragOver={(e) => {
            e.preventDefault();
            setIsDragging(true);
          }}
          onDragLeave={() => setIsDragging(false)}
          onDrop={(e) => {
            e.preventDefault();
            setIsDragging(false);
            if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
              setImportFiles(Array.from(e.dataTransfer.files));
            }
          }}
        >
          <FileUp className="w-12 h-12 text-muted-foreground mb-4" />
          <h3 className="text-xl font-bold mb-2">{t("import.title")}</h3>
          <p className="text-muted-foreground mb-6 text-center max-w-md">
            {t("import.description")}
          </p>

          {importFiles.length > 0 && (
            <div className="mb-4 text-center">
              <p className="text-sm text-muted-foreground">
                {t("import.filesSelected", importFiles.length.toString())}
              </p>
              <ul className="text-sm font-mono text-primary mt-1">
                {importFiles.map((f, i) => (
                  <li key={i}>
                    {f.name} ({Math.round(f.size / 1024)}KB)
                  </li>
                ))}
              </ul>
            </div>
          )}
          <div className="flex gap-4 items-end">
            <div className="flex-1 min-w-0">
              <Select
                value={importSupplierId}
                onValueChange={setImportSupplierId}
              >
                <SelectTrigger>
                  <SelectValue placeholder={t("import.supplier")} />
                </SelectTrigger>
                <SelectContent>
                  {activeSuppliers.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {activeSuppliers.length === 0 && (
                <p className="text-xs text-muted-foreground mt-1">
                  {t("import.noSuppliers")}
                </p>
              )}
            </div>
            <Input
              type="file"
              multiple
              className="hidden"
              id="file-upload"
              onChange={(e) => {
                if (e.target.files && e.target.files.length > 0) {
                  setImportFiles(Array.from(e.target.files));
                  e.target.value = ""; // Allow re-selecting the same file
                }
              }}
            />
            <Button
              variant="outline"
              onClick={() => document.getElementById("file-upload")?.click()}
            >
              {t("import.clickSelect")}
            </Button>
            <Button
              onClick={startImport}
              disabled={isImporting || importFiles.length === 0}
            >
              {isImporting ? (
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
              ) : (
                <FileUp className="w-4 h-4 mr-2" />
              )}
              {t("import.start")}
            </Button>
          </div>
          {importFiles.length > 0 && (
            <div className="mt-4 text-success">
              {t("import.filesSelected", importFiles.length.toString())}
            </div>
          )}
        </div>
      </div>

      {importStatus !== "idle" && (
        <div
          className={`border rounded-xl p-4 mb-6 ${importStatus === "error" ? "bg-destructive/10 border-destructive/30" : "bg-muted border-border"}`}
        >
          <div className="flex justify-between text-sm mb-2">
            <span className="text-muted-foreground font-bold">
              {t("common.status")}:
              <span
                className={
                  importStatus === "completed"
                    ? "text-success uppercase ml-2"
                    : importStatus === "error"
                      ? "text-destructive uppercase ml-2"
                      : "text-info animate-pulse uppercase ml-2"
                }
              >
                {importStatus === "completed"
                  ? t("import.completed").toUpperCase()
                  : importStatus === "error"
                    ? t("import.failed").toUpperCase()
                    : t("import.processing").replace("...", "").toUpperCase()}
              </span>
            </span>
            <span className="text-muted-foreground">
              {importStatus === "error"
                ? t("import.errorOccurred")
                : `${importProgress.processed} / ${importProgress.total} ${t("import.processed").toLowerCase()}`}
            </span>
            {(importStatus === "completed" || importStatus === "error") && (
              <button
                onClick={() => setImportStatus("idle")}
                className="text-xs text-muted-foreground hover:text-foreground underline ml-2"
              >
                {t("import.close")}
              </button>
            )}
          </div>
          {importResult.modelUsed && (
            <div className="text-xs text-muted-foreground mb-2">
              {t("import.model")}:{" "}
              <span className="text-info font-mono">
                {importResult.modelUsed}
              </span>
            </div>
          )}
          <div className="w-full bg-muted rounded-full h-2.5 mb-2 overflow-hidden">
            <div
              className={`h-2.5 rounded-full transition-all duration-500 ${
                importStatus === "completed"
                  ? "bg-success"
                  : importStatus === "error"
                    ? "bg-destructive"
                    : "bg-info animate-pulse"
              }`}
              style={{
                width: `${importProgress.total > 0 ? (importProgress.processed / importProgress.total) * 100 : 0}%`,
              }}
            ></div>
          </div>
          <div className="flex flex-col gap-2">
            {importStatus === "error" && (
              <div className="flex flex-col gap-1">
                <div className="text-xs text-destructive">
                  {t("import.errorOccurred")}
                </div>
                {importErrorMsg && (
                  <div className="text-xs text-destructive font-mono break-all">
                    {importErrorMsg}
                  </div>
                )}
              </div>
            )}
            {importStatus === "processing" && (
              <div className="text-xs text-muted-foreground">
                {t("import.largeFileNote")}
              </div>
            )}
            <div className="flex justify-between text-xs text-muted-foreground pt-1 border-t border-border">
              <span className="text-success">
                {t("import.successful")}: {importResult.success}
              </span>
              <span className="text-destructive">
                {t("import.failedCount")}: {importResult.errors}
              </span>
              <span className="text-warning">
                {t("import.duplicates")}: {importResult.existing}
              </span>
            </div>
            {importErrorLines.length > 0 && (
              <div className="flex flex-col gap-1 mt-1 max-h-48 overflow-y-auto pt-2 border-t border-border">
                <div className="text-xs text-destructive font-semibold">
                  {t("import.failedRows")}:
                </div>
                {importErrorLines.map((er, i) => (
                  <div key={i} className="text-xs flex gap-2 items-start">
                    <span className="text-muted-foreground font-mono shrink-0">
                      #{er.pageNumber}
                      {er.voucherNumber ? ` · ${er.voucherNumber}` : ""}
                    </span>
                    <span className="text-destructive break-words">
                      {er.reason && er.reason.trim()
                        ? er.reason
                        : t("import.unknownReason")}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </>
  );
}
