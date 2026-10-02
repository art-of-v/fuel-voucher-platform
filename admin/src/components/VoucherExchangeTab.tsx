import { useEffect, useState } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Replace, Loader2, FileUp, X, AlertTriangle, CheckCircle2, Check,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { apiRequest } from "@/lib/api-client";
import { toast } from "sonner";

// --- API shapes (operator→provider stock exchange, #104) --------------------------------------
interface AttentionItem {
  id: string;
  voucherNumber: string;
  provider: string;
  fuelTypeId: string;
  fuelName: string | null;
  liters: number;
  expirationDate: string;
  status: string;
  daysLeft: number;
}
interface AttentionResponse {
  data: AttentionItem[];
  thresholdDays: number;
  providers: string[];
  fuels: string[];
}
interface ImportResponse {
  importId: string;
  imported: number;
  duplicates: number;
  failed: number;
  verificationFailed: number;
  verifiedWithWarnings: number;
  errors?: { pageNumber: number; voucherNumber: string | null; reason: string }[];
}
interface CostFuel {
  fuelTypeId: string;
  fuelName: string | null;
  provider: string;
  newCount: number;
  newLiters: number;
  oldBlendedCostPerLiter: number | null;
  existingBatchCostPerLiter: number | null;
}
interface CostContext {
  importId: string;
  fuels: CostFuel[];
}
interface ConfirmResult {
  success: boolean;
  error: string | null;
  exchangeBatchId: string | null;
  expiredCount: number;
  newActivatedCount: number;
  pairedCount: number;
  unpairedOldCount: number;
  unpairedNewCount: number;
  blendedCostPerLiter: number | null;
  blendedByFuel: { fuelTypeId: string; blendedCostPerLiter: number | null }[];
}

type Step = 1 | 2 | 3 | 4; // select · upload · cost · result

const STEPS: { n: Step; key: string }[] = [
  { n: 1, key: "voucherExchange.step.select" },
  { n: 2, key: "voucherExchange.step.upload" },
  { n: 3, key: "voucherExchange.step.cost" },
  { n: 4, key: "voucherExchange.step.result" },
];

export default function VoucherExchangeTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [step, setStep] = useState<Step>(1);

  // Step 1 — old-stock selection
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [providerFilter, setProviderFilter] = useState("");
  const [fuelFilter, setFuelFilter] = useState("");

  // Step 2 — new PDF import
  const [importId, setImportId] = useState<string | null>(null);
  const [importFile, setImportFile] = useState<File | null>(null);
  const [isImporting, setIsImporting] = useState(false);
  const [importResult, setImportResult] = useState<ImportResponse | null>(null);
  const [importError, setImportError] = useState("");

  // Step 3 — surcharge + per-fuel cost + invoice
  const [surcharge, setSurcharge] = useState("");
  const [costs, setCosts] = useState<Record<string, string>>({});
  const [invoiceNumber, setInvoiceNumber] = useState("");
  const [invoiceDate, setInvoiceDate] = useState("");

  // Step 4 — confirm result
  const [result, setResult] = useState<ConfirmResult | null>(null);

  const { data: attention, isLoading } = useQuery<AttentionResponse>({
    queryKey: ["/api/admin/voucher-exchange/attention"],
    queryFn: () => apiRequest<unknown, AttentionResponse>("GET", "/api/admin/voucher-exchange/attention"),
  });
  const items = attention?.data ?? [];
  const filtered = items.filter(
    (i) => (!providerFilter || i.provider === providerFilter) && (!fuelFilter || i.fuelName === fuelFilter),
  );

  const { data: costContext, isLoading: costLoading } = useQuery<CostContext>({
    queryKey: ["/api/admin/voucher-exchange/cost-context", importId],
    enabled: !!importId,
    queryFn: () => apiRequest<unknown, CostContext>("GET", `/api/admin/voucher-exchange/cost-context?importId=${encodeURIComponent(importId!)}`),
  });

  // First time cost context lands, seed each fuel's cost/liter from its existing or old blended cost.
  useEffect(() => {
    if (!costContext) return;
    setCosts((prev) => {
      const next = { ...prev };
      for (const f of costContext.fuels) {
        if (next[f.fuelTypeId] === undefined) {
          const seed = f.existingBatchCostPerLiter ?? f.oldBlendedCostPerLiter ?? null;
          next[f.fuelTypeId] = seed != null ? String(seed) : "";
        }
      }
      return next;
    });
  }, [costContext]);

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["/api/admin/voucher-exchange/attention"] });
    queryClient.invalidateQueries({ queryKey: ["/api/admin/voucher-exchange/attention/count"] });
    queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
    queryClient.invalidateQueries({ queryKey: ["/api/admin/imports"] });
  };

  const toggle = (id: string) =>
    setSelected((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  const selectAll = () => setSelected(new Set(filtered.map((i) => i.id)));
  const clearSel = () => setSelected(new Set());

  // planning #136 — the backend now folds the whole surcharge into each fuel's cost on confirm
  // (spread evenly across all new liters). Mirror that math here read-only so the operator sees the
  // effective cost rise live; the old manual "apply" button is gone so the fold can't be skipped or
  // double-counted.
  const totalNewLiters = (costContext?.fuels ?? []).reduce((s, f) => s + f.newLiters, 0);
  const surchargeNum = parseFloat(surcharge || "0");
  const surchargePerLiter = surchargeNum > 0 && totalNewLiters > 0 ? surchargeNum / totalNewLiters : 0;
  const effectiveCost = (base: string): number | null => {
    const b = parseFloat(base ?? "");
    if (isNaN(b)) return null;
    return Math.round((b + surchargePerLiter) * 10000) / 10000;
  };

  const runImport = async () => {
    if (!importFile) return;
    setIsImporting(true);
    setImportError("");
    setImportResult(null);
    try {
      const fd = new FormData();
      fd.append("file", importFile);
      const res = await apiRequest<unknown, ImportResponse>(
        "POST", "/api/voucher-catalog/import", fd, undefined, 300_000, 0,
      );
      setImportResult(res);
      setImportId(res.importId);
      setImportFile(null);
    } catch (e) {
      setImportError(e instanceof Error ? e.message : String(e));
    }
    setIsImporting(false);
  };

  const confirmMutation = useMutation({
    mutationFn: () =>
      apiRequest<unknown, ConfirmResult>("POST", "/api/admin/voucher-exchange/confirm", {
        oldVoucherIds: Array.from(selected),
        newImportId: importId,
        costs: (costContext?.fuels ?? []).map((f) => ({
          fuelTypeId: f.fuelTypeId,
          costPerLiter: parseFloat(costs[f.fuelTypeId] ?? "0"),
        })),
        surchargeUah: parseFloat(surcharge || "0"),
        invoiceNumber: invoiceNumber.trim() || null,
        invoiceDate: invoiceDate || null,
      }),
    onSuccess: (res) => {
      setResult(res);
      setStep(4);
      invalidate();
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const costsValid =
    !!costContext &&
    costContext.fuels.length > 0 &&
    costContext.fuels.every((f) => {
      const v = parseFloat(costs[f.fuelTypeId] ?? "");
      return !isNaN(v) && v > 0;
    }) &&
    parseFloat(surcharge || "0") >= 0;

  const resetAll = () => {
    setStep(1);
    setSelected(new Set());
    setProviderFilter("");
    setFuelFilter("");
    setImportId(null);
    setImportFile(null);
    setImportResult(null);
    setImportError("");
    setSurcharge("");
    setCosts({});
    setInvoiceNumber("");
    setInvoiceDate("");
    setResult(null);
  };

  const badge = (i: AttentionItem) =>
    i.daysLeft < 0 ? (
      <span className="px-1.5 py-0.5 text-[10px] font-bold rounded-full bg-destructive/15 text-destructive whitespace-nowrap">
        {t("voucherExchange.expired")}
      </span>
    ) : (
      <span className="px-1.5 py-0.5 text-[10px] font-bold rounded-full bg-warning/15 text-warning whitespace-nowrap tabular-nums">
        {t("voucherExchange.daysLeft", String(i.daysLeft))}
      </span>
    );

  return (
    <div className="space-y-4">
      {/* Stepper */}
      <div className="flex items-center gap-2 flex-wrap">
        {STEPS.map((s, idx) => (
          <div key={s.n} className="flex items-center gap-2">
            <div
              className={
                "flex items-center gap-2 px-3 py-1.5 rounded-lg text-sm font-medium border " +
                (step === s.n
                  ? "bg-primary/15 text-primary border-primary/25"
                  : step > s.n
                  ? "text-success border-transparent"
                  : "text-muted-foreground border-transparent")
              }
            >
              <span className="w-5 h-5 rounded-full flex items-center justify-center text-[11px] font-bold border border-current tabular-nums">
                {step > s.n ? <Check className="w-3 h-3" /> : s.n}
              </span>
              {t(s.key)}
            </div>
            {idx < STEPS.length - 1 && <span className="text-muted-foreground">›</span>}
          </div>
        ))}
      </div>

      {/* STEP 1 — select lapsing stock */}
      {step === 1 && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-2">
              <Select value={providerFilter || "__all__"} onValueChange={(v) => setProviderFilter(v === "__all__" ? "" : v)}>
                <SelectTrigger className="h-8 w-44"><SelectValue placeholder={t("voucherExchange.allProviders")} /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{t("voucherExchange.allProviders")}</SelectItem>
                  {(attention?.providers ?? []).map((p) => <SelectItem key={p} value={p}>{p}</SelectItem>)}
                </SelectContent>
              </Select>
              <Select value={fuelFilter || "__all__"} onValueChange={(v) => setFuelFilter(v === "__all__" ? "" : v)}>
                <SelectTrigger className="h-8 w-44"><SelectValue placeholder={t("voucherExchange.allFuels")} /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="__all__">{t("voucherExchange.allFuels")}</SelectItem>
                  {(attention?.fuels ?? []).map((f) => <SelectItem key={f} value={f}>{f}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground tabular-nums">{t("voucherExchange.selected", String(selected.size))}</span>
              <Button variant="outline" size="sm" onClick={selectAll} disabled={filtered.length === 0}>{t("voucherExchange.selectAll")}</Button>
              <Button variant="ghost" size="sm" onClick={clearSel} disabled={selected.size === 0}>{t("voucherExchange.clear")}</Button>
            </div>
          </div>

          {isLoading ? (
            <div className="flex items-center gap-2 text-muted-foreground p-8"><Loader2 className="w-5 h-5 animate-spin" />{t("common.loading")}</div>
          ) : filtered.length === 0 ? (
            <div className="text-center py-16 text-muted-foreground">
              <CheckCircle2 className="w-8 h-8 mx-auto mb-3 opacity-40" />
              <p className="text-sm">{t("voucherExchange.attention.empty")}</p>
            </div>
          ) : (
            <div className="overflow-x-auto rounded-lg border border-border">
              <table className="w-full text-sm">
                <thead className="bg-muted/50">
                  <tr>
                    <th className="w-10 p-3"></th>
                    <th className="text-left p-3 whitespace-nowrap">{t("voucherExchange.col.voucher")}</th>
                    <th className="text-left p-3 whitespace-nowrap">{t("voucherExchange.col.provider")}</th>
                    <th className="text-left p-3 whitespace-nowrap">{t("voucherExchange.col.fuel")}</th>
                    <th className="text-right p-3 whitespace-nowrap">{t("voucherExchange.col.liters")}</th>
                    <th className="text-right p-3 whitespace-nowrap">{t("voucherExchange.col.expiry")}</th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((i) => (
                    <tr key={i.id} className="border-t border-border hover:bg-muted/20 transition-colors cursor-pointer" onClick={() => toggle(i.id)}>
                      <td className="p-3 text-center">
                        <input type="checkbox" checked={selected.has(i.id)} onChange={() => toggle(i.id)} onClick={(e) => e.stopPropagation()} className="accent-primary" />
                      </td>
                      <td className="p-3 font-mono text-xs">{i.voucherNumber}</td>
                      <td className="p-3 text-muted-foreground">{i.provider}</td>
                      <td className="p-3 text-muted-foreground">{i.fuelName ?? i.fuelTypeId}</td>
                      <td className="p-3 text-right tabular-nums">{i.liters}</td>
                      <td className="p-3 text-right whitespace-nowrap"><div className="flex items-center justify-end gap-2"><span className="text-xs text-muted-foreground tabular-nums">{i.expirationDate}</span>{badge(i)}</div></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="flex justify-end">
            <Button onClick={() => setStep(2)} disabled={selected.size === 0}>{t("voucherExchange.next")}</Button>
          </div>
        </div>
      )}

      {/* STEP 2 — import the new provider PDF */}
      {step === 2 && (
        <div className="space-y-3">
          <div className="bg-card border border-border rounded-xl p-4 space-y-3">
            <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground flex items-center gap-2">
              <FileUp className="w-4 h-4 text-primary" />{t("voucherExchange.upload.title")}
            </h4>
            <p className="text-xs text-muted-foreground">{t("voucherExchange.upload.hint")}</p>
            <div className="flex items-center gap-2 flex-wrap">
              <Input
                type="file"
                accept=".pdf"
                className="hidden"
                id="exchange-import"
                onChange={(e) => { setImportFile(e.target.files?.[0] ?? null); setImportError(""); e.target.value = ""; }}
              />
              <Button variant="outline" size="sm" onClick={() => document.getElementById("exchange-import")?.click()}>
                <FileUp className="w-3.5 h-3.5 mr-1" />{t("voucherExchange.upload.choose")}
              </Button>
              {importFile && (
                <>
                  <span className="text-sm"><span className="text-muted-foreground">{t("voucherExchange.upload.selectedFile")}: </span><span className="font-mono text-primary">{importFile.name}</span></span>
                  <Button size="sm" onClick={runImport} disabled={isImporting}>
                    {isImporting ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <FileUp className="w-3.5 h-3.5 mr-1" />}
                    {t("voucherExchange.upload.run")}
                  </Button>
                  <Button variant="ghost" size="sm" onClick={() => setImportFile(null)} disabled={isImporting}><X className="w-3.5 h-3.5" /></Button>
                </>
              )}
            </div>
            {importError && <div className="bg-destructive/10 border border-destructive/30 rounded-lg p-3 text-sm text-destructive break-words">{importError}</div>}
            {importResult && (
              <div className="bg-muted border border-border rounded-lg p-3 text-sm flex gap-4 flex-wrap">
                <span className="text-success">{t("voucherExchange.upload.imported")}: {importResult.imported}</span>
                <span className="text-info">{t("voucherExchange.upload.duplicates")}: {importResult.duplicates}</span>
                <span className="text-destructive">{t("voucherExchange.upload.failed")}: {importResult.failed}</span>
              </div>
            )}
          </div>
          <div className="flex justify-between">
            <Button variant="ghost" onClick={() => setStep(1)}>{t("voucherExchange.back")}</Button>
            <Button onClick={() => setStep(3)} disabled={!importId || (importResult?.imported ?? 0) === 0}>{t("voucherExchange.next")}</Button>
          </div>
        </div>
      )}

      {/* STEP 3 — surcharge, per-fuel cost, invoice */}
      {step === 3 && (
        <div className="space-y-3">
          <div className="bg-card border border-border rounded-xl p-4 space-y-4">
            <div className="flex items-end gap-3 flex-wrap">
              <div className="flex flex-col gap-1">
                <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("voucherExchange.cost.surcharge")}</label>
                <Input type="number" step="any" min="0" value={surcharge} onChange={(e) => setSurcharge(e.target.value)} className="h-8 w-40 text-right" placeholder="0.00" />
              </div>
              <span className="text-xs text-muted-foreground pb-2">{t("voucherExchange.cost.surchargeHint")}</span>
            </div>

            {costLoading ? (
              <div className="flex items-center gap-2 text-muted-foreground p-4"><Loader2 className="w-5 h-5 animate-spin" />{t("common.loading")}</div>
            ) : (
              <div className="overflow-x-auto rounded-lg border border-border">
                <table className="w-full text-sm">
                  <thead className="bg-muted/50">
                    <tr>
                      <th className="text-left p-3">{t("voucherExchange.cost.fuel")}</th>
                      <th className="text-right p-3">{t("voucherExchange.cost.newCount")}</th>
                      <th className="text-right p-3">{t("voucherExchange.cost.newLiters")}</th>
                      <th className="text-right p-3">{t("voucherExchange.cost.oldBlended")}</th>
                      <th className="text-right p-3">{t("voucherExchange.cost.costPerLiter")}</th>
                      <th className="text-right p-3">{t("voucherExchange.cost.effective")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(costContext?.fuels ?? []).map((f) => (
                      <tr key={f.fuelTypeId} className="border-t border-border">
                        <td className="p-3">{f.fuelName ?? f.fuelTypeId} <span className="text-xs text-muted-foreground">({f.provider})</span></td>
                        <td className="p-3 text-right tabular-nums">{f.newCount}</td>
                        <td className="p-3 text-right tabular-nums">{f.newLiters}</td>
                        <td className="p-3 text-right tabular-nums text-muted-foreground">{f.oldBlendedCostPerLiter != null ? f.oldBlendedCostPerLiter.toFixed(4) : "—"}</td>
                        <td className="p-3 text-right">
                          <Input type="number" step="any" min="0" value={costs[f.fuelTypeId] ?? ""} onChange={(e) => setCosts((c) => ({ ...c, [f.fuelTypeId]: e.target.value }))} className="h-8 w-28 text-right ml-auto" placeholder="0.0000" />
                        </td>
                        <td className="p-3 text-right tabular-nums">
                          {(() => {
                            const eff = effectiveCost(costs[f.fuelTypeId] ?? "");
                            if (eff == null) return <span className="text-muted-foreground">—</span>;
                            return <span className={surchargePerLiter > 0 ? "font-semibold text-primary" : "text-muted-foreground"}>{eff.toFixed(4)}</span>;
                          })()}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="flex items-end gap-3 flex-wrap pt-2 border-t border-border">
              <div className="flex flex-col gap-1">
                <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("voucherExchange.cost.invoiceNumber")}</label>
                <Input value={invoiceNumber} onChange={(e) => setInvoiceNumber(e.target.value)} className="h-8 w-48" />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("voucherExchange.cost.invoiceDate")}</label>
                <Input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} className="h-8 w-44" />
              </div>
            </div>
          </div>

          {/* Pre-confirm summary (aggregate — exact 1:1 pairing within provider/fuel/liters is computed on confirm) */}
          <div className="bg-muted border border-border rounded-xl p-4 text-sm flex gap-6 flex-wrap">
            <span>{t("voucherExchange.confirm.oldSelected", String(selected.size))}</span>
            <span>{t("voucherExchange.confirm.newImported", String(importResult?.imported ?? 0))}</span>
            <span>{t("voucherExchange.confirm.surcharge")}: {parseFloat(surcharge || "0").toFixed(2)} ₴</span>
          </div>
          {!costsValid && (
            <div className="text-xs text-warning flex items-center gap-1"><AlertTriangle className="w-3 h-3" />{t("voucherExchange.cost.invalid")}</div>
          )}

          <div className="flex justify-between">
            <Button variant="ghost" onClick={() => setStep(2)}>{t("voucherExchange.back")}</Button>
            <Button onClick={() => confirmMutation.mutate()} disabled={!costsValid || selected.size === 0 || confirmMutation.isPending}>
              {confirmMutation.isPending ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <Replace className="w-3.5 h-3.5 mr-1" />}
              {t("voucherExchange.confirm.button")}
            </Button>
          </div>
        </div>
      )}

      {/* STEP 4 — result */}
      {step === 4 && result && (
        <div className="space-y-4">
          <div className="bg-card border border-success/30 rounded-xl p-6 text-center">
            <CheckCircle2 className="w-10 h-10 mx-auto mb-3 text-success" />
            <h3 className="text-lg font-semibold mb-1">{t("voucherExchange.result.done")}</h3>
          </div>
          <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
            {[
              ["voucherExchange.result.expired", result.expiredCount],
              ["voucherExchange.result.activated", result.newActivatedCount],
              ["voucherExchange.result.paired", result.pairedCount],
              ["voucherExchange.result.unpairedOld", result.unpairedOldCount],
              ["voucherExchange.result.unpairedNew", result.unpairedNewCount],
            ].map(([k, v]) => (
              <div key={k as string} className="bg-muted border border-border rounded-lg p-4">
                <div className="text-2xl font-bold tabular-nums">{v as number}</div>
                <div className="text-xs text-muted-foreground">{t(k as string)}</div>
              </div>
            ))}
            <div className="bg-muted border border-border rounded-lg p-4">
              <div className="text-2xl font-bold tabular-nums">{result.blendedCostPerLiter != null ? result.blendedCostPerLiter.toFixed(4) : "—"}</div>
              <div className="text-xs text-muted-foreground">{t("voucherExchange.result.blended")}</div>
            </div>
          </div>
          <div className="flex justify-end">
            <Button onClick={resetAll}><Replace className="w-3.5 h-3.5 mr-1" />{t("voucherExchange.result.startOver")}</Button>
          </div>
        </div>
      )}
    </div>
  );
}

