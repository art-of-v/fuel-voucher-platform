import { useState, useEffect, useCallback, useRef } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Save, X, TrendingUp, Search, Filter, ChevronDown, ChevronUp,
  ArrowUpDown, Zap, History, AlertCircle, CheckCircle2, Loader2,
  RefreshCw, Copy, RotateCcw, TrendingDown
} from "lucide-react";
import { apiRequest } from "@/lib/api-client";
import { toast } from "sonner";
import type { CurrentUser } from "@/lib/admin-auth";

// ─── Types ────────────────────────────────────────────────────────────────────

interface FuelPriceDto {
  id: string;
  stationId: string;
  stationName: string;
  fuelTypeId: string;
  fuelName: string;
  liters: number;
  supplierPricePerLiter: number | null;
  marginUahPerLiter: number | null;
  marginPercent: number | null;
  finalPricePerLiter: number | null;
  priceUpdatedAt: string | null;
  priceUpdatedByUserId: string | null;
}

interface FuelPriceAuditDto {
  id: string;
  packageId: string;
  fuelName: string;
  oldSupplierPricePerLiter: number | null;
  newSupplierPricePerLiter: number | null;
  oldMarginUahPerLiter: number | null;
  newMarginUahPerLiter: number | null;
  oldMarginPercent: number | null;
  newMarginPercent: number | null;
  oldFinalPricePerLiter: number | null;
  newFinalPricePerLiter: number | null;
  changedByUserId: string;
  changedAtUtc: string;
}

type EditableField = "supplierPricePerLiter" | "marginUahPerLiter" | "marginPercent" | "finalPricePerLiter";

interface PendingEdit {
  supplierPricePerLiter: string;
  marginUahPerLiter: string;
  marginPercent: string;
  finalPricePerLiter: string;
  errors: Partial<Record<EditableField, string>>;
}

type SortField = "fuelName" | "supplierPricePerLiter" | "marginUahPerLiter" | "marginPercent" | "finalPricePerLiter";

// ─── Helpers ──────────────────────────────────────────────────────────────────

function fmt(val: number | null | undefined): string {
  if (val === null || val === undefined) return "—";
  return val.toFixed(2);
}

function fmtPct(val: number | null | undefined): string {
  if (val === null || val === undefined) return "—";
  return `${val.toFixed(2)} %`;
}

/** Client-side recalculation mirrors server logic */
function recalculate(
  supplier: number | null,
  changedField: EditableField,
  changedValue: number,
  current: { marginUahPerLiter: number | null; marginPercent: number | null; finalPricePerLiter: number | null }
): { supplierPricePerLiter: number | null; marginUahPerLiter: number | null; marginPercent: number | null; finalPricePerLiter: number | null } {
  const sup = changedField === "supplierPricePerLiter" ? changedValue : supplier;

  if (changedField === "finalPricePerLiter") {
    const fin = changedValue;
    const mUah = sup !== null ? fin - sup : null;
    const mPct = sup !== null && sup !== 0 ? (mUah! / sup) * 100 : null;
    return { supplierPricePerLiter: sup, marginUahPerLiter: mUah, marginPercent: mPct, finalPricePerLiter: fin };
  }

  if (changedField === "marginUahPerLiter") {
    const mUah = changedValue;
    const fin = sup !== null ? sup + mUah : null;
    const mPct = sup !== null && sup !== 0 ? (mUah / sup) * 100 : null;
    return { supplierPricePerLiter: sup, marginUahPerLiter: mUah, marginPercent: mPct, finalPricePerLiter: fin };
  }

  if (changedField === "marginPercent") {
    const mPct = changedValue;
    const mUah = sup !== null ? sup * (mPct / 100) : null;
    const fin = sup !== null && mUah !== null ? sup + mUah : null;
    return { supplierPricePerLiter: sup, marginUahPerLiter: mUah, marginPercent: mPct, finalPricePerLiter: fin };
  }

  // supplier changed — re-derive final if margin% is known
  if (changedField === "supplierPricePerLiter") {
    const s = changedValue;
    const mPct = current.marginPercent;
    if (mPct !== null) {
      const mUah = s * (mPct / 100);
      const fin = s + mUah;
      return { supplierPricePerLiter: s, marginUahPerLiter: mUah, marginPercent: mPct, finalPricePerLiter: fin };
    }
    const mUah = current.marginUahPerLiter;
    if (mUah !== null) {
      const fin = s + mUah;
      const newPct = s !== 0 ? (mUah / s) * 100 : null;
      return { supplierPricePerLiter: s, marginUahPerLiter: mUah, marginPercent: newPct, finalPricePerLiter: fin };
    }
    return { supplierPricePerLiter: s, marginUahPerLiter: current.marginUahPerLiter, marginPercent: current.marginPercent, finalPricePerLiter: current.finalPricePerLiter };
  }

  return { supplierPricePerLiter: sup, ...current };
}

function validateField(field: EditableField, value: number, supplier: number | null): string | null {
  if (field === "supplierPricePerLiter" && value < 0) return "Не може бути від'ємним";
  if (field === "finalPricePerLiter" && value < 0) return "Не може бути від'ємним";
  if (field === "marginPercent" && value > 500) return "Не може перевищувати 500%";
  if (field === "marginUahPerLiter" && supplier !== null && value < -supplier) return "Призведе до від'ємної ціни";
  return null;
}

// ─── Main Component ───────────────────────────────────────────────────────────

interface FuelPricesTabProps {
  user: CurrentUser | null;
}

export default function FuelPricesTab({ user }: FuelPricesTabProps) {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  // Pending edits: packageId → edit state
  const [pendingEdits, setPendingEdits] = useState<Record<string, PendingEdit>>({});
  const [activeCell, setActiveCell] = useState<{ id: string; field: EditableField } | null>(null);

  // Filters & sorting
  const [search, setSearch] = useState("");
  const [filterStation, setFilterStation] = useState("");
  const [sortField, setSortField] = useState<SortField>("fuelName");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");

  // Quick actions
  const [showQuickActions, setShowQuickActions] = useState(false);
  const [qaMode, setQaMode] = useState<"increaseMarginUah" | "decreaseMarginUah" | "increaseAllPct" | "copyMargin" | null>(null);
  const [qaValue, setQaValue] = useState("");
  const [qaCopyFrom, setQaCopyFrom] = useState("");

  // Audit log
  const [showAudit, setShowAudit] = useState(false);

  // Table ref for keyboard nav
  const tableRef = useRef<HTMLTableElement>(null);

  // ─── Data fetching ─────────────────────────────────────────────────────

  const { data: prices = [], isLoading } = useQuery<FuelPriceDto[]>({
    queryKey: ["/api/admin/fuel-prices"],
    enabled: !!user,
  });

  const { data: auditLog = [] } = useQuery<FuelPriceAuditDto[]>({
    queryKey: ["/api/admin/fuel-prices/audit"],
    enabled: !!user && showAudit,
  });

  // ─── Unsaved changes warning ───────────────────────────────────────────

  const hasPendingEdits = Object.keys(pendingEdits).length > 0;

  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (hasPendingEdits) {
        e.preventDefault();
        e.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [hasPendingEdits]);

  // ─── Mutations ──────────────────────────────────────────────────────────

  const bulkSaveMutation = useMutation({
    mutationFn: async (patches: Array<{ packageId: string } & Omit<PendingEdit, "errors">>) => {
      const payload = patches.map((p) => ({
        packageId: p.packageId,
        supplierPricePerLiter: p.supplierPricePerLiter !== "" ? parseFloat(p.supplierPricePerLiter) : undefined,
        marginUahPerLiter: p.marginUahPerLiter !== "" ? parseFloat(p.marginUahPerLiter) : undefined,
        marginPercent: p.marginPercent !== "" ? parseFloat(p.marginPercent) : undefined,
        finalPricePerLiter: p.finalPricePerLiter !== "" ? parseFloat(p.finalPricePerLiter) : undefined,
      }));
      return apiRequest<any, any>("PATCH", "/api/admin/fuel-prices/bulk", { patches: payload });
    },
    onSuccess: (data) => {
      setPendingEdits({});
      setActiveCell(null);
      queryClient.invalidateQueries({ queryKey: ["/api/admin/fuel-prices"] });
      queryClient.invalidateQueries({ queryKey: ["/api/admin/fuel-prices/audit"] });
      if (data.errors?.length > 0) {
        toast.warning(`Збережено з ${data.errors.length} помилками`);
      } else {
        toast.success(`Збережено ${data.updatedCount} пакет(ів)`);
      }
    },
    onError: (e: Error) => toast.error(e.message),
  });

  // ─── Filtered & sorted data ─────────────────────────────────────────────

  const stations = [...new Set(prices.map((p) => p.stationName))].sort();

  const displayPrices = prices
    .filter((p) => {
      if (filterStation && p.stationName !== filterStation) return false;
      if (search) {
        const q = search.toLowerCase();
        return (
          p.fuelName.toLowerCase().includes(q) ||
          p.stationName.toLowerCase().includes(q) ||
          p.id.toLowerCase().includes(q)
        );
      }
      return true;
    })
    .sort((a, b) => {
      const getVal = (row: FuelPriceDto): number | string => {
        if (sortField === "fuelName") return row.fuelName + row.stationName;
        return row[sortField] ?? -Infinity;
      };
      const va = getVal(a);
      const vb = getVal(b);
      if (va < vb) return sortDir === "asc" ? -1 : 1;
      if (va > vb) return sortDir === "asc" ? 1 : -1;
      return 0;
    });

  // ─── Edit helpers ───────────────────────────────────────────────────────

  const getDisplayRow = useCallback(
    (row: FuelPriceDto): FuelPriceDto => {
      const edit = pendingEdits[row.id];
      if (!edit) return row;
      return {
        ...row,
        supplierPricePerLiter: edit.supplierPricePerLiter !== "" ? parseFloat(edit.supplierPricePerLiter) : row.supplierPricePerLiter,
        marginUahPerLiter: edit.marginUahPerLiter !== "" ? parseFloat(edit.marginUahPerLiter) : row.marginUahPerLiter,
        marginPercent: edit.marginPercent !== "" ? parseFloat(edit.marginPercent) : row.marginPercent,
        finalPricePerLiter: edit.finalPricePerLiter !== "" ? parseFloat(edit.finalPricePerLiter) : row.finalPricePerLiter,
      };
    },
    [pendingEdits]
  );

  const startEdit = (row: FuelPriceDto) => {
    if (!pendingEdits[row.id]) {
      setPendingEdits((prev) => ({
        ...prev,
        [row.id]: {
          supplierPricePerLiter: row.supplierPricePerLiter?.toFixed(2) ?? "",
          marginUahPerLiter: row.marginUahPerLiter?.toFixed(2) ?? "",
          marginPercent: row.marginPercent?.toFixed(2) ?? "",
          finalPricePerLiter: row.finalPricePerLiter?.toFixed(2) ?? "",
          errors: {},
        },
      }));
    }
  };

  const handleCellChange = (rowId: string, field: EditableField, rawValue: string, originalRow: FuelPriceDto) => {
    const value = parseFloat(rawValue);

    if (rawValue !== "" && isNaN(value)) {
      setPendingEdits((prev) => ({
        ...prev,
        [rowId]: { ...prev[rowId], [field]: rawValue, errors: { ...prev[rowId].errors, [field]: "Невірне значення" } },
      }));
      return;
    }

    if (rawValue === "") {
      setPendingEdits((prev) => ({
        ...prev,
        [rowId]: { ...prev[rowId], [field]: "", errors: { ...prev[rowId].errors, [field]: undefined } },
      }));
      return;
    }

    const current = getDisplayRow(originalRow);
    const supplierForCalc = field === "supplierPricePerLiter" ? value : current.supplierPricePerLiter;

    const err = validateField(field, value, supplierForCalc);
    const recalced = recalculate(current.supplierPricePerLiter, field, value, {
      marginUahPerLiter: current.marginUahPerLiter,
      marginPercent: current.marginPercent,
      finalPricePerLiter: current.finalPricePerLiter,
    });

    setPendingEdits((prev) => ({
      ...prev,
      [rowId]: {
        supplierPricePerLiter: recalced.supplierPricePerLiter?.toFixed(4) ?? "",
        marginUahPerLiter: recalced.marginUahPerLiter?.toFixed(4) ?? "",
        marginPercent: recalced.marginPercent?.toFixed(4) ?? "",
        finalPricePerLiter: recalced.finalPricePerLiter?.toFixed(4) ?? "",
        errors: { ...(err ? { [field]: err } : {}) },
      },
    }));
  };

  const cancelRowEdit = (rowId: string) => {
    setPendingEdits((prev) => {
      const next = { ...prev };
      delete next[rowId];
      return next;
    });
    setActiveCell(null);
  };

  const cancelAllEdits = () => {
    if (hasPendingEdits && !window.confirm("Скасувати всі незбережені зміни?")) return;
    setPendingEdits({});
    setActiveCell(null);
  };

  const saveAll = () => {
    const hasErrors = Object.values(pendingEdits).some((e) => Object.keys(e.errors).length > 0);
    if (hasErrors) {
      toast.error("Виправте помилки перед збереженням");
      return;
    }
    const patches = Object.entries(pendingEdits).map(([id, edit]) => ({ packageId: id, ...edit }));
    bulkSaveMutation.mutate(patches);
  };

  // ─── Keyboard navigation ────────────────────────────────────────────────

  const FIELDS: EditableField[] = ["supplierPricePerLiter", "marginUahPerLiter", "marginPercent", "finalPricePerLiter"];

  const handleKeyDown = (e: React.KeyboardEvent, rowId: string, field: EditableField, _originalRow: FuelPriceDto) => {
    if (e.key === "Escape") {
      cancelRowEdit(rowId);
      return;
    }
    if (e.key === "Enter") {
      e.preventDefault();
      setActiveCell(null);
      return;
    }
    if (e.key === "Tab") {
      e.preventDefault();
      const fieldIdx = FIELDS.indexOf(field);
      const rowIdx = displayPrices.findIndex((r) => r.id === rowId);

      let nextField = fieldIdx + (e.shiftKey ? -1 : 1);
      let nextRowIdx = rowIdx;

      if (nextField >= FIELDS.length) { nextField = 0; nextRowIdx = Math.min(rowIdx + 1, displayPrices.length - 1); }
      if (nextField < 0) { nextField = FIELDS.length - 1; nextRowIdx = Math.max(rowIdx - 1, 0); }

      const nextRow = displayPrices[nextRowIdx];
      startEdit(nextRow);
      setActiveCell({ id: nextRow.id, field: FIELDS[nextField] });
      return;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      const rowIdx = displayPrices.findIndex((r) => r.id === rowId);
      if (rowIdx < displayPrices.length - 1) {
        const nextRow = displayPrices[rowIdx + 1];
        startEdit(nextRow);
        setActiveCell({ id: nextRow.id, field });
      }
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      const rowIdx = displayPrices.findIndex((r) => r.id === rowId);
      if (rowIdx > 0) {
        const prevRow = displayPrices[rowIdx - 1];
        startEdit(prevRow);
        setActiveCell({ id: prevRow.id, field });
      }
    }
  };

  // ─── Quick actions ──────────────────────────────────────────────────────

  const applyQuickAction = () => {
    const amount = parseFloat(qaValue);
    if (isNaN(amount)) { toast.error("Введіть коректне число"); return; }

    const affected = displayPrices.filter((p) =>
      p.supplierPricePerLiter !== null || qaMode === "increaseAllPct"
    );

    setPendingEdits((prev) => {
      const next = { ...prev };
      for (const row of affected) {
        const current = getDisplayRow(row);
        let sup = current.supplierPricePerLiter;
        let mUah = current.marginUahPerLiter ?? 0;
        let mPct = current.marginPercent ?? 0;
        let fin = current.finalPricePerLiter;

        if (qaMode === "increaseMarginUah") {
          mUah = mUah + amount;
          if (sup !== null) { fin = sup + mUah; mPct = sup !== 0 ? (mUah / sup) * 100 : 0; }
        } else if (qaMode === "decreaseMarginUah") {
          mUah = mUah - amount;
          if (sup !== null) { fin = sup + mUah; mPct = sup !== 0 ? (mUah / sup) * 100 : 0; }
        } else if (qaMode === "increaseAllPct") {
          if (sup !== null) sup = sup * (1 + amount / 100);
          if (sup !== null) { fin = sup + mUah; mPct = sup !== 0 ? (mUah / sup) * 100 : 0; }
        }

        next[row.id] = {
          supplierPricePerLiter: sup?.toFixed(4) ?? "",
          marginUahPerLiter: mUah.toFixed(4),
          marginPercent: mPct.toFixed(4),
          finalPricePerLiter: fin?.toFixed(4) ?? "",
          errors: {},
        };
      }
      return next;
    });

    setShowQuickActions(false);
    setQaMode(null);
    setQaValue("");
    toast.success(`Застосовано до ${affected.length} рядків`);
  };

  const applyCopyMargin = () => {
    const sourceRow = prices.find((p) => p.id === qaCopyFrom);
    if (!sourceRow || sourceRow.marginPercent === null) {
      toast.error("Джерело не має маржи");
      return;
    }

    const targetFuelType = sourceRow.fuelTypeId;
    const targetRows = displayPrices.filter((p) => p.fuelTypeId === targetFuelType && p.id !== qaCopyFrom);

    setPendingEdits((prev) => {
      const next = { ...prev };
      for (const row of targetRows) {
        const current = getDisplayRow(row);
        const sup = current.supplierPricePerLiter ?? 0;
        const mPct = sourceRow.marginPercent!;
        const mUah = sup * (mPct / 100);
        const fin = sup + mUah;
        next[row.id] = {
          supplierPricePerLiter: sup.toFixed(4),
          marginUahPerLiter: mUah.toFixed(4),
          marginPercent: mPct.toFixed(4),
          finalPricePerLiter: fin.toFixed(4),
          errors: {},
        };
      }
      return next;
    });

    setShowQuickActions(false);
    setQaMode(null);
    toast.success(`Маржу ${mPct?.toFixed(2)}% скопійовано до ${targetRows.length} пакетів`);
  };

  const applyResetMargin = () => {
    setPendingEdits((prev) => {
      const next = { ...prev };
      for (const row of displayPrices) {
        const sup = row.supplierPricePerLiter;
        next[row.id] = {
          supplierPricePerLiter: sup?.toFixed(4) ?? "",
          marginUahPerLiter: "0.0000",
          marginPercent: "0.0000",
          finalPricePerLiter: sup?.toFixed(4) ?? "",
          errors: {},
        };
      }
      return next;
    });
    setShowQuickActions(false);
    toast.success("Маржу скинуто до 0 для всіх рядків");
  };

  const mPct = prices.find((p) => p.id === qaCopyFrom)?.marginPercent;

  // ─── Sort handler ────────────────────────────────────────────────────────

  const toggleSort = (field: SortField) => {
    if (sortField === field) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    else { setSortField(field); setSortDir("asc"); }
  };

  const SortIcon = ({ field }: { field: SortField }) => {
    if (sortField !== field) return <ArrowUpDown className="w-3 h-3 opacity-40 ml-1 inline" />;
    return sortDir === "asc"
      ? <ChevronUp className="w-3 h-3 text-primary ml-1 inline" />
      : <ChevronDown className="w-3 h-3 text-primary ml-1 inline" />;
  };

  // ─── Cell renderer ───────────────────────────────────────────────────────

  const renderCell = (row: FuelPriceDto, field: EditableField) => {
    const isActive = activeCell?.id === row.id && activeCell?.field === field;
    const edit = pendingEdits[row.id];
    const displayRow = getDisplayRow(row);
    const error = edit?.errors[field];

    const rawVal = edit ? edit[field] : "";
    const displayVal = displayRow[field];

    const color =
      field === "marginUahPerLiter" || field === "marginPercent"
        ? displayVal !== null && typeof displayVal === "number" && displayVal < 0
          ? "text-red-400"
          : "text-blue-400"
        : field === "finalPricePerLiter"
        ? displayVal !== null && typeof displayVal === "number" && displayVal < 0
          ? "text-red-400"
          : "text-green-400"
        : "text-foreground";

    const suffix = field === "marginPercent" ? " %" : " ₴";

    if (isActive && edit) {
      return (
        <div className="relative">
          <input
            autoFocus
            type="number"
            step="0.01"
            className={`w-full bg-gray-800 border rounded px-2 py-1 text-sm font-mono focus:outline-none focus:ring-1 ${
              error ? "border-red-500 focus:ring-red-500" : "border-primary/60 focus:ring-primary"
            }`}
            value={rawVal}
            onChange={(e) => handleCellChange(row.id, field, e.target.value, row)}
            onKeyDown={(e) => handleKeyDown(e, row.id, field, row)}
            onBlur={() => setTimeout(() => setActiveCell(null), 100)}
          />
          {error && (
            <div className="absolute -bottom-5 left-0 text-xs text-red-400 whitespace-nowrap z-10 flex items-center gap-1">
              <AlertCircle className="w-3 h-3" /> {error}
            </div>
          )}
        </div>
      );
    }

    return (
      <div
        className={`cursor-pointer hover:bg-primary/10 rounded px-2 py-1 font-mono text-sm transition-colors select-none ${color}`}
        onClick={() => {
          startEdit(row);
          setActiveCell({ id: row.id, field });
        }}
        title="Натисніть для редагування"
      >
        {field === "marginPercent"
          ? fmtPct(displayVal as number | null)
          : displayVal !== null && displayVal !== undefined
          ? `${fmt(displayVal as number | null)}${suffix}`
          : <span className="text-muted-foreground text-xs">— встановити</span>}
      </div>
    );
  };

  // ─── Render ──────────────────────────────────────────────────────────────

  const dirtyCount = Object.keys(pendingEdits).length;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <Loader2 className="w-8 h-8 animate-spin text-primary" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* ── Header ── */}
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h2 className="text-2xl font-bold flex items-center gap-2">
            <TrendingUp className="w-6 h-6 text-primary" />
            {t('fuelprices.packagePrices')}
          </h2>
          <p className="text-muted-foreground text-sm mt-1">
            {t('fuelprices.editHint')}
          </p>
        </div>

        {/* Bulk action toolbar */}
        <div className="flex items-center gap-2 flex-wrap">
          {dirtyCount > 0 && (
            <>
              <span className="text-sm text-amber-400 font-medium flex items-center gap-1">
                <AlertCircle className="w-4 h-4" />
                {dirtyCount} змінено
              </span>
              <button
                onClick={cancelAllEdits}
                className="flex items-center gap-2 px-3 py-1.5 text-sm rounded-md border border-border text-muted-foreground hover:text-foreground hover:border-foreground/40 transition-colors"
              >
                <X className="w-4 h-4" /> Скасувати всі
              </button>
              <button
                onClick={saveAll}
                disabled={bulkSaveMutation.isPending}
                className="flex items-center gap-2 px-4 py-1.5 text-sm rounded-md bg-primary text-black font-semibold hover:bg-primary/90 transition-colors disabled:opacity-60"
              >
                {bulkSaveMutation.isPending ? (
                  <Loader2 className="w-4 h-4 animate-spin" />
                ) : (
                  <Save className="w-4 h-4" />
                )}
                Зберегти всі ({dirtyCount})
              </button>
            </>
          )}

          {/* Quick actions */}
          <div className="relative">
            <button
              onClick={() => setShowQuickActions((v) => !v)}
              className="flex items-center gap-2 px-3 py-1.5 text-sm rounded-md border border-border text-muted-foreground hover:text-foreground hover:border-primary/60 transition-colors"
            >
              <Zap className="w-4 h-4" /> Дії
              <ChevronDown className="w-3 h-3" />
            </button>

            {showQuickActions && (
              <div className="absolute right-0 top-full mt-1 w-80 bg-card border border-border rounded-xl shadow-2xl z-50 p-4 space-y-3">
                <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">Швидкі дії</p>

                {/* Mode selector */}
                <div className="grid grid-cols-1 gap-1.5">
                  {[
                    { mode: "increaseMarginUah" as const, label: "Збільшити маржу (UAH)", icon: TrendingUp },
                    { mode: "decreaseMarginUah" as const, label: "Зменшити маржу (UAH)", icon: TrendingDown },
                    { mode: "increaseAllPct" as const, label: "Збільшити ціни на %", icon: RefreshCw },
                    { mode: "copyMargin" as const, label: "Скопіювати маржу...", icon: Copy },
                  ].map(({ mode, label, icon: Icon }) => (
                    <button
                      key={mode}
                      onClick={() => setQaMode((v) => (v === mode ? null : mode))}
                      className={`flex items-center gap-2 px-3 py-2 text-sm rounded-md transition-colors text-left ${
                        qaMode === mode ? "bg-primary/20 text-primary border border-primary/40" : "hover:bg-muted text-muted-foreground hover:text-foreground"
                      }`}
                    >
                      <Icon className="w-4 h-4" /> {label}
                    </button>
                  ))}
                </div>

                {/* Action input */}
                {qaMode && qaMode !== "copyMargin" && (
                  <div className="space-y-2">
                    <input
                      type="number"
                      step="0.01"
                      placeholder={qaMode === "increaseAllPct" ? "Відсоток (наприклад 5)" : "Сума UAH (наприклад 2.50)"}
                      value={qaValue}
                      onChange={(e) => setQaValue(e.target.value)}
                      className="w-full bg-background border border-border rounded-md px-3 py-1.5 text-sm focus:outline-none focus:ring-1 focus:ring-primary"
                    />
                    <button
                      onClick={applyQuickAction}
                      className="w-full py-1.5 text-sm rounded-md bg-primary text-black font-semibold hover:bg-primary/90 transition-colors"
                    >
                      Застосувати до {displayPrices.length} рядків
                    </button>
                  </div>
                )}

                {qaMode === "copyMargin" && (
                  <div className="space-y-2">
                    <select
                      value={qaCopyFrom}
                      onChange={(e) => setQaCopyFrom(e.target.value)}
                      className="w-full bg-background border border-border rounded-md px-3 py-1.5 text-sm focus:outline-none focus:ring-1 focus:ring-primary"
                    >
                      <option value="">— Виберіть пакет —</option>
                      {prices.filter((p) => p.marginPercent !== null).map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.stationName} · {p.fuelName} {p.liters}L — {p.marginPercent?.toFixed(2)}%
                        </option>
                      ))}
                    </select>
                    {qaCopyFrom && (
                      <button
                        onClick={applyCopyMargin}
                        className="w-full py-1.5 text-sm rounded-md bg-primary text-black font-semibold hover:bg-primary/90 transition-colors"
                      >
                        Скопіювати {mPct?.toFixed(2)}% до однотипних пакетів
                      </button>
                    )}
                  </div>
                )}

                <hr className="border-border" />
                <button
                  onClick={applyResetMargin}
                  className="flex items-center gap-2 w-full px-3 py-2 text-sm rounded-md text-red-400 hover:bg-red-900/20 transition-colors"
                >
                  <RotateCcw className="w-4 h-4" /> Скинути маржу до 0 (всі видимі)
                </button>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* ── Filters ── */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="relative flex-1 min-w-48">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
          <input
            type="text"
            placeholder="Пошук за назвою пального або станцією..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-9 pr-4 py-2 bg-card border border-border rounded-lg text-sm focus:outline-none focus:ring-1 focus:ring-primary"
          />
        </div>
        <div className="relative">
          <Filter className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
          <select
            value={filterStation}
            onChange={(e) => setFilterStation(e.target.value)}
            className="pl-9 pr-8 py-2 bg-card border border-border rounded-lg text-sm focus:outline-none focus:ring-1 focus:ring-primary appearance-none"
          >
            <option value="">Всі станції</option>
            {stations.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        <span className="text-sm text-muted-foreground">
          {displayPrices.length} пакетів
        </span>
      </div>

      {/* ── Legend ── */}
      <div className="flex items-center gap-4 text-xs text-muted-foreground">
        <span className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-foreground/40 inline-block" /> Постачальник</span>
        <span className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-blue-400 inline-block" /> Маржа</span>
        <span className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-green-400 inline-block" /> Фінальна</span>
        <span className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-red-400 inline-block" /> Від'ємна маржа</span>
        <span className="flex items-center gap-1.5"><span className="w-3 h-3 rounded-sm bg-amber-900/40 inline-block border border-amber-500/30" /> Змінено</span>
      </div>

      {/* ── Table ── */}
      <div className="bg-card border border-border rounded-xl overflow-hidden">
        <div className="overflow-x-auto">
          <table ref={tableRef} className="w-full text-sm">
            <thead className="bg-muted/50 border-b border-border">
              <tr>
                <th className="text-left px-4 py-3 font-semibold text-muted-foreground">
                  <button onClick={() => toggleSort("fuelName")} className="flex items-center hover:text-foreground transition-colors">
                    Пальне / Станція <SortIcon field="fuelName" />
                  </button>
                </th>
                <th className="px-3 py-3 font-semibold text-muted-foreground text-right">Літрів</th>
                <th className="px-3 py-3 font-semibold text-muted-foreground">
                  <button onClick={() => toggleSort("supplierPricePerLiter")} className="flex items-center w-full justify-end hover:text-foreground transition-colors">
                    Постачальник ₴/л <SortIcon field="supplierPricePerLiter" />
                  </button>
                </th>
                <th className="px-3 py-3 font-semibold text-blue-400">
                  <button onClick={() => toggleSort("marginUahPerLiter")} className="flex items-center w-full justify-end hover:text-blue-300 transition-colors">
                    Маржа ₴/л <SortIcon field="marginUahPerLiter" />
                  </button>
                </th>
                <th className="px-3 py-3 font-semibold text-blue-400">
                  <button onClick={() => toggleSort("marginPercent")} className="flex items-center w-full justify-end hover:text-blue-300 transition-colors">
                    Маржа % <SortIcon field="marginPercent" />
                  </button>
                </th>
                <th className="px-3 py-3 font-semibold text-green-400">
                  <button onClick={() => toggleSort("finalPricePerLiter")} className="flex items-center w-full justify-end hover:text-green-300 transition-colors">
                    Фінальна ₴/л <SortIcon field="finalPricePerLiter" />
                  </button>
                </th>
                <th className="px-3 py-3 text-right text-muted-foreground font-semibold">Дії</th>
              </tr>
            </thead>
            <tbody>
              {displayPrices.length === 0 ? (
                <tr>
                  <td colSpan={7} className="px-4 py-12 text-center text-muted-foreground">
                    Пакети не знайдено
                  </td>
                </tr>
              ) : (
                displayPrices.map((row, rowIdx) => {
                  const isDirty = !!pendingEdits[row.id];
                  const hasError = isDirty && Object.keys(pendingEdits[row.id].errors).length > 0;


                  return (
                    <tr
                      key={row.id}
                      className={`border-t border-border transition-colors ${
                        hasError
                          ? "bg-red-950/20 border-l-2 border-l-red-500"
                          : isDirty
                          ? "bg-amber-950/20 border-l-2 border-l-amber-500"
                          : rowIdx % 2 === 0
                          ? "hover:bg-muted/30"
                          : "bg-muted/10 hover:bg-muted/30"
                      }`}
                    >
                      {/* Fuel / Station */}
                      <td className="px-4 py-2">
                        <div className="font-semibold text-foreground">{row.fuelName}</div>
                        <div className="text-xs text-muted-foreground">{row.stationName}</div>
                      </td>

                      {/* Liters */}
                      <td className="px-3 py-2 text-right text-muted-foreground font-mono">
                        {row.liters}
                      </td>

                      {/* Supplier */}
                      <td className="px-3 py-2 text-right">
                        {renderCell(row, "supplierPricePerLiter")}
                      </td>

                      {/* Margin UAH */}
                      <td className="px-3 py-2 text-right">
                        {renderCell(row, "marginUahPerLiter")}
                      </td>

                      {/* Margin % */}
                      <td className="px-3 py-2 text-right">
                        {renderCell(row, "marginPercent")}
                      </td>

                      {/* Final price */}
                      <td className="px-3 py-2 text-right">
                        {renderCell(row, "finalPricePerLiter")}
                      </td>

                      {/* Actions */}
                      <td className="px-3 py-2 text-right">
                        {isDirty ? (
                          <div className="flex items-center gap-1 justify-end">
                            {hasError ? (
                              <AlertCircle className="w-4 h-4 text-red-400" />
                            ) : (
                              <CheckCircle2 className="w-4 h-4 text-amber-400" />
                            )}
                            <button
                              onClick={() => cancelRowEdit(row.id)}
                              className="p-1 text-muted-foreground hover:text-foreground rounded transition-colors"
                              title="Скасувати"
                            >
                              <X className="w-3 h-3" />
                            </button>
                          </div>
                        ) : row.priceUpdatedAt ? (
                          <span className="text-xs text-muted-foreground" title={new Date(row.priceUpdatedAt).toLocaleString()}>
                            {new Date(row.priceUpdatedAt).toLocaleDateString()}
                          </span>
                        ) : null}
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* ── Audit log ── */}
      <div className="border border-border rounded-xl overflow-hidden">
        <button
          onClick={() => setShowAudit((v) => !v)}
          className="w-full flex items-center justify-between px-4 py-3 bg-card hover:bg-muted/30 transition-colors text-sm font-medium"
        >
          <span className="flex items-center gap-2">
            <History className="w-4 h-4 text-muted-foreground" />
            Журнал змін цін
          </span>
          {showAudit ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
        </button>

        {showAudit && (
          <div className="overflow-x-auto border-t border-border">
            <table className="w-full text-xs">
              <thead className="bg-muted/50">
                <tr>
                  <th className="text-left px-4 py-2 text-muted-foreground">Час</th>
                  <th className="text-left px-4 py-2 text-muted-foreground">Пальне</th>
                  <th className="px-4 py-2 text-muted-foreground text-right">Постачальник</th>
                  <th className="px-4 py-2 text-blue-400 text-right">Маржа ₴</th>
                  <th className="px-4 py-2 text-blue-400 text-right">Маржа %</th>
                  <th className="px-4 py-2 text-green-400 text-right">Фінальна</th>
                  <th className="text-left px-4 py-2 text-muted-foreground">Ким</th>
                </tr>
              </thead>
              <tbody>
                {auditLog.length === 0 ? (
                  <tr><td colSpan={7} className="px-4 py-8 text-center text-muted-foreground">Змін ще не було</td></tr>
                ) : auditLog.map((entry) => (
                  <tr key={entry.id} className="border-t border-border hover:bg-muted/20">
                    <td className="px-4 py-2 text-muted-foreground">{new Date(entry.changedAtUtc).toLocaleString()}</td>
                    <td className="px-4 py-2 font-medium">{entry.fuelName}</td>
                    <td className="px-4 py-2 text-right font-mono">
                      {entry.oldSupplierPricePerLiter !== entry.newSupplierPricePerLiter && (
                        <><span className="text-red-400 line-through">{fmt(entry.oldSupplierPricePerLiter)}</span>
                        <span className="mx-1 text-muted-foreground">→</span>
                        <span className="text-green-400">{fmt(entry.newSupplierPricePerLiter)} ₴</span></>
                      )}
                    </td>
                    <td className="px-4 py-2 text-right font-mono">
                      {entry.oldMarginUahPerLiter !== entry.newMarginUahPerLiter && (
                        <><span className="text-red-400 line-through">{fmt(entry.oldMarginUahPerLiter)}</span>
                        <span className="mx-1 text-muted-foreground">→</span>
                        <span className="text-blue-400">{fmt(entry.newMarginUahPerLiter)} ₴</span></>
                      )}
                    </td>
                    <td className="px-4 py-2 text-right font-mono">
                      {entry.oldMarginPercent !== entry.newMarginPercent && (
                        <><span className="text-red-400 line-through">{fmtPct(entry.oldMarginPercent)}</span>
                        <span className="mx-1 text-muted-foreground">→</span>
                        <span className="text-blue-400">{fmtPct(entry.newMarginPercent)}</span></>
                      )}
                    </td>
                    <td className="px-4 py-2 text-right font-mono">
                      {entry.oldFinalPricePerLiter !== entry.newFinalPricePerLiter && (
                        <><span className="text-red-400 line-through">{fmt(entry.oldFinalPricePerLiter)}</span>
                        <span className="mx-1 text-muted-foreground">→</span>
                        <span className="text-green-400">{fmt(entry.newFinalPricePerLiter)} ₴</span></>
                      )}
                    </td>
                    <td className="px-4 py-2 font-mono text-muted-foreground">{entry.changedByUserId.slice(0, 8)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
