import { useState, useEffect } from 'react';
import {
  View, Text, Pressable, ScrollView,
  StyleSheet, RefreshControl, Share,
} from 'react-native';
import {
  Wallet, TrendingUp, ShoppingCart, Flame,
  Calendar, Share2,
} from 'lucide-react-native';
import { getMyReport, type ReportData } from '../src/features/report/api/getReport';
import { PageLayout } from '../src/components/page-layout';
import { GridBackground } from '../src/components/grid-background';
import {
  EmptyState,
  ErrorState,
  IconButton,
  LoadingState,
  ScreenHeader,
  useContentInsets,
} from '../src/core/ui';
import { formatMoney } from '../src/core/utils/currency';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';

type PeriodFilter = 'all' | 'month' | '3months' | 'year';

export default function ReportScreen() {
  const tokens = useDesignTokens();
  const contentInsets = useContentInsets();
  const { t } = useI18n();

  const [loading, setLoading] = useState(true);
  const [report, setReport] = useState<ReportData | null>(null);
  const [period, setPeriod] = useState<PeriodFilter>('all');
  const [showPayments, setShowPayments] = useState(false);
  const [showRedemptions, setShowRedemptions] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  useEffect(() => {
    loadReport();
  }, [period]);

  const loadReport = async () => {
    try {
      setLoading(true);
      const now = new Date();
      let fromDate: string | undefined;
      let toDate: string | undefined;

      switch (period) {
        case 'month': {
          const start = new Date(now.getFullYear(), now.getMonth(), 1);
          fromDate = start.toISOString();
          toDate = now.toISOString();
          break;
        }
        case '3months': {
          const start = new Date(now.getFullYear(), now.getMonth() - 2, 1);
          fromDate = start.toISOString();
          toDate = now.toISOString();
          break;
        }
        case 'year': {
          const start = new Date(now.getFullYear(), 0, 1);
          fromDate = start.toISOString();
          toDate = now.toISOString();
          break;
        }
        default:
          break;
      }

      const data = await getMyReport(fromDate, toDate);
      setReport(data);
      setError(null);
    } catch (err: any) {
      console.error('Failed to load report:', err.message);
      setError(err?.message || 'Failed to load report');
    } finally {
      setLoading(false);
    }
  };

  const periodFilters: { key: PeriodFilter; label: string }[] = [
    { key: 'all', label: t('report.allTime') },
    { key: 'year', label: t('report.thisYear') },
    { key: '3months', label: t('report.last3Months') },
    { key: 'month', label: t('report.thisMonth') },
  ];

  const formatDate = (iso: string) => {
    const d = new Date(iso);
    const day = String(d.getDate()).padStart(2, '0');
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const year = d.getFullYear();
    const hours = String(d.getHours()).padStart(2, '0');
    const minutes = String(d.getMinutes()).padStart(2, '0');
    return `${day}.${month}.${year} ${hours}:${minutes}`;
  };

  const formatAmount = (amount: number) => formatMoney(amount);

  const handleShare = async () => {
    if (!report) return;
    const { summary, monthlyBreakdown } = report;
    const lines = [
      t('report.title'),
      `${t('report.totalSpent')}: ${formatAmount(summary.totalSpent)}`,
      `${t('report.purchased')}: ${summary.vouchersPurchased}`,
      `${t('report.used')}: ${summary.vouchersUsed}`,
      `${t('report.litersUsed')}: ${summary.totalLitersUsed.toFixed(0)}${t('common.liter')}`,
    ];
    if (monthlyBreakdown.length > 0) {
      lines.push('');
      lines.push(t('report.monthlyBreakdown'));
      monthlyBreakdown.forEach((mb) => {
        lines.push(`${mb.month}: ${formatAmount(mb.totalSpent)} / +${mb.vouchersPurchased} / -${mb.vouchersUsed} / ${mb.totalLitersUsed.toFixed(0)}${t('common.liter')}`);
      });
    }
    await Share.share({ message: lines.join('\n') });
  };

  const Header = (
    <ScreenHeader
      title={t('report.title')}
      actions={
        report ? (
          <IconButton
            icon={<Share2 />}
            onPress={handleShare}
            accessibilityLabel={t('common.share')}
            variant="outlined"
          />
        ) : undefined
      }
    />
  );

  if (loading) {
    return (
      <PageLayout header={Header} background={<GridBackground />} disableScroll>
        <LoadingState fullScreen />
      </PageLayout>
    );
  }

  if (!loading && error) {
    return (
      <PageLayout header={Header} background={<GridBackground />}>
        <ErrorState fullScreen onRetry={loadReport} detail={error} />
      </PageLayout>
    );
  }

  if (!report) {
    return (
      <PageLayout header={Header} background={<GridBackground />}>
        <EmptyState
          title={t('report.noData')}
          icon={<Wallet />}
          action={{ label: t('common.retry'), onPress: loadReport }}
        />
      </PageLayout>
    );
  }

  const { summary, payments, redemptions, monthlyBreakdown } = report;

  return (
    <PageLayout header={Header} background={<GridBackground />} disableScroll>
      <ScrollView
        style={{ flex: 1 }}
        // Owned here because of the refresh control; the clearance still comes
        // from the layout system rather than a per-screen guess.
        contentContainerStyle={{
          paddingHorizontal: tokens.spacing.containerPadding,
          paddingBottom: contentInsets.bottom,
        }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => { setRefreshing(true); loadReport().finally(() => setRefreshing(false)); }}
            tintColor={tokens.colors.primary}
          />
        }
      >
        {/* Period Filter */}
        <View style={styles.filterRow}>
          <Calendar size={14} color={tokens.colors.primary} />
          {periodFilters.map((pf) => (
            <Pressable
              key={pf.key}
              onPress={() => setPeriod(pf.key)}
              style={[
                styles.filterChip,
                {
                  backgroundColor: period === pf.key
                    ? tokens.colors.primaryDim
                    : tokens.colors.background,
                  borderColor: period === pf.key
                    ? tokens.colors.primary
                    : tokens.colors.borderLight,
                },
              ]}
            >
              <Text
                allowFontScaling={false}
                style={[
                  styles.filterChipText,
                  {
                    color: period === pf.key ? tokens.colors.primary : tokens.colors.text.dim,
                  },
                ]}
              >
                {pf.label}
              </Text>
            </Pressable>
          ))}
        </View>

        {/* Summary Cards */}
        <View style={styles.summaryGrid}>
          <View style={[styles.summaryCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
            <Wallet size={18} color={tokens.colors.primary} />
            <Text allowFontScaling={false} style={[styles.summaryValue, { color: tokens.colors.text.primary }]}>
              {formatAmount(summary.totalSpent)}
            </Text>
            <Text allowFontScaling={false} style={[styles.summaryLabel, { color: tokens.colors.text.dim }]}>
              {t('report.totalSpent')}
            </Text>
          </View>
          <View style={[styles.summaryCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
            <ShoppingCart size={18} color={tokens.colors.accent} />
            <Text allowFontScaling={false} style={[styles.summaryValue, { color: tokens.colors.text.primary }]}>
              {summary.vouchersPurchased}
            </Text>
            <Text allowFontScaling={false} style={[styles.summaryLabel, { color: tokens.colors.text.dim }]}>
              {t('report.purchased')}
            </Text>
          </View>
          <View style={[styles.summaryCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
            <Flame size={18} color={tokens.colors.error} />
            <Text allowFontScaling={false} style={[styles.summaryValue, { color: tokens.colors.text.primary }]}>
              {summary.vouchersUsed}
            </Text>
            <Text allowFontScaling={false} style={[styles.summaryLabel, { color: tokens.colors.text.dim }]}>
              {t('report.used')}
            </Text>
          </View>
          <View style={[styles.summaryCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
            <TrendingUp size={18} color={tokens.colors.primary} />
            <Text allowFontScaling={false} style={[styles.summaryValue, { color: tokens.colors.text.primary }]}>
              {summary.totalLitersUsed.toFixed(0)}{t('common.liter')}
            </Text>
            <Text allowFontScaling={false} style={[styles.summaryLabel, { color: tokens.colors.text.dim }]}>
              {t('report.litersUsed')}
            </Text>
          </View>
        </View>

        {/* Monthly Breakdown */}
        {monthlyBreakdown.length > 0 && (
          <View style={[styles.section, { borderColor: tokens.colors.borderLight }]}>
            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.primary }]}>
              {t('report.monthlyBreakdown')}
            </Text>
            {monthlyBreakdown.map((mb) => (
              <View key={mb.month} style={[styles.monthRow, { borderColor: tokens.colors.borderLight }]}>
                <Text allowFontScaling={false} style={[styles.monthLabel, { color: tokens.colors.text.primary }]}>
                  {mb.month}
                </Text>
                <View style={styles.monthStats}>
                  <Text allowFontScaling={false} style={[styles.monthStat, { color: tokens.colors.primary }]}>
                    {formatAmount(mb.totalSpent)}
                  </Text>
                  <Text allowFontScaling={false} style={[styles.monthStat, { color: tokens.colors.accent }]}>
                    +{mb.vouchersPurchased}
                  </Text>
                  <Text allowFontScaling={false} style={[styles.monthStat, { color: tokens.colors.error }]}>
                    -{mb.vouchersUsed}
                  </Text>
                  <Text allowFontScaling={false} style={[styles.monthStat, { color: tokens.colors.primary }]}>
                    {mb.totalLitersUsed.toFixed(0)}{t('common.liter')}
                  </Text>
                </View>
              </View>
            ))}
          </View>
        )}

        {/* Payments List */}
        <Pressable
          onPress={() => setShowPayments(!showPayments)}
          style={[styles.section, { borderColor: tokens.colors.borderLight }]}
        >
          <View style={styles.sectionHeader}>
            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.primary }]}>
              {t('report.payments')} ({payments.length})
            </Text>
            <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>
              {showPayments ? '▲' : '▼'}
            </Text>
          </View>
          {showPayments && payments.length === 0 && (
            <Text style={{ color: tokens.colors.text.dim, fontSize: 12, padding: 12 }}>
              {t('report.noPayments')}
            </Text>
          )}
          {showPayments && payments.map((p) => (
            <View key={p.orderId} style={[styles.entryRow, { borderColor: tokens.colors.borderLight }]}>
              <View style={styles.entryLeft}>
                <Text allowFontScaling={false} style={[styles.entryProvider, { color: tokens.colors.text.primary }]}>
                  {p.provider?.toUpperCase() || 'FUEL'}
                </Text>
                <Text allowFontScaling={false} style={[styles.entryMeta, { color: tokens.colors.text.dim }]}>
                  {p.liters}{t('common.liter')} × {p.quantity} &middot; {formatDate(p.createdAtUtc)}
                </Text>
              </View>
              <Text allowFontScaling={false} style={[styles.entryAmount, { color: tokens.colors.primary }]}>
                {formatAmount(p.amount)}
              </Text>
            </View>
          ))}
        </Pressable>

        {/* Redemptions List */}
        <Pressable
          onPress={() => setShowRedemptions(!showRedemptions)}
          style={[styles.section, { borderColor: tokens.colors.borderLight }]}
        >
          <View style={styles.sectionHeader}>
            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.error }]}>
              {t('report.redemptions')} ({redemptions.length})
            </Text>
            <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>
              {showRedemptions ? '▲' : '▼'}
            </Text>
          </View>
          {showRedemptions && redemptions.length === 0 && (
            <Text style={{ color: tokens.colors.text.dim, fontSize: 12, padding: 12 }}>
              {t('report.noRedemptions')}
            </Text>
          )}
          {showRedemptions && redemptions.map((r) => (
            <View key={r.voucherId} style={[styles.entryRow, { borderColor: tokens.colors.borderLight }]}>
              <View style={styles.entryLeft}>
                <Text allowFontScaling={false} style={[styles.entryProvider, { color: tokens.colors.text.primary }]}>
                  {r.provider?.toUpperCase()} &middot; {r.fuelName || r.fuelType}
                </Text>
                <Text allowFontScaling={false} style={[styles.entryMeta, { color: tokens.colors.text.dim }]}>
                  {r.liters}{t('common.liter')} &middot; {formatDate(r.redeemedAt)}
                </Text>
              </View>
              <View style={[styles.usedBadge, { backgroundColor: `${tokens.colors.error}14`, borderColor: `${tokens.colors.error}33` }]}>
                <Text allowFontScaling={false} style={[styles.usedBadgeText, { color: tokens.colors.error }]}>
                  {t('report.used')}
                </Text>
              </View>
            </View>
          ))}
        </Pressable>
      </ScrollView>
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  filterRow: { flexDirection: 'row', gap: 8, marginBottom: 20, alignItems: 'center', flexWrap: 'wrap' },
  filterChip: { paddingHorizontal: 14, paddingVertical: 8, borderRadius: 8, borderWidth: 1 },
  filterChipText: { fontFamily: 'Inter-Black', fontSize: 9, letterSpacing: 1, textTransform: 'uppercase' },
  summaryGrid: { flexDirection: 'row', flexWrap: 'wrap', gap: 10, marginBottom: 24 },
  summaryCard: {
    width: '48%', padding: 16, borderRadius: 2, borderWidth: 1, gap: 8,
    alignItems: 'center' as const,
  },
  summaryValue: { fontFamily: 'Rajdhani-Bold', fontSize: 22, letterSpacing: -0.5 },
  summaryLabel: { fontFamily: 'Inter-Black', fontSize: 8, letterSpacing: 1.5, textTransform: 'uppercase' },
  section: { borderWidth: 1, borderRadius: 2, padding: 16, marginBottom: 16 },
  sectionHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 },
  sectionTitle: { fontFamily: 'Rajdhani-SemiBold', fontSize: 12, letterSpacing: 4, textTransform: 'uppercase' },
  monthRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingVertical: 10, borderBottomWidth: 1 },
  monthLabel: { fontFamily: 'Rajdhani-Bold', fontSize: 16, letterSpacing: 0.5 },
  monthStats: { flexDirection: 'row', gap: 12 },
  monthStat: { fontFamily: 'Inter-Black', fontSize: 11, letterSpacing: 0.5 },
  entryRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingVertical: 12, borderBottomWidth: 1 },
  entryLeft: { flex: 1, gap: 4 },
  entryProvider: { fontFamily: 'Rajdhani-Bold', fontSize: 14, letterSpacing: 0.5, textTransform: 'uppercase' },
  entryMeta: { fontFamily: 'Inter', fontSize: 10, letterSpacing: 0.5 },
  entryAmount: { fontFamily: 'Rajdhani-Bold', fontSize: 16, letterSpacing: -0.5, marginLeft: 12 },
  usedBadge: { paddingHorizontal: 10, paddingVertical: 4, borderRadius: 6, borderWidth: 1 },
  usedBadgeText: { fontFamily: 'Inter-Black', fontSize: 8, letterSpacing: 1, textTransform: 'uppercase' },
});
