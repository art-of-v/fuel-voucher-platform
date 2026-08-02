import { useState, useMemo } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery } from "@tanstack/react-query";
import { ScrollText, Loader2, ChevronLeft, ChevronRight, Search, Download, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { apiRequest } from "@/lib/api-client";
import { formatDateTime, localizeEventSummary } from "@/lib/utils";

interface AuditEvent {
  id: string;
  aggregateType: string;
  aggregateId: string;
  eventType: string;
  changedByUserName: string | null;
  summary: string;
  changedAtUtc: string;
}

interface AuditResponse {
  total: number;
  events: AuditEvent[];
}

interface AuditFacets {
  eventTypes: string[];
  aggregateTypes: string[];
  users: string[];
}

function formatEventType(type: string) {
  return type.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function localizeEventType(type: string, t: (k: string, ...p: string[]) => string): string {
  const key = 'auditlog.event.' + type;
  const translation = t(key);
  return translation === key ? formatEventType(type) : translation;
}

const PAGE_SIZE = 50;

export default function AuditTab() {
  const { t } = useI18n();
  const [page, setPage] = useState(0);
  const [search, setSearch] = useState("");
  const [eventType, setEventType] = useState("all");
  const [aggregateType, setAggregateType] = useState("all");
  const [changedBy, setChangedBy] = useState("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const { data: facets } = useQuery<AuditFacets>({
    queryKey: ["/api/admin/audit/facets"],
    queryFn: () => apiRequest("GET", "/api/admin/audit/facets"),
    staleTime: 60_000,
  });

  const filters = useMemo(() => {
    const params = new URLSearchParams({ offset: String(page * PAGE_SIZE), limit: String(PAGE_SIZE) });
    if (search.trim()) params.set("search", search.trim());
    if (eventType !== "all") params.set("eventType", eventType);
    if (aggregateType !== "all") params.set("aggregateType", aggregateType);
    if (changedBy !== "all") params.set("changedBy", changedBy);
    if (from) params.set("from", new Date(from).toISOString());
    if (to) params.set("to", new Date(to + "T23:59:59").toISOString());
    return params.toString();
  }, [page, search, eventType, aggregateType, changedBy, from, to]);

  const { data, isLoading } = useQuery<AuditResponse>({
    queryKey: ["/api/admin/audit", filters],
    queryFn: () => apiRequest("GET", `/api/admin/audit?${filters}`),
  });

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 0;

  const hasFilters = search.trim() !== "" || eventType !== "all" || aggregateType !== "all" || changedBy !== "all" || from !== "" || to !== "";

  const resetFilters = () => {
    setSearch("");
    setEventType("all");
    setAggregateType("all");
    setChangedBy("all");
    setFrom("");
    setTo("");
    setPage(0);
  };

  const exportCsv = () => {
    const rows = data?.events ?? [];
    const header = [
      t('auditlog.date'),
      t('auditlog.eventType'),
      t('auditlog.aggregate'),
      t('auditlog.summary'),
      t('auditlog.changedBy'),
    ];
    const escape = (v: string) => `"${v.replace(/"/g, '""')}"`;
    const lines = [
      header.map(escape).join(","),
      ...rows.map((e) => [
        formatDateTime(e.changedAtUtc),
        localizeEventType(e.eventType, t),
        `${e.aggregateType}/${e.aggregateId}`,
        localizeEventSummary(e.summary, t),
        e.changedByUserName ?? '',
      ].map(escape).join(",")),
    ];
    const blob = new Blob(["\uFEFF" + lines.join("\n")], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative flex-1 min-w-48">
          <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(0); }}
            placeholder={t('common.search')}
            className="pl-9 h-9"
          />
        </div>
        <Select value={eventType} onValueChange={(v) => { setEventType(v); setPage(0); }}>
          <SelectTrigger className="h-9 w-48"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('auditlog.eventType')}: all</SelectItem>
            {facets?.eventTypes.map((et) => (
              <SelectItem key={et} value={et}>{localizeEventType(et, t)}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={aggregateType} onValueChange={(v) => { setAggregateType(v); setPage(0); }}>
          <SelectTrigger className="h-9 w-40"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('auditlog.aggregate')}: all</SelectItem>
            {facets?.aggregateTypes.map((at) => (
              <SelectItem key={at} value={at}>{at}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={changedBy} onValueChange={(v) => { setChangedBy(v); setPage(0); }}>
          <SelectTrigger className="h-9 w-44"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('auditlog.changedBy')}: all</SelectItem>
            {facets?.users.map((u) => (
              <SelectItem key={u} value={u}>{u}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Input
          type="date"
          value={from}
          onChange={(e) => { setFrom(e.target.value); setPage(0); }}
          className="h-9 w-36"
          title={t('report.from')}
        />
        <Input
          type="date"
          value={to}
          onChange={(e) => { setTo(e.target.value); setPage(0); }}
          className="h-9 w-36"
          title={t('report.to')}
        />
        {hasFilters && (
          <Button variant="ghost" size="sm" onClick={resetFilters} className="h-9">
            <X className="w-4 h-4 mr-1" />
            {t('common.close')}
          </Button>
        )}
        <Button variant="outline" size="sm" onClick={exportCsv} disabled={!data?.events.length} className="h-9">
          <Download className="w-4 h-4 mr-1" />
          CSV
        </Button>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-20">
          <Loader2 className="w-8 h-8 animate-spin text-primary" />
        </div>
      ) : !data?.events.length ? (
        <div className="text-center py-20 text-muted-foreground">
          <ScrollText className="w-12 h-12 mx-auto mb-4 opacity-40" />
          <p>{t('auditlog.noEvents')}</p>
        </div>
      ) : (
        <>
          <div className="bg-card border border-border rounded-lg overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border bg-muted/50">
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('auditlog.date')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('auditlog.eventType')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('auditlog.aggregate')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('auditlog.summary')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('auditlog.changedBy')}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.events.map((evt) => (
                    <tr key={evt.id} className="border-t border-border hover:bg-muted/20 transition-colors">
                      <td className="p-3 whitespace-nowrap text-muted-foreground text-xs tabular-nums">
                        {formatDateTime(evt.changedAtUtc)}
                      </td>
                      <td className="p-3">
                        <span className="inline-block px-2 py-0.5 rounded text-xs font-medium bg-primary/10 text-primary">
                          {localizeEventType(evt.eventType, t)}
                        </span>
                      </td>
                      <td className="p-3 text-muted-foreground text-xs">
                        {evt.aggregateType}/{evt.aggregateId.substring(0, 8)}...
                      </td>
                      <td className="p-3 text-xs">{localizeEventSummary(evt.summary, t)}</td>
                      <td className="p-3 text-xs text-muted-foreground font-mono tabular-nums">
                        {evt.changedByUserName ?? '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>{t('auditlog.total')}: {data.total}</span>
              <div className="flex items-center gap-2">
                <Button variant="ghost" size="sm" disabled={page === 0} onClick={() => setPage(p => p - 1)} className="h-8">
                  <ChevronLeft className="w-4 h-4" />
                </Button>
                <span className="text-xs">{page + 1} / {totalPages}</span>
                <Button variant="ghost" size="sm" disabled={page >= totalPages - 1} onClick={() => setPage(p => p + 1)} className="h-8">
                  <ChevronRight className="w-4 h-4" />
                </Button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
