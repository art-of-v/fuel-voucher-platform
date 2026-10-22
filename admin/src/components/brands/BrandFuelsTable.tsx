import { Plus, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { FuelPriceHistory } from "@/components/FuelPriceHistory";
import { useI18n } from "@/lib/i18n";
import { AddFuelRow } from "./AddFuelRow";
import { BrandFuelRow } from "./BrandFuelRow";
import type { BrandsController } from "./useBrandsData";
import type { BrandDto } from "./types";

interface BrandFuelsTableProps {
  brand: BrandDto;
  controller: BrandsController;
  onNavigateToImports?: () => void;
}

export function BrandFuelsTable({
  brand,
  controller,
  onNavigateToImports,
}: BrandFuelsTableProps) {
  const { t } = useI18n();
  const { addingFuel, setAddingFuel, addFuelMutation, priceHistoryFor } =
    controller;

  return (
    <div>
      <div className="flex items-center justify-between mb-3">
        <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">
          {t("price.title")}
        </h4>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setAddingFuel(brand.id)}
          disabled={addFuelMutation.isPending}
        >
          {addFuelMutation.isPending ? (
            <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
          ) : (
            <Plus className="w-3.5 h-3.5 mr-1" />
          )}
          {t("common.add")}
        </Button>
      </div>

      <div className="overflow-x-auto rounded-lg border border-border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="text-left p-3 whitespace-nowrap">
                {t("table.name")}
              </th>
              <th className="text-right p-3 whitespace-nowrap">
                <div>{t("price.supplier")}</div>
                <div className="text-[10px] text-muted-foreground font-normal">
                  {t("price.unit")}
                </div>
              </th>
              <th className="text-right p-3 whitespace-nowrap">
                <div>{t("price.margin")}</div>
                <div className="text-[10px] text-muted-foreground font-normal">
                  {t("price.unit")}
                </div>
              </th>
              <th className="text-right p-3 whitespace-nowrap">
                <div title={t("price.pumpHint")}>{t("price.pump")}</div>
                <div className="text-[10px] text-muted-foreground font-normal">
                  {t("price.unit")}
                </div>
              </th>
              <th className="text-right p-3 whitespace-nowrap">
                <div>{t("price.final")}</div>
                <div className="text-[10px] text-muted-foreground font-normal">
                  {t("price.unit")}
                </div>
              </th>
              <th className="text-center p-3 whitespace-nowrap">
                {t("table.nominals")}
              </th>
              <th className="text-center p-3 whitespace-nowrap">
                {t("common.actions")}
              </th>
            </tr>
          </thead>
          <tbody>
            {brand.fuels.map((fuel) => (
              <BrandFuelRow
                key={fuel.id}
                fuel={fuel}
                controller={controller}
                onNavigateToImports={onNavigateToImports}
              />
            ))}
            {/* The open price history, as a full-width row under the fuel it belongs to.
                One fuel at a time: two open panels side by side would answer neither. */}
            {priceHistoryFor &&
              brand.fuels.some((fuel) => fuel.id === priceHistoryFor) && (
                <tr className="border-t border-border bg-muted/20">
                  <td colSpan={8} className="p-3">
                    <FuelPriceHistory fuelId={priceHistoryFor} />
                  </td>
                </tr>
              )}
            {addingFuel === brand.id && (
              <AddFuelRow brand={brand} controller={controller} />
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
