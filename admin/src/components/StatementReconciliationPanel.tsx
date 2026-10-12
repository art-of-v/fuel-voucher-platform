import { useMemo, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Download, FileSpreadsheet, FileText, Loader2, ScrollText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { apiRequest, apiBlob } from '@/lib/api-client';
import { useI18n } from '@/lib/i18n';
import {
  defaultStatementPeriod,
  statementPeriodLabel,
  statementQuery,
  statementYears,
  STATEMENT_MERCHANTS,
  type StatementMerchant,
  type StatementPeriod,
} from '@/lib/statementPeriod';
import { toast } from 'sonner';

type MatchStatus = 'matched' | 'amountMismatch' | 'unmatched' | 'refundMatch';

interface StatementMatchSummary {
  totalRows: number;
  matchedCount: number;
  mismatchCount: number;
  unmatchedCount: number;
  refundMatchCount: number;
  matchedTotalUah: number;
  mismatchDeltaUah: number;
  unmatchedTotalUah: number;
}

interface StatementMatchRow {
  statementId: string;
  status: MatchStatus;
  invoiceId: string | null;
  reference: string | null;
  acquirerAmountKopecks: number | null;
  acquirerAmount: number | null;
  orderId: string | null;
  localAmount: number | null;
  orderReference: string | null;
  orderStatus: string | null;
  acquirerCreatedAtUtc: string | null;
  note: string | null;
}

interface MissingRow {
  orderId: string;
  invoiceId: string;
  amount: number;
  status: string;
  createdAtUtc: string;
}

interface StatementAct {
  merchant: string;
  fromUtc: string;
  toUtc: string;
  statementRowCount: number;
  summary: StatementMatchSummary;
  rows: StatementMatchRow[];
  missingFromStatement: MissingRow[];
}

/**
 * The acquirer statement act for a period (#118), in the admin.
 *
 * The backend, the CSV and the PDF have been built since 11 October; nothing called them, so the
 * reconciliation act existed only as an API. This is the surface an accountant actually signs off
 * on: pick the month, see what matched, and export the document.
 *
 * Polled off on purpose (`refetchInterval: false`) - a signed period does not change under you, and
 * the admin's 15 s default would re-fetch a report nobody is looking at.
 */
export function StatementReconciliationPanel() {
  const { t } = useI18n();
  const [period, setPeriod] = useState<StatementPeriod>(() => defaultStatementPeriod());
  const years = useMemo(() => statementYears(), []);

  const query = useMemo(() => statementQuery(period), [period]);

  const act = useQuery<StatementAct>({
    queryKey: ['/api/admin/reconciliation/statement', query],
    queryFn: () => apiRequest<unknown, StatementAct>('GET', `/api/admin/reconciliation/statement?${query}`),
    enabled: false,
    refetchInterval: false,
  });

  const download = useMutation({
    mutationFn: async (format: 'csv' | 'pdf') => {
      const { blob, fileName } = await apiBlob(
        'GET',
        `/api/admin/reconciliation/statement/${format}?${query}`,
        `reconciliation-${statementPeriodLabel(period)}.${format}`,
      );
      return { blob, fileName };
    },
    onSuccess: ({ blob, fileName }) => {
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = fileName;
      anchor.click();
      setTimeout(() => URL.revokeObjectURL(url), 0);
    },
    onError: (error: Error) => toast.error(`${t('statementAct.downloadFailed')}: ${error.message}`),
  });

  const set = <K extends keyof StatementPeriod>(key: K) => (value: StatementPeriod[K]) =>
    setPeriod((current) => ({ ...current, [key]: value }));

  const summary = act.data?.summary;

  return (
    <section className="glass-panel p-4 space-y-4">
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div>
          <h3 className="font-semibold flex items-center gap-2">
            <ScrollText className="w-4 h-4" />
            {t('statementAct.title')}
          </h3>
          <p className="text-xs text-muted-foreground mt-1">{t('statementAct.hint')}</p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" disabled={download.isPending}
            onClick={() => download.mutate('csv')}>
            <FileSpreadsheet className="w-4 h-4 mr-2" />{t('statementAct.exportCsv')}
          </Button>
          <Button variant="outline" size="sm" disabled={download.isPending}
            onClick={() => download.mutate('pdf')}>
            <FileText className="w-4 h-4 mr-2" />{t('statementAct.exportPdf')}
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap gap-3 items-end">
        <label className="text-xs text-muted-foreground">
          {t('statementAct.merchant')}
          <select
            value={period.merchant}
            onChange={(e) => set('merchant')(e.target.value as StatementMerchant)}
            className="glass-input rounded-lg px-3 py-2 text-sm text-foreground block mt-1"
          >
            {STATEMENT_MERCHANTS.map((merchant) => (
              <option key={merchant} value={merchant}>
                {t(`statementAct.merchant.${merchant}`)}
              </option>
            ))}
          </select>
        </label>

        <label className="text-xs text-muted-foreground">
          {t('statementAct.month')}
          <select
            value={period.month}
            onChange={(e) => set('month')(Number(e.target.value))}
            className="glass-input rounded-lg px-3 py-2 text-sm text-foreground block mt-1"
          >
            {Array.from({ length: 12 }, (_, index) => index + 1).map((month) => (
              <option key={month} value={month}>{t(`statementAct.month.${month}`)}</option>
            ))}
          </select>
        </label>

        <label className="text-xs text-muted-foreground">
          {t('statementAct.year')}
          <select
            value={period.year}
            onChange={(e) => set('year')(Number(e.target.value))}
            className="glass-input rounded-lg px-3 py-2 text-sm text-foreground block mt-1"
          >
            {years.map((year) => (
              <option key={year} value={year}>{year}</option>
            ))}
          </select>
        </label>

        <Button size="sm" onClick={() => act.refetch()} disabled={act.isFetching}>
          {act.isFetching
            ? <Loader2 className="w-4 h-4 mr-2 animate-spin" />
            : <Download className="w-4 h-4 mr-2" />}
          {t('statementAct.build')}
        </Button>
      </div>

      {act.isError && (
        <p className="text-sm text-destructive">{t('statementAct.loadFailed')}</p>
      )}

      {act.data && summary && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 md:grid-cols-5 gap-3">
            <Tile label={t('statementAct.totalRows')} value={String(summary.totalRows)} />
            <Tile label={t('statementAct.matched')} value={String(summary.matchedCount)} />
            <Tile label={t('statementAct.mismatches')} value={String(summary.mismatchCount)}
              tone={summary.mismatchCount > 0 ? 'bad' : undefined} />
            <Tile label={t('statementAct.unmatched')} value={String(summary.unmatchedCount)}
              tone={summary.unmatchedCount > 0 ? 'bad' : undefined} />
            <Tile label={t('statementAct.refundMatches')} value={String(summary.refundMatchCount)} />
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-left">
                  <th className="p-3">{t('statementAct.acquirerTime')}</th>
                  <th className="p-3">{t('statementAct.verdict')}</th>
                  <th className="p-3">{t('statementAct.invoice')}</th>
                  <th className="p-3">{t('statementAct.acquirerAmount')}</th>
                  <th className="p-3">{t('statementAct.ourAmount')}</th>
                  <th className="p-3">{t('statementAct.note')}</th>
                </tr>
              </thead>
              <tbody>
                {act.data.rows.length === 0 && (
                  <tr>
                    <td colSpan={6} className="p-4 text-muted-foreground">
                      {t('statementAct.noRows')}
                    </td>
                  </tr>
                )}
                {act.data.rows.map((row) => (
                  <tr key={row.statementId} className="border-b border-border/50">
                    <td className="p-3 whitespace-nowrap">
                      {row.acquirerCreatedAtUtc ? row.acquirerCreatedAtUtc.slice(0, 16).replace('T', ' ') : '—'}
                    </td>
                    <td className="p-3">
                      <span className={`px-1.5 py-0.5 rounded text-xs ${
                        row.status === 'matched' ? 'bg-success/20 text-success'
                        : row.status === 'refundMatch' ? 'bg-muted text-muted-foreground'
                        : 'bg-destructive/15 text-destructive'}`}>
                        {t(`statementAct.status.${row.status}`)}
                      </span>
                    </td>
                    <td className="p-3 font-mono text-xs">{row.invoiceId ?? '—'}</td>
                    <td className="p-3 whitespace-nowrap">
                      {row.acquirerAmount === null ? '—' : `${row.acquirerAmount} ₴`}
                    </td>
                    <td className="p-3 whitespace-nowrap">
                      {row.localAmount === null ? '—' : `${row.localAmount} ₴`}
                    </td>
                    <td className="p-3 text-muted-foreground">{row.note ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {act.data.missingFromStatement.length > 0 && (
            <div>
              <h4 className="font-medium text-sm mb-2">{t('statementAct.missingTitle')}</h4>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border text-left">
                      <th className="p-3">{t('statementAct.invoice')}</th>
                      <th className="p-3">{t('statementAct.acquirerAmount')}</th>
                      <th className="p-3">{t('common.status')}</th>
                      <th className="p-3">{t('common.date')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {act.data.missingFromStatement.map((row) => (
                      <tr key={row.orderId} className="border-b border-border/50">
                        <td className="p-3 font-mono text-xs">{row.invoiceId}</td>
                        <td className="p-3 whitespace-nowrap">{row.amount} ₴</td>
                        <td className="p-3">{row.status}</td>
                        <td className="p-3">{row.createdAtUtc.slice(0, 10)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}
    </section>
  );
}

function Tile({ label, value, tone }: { label: string; value: string; tone?: 'bad' }) {
  return (
    <div className="glass-input rounded-lg p-3">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={`text-lg font-semibold ${tone === 'bad' ? 'text-destructive' : ''}`}>{value}</p>
    </div>
  );
}
