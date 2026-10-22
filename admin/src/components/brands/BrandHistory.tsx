import { ChevronDown, ChevronRight, History } from "lucide-react";
import { useI18n } from "@/lib/i18n";
import { formatDateTime, localizeEventSummary } from "@/lib/utils";
import type { BrandsController } from "./useBrandsData";
import type { BrandDto } from "./types";

interface BrandHistoryProps {
  brand: BrandDto;
  controller: BrandsController;
}

export function BrandHistory({ brand, controller }: BrandHistoryProps) {
  const { t } = useI18n();
  const { expandedHistory, setExpandedHistory, history } = controller;
  const isHistoryExpanded = expandedHistory === brand.id;

  return (
    <div>
      <div
        className="flex items-center gap-2 cursor-pointer text-muted-foreground hover:text-foreground transition-colors p-2 -ml-2 rounded hover:bg-muted/30"
        onClick={() => setExpandedHistory(isHistoryExpanded ? null : brand.id)}
      >
        <History className="w-4 h-4" />
        <span className="text-sm font-medium">{t("common.history")}</span>
        {isHistoryExpanded ? (
          <ChevronDown className="w-4 h-4" />
        ) : (
          <ChevronRight className="w-4 h-4" />
        )}
      </div>
      {isHistoryExpanded && (
        <div className="mt-2 max-h-64 overflow-y-auto space-y-1 animate-in fade-in slide-in-from-top-1 duration-150">
          {history.length === 0 ? (
            <p className="text-sm text-muted-foreground p-2">
              {t("common.noData")}
            </p>
          ) : (
            history.map((evt) => (
              <div
                key={evt.id}
                className="flex items-start gap-2 p-2 rounded bg-muted/30 text-sm hover:bg-muted/50 transition-colors"
              >
                <div
                  className={`w-1.5 h-1.5 rounded-full mt-1.5 shrink-0 ${
                    evt.eventType === "BrandCreated"
                      ? "bg-success"
                      : evt.eventType === "BrandDeleted"
                        ? "bg-destructive"
                        : evt.eventType === "PriceChanged"
                          ? "bg-warning"
                          : evt.eventType === "FuelAdded"
                            ? "bg-info"
                            : evt.eventType === "FuelRemoved"
                              ? "bg-destructive"
                              : "bg-info"
                  }`}
                />
                <div className="flex-1 min-w-0">
                  <p className="text-xs font-medium">
                    {localizeEventSummary(evt.summary, t)}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {evt.changedByUserName ?? "System"} ·{" "}
                    {formatDateTime(evt.changedAtUtc)}
                  </p>
                </div>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
}
