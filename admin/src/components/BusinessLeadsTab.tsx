import { useMemo, useState } from "react";
import {
  BriefcaseBusiness,
  ChevronLeft,
  ChevronRight,
  Loader2,
  RefreshCw,
} from "lucide-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import BusinessLeadDrawer from "./BusinessLeadDrawer";
import BusinessLeadFilters from "./BusinessLeadFilters";
import {
  BUSINESS_LEAD_STATUSES,
  buildBusinessLeadQuery,
  businessLeadKeys,
  getBusinessLeadFacets,
  getBusinessLeads,
  updateBusinessLeadStatus,
  type BusinessLead,
  type BusinessLeadStatus,
  type BusinessLeadStatusFilter,
} from "./business-leads";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useI18n } from "@/lib/i18n";
import { cn, formatDateTime } from "@/lib/utils";

const PAGE_SIZE = 50;

export default function BusinessLeadsTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<BusinessLeadStatusFilter>("active");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkStatus, setBulkStatus] = useState<BusinessLeadStatus>("Contacted");
  const [leadId, setLeadId] = useState<string | null>(null);

  const query = useMemo(
    () =>
      buildBusinessLeadQuery({
        offset: page * PAGE_SIZE,
        limit: PAGE_SIZE,
        search,
        status,
        from,
        to,
      }),
    [page, search, status, from, to],
  );

  const leadsQuery = useQuery({
    queryKey: businessLeadKeys.list(query),
    queryFn: () => getBusinessLeads(query),
  });
  const facetsQuery = useQuery({
    queryKey: businessLeadKeys.facets,
    queryFn: getBusinessLeadFacets,
    staleTime: 60_000,
  });

  const statusMutation = useMutation({
    mutationFn: ({
      ids,
      status: nextStatus,
    }: {
      ids: string[];
      status: BusinessLeadStatus;
    }) => updateBusinessLeadStatus(ids, nextStatus),
    onSuccess: () => {
      setSelected(new Set());
      queryClient.invalidateQueries({ queryKey: businessLeadKeys.all });
      toast.success(t("businessLeads.stateChanged"));
    },
    onError: (error: unknown) =>
      toast.error(
        error instanceof Error
          ? error.message
          : t("businessLeads.updateFailed"),
      ),
  });

  const listed = leadsQuery.data?.items ?? [];
  const listedIds = listed.map((lead) => lead.id);
  const selectedListed = listedIds.filter((id) => selected.has(id));
  const everyListedSelected =
    listedIds.length > 0 && selectedListed.length === listedIds.length;
  const selectedLead = listed.find((lead) => lead.id === leadId) ?? null;
  const totalPages = leadsQuery.data
    ? Math.ceil(leadsQuery.data.total / PAGE_SIZE)
    : 0;

  const resetFilters = () => {
    setSearch("");
    setStatus("active");
    setFrom("");
    setTo("");
    setPage(0);
    setSelected(new Set());
  };

  const updateFilter = (change: () => void) => {
    change();
    setPage(0);
    setSelected(new Set());
  };

  return (
    <div className="space-y-4">
      <BusinessLeadFilters
        search={search}
        status={status}
        from={from}
        to={to}
        onSearchChange={(value) => updateFilter(() => setSearch(value))}
        onStatusChange={(value) => updateFilter(() => setStatus(value))}
        onFromChange={(value) => updateFilter(() => setFrom(value))}
        onToChange={(value) => updateFilter(() => setTo(value))}
        onReset={resetFilters}
      />

      <LeadSummary
        total={facetsQuery.data?.total ?? 0}
        active={facetsQuery.data?.active ?? 0}
        newCount={facetsQuery.data?.newCount ?? 0}
      />

      {selectedListed.length > 0 && (
        <div className="flex flex-wrap items-center gap-2 rounded-lg border border-primary/25 bg-primary/10 p-3">
          <span className="mr-auto text-sm font-medium">
            {t("businessLeads.selected", selectedListed.length)}
          </span>
          <Select
            value={bulkStatus}
            onValueChange={(value) =>
              setBulkStatus(value as BusinessLeadStatus)
            }
          >
            <SelectTrigger
              className="h-9 w-44"
              aria-label={t("businessLeads.moveTo")}
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {BUSINESS_LEAD_STATUSES.map((item) => (
                <SelectItem key={item} value={item}>
                  {t(`businessLeads.status.${item}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button
            type="button"
            size="sm"
            disabled={statusMutation.isPending}
            onClick={() =>
              statusMutation.mutate({
                ids: selectedListed,
                status: bulkStatus,
              })
            }
          >
            {t("businessLeads.apply")}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={() => setSelected(new Set())}
          >
            {t("common.cancel")}
          </Button>
        </div>
      )}

      {leadsQuery.isLoading ? (
        <div className="flex items-center justify-center py-20">
          <Loader2 className="h-8 w-8 animate-spin text-primary" />
        </div>
      ) : leadsQuery.isError ? (
        <div className="flex flex-col items-center gap-3 py-20 text-center text-muted-foreground">
          <BriefcaseBusiness className="h-12 w-12 opacity-40" />
          <p>{t("businessLeads.loadFailed")}</p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => leadsQuery.refetch()}
          >
            <RefreshCw className="h-4 w-4" />
            {t("businessLeads.retry")}
          </Button>
        </div>
      ) : listed.length === 0 ? (
        <div className="py-20 text-center text-muted-foreground">
          <BriefcaseBusiness className="mx-auto mb-4 h-12 w-12 opacity-40" />
          <p>{t("businessLeads.empty")}</p>
        </div>
      ) : (
        <>
          <div className="overflow-hidden rounded-lg border border-border bg-card">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border bg-muted/50">
                    <th className="w-10 p-3">
                      <input
                        type="checkbox"
                        checked={everyListedSelected}
                        aria-label={t("businessLeads.selectPage")}
                        onChange={(event) =>
                          setSelected(
                            event.target.checked
                              ? new Set(listedIds)
                              : new Set(),
                          )
                        }
                        className="h-4 w-4 accent-primary"
                      />
                    </th>
                    <Heading>{t("businessLeads.received")}</Heading>
                    <Heading>{t("businessLeads.company")}</Heading>
                    <Heading>{t("businessLeads.contact")}</Heading>
                    <Heading>{t("businessLeads.fleetSize")}</Heading>
                    <Heading>{t("businessLeads.message")}</Heading>
                    <Heading>{t("businessLeads.status")}</Heading>
                    <th className="w-24 p-3" />
                  </tr>
                </thead>
                <tbody>
                  {listed.map((lead) => (
                    <LeadRow
                      key={lead.id}
                      lead={lead}
                      selected={selected.has(lead.id)}
                      onSelectedChange={(checked) =>
                        setSelected((current) => {
                          const next = new Set(current);
                          if (checked) next.add(lead.id);
                          else next.delete(lead.id);
                          return next;
                        })
                      }
                      onOpen={() => setLeadId(lead.id)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div className="flex items-center justify-between gap-4">
            <p className="text-sm text-muted-foreground">
              {t(
                "businessLeads.showing",
                page * PAGE_SIZE + 1,
                Math.min((page + 1) * PAGE_SIZE, leadsQuery.data?.total ?? 0),
                leadsQuery.data?.total ?? 0,
              )}
            </p>
            <div className="flex items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                aria-label={t("businessLeads.previous")}
                disabled={page === 0}
                onClick={() => {
                  setPage((current) => current - 1);
                  setSelected(new Set());
                }}
              >
                <ChevronLeft className="h-4 w-4" />
              </Button>
              <span className="text-sm text-muted-foreground">
                {page + 1} / {Math.max(totalPages, 1)}
              </span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                aria-label={t("businessLeads.next")}
                disabled={page + 1 >= totalPages}
                onClick={() => {
                  setPage((current) => current + 1);
                  setSelected(new Set());
                }}
              >
                <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          </div>
        </>
      )}

      <BusinessLeadDrawer
        leadId={leadId}
        fallback={selectedLead}
        statusPending={statusMutation.isPending}
        onOpenChange={(open) => {
          if (!open) setLeadId(null);
        }}
        onStatusChange={(id, nextStatus) =>
          statusMutation.mutate({ ids: [id], status: nextStatus })
        }
      />
    </div>
  );
}

function LeadSummary({
  total,
  active,
  newCount,
}: {
  total: number;
  active: number;
  newCount: number;
}) {
  const { t } = useI18n();
  return (
    <div className="grid gap-px overflow-hidden rounded-lg border border-border bg-border sm:grid-cols-3">
      {[
        [t("businessLeads.summaryNew"), newCount],
        [t("businessLeads.summaryActive"), active],
        [t("businessLeads.summaryTotal"), total],
      ].map(([label, value]) => (
        <div key={String(label)} className="bg-card px-4 py-3">
          <p className="text-xs text-muted-foreground">{label}</p>
          <p className="mt-1 text-2xl font-semibold tabular-nums">{value}</p>
        </div>
      ))}
    </div>
  );
}

function Heading({ children }: { children: React.ReactNode }) {
  return (
    <th className="p-3 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground">
      {children}
    </th>
  );
}

function LeadRow({
  lead,
  selected,
  onSelectedChange,
  onOpen,
}: {
  lead: BusinessLead;
  selected: boolean;
  onSelectedChange: (checked: boolean) => void;
  onOpen: () => void;
}) {
  const { t } = useI18n();
  return (
    <tr className="border-b border-border/60 hover:bg-muted/30">
      <td className="p-3 align-top">
        <input
          type="checkbox"
          checked={selected}
          aria-label={t("businessLeads.selectLead", lead.companyName)}
          onChange={(event) => onSelectedChange(event.target.checked)}
          className="h-4 w-4 accent-primary"
        />
      </td>
      <td className="whitespace-nowrap p-3 align-top text-xs text-muted-foreground tabular-nums">
        {formatDateTime(lead.createdAtUtc)}
      </td>
      <td className="p-3 align-top font-medium">{lead.companyName}</td>
      <td className="p-3 align-top">
        <p>{lead.contactName}</p>
        <p className="text-xs text-muted-foreground">{lead.phone}</p>
        {lead.email && (
          <p className="max-w-56 truncate text-xs text-muted-foreground">
            {lead.email}
          </p>
        )}
      </td>
      <td className="p-3 align-top tabular-nums">
        {lead.fleetSize?.toLocaleString() ?? "—"}
      </td>
      <td className="max-w-sm p-3 align-top text-muted-foreground">
        <p className="line-clamp-2 break-words">
          {lead.message || t("businessLeads.noMessage")}
        </p>
      </td>
      <td className="p-3 align-top">
        <span
          className={cn(
            "inline-flex rounded-md px-2 py-0.5 text-xs font-medium",
            statusTone(lead.status),
          )}
        >
          {t(`businessLeads.status.${lead.status}`)}
        </span>
      </td>
      <td className="p-3 text-right align-top">
        <Button type="button" variant="ghost" size="sm" onClick={onOpen}>
          {t("businessLeads.detail")}
        </Button>
      </td>
    </tr>
  );
}

function statusTone(status: BusinessLeadStatus): string {
  if (status === "New") return "bg-warning/15 text-warning";
  if (status === "Contacted") return "bg-primary/15 text-primary";
  if (status === "Qualified") return "bg-success/15 text-success";
  if (status === "Converted") return "bg-success/25 text-success";
  return "bg-muted text-muted-foreground";
}
