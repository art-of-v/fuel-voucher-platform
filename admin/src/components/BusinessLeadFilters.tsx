import { Search, X } from "lucide-react";
import DateInput from "@/components/DateInput";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useI18n } from "@/lib/i18n";
import {
  BUSINESS_LEAD_STATUSES,
  type BusinessLeadStatusFilter,
} from "./business-leads";

interface BusinessLeadFiltersProps {
  search: string;
  status: BusinessLeadStatusFilter;
  from: string;
  to: string;
  onSearchChange: (value: string) => void;
  onStatusChange: (value: BusinessLeadStatusFilter) => void;
  onFromChange: (value: string) => void;
  onToChange: (value: string) => void;
  onReset: () => void;
}

export default function BusinessLeadFilters({
  search,
  status,
  from,
  to,
  onSearchChange,
  onStatusChange,
  onFromChange,
  onToChange,
  onReset,
}: BusinessLeadFiltersProps) {
  const { t } = useI18n();
  const hasFilters =
    search.trim() !== "" || status !== "active" || from !== "" || to !== "";

  return (
    <div className="flex flex-wrap items-center gap-2">
      <div className="relative flex-1 min-w-56">
        <Search className="absolute left-3 top-1/2 w-4 h-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={search}
          onChange={(event) => onSearchChange(event.target.value)}
          placeholder={t("businessLeads.searchPlaceholder")}
          aria-label={t("businessLeads.searchPlaceholder")}
          className="h-9 pl-9"
        />
      </div>

      <Select
        value={status}
        onValueChange={(value) =>
          onStatusChange(value as BusinessLeadStatusFilter)
        }
      >
        <SelectTrigger
          className="h-9 w-48"
          aria-label={t("businessLeads.status")}
        >
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="active">
            {t("businessLeads.status.Active")}
          </SelectItem>
          <SelectItem value="all">{t("businessLeads.status.All")}</SelectItem>
          {BUSINESS_LEAD_STATUSES.map((item) => (
            <SelectItem key={item} value={item}>
              {t(`businessLeads.status.${item}`)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <DateInput
        value={from}
        onChange={onFromChange}
        className="h-9 w-36"
        title={t("businessLeads.from")}
      />
      <DateInput
        value={to}
        onChange={onToChange}
        className="h-9 w-36"
        title={t("businessLeads.to")}
      />

      {hasFilters && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onReset}
          className="h-9"
        >
          <X className="w-4 h-4" />
          {t("businessLeads.reset")}
        </Button>
      )}
    </div>
  );
}
