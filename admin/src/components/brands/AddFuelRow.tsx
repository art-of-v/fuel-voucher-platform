import { Plus, X, Loader2, AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useI18n } from "@/lib/i18n";
import type { BrandsController } from "./useBrandsData";
import type { BrandDto } from "./types";

interface AddFuelRowProps {
  brand: BrandDto;
  controller: BrandsController;
}

export function AddFuelRow({ brand, controller }: AddFuelRowProps) {
  const { t } = useI18n();
  const {
    newFuelName,
    setNewFuelName,
    newFuelSupplierPrice,
    setNewFuelSupplierPrice,
    newFuelMargin,
    setNewFuelMargin,
    newFuelPump,
    setNewFuelPump,
    newFuelAllowBelowCost,
    setNewFuelAllowBelowCost,
    newFuelBrandFuelCode,
    setNewFuelBrandFuelCode,
    newFuelIsTestData,
    setNewFuelIsTestData,
    newFuelFinalPrice,
    newFuelBelowCost,
    addFuelMutation,
    setAddingFuel,
    handleAddFuel,
  } = controller;

  return (
    <tr className="border-t border-border bg-muted/30">
      <td colSpan={8} className="p-3">
        <div className="flex items-end gap-3 flex-wrap">
          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("table.name")}
            </label>
            <Input
              placeholder="A-95"
              value={newFuelName}
              onChange={(e) => setNewFuelName(e.target.value)}
              className="h-8 w-36"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("fuel.code")}
            </label>
            <Input
              placeholder="A-95"
              value={newFuelBrandFuelCode}
              title={t("fuel.codeHint")}
              onChange={(e) => setNewFuelBrandFuelCode(e.target.value)}
              className="h-8 w-24"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("price.supplier")}, {t("price.unit")}
            </label>
            <Input
              type="number"
              step="0.01"
              placeholder="50.00"
              value={newFuelSupplierPrice}
              onChange={(e) => setNewFuelSupplierPrice(e.target.value)}
              className="h-8 w-28 text-right"
            />
            {/* A brand-new fuel has no batch yet, so this seed is the only cost source
                until the first batch cost is recorded, after which it is recomputed. */}
            <span className="text-[10px] text-muted-foreground leading-tight w-28">
              {t("price.supplierSeedHint")}
            </span>
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("price.margin")}, {t("price.unit")}
            </label>
            <Input
              type="number"
              step="0.01"
              placeholder="2.00"
              value={newFuelMargin}
              onChange={(e) => setNewFuelMargin(e.target.value)}
              className="h-8 w-28 text-right"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label
              className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider"
              title={t("price.pumpHint")}
            >
              {t("price.pump")}, {t("price.unit")}
            </label>
            <Input
              type="number"
              step="0.01"
              min={0}
              placeholder="—"
              value={newFuelPump}
              onChange={(e) => setNewFuelPump(e.target.value)}
              className="h-8 w-28 text-right"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("price.final")}, {t("price.unit")}
            </label>
            <div
              className={`h-8 flex items-center justify-end gap-1 text-right font-bold tabular-nums text-sm rounded px-3 ${newFuelBelowCost ? "text-destructive bg-destructive/5" : "text-primary bg-primary/5"}`}
            >
              {newFuelBelowCost && (
                <span
                  className="inline-flex items-center gap-0.5 text-[10px] font-semibold uppercase tracking-wide"
                  title={t("price.belowCostHint")}
                >
                  <AlertTriangle className="w-3 h-3" /> {t("price.belowCost")}
                </span>
              )}
              {newFuelFinalPrice.toFixed(2)}
            </div>
          </div>
          {/* Same as the edit form: the opt-in is a standing permission, so it is
              offered whether or not the price currently sits below cost.
              Waiting for belowCost to reveal it meant a new fuel could never
              be pre-authorised at all. */}
          <div className="flex flex-col gap-1">
            <label
              className={`text-[11px] font-medium uppercase tracking-wider ${newFuelBelowCost ? "text-destructive" : "text-muted-foreground"}`}
            >
              {t("price.belowCost")}
            </label>
            <label
              className={`h-8 flex items-center gap-1.5 text-xs font-medium cursor-pointer ${newFuelBelowCost || newFuelAllowBelowCost ? "text-destructive" : "text-muted-foreground"}`}
              title={t("price.allowBelowCostHint")}
            >
              <input
                type="checkbox"
                checked={newFuelAllowBelowCost}
                onChange={(e) => setNewFuelAllowBelowCost(e.target.checked)}
                className="h-3.5 w-3.5 accent-destructive"
              />
              {t("price.allowBelowCost")}
            </label>
            {/* Same control as on the edit row, and present for the same
                reason: a fuel added from here used to land with the API's
                default, which is "not a rehearsal" - i.e. on sale. */}
            <label
              className="flex items-center gap-1.5 text-[10px] font-medium cursor-pointer text-muted-foreground"
              title={t("price.isTestDataHint")}
            >
              <input
                type="checkbox"
                checked={newFuelIsTestData}
                onChange={(e) => setNewFuelIsTestData(e.target.checked)}
                className="h-3.5 w-3.5 accent-primary"
              />
              {t("price.isTestData")}
            </label>
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("table.nominals")}
            </label>
            <div className="h-8 flex items-center text-xs text-muted-foreground">
              {t("price.nominalsAuto")}
            </div>
          </div>
          <div className="flex items-end gap-1 pb-0.5">
            <Button
              size="sm"
              onClick={() => handleAddFuel(brand.id)}
              disabled={
                !newFuelName || !newFuelFinalPrice || addFuelMutation.isPending
              }
              className="h-8"
            >
              {addFuelMutation.isPending ? (
                <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
              ) : (
                <Plus className="w-3.5 h-3.5 mr-1" />
              )}
              {t("common.add")}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setAddingFuel(null)}
              disabled={addFuelMutation.isPending}
              className="h-8"
            >
              <X className="w-3.5 h-3.5" />
            </Button>
          </div>
        </div>
      </td>
    </tr>
  );
}
