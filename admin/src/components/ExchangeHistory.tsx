import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Loader2, ArrowLeft } from "lucide-react";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { formatMoney } from "@/lib/money";

export interface ExchangeBatch {
  exchangeBatchId: string;
  atUtc: string;
  supplierId: string;
  supplierName: string | null;
  brands: string[];
  fuelTypeIds: string[];
  vouchersExchanged: number;
  litersOut: number;
  surchargeUah: number;
  costPerLiterBefore: number | null;
  costPerLiterAfter: number | null;
  recordedBy: string | null;
}

export interface ExchangeBatchesResponse {
  data: ExchangeBatch[];
  total: number;
  page: number;
  limit: number;
  suppliers: { id: string; name: string }[];
}

export interface ExchangePair {
  oldVoucherId: string;
  oldVoucherNumber: string | null;
  oldLiters: number | null;
  oldCostPerLiter: number | null;
  newVoucherId: string | null;
  newVoucherNumber: string | null;
  fuelTypeId: string;
  brand: string;
  surchargeShareUah: number;
  newCostPerLiter: number | null;
}

export interface ExchangeBatchDetail {
  exchangeBatchId: string;
  atUtc: string;
  supplierId: string;
  supplierName: string | null;
  recordedBy: string | null;
  totalSurchargeUah: number;
  totalLiters: number;
  pairs: ExchangePair[];
}

const ALL = "__all__";

/**
 * The exchange audit: every recorded exchange, and every pair inside one of them.
 *
 * This exists because `voucher_exchanges` was written by every exchange and read by nothing - the
 * one table that records who settled with whom, for what, at what cost, was reachable only through
 * SQL. Suppliers issue no invoices, so there is no paper to reconcile against; this screen is the
 * record.
 *
 * The surcharge is shown two ways on purpose. The list carries the batch's whole charge; the
 * drill-down carries each pair's own share of it, because a single row of `voucher_exchanges` is
 * only that pair's fraction. Reading one row and calling it the total once shipped as a bug.
 */
export function ExchangeHistory() {
  const { t } = useI18n();
  const [page, setPage] = useState(1);
  const [supplierId, setSupplierId] = useState(ALL);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [openBatchId, setOpenBatchId] = useState<string | null>(null);

  const params = new URLSearchParams({ page: String(page), limit: "20" });
  if (supplierId !== ALL) params.set("supplierId", supplierId);
  if (from) params.set("from", from);
  if (to) params.set("to", to);

  const { data, isLoading, isError } = useQuery<ExchangeBatchesResponse>({
    // Filters live in the key, so changing them re-queries rather than filtering a stale page.
    queryKey: [
      "/api/admin/voucher-exchange/batches",
      supplierId,
      from,
      to,
      page,
    ],
    queryFn: () =>
      apiRequest<unknown, ExchangeBatchesResponse>(
        "GET",
        `/api/admin/voucher-exchange/batches?${params.toString()}`,
      ),
    staleTime: 30_000,
  });

  if (openBatchId) {
    return (
      <ExchangeBatchDetailView
        batchId={openBatchId}
        onBack={() => setOpenBatchId(null)}
      />
    );
  }

  const batches = data?.data ?? [];
  const total = data?.total ?? 0;
  const pageCount = Math.max(1, Math.ceil(total / (data?.limit || 20)));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <select
          aria-label={t("voucherExchange.historySupplier")}
          value={supplierId}
          onChange={(e) => {
            setSupplierId(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-border bg-background px-3 py-2 text-sm"
        >
          <option value={ALL}>
            {t("voucherExchange.historyAllSuppliers")}
          </option>
          {(data?.suppliers ?? []).map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </select>

        <input
          type="date"
          aria-label={t("voucherExchange.historyFrom")}
          value={from}
          onChange={(e) => {
            setFrom(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-border bg-background px-3 py-2 text-sm"
        />
        <input
          type="date"
          aria-label={t("voucherExchange.historyTo")}
          value={to}
          onChange={(e) => {
            setTo(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-border bg-background px-3 py-2 text-sm"
        />
      </div>

      <div className="overflow-x-auto rounded-lg border border-border">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-muted/50 text-left text-muted-foreground">
              <th className="p-2 font-medium">
                {t("voucherExchange.historyDate")}
              </th>
              <th className="p-2 font-medium">
                {t("voucherExchange.historySupplier")}
              </th>
              <th className="p-2 font-medium text-right">
                {t("voucherExchange.historyVouchers")}
              </th>
              <th className="p-2 font-medium text-right">
                {t("voucherExchange.historyLiters")}
              </th>
              <th className="p-2 font-medium text-right">
                {t("voucherExchange.historySurcharge")}
              </th>
              <th className="p-2 font-medium text-right">
                {t("voucherExchange.historyCostMove")}
              </th>
              <th className="p-2 font-medium">
                {t("voucherExchange.historyRecordedBy")}
              </th>
            </tr>
          </thead>
          <tbody>
            {batches.map((b) => (
              <tr
                key={b.exchangeBatchId}
                onClick={() => setOpenBatchId(b.exchangeBatchId)}
                className="cursor-pointer border-t border-border hover:bg-muted/40"
              >
                <td className="p-2 font-mono text-xs">
                  {b.atUtc.slice(0, 10)}
                </td>
                <td className="p-2">{b.supplierName ?? "-"}</td>
                <td className="p-2 text-right tabular-nums">
                  {b.vouchersExchanged}
                </td>
                <td className="p-2 text-right tabular-nums">{b.litersOut}</td>
                <td className="p-2 text-right font-mono tabular-nums">
                  {formatMoney(b.surchargeUah)}
                </td>
                <td className="p-2 text-right font-mono tabular-nums">
                  {perL(b.costPerLiterBefore)} → {perL(b.costPerLiterAfter)}
                </td>
                <td className="p-2 text-xs text-muted-foreground">
                  {b.recordedBy ?? "-"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {isLoading && (
          <p className="flex items-center gap-2 p-4 text-sm text-muted-foreground">
            <Loader2 className="w-4 h-4 animate-spin" /> {t("common.loading")}
          </p>
        )}
        {isError && (
          <p className="p-4 text-sm text-destructive">
            {t("voucherExchange.historyLoadFailed")}
          </p>
        )}
        {!isLoading && !isError && batches.length === 0 && (
          <p className="p-4 text-sm text-muted-foreground">
            {t("voucherExchange.historyEmpty")}
          </p>
        )}
      </div>

      {pageCount > 1 && (
        <div className="flex items-center justify-between text-sm">
          <span className="text-muted-foreground">
            {t("voucherExchange.historyPage", String(page), String(pageCount))}
          </span>
          <div className="flex gap-2">
            <button
              type="button"
              aria-label={t("voucherExchange.historyPrev")}
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="inline-flex items-center gap-1 rounded-lg border border-border px-3 py-1.5 disabled:opacity-40"
            >
              <ChevronLeft className="w-4 h-4" />
            </button>
            <button
              type="button"
              aria-label={t("voucherExchange.historyNext")}
              disabled={page >= pageCount}
              onClick={() => setPage((p) => p + 1)}
              className="inline-flex items-center gap-1 rounded-lg border border-border px-3 py-1.5 disabled:opacity-40"
            >
              <ChevronRight className="w-4 h-4" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function ExchangeBatchDetailView({
  batchId,
  onBack,
}: {
  batchId: string;
  onBack: () => void;
}) {
  const { t } = useI18n();

  const { data, isLoading, isError } = useQuery<ExchangeBatchDetail>({
    queryKey: ["/api/admin/voucher-exchange/batches", batchId],
    queryFn: () =>
      apiRequest<unknown, ExchangeBatchDetail>(
        "GET",
        `/api/admin/voucher-exchange/batches/${encodeURIComponent(batchId)}`,
      ),
    staleTime: 30_000,
  });

  return (
    <div className="space-y-4">
      <button
        type="button"
        onClick={onBack}
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="w-4 h-4" /> {t("voucherExchange.historyBack")}
      </button>

      {isLoading && (
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="w-4 h-4 animate-spin" /> {t("common.loading")}
        </p>
      )}
      {isError && (
        <p className="text-sm text-destructive">
          {t("voucherExchange.historyLoadFailed")}
        </p>
      )}

      {data && (
        <>
          <div className="flex flex-wrap items-center gap-x-6 gap-y-1 text-sm">
            <span>
              <span className="text-muted-foreground">
                {t("voucherExchange.historySupplier")}:{" "}
              </span>
              {data.supplierName ?? "-"}
            </span>
            <span>
              <span className="text-muted-foreground">
                {t("voucherExchange.historyDate")}:{" "}
              </span>
              <span className="font-mono">{data.atUtc.slice(0, 10)}</span>
            </span>
            <span>
              <span className="text-muted-foreground">
                {t("voucherExchange.historyRecordedBy")}:{" "}
              </span>
              {data.recordedBy ?? "-"}
            </span>
          </div>

          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-muted/50 text-left text-muted-foreground">
                  <th className="p-2 font-medium">
                    {t("voucherExchange.historyOld")}
                  </th>
                  <th className="p-2 font-medium">
                    {t("voucherExchange.historyNew")}
                  </th>
                  <th className="p-2 font-medium text-right">
                    {t("voucherExchange.historyLiters")}
                  </th>
                  <th className="p-2 font-medium text-right">
                    {t("voucherExchange.historyCostPerLiter")}
                  </th>
                  <th className="p-2 font-medium text-right">
                    {t("voucherExchange.historyShare")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.pairs.map((p) => (
                  <tr key={p.oldVoucherId} className="border-t border-border">
                    <td className="p-2 font-mono text-xs">
                      {p.oldVoucherNumber ?? "-"}
                    </td>
                    <td className="p-2 font-mono text-xs">
                      {p.newVoucherNumber ?? "-"}
                    </td>
                    <td className="p-2 text-right tabular-nums">
                      {p.oldLiters ?? "-"}
                    </td>
                    <td className="p-2 text-right font-mono tabular-nums">
                      {perL(p.oldCostPerLiter)} → {perL(p.newCostPerLiter)}
                    </td>
                    {/* One pair's share, never the batch total - that is the row above. */}
                    <td className="p-2 text-right font-mono tabular-nums">
                      {formatMoney(p.surchargeShareUah)}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-border bg-muted/30 font-medium">
                  <td className="p-2" colSpan={2}>
                    {t("voucherExchange.historyTotal")}
                  </td>
                  <td className="p-2 text-right tabular-nums">
                    {data.totalLiters}
                  </td>
                  <td className="p-2" />
                  <td className="p-2 text-right font-mono tabular-nums">
                    {formatMoney(data.totalSurchargeUah)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>
        </>
      )}
    </div>
  );
}

function perL(value: number | null | undefined): string {
  return value == null ? "-" : value.toFixed(2);
}
