import { useEffect, useMemo, useState } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Search, Loader2, CalendarClock, RefreshCw, PackageOpen, CheckCircle2, ArrowRight, X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { apiRequest, ApiError } from "@/lib/api-client";
import { toast } from "sonner";
import { formatDate } from "@/lib/utils";
import DateInput from "@/components/DateInput";

// Mirrors the backend DTOs (System.Text.Json camelCase).
interface RenewableVoucher {
  id: string;
  voucherNumber: string;
  provider: string;
  fuelTypeId: string;
  fuelName: string | null;
  liters: number;
  expirationDate: string; // yyyy-MM-dd
  status: string;
  daysLeft: number;
  ownerUserId: string;
  ownerName: string | null;
}

interface RenewalTier {
  term: string;
  ratePerLiterUah: number;
  offerable: boolean;
}

interface RenewalConfig {
  enabled: boolean;
  thresholdDays: number;
  tiers: RenewalTier[];
}

interface ConfirmResult {
  branch: string;
  voucherId: string;
  voucherNumber: string;
  replacementVoucherId: string | null;
  replacementVoucherNumber: string | null;
  oldExpiration: string;
  newExpiration: string;
  termCode: string;
  surchargeUah: number;
  customerUserId: string;
  customerName: string | null;
}

// "1w"/"2w" add calendar days; "1m".."6m" add calendar months with end-of-month clamping,
// matching the server's VoucherRenewalTerms.ApplyTo so the extend preview lines up.
function applyTerm(fromIso: string, code: string): string | null {
  const m = /^(\d+)([wm])$/.exec(code);
  if (!m) return null;
  const n = parseInt(m[1], 10);
  const [y, mo, d] = fromIso.split("-").map((p) => parseInt(p, 10));
  if (!y || !mo || !d) return null;
  const date = new Date(Date.UTC(y, mo - 1, d));
  if (m[2] === "w") {
    date.setUTCDate(date.getUTCDate() + n * 7);
  } else {
    const targetMonth = date.getUTCMonth() + n;
    const anchorDay = date.getUTCDate();
    date.setUTCDate(1);
    date.setUTCMonth(targetMonth);
    const lastDay = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)).getUTCDate();
    date.setUTCDate(Math.min(anchorDay, lastDay));
  }
  const yy = date.getUTCFullYear();
  const mm = String(date.getUTCMonth() + 1).padStart(2, "0");
  const dd = String(date.getUTCDate()).padStart(2, "0");
  return `${yy}-${mm}-${dd}`;
}

export default function CustomerRenewalTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [queryInput, setQueryInput] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [providerFilter, setProviderFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");

  const [selected, setSelected] = useState<RenewableVoucher | null>(null);
  const [termCode, setTermCode] = useState("");
  const [surcharge, setSurcharge] = useState("0");
  const [invoiceNumber, setInvoiceNumber] = useState("");
  const [invoiceDate, setInvoiceDate] = useState("");
  const [result, setResult] = useState<ConfirmResult | null>(null);

  // Human label for a term code, e.g. "1w" → "1 тиж.", "3m" → "3 міс.".
  const termLabel = (code: string): string => {
    const m = /^(\d+)([wm])$/.exec(code);
    if (!m) return code;
    const unit = m[2] === "w" ? t("customerRenewal.unitWeek") : t("customerRenewal.unitMonth");
    return `${m[1]} ${unit}`;
  };

  useEffect(() => {
    const id = setTimeout(() => setDebouncedQuery(queryInput.trim()), 300);
    return () => clearTimeout(id);
  }, [queryInput]);

  const { data: config } = useQuery<RenewalConfig>({
    queryKey: ["/api/admin/voucher-renewal/terms"],
    queryFn: () => apiRequest<unknown, RenewalConfig>("GET", "/api/admin/voucher-renewal/terms"),
  });

  const tiers = config?.tiers ?? [];

  const { data: vouchers = [], isLoading, isFetching } = useQuery<RenewableVoucher[]>({
    queryKey: ["/api/admin/voucher-renewal/vouchers", debouncedQuery, providerFilter, statusFilter],
    queryFn: () => {
      const params = new URLSearchParams();
      if (debouncedQuery) params.set("query", debouncedQuery);
      if (providerFilter) params.set("provider", providerFilter);
      if (statusFilter) params.set("status", statusFilter);
      const qs = params.toString();
      return apiRequest<unknown, RenewableVoucher[]>(
        "GET", `/api/admin/voucher-renewal/vouchers${qs ? `?${qs}` : ""}`);
    },
  });

  const providerOptions = useMemo(
    () => Array.from(new Set(vouchers.map((v) => v.provider))).sort(),
    [vouchers],
  );

  // Renewal rejections come back as { code, message }. The codes are stable, the message is an English
// sentence written for whoever debugs it, so localise from the CODE and keep the message only as the
// fallback for a code this screen has never heard of — otherwise a future code would render as an
// English paragraph in a Ukrainian admin. `below_cost` and `no_stock` are the two an operator hits in
// normal use; the rest are reachable but rarer.
const RENEWAL_ERROR_KEYS: Record<string, string> = {
  below_cost: "customerRenewal.error.belowCost",
  no_stock: "customerRenewal.error.noStock",
  not_renewable: "customerRenewal.error.notRenewable",
  not_customer_voucher: "customerRenewal.error.notCustomerVoucher",
  invalid_surcharge: "customerRenewal.error.invalidSurcharge",
  unknown_term: "customerRenewal.error.unknownTerm",
  provider_term_exhausted: "customerRenewal.error.providerTermExhausted",
  not_found: "customerRenewal.error.notFound",
};

/**
 * Which localised sentence to use for a coded rejection, and which structured numbers go into it.
 *
 * A code that carries numbers has two sentences: one that quotes the figure and one that does not.
 * Returning null picks the plain one, so a server that sends no `data` renders readable text instead
 * of a sentence with a literal `{0}` left in it. The number is formatted here rather than in the
 * translation, so every locale renders it the same way.
 */
const RENEWAL_ERROR_PARAMS: Record<string, (error: ApiError) => { key: string; params: string[] } | null> = {
  below_cost: (error) => {
    const shortfall = error.data?.shortfallUah;
    return typeof shortfall === "number"
      ? { key: "customerRenewal.error.belowCostBy", params: [shortfall.toFixed(2)] }
      : null;
  },
};

  const confirmMutation = useMutation({
    mutationFn: (body: Record<string, unknown>) =>
      apiRequest<Record<string, unknown>, ConfirmResult>("POST", "/api/admin/voucher-renewal/confirm", body),
    onSuccess: (res) => {
      setResult(res);
      setSelected(null);
      setTermCode("");
      setSurcharge("0");
      setInvoiceNumber("");
      setInvoiceDate("");
      queryClient.invalidateQueries({ queryKey: ["/api/admin/voucher-renewal/vouchers"] });
      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
      toast.success(t("customerRenewal.confirmed"));
    },
    onError: (e: Error) => {
      const code = e instanceof ApiError ? e.code : undefined;
      const key = code ? RENEWAL_ERROR_KEYS[code] : undefined;
      if (!key) {
        toast.error(e.message);
        return;
      }
      // A code that carries numbers substitutes them into its localised sentence. Without this the
      // operator reads "priced below cost" with no figure — the one thing they need to judge it by.
      const withData = code ? RENEWAL_ERROR_PARAMS[code]?.(e as ApiError) : undefined;
      toast.error(withData ? t(withData.key, ...withData.params) : t(key));
    },
  });

  // Branch + preview expiry (client-side; the server is authoritative on confirm).
  const willExtend = selected ? selected.daysLeft >= 0 : false;
  const previewExpiry = useMemo(() => {
    if (!selected || !termCode || !willExtend) return null;
    return applyTerm(selected.expirationDate, termCode);
  }, [selected, termCode, willExtend]);

  const surchargeNum = parseFloat(surcharge);
  const surchargeValid = !isNaN(surchargeNum) && surchargeNum >= 0;
  const canConfirm = !!selected && !!termCode && surchargeValid && !confirmMutation.isPending;

  const submit = () => {
    if (!canConfirm || !selected) return;
    const body: Record<string, unknown> = {
      voucherId: selected.id,
      termCode,
      surchargeUah: surchargeNum,
    };
    if (invoiceNumber.trim()) body.invoiceNumber = invoiceNumber.trim();
    if (invoiceDate) body.invoiceDate = invoiceDate;
    confirmMutation.mutate(body);
  };

  const statusBadge = (status: string, daysLeft: number) => {
    const expired = status === "Expired" || daysLeft < 0;
    return (
      <span className={`px-2 py-0.5 rounded text-xs font-semibold ${
        expired ? "bg-warning/15 text-warning" : "bg-success/15 text-success"
      }`}>
        {expired ? t("customerRenewal.expired") : t("customerRenewal.active")}
      </span>
    );
  };

  return (
    <div className="space-y-4">
      {/* Result card from the last confirmed renewal */}
      {result && (
        <div className="glass-panel border border-success/30 p-5 animate-in fade-in slide-in-from-top-2 duration-200">
          <div className="flex items-start justify-between gap-3">
            <div className="flex items-center gap-2">
              <CheckCircle2 className="w-5 h-5 text-success" />
              <h3 className="font-bold">{t("customerRenewal.resultTitle")}</h3>
            </div>
            <button onClick={() => setResult(null)} className="text-muted-foreground hover:text-foreground">
              <X className="w-4 h-4" />
            </button>
          </div>
          <div className="mt-3 text-sm space-y-1">
            <div className="flex items-center gap-2">
              <span className="text-muted-foreground">{t("customerRenewal.branch")}:</span>
              <span className="font-medium inline-flex items-center gap-1">
                {result.branch === "extend"
                  ? <><CalendarClock className="w-4 h-4 text-info" /> {t("customerRenewal.branchExtend")}</>
                  : <><PackageOpen className="w-4 h-4 text-warning" /> {t("customerRenewal.branchReplace")}</>}
              </span>
            </div>
            <div className="flex items-center gap-2 font-mono text-xs">
              <span>{result.voucherNumber}</span>
              {result.replacementVoucherNumber && (
                <><ArrowRight className="w-3 h-3 text-muted-foreground" /><span>{result.replacementVoucherNumber}</span></>
              )}
            </div>
            <div className="flex items-center gap-2">
              <span className="text-muted-foreground">{t("customerRenewal.expiry")}:</span>
              <span className="font-mono">{formatDate(result.oldExpiration)}</span>
              <ArrowRight className="w-3 h-3 text-muted-foreground" />
              <span className="font-mono text-primary">{formatDate(result.newExpiration)}</span>
            </div>
            <div>
              <span className="text-muted-foreground">{t("customerRenewal.surcharge")}:</span>{" "}
              <span className="font-medium">₴{result.surchargeUah}</span>
            </div>
          </div>
        </div>
      )}

      {/* Search toolbar */}
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative flex-1 min-w-[220px]">
          <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={queryInput}
            onChange={(e) => setQueryInput(e.target.value)}
            placeholder={t("customerRenewal.searchPlaceholder")}
            className="pl-9"
          />
        </div>
        <Select value={providerFilter || "__all__"} onValueChange={(v) => setProviderFilter(v === "__all__" ? "" : v)}>
          <SelectTrigger className="h-9 w-40"><SelectValue placeholder={t("customerRenewal.allProviders")} /></SelectTrigger>
          <SelectContent>
            <SelectItem value="__all__">{t("customerRenewal.allProviders")}</SelectItem>
            {providerOptions.map((p) => <SelectItem key={p} value={p}>{p}</SelectItem>)}
          </SelectContent>
        </Select>
        <Select value={statusFilter || "__all__"} onValueChange={(v) => setStatusFilter(v === "__all__" ? "" : v)}>
          <SelectTrigger className="h-9 w-40"><SelectValue placeholder={t("customerRenewal.allStatuses")} /></SelectTrigger>
          <SelectContent>
            <SelectItem value="__all__">{t("customerRenewal.allStatuses")}</SelectItem>
            <SelectItem value="Assigned">{t("customerRenewal.active")}</SelectItem>
            <SelectItem value="Expired">{t("customerRenewal.expired")}</SelectItem>
          </SelectContent>
        </Select>
        {isFetching && <Loader2 className="w-4 h-4 animate-spin text-muted-foreground" />}
      </div>

      {/* Voucher list */}
      {isLoading ? (
        <div className="flex items-center justify-center py-12 text-muted-foreground">
          <Loader2 className="w-5 h-5 animate-spin" />
        </div>
      ) : vouchers.length === 0 ? (
        <div className="glass-panel p-8 text-center text-muted-foreground">
          {t("customerRenewal.noVouchers")}
        </div>
      ) : (
        <div className="glass-panel overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-left text-muted-foreground">
                  <th className="px-3 py-2 font-medium">{t("customerRenewal.colVoucher")}</th>
                  <th className="px-3 py-2 font-medium">{t("customerRenewal.colOwner")}</th>
                  <th className="px-3 py-2 font-medium">{t("customerRenewal.colFuel")}</th>
                  <th className="px-3 py-2 font-medium text-right">{t("customerRenewal.colLiters")}</th>
                  <th className="px-3 py-2 font-medium">{t("customerRenewal.colExpiry")}</th>
                  <th className="px-3 py-2 font-medium">{t("customerRenewal.colStatus")}</th>
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody>
                {vouchers.map((v) => {
                  const isSel = selected?.id === v.id;
                  return (
                    <tr
                      key={v.id}
                      className={`border-b border-border/50 last:border-0 ${isSel ? "bg-primary/10" : "hover:bg-muted/40"}`}
                    >
                      <td className="px-3 py-2 font-mono text-xs">{v.voucherNumber}</td>
                      <td className="px-3 py-2">{v.ownerName ?? "—"}</td>
                      <td className="px-3 py-2">{v.fuelName ?? v.fuelTypeId} · {v.provider}</td>
                      <td className="px-3 py-2 text-right">{v.liters}</td>
                      <td className="px-3 py-2 font-mono text-xs">
                        {formatDate(v.expirationDate)}
                        <span className="text-muted-foreground"> ({v.daysLeft}{t("customerRenewal.daysShort")})</span>
                      </td>
                      <td className="px-3 py-2">{statusBadge(v.status, v.daysLeft)}</td>
                      <td className="px-3 py-2 text-right">
                        <Button
                          size="sm"
                          variant={isSel ? "secondary" : "outline"}
                          onClick={() => { setSelected(v); setResult(null); }}
                        >
                          {t("customerRenewal.select")}
                        </Button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Renewal form for the selected voucher */}
      {selected && (
        <div className="glass-panel border border-primary/30 p-5 space-y-4 animate-in fade-in slide-in-from-bottom-2 duration-200">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h3 className="font-bold flex items-center gap-2">
                <RefreshCw className="w-4 h-4 text-primary" />
                {t("customerRenewal.formTitle")}
              </h3>
              <p className="text-sm text-muted-foreground mt-1 font-mono">
                {selected.voucherNumber} · {selected.ownerName ?? "—"} · {selected.liters} л
              </p>
            </div>
            <button onClick={() => setSelected(null)} className="text-muted-foreground hover:text-foreground">
              <X className="w-4 h-4" />
            </button>
          </div>

          {/* Branch indicator */}
          <div className="flex items-center gap-2 text-sm rounded-md bg-muted/40 px-3 py-2">
            {willExtend ? (
              <><CalendarClock className="w-4 h-4 text-info" />
                <span className="font-medium">{t("customerRenewal.branchExtend")}</span>
                <span className="text-muted-foreground">— {t("customerRenewal.branchExtendHint")}</span></>
            ) : (
              <><PackageOpen className="w-4 h-4 text-warning" />
                <span className="font-medium">{t("customerRenewal.branchReplace")}</span>
                <span className="text-muted-foreground">— {t("customerRenewal.branchReplaceHint")}</span></>
            )}
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            {/* Term */}
            <div className="space-y-1">
              <label className="text-sm font-medium">{t("customerRenewal.term")}</label>
              <Select value={termCode} onValueChange={setTermCode}>
                <SelectTrigger><SelectValue placeholder={t("customerRenewal.termPlaceholder")} /></SelectTrigger>
                <SelectContent>
                  {tiers.map((tier) => (
                    <SelectItem key={tier.term} value={tier.term}>{termLabel(tier.term)}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            {/* Surcharge */}
            <div className="space-y-1">
              <label className="text-sm font-medium">{t("customerRenewal.surcharge")} (₴)</label>
              <Input
                type="number"
                min="0"
                step="0.01"
                value={surcharge}
                onChange={(e) => setSurcharge(e.target.value)}
                className={!surchargeValid ? "border-destructive" : ""}
              />
              <p className="text-xs text-muted-foreground">{t("customerRenewal.surchargeHint")}</p>
            </div>

            {/* Invoice number (optional) */}
            <div className="space-y-1">
              <label className="text-sm font-medium">{t("customerRenewal.invoiceNumber")}</label>
              <Input
                value={invoiceNumber}
                onChange={(e) => setInvoiceNumber(e.target.value)}
                placeholder={t("customerRenewal.optional")}
              />
            </div>

            {/* Invoice date (optional) */}
            <div className="space-y-1">
              <label className="text-sm font-medium">{t("customerRenewal.invoiceDate")}</label>
              <DateInput value={invoiceDate} onChange={setInvoiceDate} />
            </div>
          </div>

          {/* Preview */}
          <div className="flex items-center gap-2 text-sm">
            <span className="text-muted-foreground">{t("customerRenewal.newExpiry")}:</span>
            <span className="font-mono">{formatDate(selected.expirationDate)}</span>
            <ArrowRight className="w-3 h-3 text-muted-foreground" />
            {willExtend
              ? <span className="font-mono text-primary">{previewExpiry ? formatDate(previewExpiry) : "—"}</span>
              : <span className="text-muted-foreground italic">{t("customerRenewal.fromStock")}</span>}
          </div>

          <div className="flex justify-end gap-2 pt-1">
            <Button variant="outline" onClick={() => setSelected(null)}>{t("customerRenewal.cancel")}</Button>
            <Button onClick={submit} disabled={!canConfirm}>
              {confirmMutation.isPending && <Loader2 className="w-4 h-4 animate-spin mr-2" />}
              {t("customerRenewal.confirm")}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
