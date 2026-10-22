import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ChevronLeft,
  ChevronRight,
  Loader2,
  ScrollText,
  XCircle,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { apiBlob, apiRequest } from "@/lib/api-client";
import { useAdminFuelTypes, useAdminUserOptions } from "@/lib/admin-lookups";
import { useI18n } from "@/lib/i18n";
import { orderStatusKey } from "@/lib/status-keys";
import { useSession } from "@/lib/session-store";
import { formatDateTime } from "@/lib/utils";
import type { AdminOrder, PagedResult } from "@/lib/api-types";

/** The purchases list is a PAGE, not the whole table. */
const PURCHASES_PAGE_SIZE = 50;

/**
 * The orders the shop has taken, a page at a time, with the receipt and the refund.
 *
 * <p>
 * The endpoint used to answer with every order the shop had ever taken, and the table grew with
 * it: the screen got slower as the business did, and the server held every order in memory to
 * draw a screen that shows fifty rows.
 */
export default function PurchasesTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const user = useSession((s) => s.user);

  const [purchasesPage, setPurchasesPage] = useState(1);
  const [refundTarget, setRefundTarget] = useState<AdminOrder | null>(null);
  // Downloading a receipt is a pure read - nothing mutates - so unlike the refund it needs no
  // confirmation dialog and no cache invalidation.
  //
  // The in-flight id is tracked per row rather than relying on mutation.isPending: a useMutation
  // is one instance, so isPending would grey out the button on every row the moment any one of
  // them was clicked.
  const [receiptInFlightId, setReceiptInFlightId] = useState<string | null>(
    null,
  );

  const { data: userOptions = [] } = useAdminUserOptions();
  const { data: fuelTypesList = [] } = useAdminFuelTypes();

  const { data: purchasesPageData } = useQuery<PagedResult<AdminOrder>>({
    queryKey: ["/api/admin/purchases", purchasesPage],
    queryFn: () =>
      apiRequest<void, PagedResult<AdminOrder>>(
        "GET",
        `/api/admin/purchases?page=${purchasesPage}&limit=${PURCHASES_PAGE_SIZE}`,
      ),
    enabled: !!user,
    // The orders table has to converge on its own because the changes that matter arrive
    // out-of-band, not from an action taken in this tab:
    //  - a freshly paid order shows up from the mobile checkout, with no admin mutation to
    //    trigger a refetch — without a baseline poll it stays invisible until a manual refresh;
    //  - a refund confirms asynchronously (RefundStatusSyncService flips the order to
    //    Refunded/PartiallyRefunded only after Monobank confirms the cancel), so the one-shot
    //    refetch on refund success (below) fires while the refund is still Processing and
    //    misses the flip.
    // The global default already polls at 15s and refetches on focus; this query overrides with a
    // faster cadence: 4s while any row is still settling (refund Processing, or Completed but the
    // order status hasn't caught up) for quick convergence, falling back to the same 15s baseline
    // otherwise. refetchOnWindowFocus is kept explicit for clarity.
    refetchInterval: (query) => {
      const rows = (query.state.data as PagedResult<AdminOrder> | undefined)
        ?.items;
      const settling = rows?.some(
        (p) =>
          p.refundStatus === "Processing" ||
          (p.refundStatus === "Completed" &&
            p.status !== "Refunded" &&
            p.status !== "PartiallyRefunded"),
      );
      return settling ? 4000 : 15000;
    },
    refetchOnWindowFocus: true,
  });

  const purchases = purchasesPageData?.items ?? [];
  const purchasesTotalPages = purchasesPageData?.totalPages ?? 0;

  // The purchases table shows human-readable values instead of raw IDs: resolve the fuel-type
  // UUID to its name and the buyer's userId to their phone (both datasets loaded above).
  const fuelNameById = new Map(
    fuelTypesList.map((f) => [f.id, f.name] as const),
  );
  // The names come from the OPTIONS list, not from the paged customers table: the purchases table
  // shows orders of customers who are not on the page the admin happens to be looking at.
  const usersById = new Map(userOptions.map((u) => [u.id, u] as const));
  const clientLabel = (userId: string) => {
    const u = usersById.get(userId);
    return (
      u?.phone ||
      [u?.firstName, u?.lastName].filter(Boolean).join(" ").trim() ||
      userId.slice(0, 8)
    );
  };

  const refundPurchaseMutation = useMutation({
    mutationFn: async (orderId: string) => {
      return await apiRequest<
        Record<string, never>,
        {
          success: boolean;
          refundId: string;
          amountKopecks: number;
          status: string;
        }
      >("POST", `/api/admin/orders/${orderId}/refund`, {});
    },
    onSuccess: (data) => {
      setRefundTarget(null);
      queryClient.invalidateQueries({ queryKey: ["/api/admin/purchases"] });
      toast.success(
        data.success
          ? t("purchases.refundSuccess", (data.amountKopecks / 100).toFixed(2))
          : t("purchases.refundFailed"),
      );
    },
    onError: (err: Error) => {
      setRefundTarget(null);
      toast.error(`${t("purchases.refundFailed")}: ${err.message}`);
    },
  });

  const receiptMutation = useMutation({
    mutationFn: async (orderId: string) => {
      const { blob, fileName } = await apiBlob(
        "GET",
        `/api/admin/orders/${orderId}/receipt`,
        `receipt-${orderId}.pdf`,
      );
      return { blob, fileName };
    },
    onSuccess: ({ blob, fileName }) => {
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = fileName;
      anchor.click();
      // Revoked on the next tick, not inline: revoking before the browser has started the
      // download cancels it in some browsers.
      setTimeout(() => URL.revokeObjectURL(url), 0);
    },
    onError: (err: Error) => {
      toast.error(`${t("purchases.receiptFailed")}: ${err.message}`);
    },
    onSettled: () => {
      setReceiptInFlightId(null);
    },
  });

  return (
    <div className="space-y-6 animate-in fade-in duration-300">
      <div className="glass-panel overflow-x-auto">
        <table className="w-full">
          <thead className="bg-muted">
            <tr>
              <th className="text-left p-4">{t("table.number")}</th>
              <th className="text-left p-4">{t("table.client")}</th>
              <th className="text-left p-4">{t("table.station")}</th>
              <th className="text-left p-4">{t("table.fuel")}</th>
              <th className="text-left p-4">{t("table.liters")}</th>
              <th className="text-left p-4">{t("table.price")}</th>
              <th className="text-left p-4">{t("common.status")}</th>
              <th className="text-left p-4">{t("common.date")}</th>
              <th className="text-left p-4">{t("table.vouchers")}</th>
              <th className="text-left p-4">{t("purchases.acquirer")}</th>
              <th className="text-left p-4">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {purchases.map((purchase, index) => (
              <tr key={purchase.id} className="border-border">
                <td className="p-4 font-mono text-xs" title={purchase.id}>
                  {index + 1}
                </td>
                <td className="p-4">{clientLabel(purchase.userId)}</td>
                <td className="p-4">{purchase.brand}</td>
                <td className="p-4">
                  {fuelNameById.get(purchase.fuelTypeId) || purchase.fuelTypeId}
                </td>
                <td className="p-4">{purchase.liters}L</td>
                <td className="p-4 text-primary font-bold">
                  {purchase.price} UAH
                </td>
                <td className="p-4">
                  <span
                    className={`px-2 py-1 rounded text-xs ${
                      purchase.status === "Fulfilled"
                        ? "bg-success/20 text-success"
                        : purchase.status === "PartiallyFulfilled"
                          ? "bg-warning/20 text-warning"
                          : purchase.status === "PartiallyRefunded"
                            ? "bg-info/20 text-info"
                            : purchase.status === "Refunded"
                              ? "bg-info/20 text-info"
                              : purchase.status === "PendingPayment" ||
                                  purchase.status === "PendingFulfillment"
                                ? "bg-warning/20 text-warning"
                                : "bg-destructive/20 text-destructive"
                    }`}
                  >
                    {t("order.status." + orderStatusKey(purchase.status))}
                  </span>
                  {purchase.refundStatus && (
                    <span
                      className={`block mt-1 px-2 py-1 rounded text-xs ${
                        purchase.refundStatus === "Completed"
                          ? "bg-success/20 text-success"
                          : purchase.refundStatus === "Failed"
                            ? "bg-destructive/20 text-destructive"
                            : "bg-muted text-muted-foreground"
                      }`}
                    >
                      {t("purchases.refundStatus." + purchase.refundStatus)}
                    </span>
                  )}
                </td>
                <td className="p-4 text-muted-foreground text-sm">
                  {formatDateTime(purchase.createdAtUtc)}
                </td>
                <td className="p-4">
                  {purchase.voucherCount} / {purchase.quantity}
                </td>
                <td className="p-4">
                  {/* The acquirer's own record of the payment, next to our receipt.
                      monobankInvoiceId and monobankStatus were on the type from the start and
                      rendered nowhere, so the invoice was in the wire and in nobody's eye. */}
                  {purchase.monobankInvoiceId ? (
                    <div className="space-y-1">
                      <div
                        className="font-mono text-xs break-all"
                        title={purchase.monobankInvoiceId}
                      >
                        {purchase.monobankInvoiceId}
                      </div>
                      {purchase.monobankStatus && (
                        <span
                          className={`inline-block px-1.5 py-0.5 rounded text-xs ${
                            purchase.monobankStatus === "Success"
                              ? "bg-success/20 text-success"
                              : "bg-muted text-muted-foreground"
                          }`}
                        >
                          {t(
                            "monobank.status." +
                              purchase.monobankStatus.toLowerCase(),
                          )}
                        </span>
                      )}
                      {purchase.refundMonobankStatus && (
                        <span className="block text-xs text-muted-foreground">
                          {t(
                            "purchases.refundStatus." +
                              purchase.refundMonobankStatus,
                          )}
                        </span>
                      )}
                    </div>
                  ) : (
                    <span className="text-muted-foreground">—</span>
                  )}
                </td>
                <td className="p-4">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                      setReceiptInFlightId(purchase.id);
                      receiptMutation.mutate(purchase.id);
                    }}
                    disabled={receiptInFlightId === purchase.id}
                    className="text-info hover:bg-info/10"
                  >
                    {receiptInFlightId === purchase.id ? (
                      <Loader2 className="w-4 h-4 animate-spin" />
                    ) : (
                      <ScrollText className="w-4 h-4" />
                    )}
                    <span className="ml-1 text-xs">
                      {t("purchases.downloadReceipt")}
                    </span>
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setRefundTarget(purchase)}
                    disabled={refundPurchaseMutation.isPending}
                    className="text-warning hover:bg-warning/10"
                  >
                    <XCircle className="w-4 h-4" />
                    <span className="ml-1 text-xs">
                      {t("purchases.refund")}
                    </span>
                  </Button>
                </td>
              </tr>
            ))}
            {purchases.length === 0 && (
              <tr>
                <td
                  colSpan={11}
                  className="p-8 text-center text-muted-foreground"
                >
                  No purchases found
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {purchasesTotalPages > 1 && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>
            {t("purchases.total")}: {purchasesPageData?.totalCount ?? 0}
          </span>
          <div className="flex items-center gap-2">
            <Button
              variant="ghost"
              size="sm"
              disabled={purchasesPage <= 1}
              onClick={() => setPurchasesPage((p) => Math.max(1, p - 1))}
              className="h-8"
            >
              <ChevronLeft className="w-4 h-4" />
            </Button>
            <span className="text-xs">
              {purchasesPage} / {purchasesTotalPages}
            </span>
            <Button
              variant="ghost"
              size="sm"
              disabled={purchasesPage >= purchasesTotalPages}
              onClick={() => setPurchasesPage((p) => p + 1)}
              className="h-8"
            >
              <ChevronRight className="w-4 h-4" />
            </Button>
          </div>
        </div>
      )}

      {refundTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-md p-4">
          <div
            className="glass-panel p-6 max-w-sm w-full animate-in zoom-in-50 duration-200"
            onClick={(e: React.MouseEvent) => e.stopPropagation()}
          >
            <h3 className="text-xl font-bold text-foreground mb-2">
              {t("purchases.refundTitle")}
            </h3>
            <p className="text-muted-foreground mb-2">
              {t(
                "purchases.refundConfirm",
                (refundTarget.refundableAmountKopecks / 100).toFixed(2),
              )}
            </p>
            {refundTarget.refundableAmountKopecks <= 0 && (
              <p className="text-warning text-sm mb-6">
                {t("purchases.refundNothingToRefund")}
              </p>
            )}
            <div className="flex gap-2">
              <Button
                variant="outline"
                className="flex-1"
                onClick={() => setRefundTarget(null)}
              >
                {t("vouchers.cancel")}
              </Button>
              <Button
                variant="destructive"
                className="flex-1"
                onClick={() => refundPurchaseMutation.mutate(refundTarget.id)}
                disabled={
                  refundPurchaseMutation.isPending ||
                  refundTarget.refundableAmountKopecks <= 0
                }
              >
                {t("purchases.refund")}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
