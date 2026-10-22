import {
  Edit2,
  Save,
  X,
  Loader2,
  History,
  Trash2,
  AlertTriangle,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { DecimalSettingInput } from "@/components/DecimalSettingInput";
import { resolveAllowBelowCost } from "@/lib/belowCostOptIn";
import { computeFinal } from "@/lib/finalPrice";
import { useI18n } from "@/lib/i18n";
import type { BrandsController } from "./useBrandsData";
import type { BrandFuelDto } from "./types";

interface BrandFuelRowProps {
  fuel: BrandFuelDto;
  controller: BrandsController;
  onNavigateToImports?: () => void;
}

export function BrandFuelRow({
  fuel,
  controller,
  onNavigateToImports,
}: BrandFuelRowProps) {
  const { t } = useI18n();
  const {
    editingFuel,
    setEditingFuel,
    editValues,
    setEditValues,
    updateFuelMutation,
    deleteFuelMutation,
    confirmDeleteFuel,
    setConfirmDeleteFuel,
    startEditFuel,
    saveFuel,
    setPriceHistoryFor,
    isQaFuel,
  } = controller;

  const isEditing = editingFuel === fuel.id;
  const vals = editValues[fuel.id];
  const saving = updateFuelMutation.isPending && editingFuel === fuel.id;
  const deleting = deleteFuelMutation.isPending;
  const editCost = vals?.supplierPricePerLiter ?? fuel.supplierPricePerLiter;
  const editProfit = vals?.marginUahPerLiter ?? fuel.marginUahPerLiter;
  const editPump =
    vals && vals.pumpPricePerLiter !== undefined
      ? vals.pumpPricePerLiter
      : (fuel.pumpPricePerLiter ?? null);
  const computedFinal = computeFinal(editCost, editProfit, editPump);
  const shownFinal = isEditing ? computedFinal : fuel.finalPricePerLiter;
  const shownCost = isEditing ? editCost : fuel.supplierPricePerLiter;
  // A null cost is "we do not know what we paid", not "we paid nothing",
  // so it is NOT a below-cost price - and the row shows the column beside it.
  const belowCost =
    shownCost !== null &&
    shownCost > 0 &&
    shownFinal !== null &&
    shownFinal < shownCost;

  return (
    <tr className="border-t border-border hover:bg-muted/20 transition-colors">
      <td className="p-3">
        {isEditing ? (
          <Input
            value={vals?.name ?? ""}
            onChange={(e) =>
              setEditValues((prev) => ({
                ...prev,
                [fuel.id]: {
                  ...prev[fuel.id],
                  name: e.target.value,
                },
              }))
            }
            className="w-32 h-8 text-xs"
          />
        ) : (
          <div className="flex flex-col gap-0.5">
            <span className="font-medium">{fuel.name}</span>
            {/* The code is what the catalogue import matches on. A fuel
                without one is invisible to the next sync tick, which is
                the usual reason a grade gets typed in twice. */}
            <span
              className="text-[10px] text-muted-foreground tabular-nums"
              title={t("fuel.codeHint")}
            >
              {fuel.brandFuelCode ? fuel.brandFuelCode : t("fuel.codeNone")}
            </span>
          </div>
        )}
        {isEditing && (
          <Input
            value={vals?.brandFuelCode ?? ""}
            placeholder={t("fuel.codePlaceholder")}
            title={t("fuel.codeHint")}
            onChange={(e) =>
              setEditValues((prev) => ({
                ...prev,
                [fuel.id]: {
                  ...prev[fuel.id],
                  brandFuelCode: e.target.value,
                },
              }))
            }
            className="w-32 h-7 text-[11px] mt-1"
          />
        )}
      </td>
      <td className="p-3">
        {/* Cost is derived (weighted average of batch costs) since pricing slice 2a, so
            it is read-only here — a manual edit would be silently reverted on the next
            batch reprice. Operators set it on the Imports tab → batch costs. */}
        {isEditing ? (
          <div className="flex flex-col items-end gap-0.5">
            <span
              className="block text-right tabular-nums text-muted-foreground"
              title={t("price.supplierDerivedHint")}
            >
              {fuel.supplierPricePerLiter !== null
                ? fuel.supplierPricePerLiter.toFixed(2)
                : "—"}
            </span>
            <button
              type="button"
              onClick={onNavigateToImports}
              disabled={!onNavigateToImports}
              title={t("price.supplierDerivedHint")}
              className="text-[10px] text-muted-foreground underline decoration-dotted hover:text-foreground transition-colors disabled:no-underline disabled:cursor-default"
            >
              {t("price.supplierDerived")}
            </button>
          </div>
        ) : (
          <span className="block text-right tabular-nums">
            {fuel.supplierPricePerLiter !== null
              ? fuel.supplierPricePerLiter.toFixed(2)
              : "—"}
          </span>
        )}
      </td>
      <td className="p-3">
        {isEditing ? (
          <DecimalSettingInput
            type="number"
            inputMode="decimal"
            step="0.01"
            value={vals?.marginUahPerLiter ?? 0}
            onCommit={(value) =>
              setEditValues((prev) => ({
                ...prev,
                [fuel.id]: {
                  ...prev[fuel.id],
                  marginUahPerLiter: value ?? 0,
                },
              }))
            }
            className="w-24 h-8 text-right text-xs"
            aria-label={`${t("price.margin")} — ${fuel.name}`}
          />
        ) : (
          <span className="block text-right tabular-nums text-primary">
            {fuel.marginUahPerLiter !== null
              ? fuel.marginUahPerLiter.toFixed(2)
              : "—"}
          </span>
        )}
      </td>
      <td className="p-3">
        {isEditing ? (
          <DecimalSettingInput
            type="number"
            inputMode="decimal"
            step="0.01"
            min={0}
            placeholder="—"
            value={vals?.pumpPricePerLiter ?? null}
            emptyValue={null}
            onCommit={(value) =>
              setEditValues((prev) => ({
                ...prev,
                [fuel.id]: {
                  ...prev[fuel.id],
                  pumpPricePerLiter: value,
                },
              }))
            }
            className="w-24 h-8 text-right text-xs"
            aria-label={`${t("price.pump")} — ${fuel.name}`}
          />
        ) : (
          <span className="block text-right tabular-nums text-muted-foreground">
            {fuel.pumpPricePerLiter != null
              ? fuel.pumpPricePerLiter.toFixed(2)
              : "—"}
          </span>
        )}
      </td>
      <td className="p-3">
        <div
          className={`flex items-center justify-end gap-1 ${isEditing ? "px-2 py-1.5 bg-primary/5 rounded" : ""}`}
        >
          {belowCost && (
            <span
              className="inline-flex items-center gap-0.5 text-[10px] font-semibold text-destructive uppercase tracking-wide"
              title={t("price.belowCostHint")}
            >
              <AlertTriangle className="w-3 h-3" /> {t("price.belowCost")}
            </span>
          )}
          {fuel.priceSpread && (
            <span
              className="inline-flex items-center gap-0.5 text-[10px] font-semibold text-amber-600 dark:text-amber-500 uppercase tracking-wide"
              title={t("price.spreadHint")}
            >
              <AlertTriangle className="w-3 h-3" /> {t("price.spread")}
            </span>
          )}
          <span
            className={`text-right font-bold tabular-nums ${isEditing ? "text-primary text-sm" : belowCost ? "text-destructive" : ""}`}
          >
            {shownFinal.toFixed(2)}
          </span>
        </div>
        {/* The packages of this fuel disagree. Saving from this row rewrites every
                    one of them to what is shown here, so the operator is told what they are about to
                    flatten - and what the disagreement was - before they do it, not after. */}
        {fuel.priceSpread && !isEditing && (
          <div className="mt-0.5 text-right text-[10px] text-muted-foreground tabular-nums">
            {t(
              "price.spreadCost",
              fuel.priceSpread.minSupplierPricePerLiter.toFixed(2),
              fuel.priceSpread.maxSupplierPricePerLiter.toFixed(2),
            )}
          </div>
        )}
        {/* Always offered, not only once the price is already below cost. Arming it in
                    advance is the point: clearing near-expiry stock below cost is a legitimate
                    decision, and an operator who hits the renewal guard has to be able to find this
                    switch without first mispricing live fuel to reveal it.
                    `fuel.allowBelowCost` is the fallback for "untouched yet" — otherwise a fuel that
                    IS opted in renders as unchecked the moment the editor opens. */}
        {isEditing && (
          <label
            className={`mt-1.5 flex items-center justify-end gap-1.5 text-[10px] font-medium cursor-pointer ${belowCost || resolveAllowBelowCost(vals?.allowBelowCost, fuel.allowBelowCost) ? "text-destructive" : "text-muted-foreground"}`}
            title={t("price.allowBelowCostHint")}
          >
            <input
              type="checkbox"
              checked={resolveAllowBelowCost(
                vals?.allowBelowCost,
                fuel.allowBelowCost,
              )}
              onChange={(e) =>
                setEditValues((prev) => ({
                  ...prev,
                  [fuel.id]: {
                    ...prev[fuel.id],
                    allowBelowCost: e.target.checked,
                  },
                }))
              }
              className="h-3 w-3 accent-destructive"
            />
            {t("price.allowBelowCost")}
          </label>
        )}

        {/* The rehearsal flag decides whether these packages are sellable,
                                    and until now the panel had no control for it at all: a fuel
                                    added here landed with the API's default (not a rehearsal),
                                    and an edit rewrote the flag on every package of the fuel from
                                    whatever the client happened to send. Shown always, not only
                                    while editing - a fuel that is silently flagged is exactly
                                    what an operator needs to be able to see. */}
        <label
          className={`mt-1.5 flex items-center justify-end gap-1.5 text-[10px] font-medium cursor-pointer ${isQaFuel(vals?.isTestData, fuel.isTestData) ? "text-primary" : "text-muted-foreground"}`}
          title={t("price.isTestDataHint")}
        >
          <input
            type="checkbox"
            checked={isQaFuel(vals?.isTestData, fuel.isTestData)}
            onChange={(e) =>
              setEditValues((prev) => ({
                ...prev,
                [fuel.id]: {
                  ...prev[fuel.id],
                  isTestData: e.target.checked,
                },
              }))
            }
            className="h-3 w-3 accent-primary"
          />
          {isQaFuel(vals?.isTestData, fuel.isTestData)
            ? t("price.isTestData")
            : t("price.live")}
        </label>
      </td>
      <td className="p-3 text-center">
        <span className="text-xs text-muted-foreground">
          {fuel.packageLiters.join(", ")} L
        </span>
      </td>
      <td className="p-3">
        <div className="flex justify-center gap-1">
          {isEditing ? (
            <>
              <Button
                size="sm"
                onClick={() => saveFuel(fuel)}
                disabled={saving}
                className="h-8"
              >
                {saving ? (
                  <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                ) : (
                  <Save className="w-3.5 h-3.5 mr-1" />
                )}
                {t("common.save")}
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setEditingFuel(null)}
                disabled={saving}
                className="h-8"
              >
                <X className="w-3.5 h-3.5" />
              </Button>
            </>
          ) : confirmDeleteFuel === fuel.id ? (
            <div className="flex items-center gap-1 animate-in fade-in slide-in-from-right-2">
              <Button
                size="sm"
                variant="destructive"
                disabled={deleting}
                className="h-8"
                onClick={() => {
                  deleteFuelMutation.mutate(fuel.id);
                  setConfirmDeleteFuel(null);
                }}
              >
                {deleting ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                ) : (
                  <Trash2 className="w-3.5 h-3.5 mr-1" />
                )}
                {t("brands.deleteConfirm", fuel.name)}
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setConfirmDeleteFuel(null)}
                disabled={deleting}
                className="h-8"
              >
                <X className="w-3.5 h-3.5" />
              </Button>
            </div>
          ) : (
            <>
              {/* The price history is a whole panel, not a field of this row, so it
                                          opens underneath rather than in a dialog: an operator comparing a
                                          price against the row that shows it today needs both on screen. */}
              <Button
                variant="ghost"
                size="sm"
                onClick={() =>
                  setPriceHistoryFor((current) =>
                    current === fuel.id ? null : fuel.id,
                  )
                }
                title={t("priceHistory.openHint")}
                className="h-8"
              >
                <History className="w-3.5 h-3.5" />
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => startEditFuel(fuel)}
                className="text-info hover:brightness-125 h-8"
              >
                <Edit2 className="w-3.5 h-3.5" />
              </Button>
              <Button
                variant="ghost"
                size="sm"
                disabled={deleting}
                onClick={() => setConfirmDeleteFuel(fuel.id)}
                className="text-destructive hover:text-destructive h-8"
              >
                {deleting ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                ) : (
                  <Trash2 className="w-3.5 h-3.5" />
                )}
              </Button>
            </>
          )}
        </div>
      </td>
    </tr>
  );
}
