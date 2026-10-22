import type { Dispatch, SetStateAction } from "react";
import { Save, X, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { DecimalSettingInput } from "@/components/DecimalSettingInput";
import { useI18n } from "@/lib/i18n";
import type { BrandFormValues } from "./types";

interface BrandFormPanelProps {
  mode: "create" | "edit";
  values: BrandFormValues;
  onChange: Dispatch<SetStateAction<BrandFormValues>>;
  onSubmit: () => void;
  onCancel: () => void;
  isPending: boolean;
}

export function BrandFormPanel({
  mode,
  values,
  onChange,
  onSubmit,
  onCancel,
  isPending,
}: BrandFormPanelProps) {
  const { t } = useI18n();

  return (
    <div className="bg-card border border-border rounded-xl p-4 animate-in fade-in slide-in-from-top-2 duration-200">
      {mode === "edit" && (
        <div className="flex items-center justify-between mb-3">
          <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">
            {t("brands.editBrand")}
          </h4>
        </div>
      )}
      <div className="flex items-end gap-3 flex-wrap">
        <div className="flex flex-col gap-1">
          <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
            {t("table.name")}
          </label>
          <Input
            autoFocus
            placeholder={t("brands.namePlaceholder")}
            value={values.name}
            onChange={(e) =>
              onChange((prev) => ({ ...prev, name: e.target.value }))
            }
            className="h-8 w-64"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
            {t("brands.logoText")}
          </label>
          <Input
            maxLength={6}
            placeholder={t("brands.logoTextPlaceholder")}
            value={values.logoText}
            onChange={(e) =>
              onChange((prev) => ({ ...prev, logoText: e.target.value }))
            }
            className="h-8 w-28"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
            {t("brands.color")}
          </label>
          <div className="flex items-center gap-2">
            <input
              type="color"
              value={values.color}
              onChange={(e) =>
                onChange((prev) => ({ ...prev, color: e.target.value }))
              }
              className="h-8 w-12 cursor-pointer rounded bg-transparent border border-border"
            />
            <span className="text-xs font-mono text-muted-foreground">
              {values.color}
            </span>
          </div>
        </div>
        <div className="flex flex-col gap-1">
          <label
            className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider"
            title={t("brands.priorityHint")}
          >
            {t("brands.priority")}
          </label>
          <DecimalSettingInput
            type="number"
            inputMode="numeric"
            min={1}
            max={999}
            placeholder={mode === "create" ? "999" : undefined}
            value={values.sortOrder}
            onCommit={(value) =>
              onChange((prev) => ({
                ...prev,
                sortOrder: Math.max(1, Math.round(value ?? 999)),
              }))
            }
            className="h-8 w-20 text-right"
            aria-label={t("brands.priority")}
          />
        </div>
        <div className="flex items-end gap-1 pb-0.5 ml-auto">
          <Button
            size="sm"
            onClick={onSubmit}
            disabled={!values.name.trim() || isPending}
            className="h-8"
          >
            {isPending ? (
              <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
            ) : (
              <Save className="w-3.5 h-3.5 mr-1" />
            )}
            {mode === "create" ? t("common.create") : t("common.save")}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={onCancel}
            disabled={isPending}
            className="h-8"
          >
            <X className="w-3.5 h-3.5" />
          </Button>
        </div>
      </div>
    </div>
  );
}
