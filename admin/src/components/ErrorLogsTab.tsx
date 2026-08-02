import { useState, useMemo, Fragment } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Bug, Loader2, ChevronLeft, ChevronRight, Search, X, Trash2, ChevronDown, ChevronUp, Download } from "lucide-react";
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
import { cn, formatDateTime } from "@/lib/utils";
import DateInput from "@/components/DateInput";

interface ErrorLogItem {
  id: string;
  loggedAtUtc: string;
  level: string;
  message: string;
  exceptionType: string | null;
  exceptionMessage: string | null;
  stackTrace: string | null;
  source: string | null;
  requestPath: string | null;
  requestMethod: string | null;
  userName: string | null;
}

interface ErrorLogResponse {
  total: number;
  items: ErrorLogItem[];
}

interface ErrorLogFacets {
  levels: string[];
  sources: string[];
}

const PAGE_SIZE = 50;

function levelColor(level: string): string {
  const l = level.toLowerCase();
  if (l === "critical" || l === "fatal") return "bg-red-600/20 text-red-400";
  if (l === "error") return "bg-red-500/15 text-red-400";
  return "bg-yellow-500/15 text-yellow-400";
}

export default function ErrorLogsTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [search, setSearch] = useState("");
  const [level, setLevel] = useState("all");
  const [source, setSource] = useState("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);

  const { data: facets } = useQuery<ErrorLogFacets>({
    queryKey: ["/api/admin/errors/facets"],
    queryFn: () => apiRequest("GET", "/api/admin/errors/facets"),
    staleTime: 60_000,
  });

  const filters = useMemo(() => {
    const params = new URLSearchParams({ offset: String(page * PAGE_SIZE), limit: String(PAGE_SIZE) });
    if (search.trim()) params.set("search", search.trim());
    if (level !== "all") params.set("level", level);
    if (source !== "all") params.set("source", source);
    if (from) params.set("from", new Date(from).toISOString());
    if (to) params.set("to", new Date(to + "T23:59:59").toISOString());
    return params.toString();
  }, [page, search, level, source, from, to]);

  const { data, isLoading } = useQuery<ErrorLogResponse>({
    queryKey: ["/api/admin/errors", filters],
    queryFn: () => apiRequest("GET", `/api/admin/errors?${filters}`),
  });

  const clearMutation = useMutation({
    mutationFn: () => apiRequest("DELETE", "/api/admin/errors"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/errors"] });
      queryClient.invalidateQueries({ queryKey: ["/api/admin/errors/facets"] });
    },
  });

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 0;

  const hasFilters = search.trim() !== "" || level !== "all" || source !== "all" || from !== "" || to !== "";

  const resetFilters = () => {
    setSearch("");
    setLevel("all");
    setSource("all");
    setFrom("");
    setTo("");
    setPage(0);
  };

  const handleClear = () => {
    if (window.confirm(t('errorlogs.clearConfirm'))) {
      clearMutation.mutate();
    }
  };

  const exportCsv = () => {
    const rows = data?.items ?? [];
    const header = [
      t('errorlogs.date'),
      t('errorlogs.level'),
      t('errorlogs.source'),
      t('errorlogs.message'),
      t('errorlogs.exception'),
      t('errorlogs.request'),
      t('errorlogs.user'),
    ];
    const escape = (v: string) => `"${v.replace(/"/g, '""')}"`;
    const lines = [
      header.map(escape).join(","),
      ...rows.map((e) => [
        formatDateTime(e.loggedAtUtc),
        e.level,
        e.source ?? '',
        e.message,
        e.exceptionMessage ?? '',
        `${e.requestMethod ?? ''} ${e.requestPath ?? ''}`.trim(),
        e.userName ?? '',
      ].map(escape).join(",")),
    ];
    const blob = new Blob(["\uFEFF" + lines.join("\n")], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `error-logs-${new Date().toISOString().slice(0, 10)}.csv`;
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
        <Select value={level} onValueChange={(v) => { setLevel(v); setPage(0); }}>
          <SelectTrigger className="h-9 w-36"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('errorlogs.level')}: all</SelectItem>
            {facets?.levels.map((l) => (
              <SelectItem key={l} value={l}>{l}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={source} onValueChange={(v) => { setSource(v); setPage(0); }}>
          <SelectTrigger className="h-9 w-52"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('errorlogs.source')}: all</SelectItem>
            {facets?.sources.map((s) => (
              <SelectItem key={s} value={s}>{s}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <DateInput
          value={from}
          onChange={(v) => { setFrom(v); setPage(0); }}
          className="h-9 w-36"
          title={t('report.from')}
        />
        <DateInput
          value={to}
          onChange={(v) => { setTo(v); setPage(0); }}
          className="h-9 w-36"
          title={t('report.to')}
        />
        {hasFilters && (
          <Button variant="ghost" size="sm" onClick={resetFilters} className="h-9">
            <X className="w-4 h-4 mr-1" />
            {t('common.close')}
          </Button>
        )}
        <Button variant="outline" size="sm" onClick={exportCsv} disabled={!data?.items.length} className="h-9">
          <Download className="w-4 h-4 mr-1" />
          CSV
        </Button>
        <Button variant="destructive" size="sm" onClick={handleClear} disabled={clearMutation.isPending || !data?.items.length} className="h-9">
          <Trash2 className="w-4 h-4 mr-1" />
          {t('errorlogs.clear')}
        </Button>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-20">
          <Loader2 className="w-8 h-8 animate-spin text-primary" />
        </div>
      ) : !data?.items.length ? (
        <div className="text-center py-20 text-muted-foreground">
          <Bug className="w-12 h-12 mx-auto mb-4 opacity-40" />
          <p>{t('errorlogs.noLogs')}</p>
        </div>
      ) : (
        <>
          <div className="bg-card border border-border rounded-lg overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border bg-muted/50">
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('errorlogs.date')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('errorlogs.level')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('errorlogs.source')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('errorlogs.message')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('errorlogs.request')}</th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">{t('errorlogs.user')}</th>
                    <th className="w-8 p-3"></th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((item) => {
                    const isExpanded = expanded === item.id;
                    return (
                      <Fragment key={item.id}>
                        <tr
                          onClick={() => setExpanded(isExpanded ? null : item.id)}
                          className="border-t border-border hover:bg-muted/20 transition-colors cursor-pointer"
                        >
                          <td className="p-3 whitespace-nowrap text-muted-foreground text-xs tabular-nums">
                            {formatDateTime(item.loggedAtUtc)}
                          </td>
                          <td className="p-3">
                            <span className={cn("inline-block px-2 py-0.5 rounded text-xs font-medium", levelColor(item.level))}>
                              {item.level}
                            </span>
                          </td>
                          <td className="p-3 text-muted-foreground text-xs font-mono max-w-52 truncate">
                            {item.source ?? '—'}
                          </td>
                          <td className="p-3 text-xs max-w-md">
                            <span className="line-clamp-2 break-words">{item.message}</span>
                          </td>
                          <td className="p-3 text-muted-foreground text-xs font-mono whitespace-nowrap">
                            {item.requestMethod ? `${item.requestMethod} ${item.requestPath ?? ''}` : '—'}
                          </td>
                          <td className="p-3 text-xs text-muted-foreground font-mono tabular-nums">
                            {item.userName ?? '—'}
                          </td>
                          <td className="p-3 text-muted-foreground">
                            {isExpanded ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
                          </td>
                        </tr>
                        {isExpanded && (
                          <tr className="border-t border-border bg-muted/30">
                            <td colSpan={7} className="p-4">
                              <div className="space-y-2 text-xs">
                                {item.exceptionType && (
                                  <div>
                                    <span className="text-muted-foreground font-medium">{t('errorlogs.exceptionType')}: </span>
                                    <span className="font-mono text-red-300">{item.exceptionType}</span>
                                  </div>
                                )}
                                {item.exceptionMessage && (
                                  <div>
                                    <span className="text-muted-foreground font-medium">{t('errorlogs.exception')}: </span>
                                    <span className="font-mono break-all">{item.exceptionMessage}</span>
                                  </div>
                                )}
                                {item.stackTrace && (
                                  <pre className="bg-black/40 border border-border rounded-md p-3 overflow-x-auto text-[11px] leading-relaxed font-mono text-muted-foreground whitespace-pre-wrap break-words">
                                    {item.stackTrace}
                                  </pre>
                                )}
                                {!item.exceptionType && !item.exceptionMessage && !item.stackTrace && (
                                  <span className="text-muted-foreground">{t('errorlogs.noException')}</span>
                                )}
                              </div>
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>{t('errorlogs.total')}: {data.total}</span>
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
