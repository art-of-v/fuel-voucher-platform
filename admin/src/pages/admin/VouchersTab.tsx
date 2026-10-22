import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowUpDown,
  CheckSquare,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Filter,
  History,
  Loader2,
  Package,
  QrCode,
  Trash2,
  X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "sonner";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import DateInput from "@/components/DateInput";
import { VoucherLifecycleDrawer } from "@/components/VoucherLifecycleDrawer";
import { apiRequest } from "@/lib/api-client";
import { useAdminFuelTypes } from "@/lib/admin-lookups";
import { useI18n } from "@/lib/i18n";
import { formatMoney } from "@/lib/money";
import { voucherStatusKey } from "@/lib/status-keys";
import { useSession } from "@/lib/session-store";
import { formatDate } from "@/lib/utils";
import type { AdminVoucherDetail, AdminVoucherList } from "@/lib/api-types";
import CatalogImportPanel from "./CatalogImportPanel";

/** How many rows the stock table shows at a time. */
const LIMIT = 50;

/**
 * The voucher stock: the catalog import on top, then the stock itself with its filters.
 *
 * <p>
 * The list is a PAGE, and the filters are the ones the server understands - the brand dropdown
 * offers the brands the stock actually carries, and the fuels are scoped to the chosen brand, so
 * the operator cannot pick a pairing that can never match a row.
 */
export default function VouchersTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const user = useSession((s) => s.user);

  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState("createdAt");
  const [sortOrder, setSortOrder] = useState<"asc" | "desc">("desc");
  const [filterFuelType, setFilterFuelType] = useState("");
  const [filterStatus, setFilterStatus] = useState("");
  const [filterProvider, setFilterProvider] = useState("");
  const [filterAmount, setFilterAmount] = useState("");
  const [filterExpirationDate, setFilterExpirationDate] = useState("");
  const [selectedVoucherIds, setSelectedVoucherIds] = useState<Set<string>>(
    new Set(),
  );
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [showDeleteAllConfirm, setShowDeleteAllConfirm] = useState(false);
  const [selectedQrId, setSelectedQrId] = useState<string | null>(null);
  // The D-13 record read for one voucher (VOUCHER_LIFECYCLE.md slice 2). Null id means closed.
  const [lifecycleVoucher, setLifecycleVoucher] = useState<{
    id: string;
    number: string;
  } | null>(null);

  const { data: fuelTypesList = [] } = useAdminFuelTypes();

  const { data: vouchersResponse, isLoading: isVouchersLoading } =
    useQuery<AdminVoucherList>({
      queryKey: [
        "/api/admin/vouchers",
        page,
        sortBy,
        sortOrder,
        filterFuelType,
        filterStatus,
        filterProvider,
        filterAmount,
        filterExpirationDate,
      ],
      enabled: !!user,
      staleTime: 30_000,
      queryFn: async () => {
        const params = new URLSearchParams({
          page: page.toString(),
          limit: LIMIT.toString(),
          sortBy,
          sortDirection: sortOrder,
          ...(filterFuelType ? { fuelTypeId: filterFuelType } : {}),
          ...(filterStatus ? { status: filterStatus } : {}),
          ...(filterProvider ? { brand: filterProvider } : {}),
          ...(filterAmount ? { amount: filterAmount } : {}),
          ...(filterExpirationDate
            ? { expirationDate: filterExpirationDate }
            : {}),
        });
        return await apiRequest<void, AdminVoucherList>(
          "GET",
          `/api/admin/vouchers?${params.toString()}`,
        );
      },
    });

  // Fetch single voucher details when modal is open to get decrypted QR
  const { data: fullVoucherData, isLoading: isVoucherLoading } =
    useQuery<AdminVoucherDetail | null>({
      queryKey: ["/api/admin/vouchers", selectedQrId],
      queryFn: async () => {
        if (!selectedQrId) return null;
        return await apiRequest<void, AdminVoucherDetail>(
          "GET",
          `/api/admin/vouchers/${selectedQrId}`,
        );
      },
      enabled: !!user && !!selectedQrId,
    });

  const vouchers = vouchersResponse?.data || [];
  const totalVouchers = vouchersResponse?.total || 0;
  const globalTotal = vouchersResponse?.globalTotal || 0;
  // Scoped by the selected brand, so the operator cannot pick a pairing that can never match a
  // voucher. With no brand chosen, every fuel any voucher actually carries is offered.
  const fuelTypesByProvider = vouchersResponse?.fuelTypesByProvider || {};
  const dropdownFuelTypes = filterProvider
    ? fuelTypesByProvider[filterProvider] || []
    : Object.values(fuelTypesByProvider).flat();
  const dropdownProviders = vouchersResponse?.providers || [];
  const dropdownStatuses = vouchersResponse?.statuses || [];
  const dropdownAmounts = vouchersResponse?.amounts || [];

  const fuelNameById = new Map(
    fuelTypesList.map((f) => [f.id, f.name] as const),
  );

  const bulkDeleteMutation = useMutation({
    mutationFn: async () => {
      await apiRequest<{ action: string; ids: string[] }, unknown>(
        "POST",
        "/api/admin/vouchers/bulk-action",
        { action: "delete_all", ids: [] },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const deleteSelectedMutation = useMutation({
    mutationFn: async (ids: string[]) => {
      await apiRequest<{ action: string; ids: string[] }, unknown>(
        "POST",
        "/api/admin/vouchers/bulk-action",
        { action: "delete", ids },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
      setSelectedVoucherIds(new Set());
    },
    onError: (e: Error) => toast.error(e.message),
  });

  // The undo for the delete above. Deletes are soft (#217), so an archived voucher is still listed
  // here - without this it could be found but never brought back.
  const unarchiveVoucherMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest<void, unknown>(
        "POST",
        `/api/admin/vouchers/${id}/unarchive`,
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/vouchers"] });
      toast.success(t("vouchers.unarchive") + " ✓");
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const toggleSort = (column: string) => {
    if (sortBy === column) {
      setSortOrder(sortOrder === "asc" ? "desc" : "asc");
    } else {
      setSortBy(column);
      setSortOrder("desc");
    }
  };

  const toggleSelectAll = () => {
    if (selectedVoucherIds.size === vouchers.length && vouchers.length > 0) {
      setSelectedVoucherIds(new Set());
    } else {
      const newSet = new Set(selectedVoucherIds);
      vouchers.forEach((v) => newSet.add(v.id));
      setSelectedVoucherIds(newSet);
    }
  };

  const toggleSelectRow = (id: string) => {
    const newSet = new Set(selectedVoucherIds);
    if (newSet.has(id)) newSet.delete(id);
    else newSet.add(id);
    setSelectedVoucherIds(newSet);
  };

  // Whether this actor may open a voucher's redeemable code. The payload is a bearer
  // instrument: whoever holds it can spend the customer's fuel, and nothing records that they
  // did. The server holds that capability too (AuthPolicies.VoucherPayload) and hides the code
  // from anyone outside it - so the button is hidden as well, rather than opening a modal with
  // nothing in it.
  const actorRole = user?.role ?? "";
  const canSeeVoucherPayload =
    actorRole === "Admin" || actorRole === "ProductOwner";

  return (
    <div className="space-y-6 animate-in fade-in duration-300">
      <CatalogImportPanel />

      {/* Controls Header */}
      <div className="flex flex-col gap-4 mb-4">
        <div className="flex flex-wrap justify-between items-center glass-panel p-4 gap-4">
          <div className="flex gap-6 items-center flex-wrap">
            <div>
              <div className="text-xs text-muted-foreground uppercase tracking-wider">
                {t("vouchers.total")}
              </div>
              <div className="text-2xl font-bold text-foreground">
                {globalTotal}
              </div>
            </div>
            {filterFuelType && (
              <div className="animate-in fade-in">
                <div className="text-xs text-muted-foreground uppercase tracking-wider">
                  {t("vouchers.filtered")} (
                  {fuelNameById.get(filterFuelType) || filterFuelType})
                </div>
                <div className="text-2xl font-bold text-primary">
                  {totalVouchers}
                </div>
              </div>
            )}
            {selectedVoucherIds.size > 0 && (
              <div className="animate-in fade-in">
                <div className="text-xs text-muted-foreground uppercase tracking-wider">
                  {t("vouchers.selected")}
                </div>
                <div className="text-2xl font-bold text-info">
                  {selectedVoucherIds.size}
                </div>
              </div>
            )}
          </div>

          <div className="flex gap-2 items-center">
            <div className="flex items-center gap-2">
              <Select
                value={filterFuelType || "all"}
                onValueChange={(val) => {
                  setFilterFuelType(val === "all" ? "" : val);
                  setPage(1);
                }}
              >
                <SelectTrigger className="w-[180px] bg-muted border-border text-foreground rounded-lg h-9">
                  <div className="flex items-center gap-2">
                    <Filter className="w-3.5 h-3.5 text-muted-foreground" />
                    <SelectValue placeholder={t("vouchers.fuelType")} />
                  </div>
                </SelectTrigger>
                <SelectContent className="text-foreground shadow-2xl">
                  <SelectItem value="all">
                    {t("vouchers.allFuelTypes")}
                  </SelectItem>
                  {[...dropdownFuelTypes]
                    .sort((a, b) => a.name.localeCompare(b.name))
                    .map((ft) => (
                      <SelectItem key={ft.id} value={ft.id}>
                        {ft.name}
                      </SelectItem>
                    ))}
                </SelectContent>
              </Select>

              <Select
                value={filterStatus || "all"}
                onValueChange={(val) => {
                  setFilterStatus(val === "all" ? "" : val);
                  setPage(1);
                }}
              >
                <SelectTrigger className="w-[140px] bg-muted border-border text-foreground rounded-lg h-9">
                  <SelectValue placeholder={t("vouchers.status")} />
                </SelectTrigger>
                <SelectContent className="text-foreground shadow-2xl">
                  <SelectItem value="all">
                    {t("vouchers.allStatuses")}
                  </SelectItem>
                  {dropdownStatuses.map((s) => (
                    <SelectItem key={s} value={s}>
                      {t("status." + voucherStatusKey(s))}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <Select
                value={filterProvider || "all"}
                onValueChange={(val) => {
                  setFilterProvider(val === "all" ? "" : val);
                  // The fuel filter is scoped to the brand, so a fuel chosen under the old
                  // brand may not exist under the new one. Drop it rather than leave a filter
                  // that silently returns nothing.
                  if (
                    val !== "all" &&
                    filterFuelType &&
                    !(fuelTypesByProvider[val] || []).some(
                      (f) => f.id === filterFuelType,
                    )
                  ) {
                    setFilterFuelType("");
                  }
                  setPage(1);
                }}
              >
                <SelectTrigger className="w-[140px] bg-muted border-border text-foreground rounded-lg h-9">
                  <SelectValue placeholder={t("vouchers.brand")} />
                </SelectTrigger>
                <SelectContent className="text-foreground shadow-2xl">
                  <SelectItem value="all">{t("vouchers.allBrands")}</SelectItem>
                  {dropdownProviders.map((p) => (
                    <SelectItem key={p} value={p}>
                      {p}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <Select
                value={filterAmount || "all"}
                onValueChange={(val) => {
                  setFilterAmount(val === "all" ? "" : val);
                  setPage(1);
                }}
              >
                <SelectTrigger className="w-[100px] bg-muted border-border text-foreground rounded-lg h-9">
                  <SelectValue placeholder={t("vouchers.volume")} />
                </SelectTrigger>
                <SelectContent className="text-foreground shadow-2xl">
                  <SelectItem value="all">{t("vouchers.all")}</SelectItem>
                  {[...dropdownAmounts]
                    .sort((a, b) => a - b)
                    .map((a) => (
                      <SelectItem key={a} value={a.toString()}>
                        {a} L
                      </SelectItem>
                    ))}
                </SelectContent>
              </Select>

              <DateInput
                value={filterExpirationDate}
                onChange={(v) => {
                  setFilterExpirationDate(v);
                  setPage(1);
                }}
                className="w-[140px] bg-muted border-border text-foreground rounded-lg h-9 text-xs"
              />

              {(filterFuelType ||
                filterStatus ||
                filterProvider ||
                filterAmount ||
                filterExpirationDate) && (
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => {
                    setFilterFuelType("");
                    setFilterStatus("");
                    setFilterProvider("");
                    setFilterAmount("");
                    setFilterExpirationDate("");
                    setPage(1);
                  }}
                  title={t("vouchers.clearFilters")}
                  className="h-9 w-9 text-muted-foreground hover:bg-foreground/5 rounded-lg"
                >
                  <X className="w-4 h-4" />
                </Button>
              )}
            </div>

            {selectedVoucherIds.size > 0 ? (
              <Button
                variant="destructive"
                size="sm"
                onClick={() => setShowDeleteConfirm(true)}
              >
                <Trash2 className="w-4 h-4 mr-2" />
                {t("vouchers.delete")} ({selectedVoucherIds.size})
              </Button>
            ) : (
              vouchers.length > 0 && (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setShowDeleteAllConfirm(true)}
                  className="text-destructive hover:bg-destructive/10"
                >
                  {t("common.deleteAll")} ({globalTotal})
                </Button>
              )
            )}
          </div>
        </div>
      </div>

      <div className="glass-panel overflow-hidden shadow-xl">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-muted text-muted-foreground uppercase text-xs backdrop-blur-sm">
              <tr>
                <th className="p-4 w-10 sticky left-0 bg-muted z-10">
                  <div
                    className={`w-4 h-4 border rounded cursor-pointer flex items-center justify-center transition-colors ${selectedVoucherIds.size === vouchers.length && vouchers.length > 0 ? "bg-primary border-primary" : "border-border hover:border-foreground/40"}`}
                    onClick={toggleSelectAll}
                  >
                    {selectedVoucherIds.size === vouchers.length &&
                      vouchers.length > 0 && (
                        <CheckSquare className="w-3 h-3 text-primary-foreground" />
                      )}
                  </div>
                </th>
                {[
                  {
                    id: "qrCodeData",
                    label: t("vouchers.image"),
                    sortable: false,
                  },
                  {
                    id: "amount",
                    label: t("vouchers.volume"),
                    sortable: true,
                  },
                  {
                    id: "fuelType",
                    label: t("vouchers.fuelType"),
                    sortable: true,
                  },
                  {
                    id: "brand",
                    label: t("vouchers.brand"),
                    sortable: true,
                  },
                  {
                    id: "supplier",
                    label: t("vouchers.supplier"),
                    sortable: true,
                  },
                  {
                    id: "costPerLiter",
                    label: t("vouchers.costPerLiter"),
                    sortable: true,
                  },
                  {
                    id: "expirationDate",
                    label: t("vouchers.expires"),
                    sortable: true,
                  },
                  {
                    id: "externalId",
                    label: t("vouchers.externalId"),
                    sortable: true,
                  },
                  {
                    id: "status",
                    label: t("vouchers.status"),
                    sortable: true,
                  },
                  {
                    id: "createdAt",
                    label: t("common.date"),
                    sortable: true,
                  },
                  // Not sortable: it is an action, not a fact about the voucher. Declared with the
                  // other columns so the header and the cells stay in step - the alternative is a
                  // header outside the map and a cell inside it, which drifts the moment one of them
                  // gains an entry.
                  {
                    id: "lifecycle",
                    label: t("voucherLifecycle.open"),
                    sortable: false,
                  },
                ].map((col) => (
                  <th
                    key={col.id}
                    className={`text-left p-4 transition-colors ${col.sortable ? "cursor-pointer hover:text-foreground hover:bg-foreground/5" : ""}`}
                    onClick={() => col.sortable && toggleSort(col.id)}
                  >
                    <div className="flex items-center gap-1.5">
                      {col.label}
                      {col.sortable &&
                        (sortBy === col.id ? (
                          sortOrder === "asc" ? (
                            <ChevronUp className="w-3.5 h-3.5 text-primary" />
                          ) : (
                            <ChevronDown className="w-3.5 h-3.5 text-primary" />
                          )
                        ) : (
                          <ArrowUpDown className="w-3 h-3 opacity-20" />
                        ))}
                    </div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {vouchers.map((v) => {
                const statusKey = voucherStatusKey(v.status);
                const isSelected = selectedVoucherIds.has(v.id);
                return (
                  <tr
                    key={v.id}
                    className={`transition-colors ${isSelected ? "bg-primary/5 hover:bg-primary/10" : "hover:bg-foreground/5"}`}
                  >
                    <td className="p-4 sticky left-0 z-10">
                      <div
                        className={`w-4 h-4 border rounded cursor-pointer flex items-center justify-center transition-colors ${isSelected ? "bg-primary border-primary" : "border-border hover:border-foreground/40"}`}
                        onClick={() => toggleSelectRow(v.id)}
                      >
                        {isSelected && (
                          <CheckSquare className="w-3 h-3 text-primary-foreground" />
                        )}
                      </div>
                    </td>
                    <td
                      className="p-4"
                      onClick={
                        canSeeVoucherPayload
                          ? () => setSelectedQrId(v.id)
                          : undefined
                      }
                    >
                      {canSeeVoucherPayload && (
                        <div className="cursor-pointer hover:scale-105 transition-transform bg-muted p-1 rounded-md w-fit border border-border">
                          <QrCode className="w-6 h-6 text-muted-foreground" />
                        </div>
                      )}
                    </td>
                    <td className="p-4 font-bold text-foreground">
                      {v.liters} L
                    </td>
                    <td className="p-4">
                      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-muted text-foreground/80 border border-border">
                        {v.fuelType?.name || v.fuelTypeId}
                      </span>
                    </td>
                    <td className="p-4 font-medium text-foreground/80 uppercase">
                      {v.brand || "Unknown"}
                    </td>
                    <td className="p-4 text-muted-foreground">
                      {v.supplierName || <span className="opacity-50">-</span>}
                    </td>
                    <td className="p-4 font-mono text-xs text-muted-foreground">
                      {/* formatMoney already renders the symbol and already renders "-" for a
                          null, so appending a unit here would give "94,90 ₴ ₴/л". The header
                          says Cost/L, so the cell is just the amount. */}
                      {formatMoney(v.costPerLiter)}
                    </td>
                    <td className="p-4 text-muted-foreground font-mono text-xs">
                      {v.supplierExpirationDate
                        ? formatDate(v.supplierExpirationDate)
                        : "-"}
                      {/* Only worth a second line once we sell shorter terms than we bought -
                          today the two are always identical. */}
                      {v.customerExpirationDate !==
                        v.supplierExpirationDate && (
                        <div className="text-[10px] text-muted-foreground/70">
                          {t("vouchers.customerExpires")}:{" "}
                          {formatDate(v.customerExpirationDate)}
                        </div>
                      )}
                    </td>
                    <td className="p-4 font-mono text-xs text-muted-foreground">
                      {v.voucherNumber}
                    </td>
                    <td className="p-4">
                      <div className="flex items-center gap-2">
                        <span
                          className={`px-2 py-1 rounded-md text-xs font-bold uppercase border backdrop-blur-md ${
                            statusKey === "available"
                              ? "bg-success/10 text-success border-success/20"
                              : statusKey === "assigned"
                                ? "bg-info/10 text-info border-info/20"
                                : statusKey === "used"
                                  ? "bg-muted text-muted-foreground border-border"
                                  : statusKey === "sold"
                                    ? "bg-warning/10 text-warning border-warning/20"
                                    : "bg-warning/10 text-warning border-warning/20"
                          }`}
                        >
                          {t(`status.${statusKey}`)}
                        </span>
                        {/* An archived voucher still holds its status - Available on an
                            archived row means "was sellable, is not any more", which reads as
                            live stock unless the row says otherwise. */}
                        {v.isDeleted && (
                          <>
                            <span className="px-2 py-1 rounded-md text-xs font-bold uppercase border border-destructive/30 bg-destructive/10 text-destructive">
                              {t("vouchers.archived")}
                            </span>
                            <Button
                              variant="outline"
                              size="sm"
                              className="h-7 text-xs"
                              disabled={unarchiveVoucherMutation.isPending}
                              onClick={() =>
                                unarchiveVoucherMutation.mutate(v.id)
                              }
                            >
                              {t("vouchers.unarchive")}
                            </Button>
                          </>
                        )}
                      </div>
                    </td>
                    <td className="p-4 text-muted-foreground text-xs font-mono">
                      {formatDate(v.createdAtUtc)}
                    </td>
                    <td className="p-4">
                      <Button
                        variant="outline"
                        size="sm"
                        className="h-7 text-xs"
                        aria-label={`${t("voucherLifecycle.open")}: ${v.voucherNumber}`}
                        onClick={() =>
                          setLifecycleVoucher({
                            id: v.id,
                            number: v.voucherNumber,
                          })
                        }
                      >
                        <History className="w-3 h-3 mr-1" />
                        {t("voucherLifecycle.open")}
                      </Button>
                    </td>
                  </tr>
                );
              })}
              {isVouchersLoading && (
                <tr>
                  <td
                    colSpan={12}
                    className="p-16 text-center text-muted-foreground"
                  >
                    <Loader2 className="w-6 h-6 animate-spin mx-auto mb-2" />
                    Loading...
                  </td>
                </tr>
              )}
              {!isVouchersLoading && vouchers.length === 0 && (
                <tr>
                  <td
                    colSpan={12}
                    className="p-16 text-center text-muted-foreground flex flex-col items-center justify-center gap-2"
                  >
                    <Package className="w-8 h-8 opacity-20" />
                    {t("import.noVouchers")}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="flex items-center justify-between p-4 border-t border-border bg-muted">
          <div className="text-xs text-muted-foreground">
            Showing {vouchers.length} items (Page {page})
          </div>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page === 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="h-8"
            >
              <ChevronLeft className="w-4 h-4 mr-1" /> Prev
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={vouchers.length < LIMIT}
              onClick={() => setPage((p) => p + 1)}
              className="h-8"
            >
              Next <ChevronRight className="w-4 h-4 ml-1" />
            </Button>
          </div>
        </div>
      </div>

      {selectedQrId && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-md p-4"
          onClick={() => setSelectedQrId(null)}
        >
          <div
            className="bg-white p-6 rounded-lg max-w-sm w-full animate-in zoom-in-50 duration-200"
            onClick={(e: React.MouseEvent) => e.stopPropagation()}
          >
            <h3 className="text-xl font-bold text-black mb-4">
              {t("import.scanTitle")}
            </h3>

            {isVoucherLoading ? (
              <div className="w-full h-64 flex items-center justify-center">
                <Loader2 className="w-8 h-8 animate-spin text-primary" />
              </div>
            ) : (
              <>
                <div className="w-full h-64 bg-white flex items-center justify-center mb-4 rounded-lg border-2 border-dashed border-gray-200">
                  <img
                    src={fullVoucherData?.qrImage ?? ""}
                    alt="QR"
                    style={{
                      width: 200,
                      height: 200,
                      imageRendering: "pixelated",
                    }}
                  />
                </div>
                <p className="font-mono text-xs break-all text-muted-foreground mb-4 bg-gray-100 p-2 rounded">
                  {fullVoucherData?.qrPayload}
                </p>
              </>
            )}

            <Button
              className="w-full font-bold"
              onClick={() => setSelectedQrId(null)}
            >
              {t("common.close")}
            </Button>
          </div>
        </div>
      )}

      {showDeleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-md p-4">
          <div
            className="glass-panel p-6 max-w-sm w-full animate-in zoom-in-50 duration-200"
            onClick={(e: React.MouseEvent) => e.stopPropagation()}
          >
            <h3 className="text-xl font-bold text-foreground mb-2">
              {t("vouchers.deleteConfirmTitle")}
            </h3>
            <p className="text-muted-foreground mb-6">
              {t("vouchers.deleteConfirm", selectedVoucherIds.size.toString())}
            </p>
            <div className="flex gap-2">
              <Button
                variant="outline"
                className="flex-1"
                onClick={() => setShowDeleteConfirm(false)}
              >
                {t("vouchers.cancel")}
              </Button>
              <Button
                variant="destructive"
                className="flex-1"
                onClick={() => {
                  deleteSelectedMutation.mutate(Array.from(selectedVoucherIds));
                  setShowDeleteConfirm(false);
                }}
              >
                {t("vouchers.delete")}
              </Button>
            </div>
          </div>
        </div>
      )}

      {showDeleteAllConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-md p-4">
          <div
            className="glass-panel p-6 max-w-sm w-full animate-in zoom-in-50 duration-200"
            onClick={(e: React.MouseEvent) => e.stopPropagation()}
          >
            <h3 className="text-xl font-bold text-foreground mb-2">
              {t("vouchers.deleteAllTitle")}
            </h3>
            <p className="text-muted-foreground mb-6">
              {t("vouchers.deleteAllConfirm", globalTotal.toString())}
            </p>
            <div className="flex gap-2">
              <Button
                variant="outline"
                className="flex-1"
                onClick={() => setShowDeleteAllConfirm(false)}
              >
                {t("vouchers.cancel")}
              </Button>
              <Button
                variant="destructive"
                className="flex-1"
                onClick={() => {
                  bulkDeleteMutation.mutate();
                  setShowDeleteAllConfirm(false);
                }}
              >
                {t("vouchers.deleteAllAction")}
              </Button>
            </div>
          </div>
        </div>
      )}

      <VoucherLifecycleDrawer
        voucherId={lifecycleVoucher?.id ?? null}
        voucherNumber={lifecycleVoucher?.number ?? null}
        onOpenChange={(open) => {
          if (!open) setLifecycleVoucher(null);
        }}
      />
    </div>
  );
}
