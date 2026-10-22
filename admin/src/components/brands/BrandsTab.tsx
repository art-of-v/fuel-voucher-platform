import { Plus, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import CatalogueSyncButton from "@/components/CatalogueSyncButton";
import { useI18n } from "@/lib/i18n";
import { BrandCard } from "./BrandCard";
import { BrandFormPanel } from "./BrandFormPanel";
import { emptyBrandForm } from "./types";
import { useBrandsData } from "./useBrandsData";

interface BrandsTabProps {
  // Jumps to the Imports tab, where batch costs — the source of the derived supplier cost — are entered.
  onNavigateToImports?: () => void;
}

export default function BrandsTab({ onNavigateToImports }: BrandsTabProps) {
  const { t } = useI18n();
  const controller = useBrandsData();

  if (controller.isLoading) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-8">
        <Loader2 className="w-5 h-5 animate-spin" />
        {t("common.loading")}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-end gap-2">
        <CatalogueSyncButton />

        <Button
          variant="outline"
          size="sm"
          onClick={() => controller.setIsAddingBrand(true)}
        >
          <Plus className="w-3.5 h-3.5 mr-1" />
          {t("brands.addBrand")}
        </Button>
      </div>

      {controller.isAddingBrand && (
        <BrandFormPanel
          mode="create"
          values={controller.newBrand}
          onChange={controller.setNewBrand}
          onSubmit={controller.handleCreateBrand}
          onCancel={() => {
            controller.setIsAddingBrand(false);
            controller.setNewBrand(emptyBrandForm());
          }}
          isPending={controller.createBrandMutation.isPending}
        />
      )}

      {controller.editingBrandId &&
        (() => {
          const brand = controller.brands.find(
            (p) => p.id === controller.editingBrandId,
          );
          if (!brand) return null;
          return (
            <BrandFormPanel
              mode="edit"
              values={controller.editBrand}
              onChange={controller.setEditBrand}
              onSubmit={controller.handleSaveBrand}
              onCancel={() => controller.setEditingBrandId(null)}
              isPending={controller.updateBrandMutation.isPending}
            />
          );
        })()}

      {controller.brands.map((brand) => (
        <BrandCard
          key={brand.id}
          brand={brand}
          controller={controller}
          onNavigateToImports={onNavigateToImports}
        />
      ))}
      {controller.brands.length === 0 && !controller.isLoading && (
        <div className="text-center py-16 text-muted-foreground">
          <h3 className="text-lg font-medium mb-1">{t("common.noData")}</h3>
          <p className="text-sm mb-4">{t("brands.noBrands")}</p>
          <Button
            variant="outline"
            size="sm"
            onClick={() => controller.setIsAddingBrand(true)}
          >
            <Plus className="w-3.5 h-3.5 mr-1" />
            {t("brands.addBrand")}
          </Button>
        </div>
      )}
    </div>
  );
}
