import { View, Text } from 'react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import type { WorkerUsageReport } from '../api/workerUsageApi';

type WorkerUsageSectionProps = {
  report: WorkerUsageReport | null;
  isLoading: boolean;
};

/**
 * A worker's usage report (epic #103 S5, #150).
 *
 * The header above already counts the vouchers; this answers the question that count cannot - how
 * much fuel, and *when*. The dates come from the server's `issuedAtUtc` / `usedAtUtc` columns (#963)
 * rather than from anything derived client-side, so a later block or recall cannot make the receipt
 * date move under the worker.
 *
 * Renders nothing at all when there is no fuel, so the screen does not grow an empty section.
 */
export function WorkerUsageSection({ report, isLoading }: WorkerUsageSectionProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  if (isLoading || !report || report.totals.count === 0) {
    return null;
  }

  const { totals } = report;

  return (
    <View style={{ gap: 8 }}>
      <Text
        allowFontScaling={false}
        style={{
          fontSize: 11,
          fontFamily: 'Rajdhani-Bold',
          letterSpacing: 1,
          color: tokens.colors.text.muted,
        }}
      >
        {t('company.usage.title')}
      </Text>

      {/* Litres, not counts: the header already has the counts, and "30 л з 50" is the number a
          worker actually reconciles against. */}
      <View style={{ flexDirection: 'row', gap: 8 }}>
        <Stat
          value={totals.litersReceived}
          label={t('codes.stock.issuedShort')}
          color={tokens.colors.text.primary}
        />
        <Stat
          value={totals.litersUsed}
          label={t('codes.stock.usedShort')}
          color={tokens.colors.accent}
        />
        <Stat
          value={totals.litersRemaining}
          label={t('codes.stock.leftShort')}
          color={tokens.colors.primary}
        />
      </View>

      {/* Only the dated rows. A voucher issued before the columns existed has a null date and would
          otherwise render a misleading empty line, so it is listed without one. */}
      {report.items
        .filter((item) => item.issuedAtUtc || item.usedAtUtc)
        .map((item) => (
          <View
            key={item.id}
            style={{
              flexDirection: 'row',
              alignItems: 'center',
              justifyContent: 'space-between',
              paddingVertical: 6,
              paddingHorizontal: 10,
              borderRadius: 8,
              backgroundColor: tokens.colors.surfaceSunken,
              borderWidth: 1,
              borderColor: tokens.colors.borderSubtle,
            }}
          >
            <Text
              allowFontScaling={false}
              numberOfLines={1}
              style={{ flex: 1, fontSize: 12, color: tokens.colors.text.primary }}
            >
              {item.voucherNumber} · {item.liters} {t('common.liter')}
            </Text>
            <Text
              allowFontScaling={false}
              style={{ fontSize: 11, color: tokens.colors.text.dim, marginLeft: 8 }}
            >
              {item.issuedAtUtc
                ? `${t('company.usage.received')} ${formatDay(item.issuedAtUtc)}`
                : t('company.usage.receivedUnknown')}
              {item.usedAtUtc ? ` · ${t('company.usage.used')} ${formatDay(item.usedAtUtc)}` : ''}
            </Text>
          </View>
        ))}
    </View>
  );
}

function Stat({ value, label, color }: { value: number; label: string; color: string }) {
  const tokens = useDesignTokens();
  return (
    <View
      style={{
        flex: 1,
        alignItems: 'center',
        paddingVertical: 10,
        borderRadius: 8,
        backgroundColor: tokens.colors.surfaceSunken,
        borderWidth: 1,
        borderColor: tokens.colors.borderSubtle,
      }}
    >
      <Text allowFontScaling={false} style={{ fontSize: 16, fontWeight: '800', color }}>
        {value}
      </Text>
      <Text
        allowFontScaling={false}
        style={{ fontSize: 9, color: tokens.colors.text.muted, marginTop: 2 }}
      >
        {label}
      </Text>
    </View>
  );
}

/**
 * Day-precision local date. A usage report answers "which month did I burn this in", so the time of
 * day is noise - and rendering it invites the reading that the stamp is a redemption timestamp,
 * which it is not.
 */
function formatDay(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';
  return `${String(date.getDate()).padStart(2, '0')}.${String(date.getMonth() + 1).padStart(2, '0')}.${date.getFullYear()}`;
}
