import {
  Edit2,
  Trash2,
  X,
  Loader2,
  ChevronDown,
  ChevronRight,
  AlertTriangle,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { useI18n } from "@/lib/i18n";
import { BrandFuelsTable } from "./BrandFuelsTable";
import { BrandHistory } from "./BrandHistory";
import { BrandNominals } from "./BrandNominals";
import type { BrandsController } from "./useBrandsData";
import type { BrandDto } from "./types";

interface BrandCardProps {
  brand: BrandDto;
  controller: BrandsController;
  onNavigateToImports?: () => void;
}

export function BrandCard({
  brand,
  controller,
  onNavigateToImports,
}: BrandCardProps) {
  const { t } = useI18n();
  const {
    expandedBrand,
    setExpandedBrand,
    confirmDelete,
    setConfirmDelete,
    deleteBrandMutation,
    updateFuelMutation,
    deleteFuelMutation,
    startEditBrand,
  } = controller;

  const isExpanded = expandedBrand === brand.id;
  const busy =
    updateFuelMutation.isPending ||
    deleteFuelMutation.isPending ||
    deleteBrandMutation.isPending;

  return (
    <div className="bg-card border border-border rounded-xl overflow-hidden transition-all duration-200 hover:border-primary/20">
      {/* Brand Header */}
      <div
        className="flex items-center justify-between p-4 cursor-pointer hover:bg-muted/50 transition-colors"
        onClick={() => setExpandedBrand(isExpanded ? null : brand.id)}
      >
        <div className="flex items-center gap-3">
          <div
            className="min-w-10 h-10 px-2.5 rounded-lg flex items-center justify-center text-lg font-bold text-black"
            style={{ backgroundColor: brand.color }}
          >
            {brand.logoText}
          </div>
          <div>
            <h3 className="text-lg font-bold">{brand.name}</h3>
            <p className="text-xs text-muted-foreground">
              {brand.fuels.length} {t("nav.fueltypes")} ·{" "}
              {t("brands.nominalsCount", brand.nominals.length.toString())}
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="ghost"
            size="sm"
            onClick={(e) => {
              e.stopPropagation();
              startEditBrand(brand);
            }}
            className="text-info hover:brightness-125"
          >
            <Edit2 className="w-4 h-4" />
          </Button>
          {confirmDelete === brand.id ? (
            <div className="flex items-center gap-1 animate-in fade-in slide-in-from-right-2">
              <span className="text-xs text-destructive font-medium flex items-center gap-1 mr-1">
                <AlertTriangle className="w-3 h-3" />{" "}
                {t("brands.deleteConfirm", brand.name)}
              </span>
              <Button
                variant="destructive"
                size="sm"
                className="h-7 text-xs"
                disabled={deleteBrandMutation.isPending}
                onClick={(e) => {
                  e.stopPropagation();
                  deleteBrandMutation.mutate(brand.id);
                  setConfirmDelete(null);
                }}
              >
                {deleteBrandMutation.isPending ? (
                  <Loader2 className="w-3 h-3 animate-spin" />
                ) : (
                  <Trash2 className="w-3 h-3" />
                )}
              </Button>
              <Button
                variant="ghost"
                size="sm"
                className="h-7 text-xs"
                onClick={(e) => {
                  e.stopPropagation();
                  setConfirmDelete(null);
                }}
              >
                <X className="w-3 h-3" />
              </Button>
            </div>
          ) : (
            <Button
              variant="ghost"
              size="sm"
              onClick={(e) => {
                e.stopPropagation();
                setConfirmDelete(brand.id);
              }}
              className="text-destructive hover:text-destructive"
            >
              {busy ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <Trash2 className="w-4 h-4" />
              )}
            </Button>
          )}
          {isExpanded ? (
            <ChevronDown className="w-5 h-5 text-muted-foreground" />
          ) : (
            <ChevronRight className="w-5 h-5 text-muted-foreground" />
          )}
        </div>
      </div>

      {/* Expanded Content */}
      {isExpanded && (
        <div className="border-t border-border p-4 md:p-6 space-y-6 animate-in fade-in slide-in-from-top-2 duration-200">
          <BrandFuelsTable
            brand={brand}
            controller={controller}
            onNavigateToImports={onNavigateToImports}
          />
          <BrandNominals brand={brand} controller={controller} />
          <BrandHistory brand={brand} controller={controller} />
        </div>
      )}
    </div>
  );
}
