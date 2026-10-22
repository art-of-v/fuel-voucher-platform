import { Edit2, Save, X, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useI18n } from "@/lib/i18n";
import type { BrandsController } from "./useBrandsData";
import type { BrandDto } from "./types";

interface BrandNominalsProps {
  brand: BrandDto;
  controller: BrandsController;
}

export function BrandNominals({ brand, controller }: BrandNominalsProps) {
  const { t } = useI18n();
  const {
    editingNominals,
    setEditingNominals,
    nominalInput,
    setNominalInput,
    updateNominalsMutation,
    handleSaveNominals,
  } = controller;

  return (
    <div>
      <div className="flex items-center justify-between mb-3">
        <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">
          {t("table.nominals")}
        </h4>
        <Button
          variant="outline"
          size="sm"
          disabled={updateNominalsMutation.isPending}
          onClick={() => {
            setEditingNominals(brand.id);
            setNominalInput(brand.nominals.join(", "));
          }}
        >
          {updateNominalsMutation.isPending ? (
            <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
          ) : (
            <Edit2 className="w-3.5 h-3.5 mr-1" />
          )}
          {t("common.edit")}
        </Button>
      </div>
      {editingNominals === brand.id ? (
        <div className="flex gap-2 items-center">
          <Input
            value={nominalInput}
            onChange={(e) => setNominalInput(e.target.value)}
            placeholder="2, 3, 5, 10, 20, 50, 100"
            className="flex-1"
          />
          <Button
            size="sm"
            disabled={updateNominalsMutation.isPending}
            onClick={() => handleSaveNominals(brand)}
          >
            {updateNominalsMutation.isPending ? (
              <Loader2 className="w-4 h-4 mr-1 animate-spin" />
            ) : (
              <Save className="w-4 h-4 mr-1" />
            )}
            {t("common.save")}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setEditingNominals(null)}
            disabled={updateNominalsMutation.isPending}
          >
            <X className="w-4 h-4" />
          </Button>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          {brand.nominals.map((n) => (
            <span
              key={n}
              className="px-3 py-1 bg-muted rounded-full text-sm font-mono border border-border"
            >
              {n} L
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
