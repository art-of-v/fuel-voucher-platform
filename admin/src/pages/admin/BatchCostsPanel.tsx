import { useMutation, useQueryClient } from "@tanstack/react-query";
import { FileUp, Loader2, Package } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { BatchCostTimeline } from "@/components/BatchCostTimeline";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { useSession } from "@/lib/session-store";
import type { ImportBatchCost, ParsedInvoiceResponse } from "@/lib/api-types";

/**
 * The batch's cost per fuel: what it cost, what it costs across the whole pool, and where the
 * number came from.
 *
 * <p>
 * The cost is a DRAFT until the operator saves it: the panel shows what the batch says, what the
 * operator has typed so far, and the blended cost the whole pool is at. A batch that is not
 * costed has no cost - which is not the same as costing it zero, and the panel never says zero.
 */
export default function BatchCostsPanel({
  importId,
  batchCosts,
  batchCostInputs,
  onBatchCostInputsChange,
}: {
  importId: string;
  batchCosts: ImportBatchCost[];
  batchCostInputs: Record<string, string>;
  onBatchCostInputsChange: (next: Record<string, string>) => void;
}) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const user = useSession((s) => s.user);

  const setBatchCostMutation = useMutation({
    mutationFn: async ({
      fuelTypeId,
      costPerLiter,
    }: {
      fuelTypeId: string;
      costPerLiter: number;
    }) => {
      await apiRequest<{ costPerLiter: number }, unknown>(
        "PUT",
        `/api/admin/voucher-imports/${importId}/batch-costs/${encodeURIComponent(fuelTypeId)}`,
        { costPerLiter },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/voucher-imports", importId, "batch-costs"],
      });
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/voucher-imports", importId, "batch-pnl"],
      });
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/voucher-imports", importId, "vouchers"],
      });
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/voucher-imports"],
      });
      toast.success(t("imports.costSaved"));
    },
    onError: (e: Error) =>
      toast.error(`${t("imports.costSaveFailed")}: ${e.message}`),
  });

  // RQ-3: upload a supplier invoice (.xlsx) and pre-fill the per-fuel cost drafts from it.
  // Read-only on the server — the operator still reviews each row and saves via
  // setBatchCostMutation.
  const parseInvoiceMutation = useMutation({
    mutationFn: async (file: File) => {
      const fd = new FormData();
      fd.append("file", file);
      return await apiRequest<FormData, ParsedInvoiceResponse>(
        "POST",
        `/api/admin/voucher-imports/${importId}/parse-invoice`,
        fd,
        undefined,
        120_000,
        0,
      );
    },
    onSuccess: (result) => {
      const prefill: Record<string, string> = {};
      let matched = 0;
      for (const line of result.lines) {
        if (
          line.matched &&
          line.fuelTypeId &&
          line.costPerLiter != null &&
          line.costPerLiter > 0
        ) {
          prefill[line.fuelTypeId] = String(line.costPerLiter);
          matched++;
        }
      }
      if (matched > 0) {
        onBatchCostInputsChange({ ...batchCostInputs, ...prefill });
      }

      const issues: string[] = [];
      const unmatchedLines = result.lines.filter((l) => !l.matched).length;
      const warnings = result.lines.filter((l) => l.warning).length;
      if (unmatchedLines > 0) {
        issues.push(t("imports.invoiceUnmatchedLines", String(unmatchedLines)));
      }
      if (result.unmatchedImportFuels.length > 0) {
        issues.push(
          t(
            "imports.invoiceUncoveredFuels",
            result.unmatchedImportFuels.join(", "),
          ),
        );
      }
      if (warnings > 0) {
        issues.push(t("imports.invoiceWarnings", String(warnings)));
      }
      if (result.errors.length > 0) {
        issues.push(
          t("imports.invoiceRowErrors", String(result.errors.length)),
        );
      }
      const suffix = issues.length ? ` · ${issues.join(" · ")}` : "";

      if (matched > 0) {
        toast.success(
          `${t("imports.invoiceParsed", String(matched))}${suffix}`,
        );
      } else {
        toast.error(`${t("imports.invoiceNoMatch")}${suffix}`);
      }
    },
    onError: (e: Error) =>
      toast.error(`${t("imports.invoiceParseFailed")}: ${e.message}`),
  });

  return (
    <div className="glass-panel p-6">
      <div className="flex items-center justify-between gap-2 mb-1">
        <div className="flex items-center gap-2">
          <Package className="w-5 h-5 text-primary" />
          <h3 className="text-lg font-bold">{t("imports.batchCostsTitle")}</h3>
        </div>
        <input
          id="invoice-upload"
          type="file"
          accept=".xlsx"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) parseInvoiceMutation.mutate(file);
            e.target.value = ""; // allow re-selecting the same file
          }}
        />
        <Button
          variant="outline"
          size="sm"
          disabled={parseInvoiceMutation.isPending}
          onClick={() => document.getElementById("invoice-upload")?.click()}
        >
          {parseInvoiceMutation.isPending ? (
            <Loader2 className="w-4 h-4 mr-2 animate-spin" />
          ) : (
            <FileUp className="w-4 h-4 mr-2" />
          )}
          {parseInvoiceMutation.isPending
            ? t("imports.invoiceParsing")
            : t("imports.parseInvoice")}
        </Button>
      </div>
      <p className="text-sm text-muted-foreground mb-4">
        {t("imports.batchCostsHint")}
      </p>
      <div className="space-y-3">
        {batchCosts.map((b) => {
          const draft =
            batchCostInputs[b.fuelTypeId] ??
            (b.costPerLiter != null ? String(b.costPerLiter) : "");
          const parsed = parseFloat(draft);
          const canSave =
            !isNaN(parsed) && parsed > 0 && parsed !== b.costPerLiter;
          return (
            <div
              key={b.fuelTypeId}
              className="flex flex-wrap items-end gap-4 rounded-lg border border-border p-4"
            >
              <div className="min-w-[180px] flex-1">
                <div className="font-semibold">
                  {b.fuelTypeName || b.fuelTypeId}
                </div>
                <div className="text-xs uppercase text-muted-foreground">
                  {b.brand}
                </div>
                <div className="text-xs text-muted-foreground mt-1">
                  {t("imports.batchStock", b.voucherCount, b.totalLiters)}
                </div>
              </div>
              <div className="w-40">
                <label className="text-xs text-muted-foreground">
                  {t("imports.costPerLiter")}
                </label>
                <Input
                  type="number"
                  step="0.01"
                  min="0"
                  value={draft}
                  onChange={(e) =>
                    onBatchCostInputsChange({
                      ...batchCostInputs,
                      [b.fuelTypeId]: e.target.value,
                    })
                  }
                />
              </div>
              <div className="min-w-[120px]">
                <div className="text-xs text-muted-foreground">
                  {t("imports.blendedCost")}
                </div>
                <div className="font-mono text-sm">
                  {b.blendedCostPerLiter != null
                    ? `${b.blendedCostPerLiter} ₴`
                    : "—"}
                </div>
              </div>
              <Button
                size="sm"
                disabled={!canSave || setBatchCostMutation.isPending}
                onClick={() =>
                  setBatchCostMutation.mutate({
                    fuelTypeId: b.fuelTypeId,
                    costPerLiter: parsed,
                  })
                }
              >
                {t("imports.saveCost")}
              </Button>
              {/* Why this cost is what it is (#178). The panel above edits a number; this shows where
                the number came from, so an operator can defend it rather than restate it. */}
              <BatchCostTimeline importId={importId} enabled={!!user} />
            </div>
          );
        })}
      </div>
    </div>
  );
}
