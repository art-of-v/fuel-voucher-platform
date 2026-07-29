import { useState } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery } from "@tanstack/react-query";
import { ScrollText, Loader2, ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { apiRequest } from "@/lib/api-client";
import { formatDate } from "@/lib/utils";

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

const PAGE_SIZE = 50;

export default function AuditTab() {
  const { t } = useI18n();
  const [page, setPage] = useState(0);

  const { data, isLoading } = useQuery<AuditResponse>({
    queryKey: ["/api/admin/audit", page],
    queryFn: () => apiRequest("GET", `/api/admin/audit?offset=${page * PAGE_SIZE}&limit=${PAGE_SIZE}`),
  });

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 0;

  return (
    <div className="space-y-4">
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
                        {formatDate(evt.changedAtUtc)}
                      </td>
                      <td className="p-3">
                        <span className="inline-block px-2 py-0.5 rounded text-xs font-medium bg-primary/10 text-primary">
                          {evt.eventType}
                        </span>
                      </td>
                      <td className="p-3 text-muted-foreground text-xs">
                        {evt.aggregateType}/{evt.aggregateId.substring(0, 8)}...
                      </td>
                      <td className="p-3 text-xs">{evt.summary}</td>
                      <td className="p-3 text-xs text-muted-foreground">
                        {evt.changedByUserName ?? 'System'}
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
