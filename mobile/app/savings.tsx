import { useState, useEffect } from 'react';
import { View, Text, Pressable, ScrollView, StyleSheet, RefreshControl } from 'react-native';
import { Wallet, PiggyBank, Fuel, Ticket, Calendar } from 'lucide-react-native';
import { getMySavings, type SavingsReport } from '../src/features/savings/api/getSavings';
import {
  EmptyState,
  ErrorState,
  GridBackground,
  GridPageLayout,
  LoadingState,
  ScreenHeader,
  useContentInsets,
} from '../src/core/ui';
import { formatMoney } from '../src/core/utils/currency';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';

type PeriodFilter = 'all' | 'month' | '3months' | 'year';

export default function SavingsScreen() {
  const tokens = useDesignTokens();
  const contentInsets = useContentInsets();
  const { t } = useI18n();

  const [loading, setLoading] = useState(true);
  const [savings, setSavings] = useState<SavingsReport | null>(null);
  const [period, setPeriod] = useState<PeriodFilter>('all');
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  useEffect(() => {
    loadSavings();
  }, [period]);

  const loadSavings = async () => {
    try {
      setLoading(true);
      const now = new Date();
      let fromDate: string | undefined;
      let toDate: string | undefined;

      switch (period) {
        case 'month': {
          fromDate = new Date(now.getFullYear(), now.getMonth(), 1).toISOString();
          toDate = now.toISOString();
          break;
        }
        case '3months': {
          fromDate = new Date(now.getFullYear(), now.getMonth() - 2, 1).toISOString();
          toDate = now.toISOString();
          break;
        }
        case 'year': {
          fromDate = new Date(now.getFullYear(), 0, 1).toISOString();
          toDate = now.toISOString();
          break;
        }
        default:
          break;
      }

      const data = await getMySavings(fromDate, toDate);
      setSavings(data);
      setError(null);
    } catch (err: any) {
      console.error('Failed to load savings:', err.message);
      setError(err?.message || 'Failed to load savings');
    } finally {
      setLoading(false);
    }
  };

  const periodFilters: { key: PeriodFilter; label: string }[] = [
    { key: 'all', label: t('savings.allTime') },
    { key: 'year', label: t('savings.thisYear') },
    { key: '3months', label: t('savings.last3Months') },
    { key: 'month', label: t('savings.thisMonth') },
  ];

  const Header = <ScreenHeader title={t('savings.title')} />;

  if (loading) {
    return (
      <GridPageLayout header={Header} background={<GridBackground />} disableScroll>
        <LoadingState fullScreen />
      </GridPageLayout>
    );
  }

  if (!loading && error) {
    return (
      <GridPageLayout header={Header} background={<GridBackground />}>
        <ErrorState fullScreen onRetry={loadSavings} detail={error} />
      </GridPageLayout>
    );
  }

  // Only the all-time view with zero orders means "you have never bought" — a full-screen empty
  // state. A narrower period that happens to be empty still renders the chips (with zeroed cards)
  // so the customer can switch back.
  if (!savings || (period === 'all' && savings.ordersCount === 0)) {
    return (
      <GridPageLayout header={Header} background={<GridBackground />}>
        <EmptyState
          title={t('savings.noData')}
          icon={<PiggyBank />}
          action={{ label: t('common.retry'), onPress: loadSavings }}
        />
      </GridPageLayout>
    );
  }

  const cards: { key: string; icon: React.ReactNode; value: string; label: string; accent: string }[] = [
    {
      key: 'paid',
      icon: <Wallet size={18} color={tokens.colors.primary} />,
      value: formatMoney(savings.totalPaid),
      label: t('savings.totalPaid'),
      accent: tokens.colors.primary,
    },
    {
      key: 'saved',
      icon: <PiggyBank size={18} color={tokens.colors.success} />,
      value: formatMoney(savings.totalSavings),
      label: t('savings.totalSaved'),
      accent: tokens.colors.success,
    },
    {
      key: 'remainingLiters',
      icon: <Fuel size={18} color={tokens.colors.accent} />,
      value: `${savings.remainingLiters.toFixed(0)}${t('common.liter')}`,
      label: t('savings.remainingLiters'),
      accent: tokens.colors.accent,
    },
    {
      key: 'remainingVouchers',
      icon: <Ticket size={18} color={tokens.colors.text.primary} />,
      value: String(savings.remainingVouchers),
      label: t('savings.remainingVouchers'),
      accent: tokens.colors.text.primary,
    },
  ];

  return (
    <GridPageLayout header={Header} background={<GridBackground />} disableScroll>
      <ScrollView
        style={{ flex: 1 }}
        contentContainerStyle={{
          paddingHorizontal: tokens.spacing.containerPadding,
          paddingBottom: contentInsets.bottom,
        }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => { setRefreshing(true); loadSavings().finally(() => setRefreshing(false)); }}
            tintColor={tokens.colors.primary}
          />
        }
      >
        {/* Period filter */}
        <View style={styles.filterRow}>
          <Calendar size={14} color={tokens.colors.primary} />
          {periodFilters.map((pf) => (
            <Pressable
              key={pf.key}
              onPress={() => setPeriod(pf.key)}
              style={[
                styles.filterChip,
                {
                  backgroundColor: period === pf.key ? tokens.colors.primaryDim : tokens.colors.background,
                  borderColor: period === pf.key ? tokens.colors.primary : tokens.colors.borderLight,
                },
              ]}
            >
              <Text
                allowFontScaling={false}
                style={[styles.filterChipText, { color: period === pf.key ? tokens.colors.primary : tokens.colors.text.dim }]}
              >
                {pf.label}
              </Text>
            </Pressable>
          ))}
        </View>

        <View style={styles.summaryGrid}>
          {cards.map((c) => (
            <View
              key={c.key}
              style={[styles.summaryCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}
            >
              {c.icon}
              <Text allowFontScaling={false} style={[styles.summaryValue, { color: tokens.colors.text.primary }]}>
                {c.value}
              </Text>
              <Text allowFontScaling={false} style={[styles.summaryLabel, { color: tokens.colors.text.dim }]}>
                {c.label}
              </Text>
            </View>
          ))}
        </View>

        {/* Litres bought — a quiet footnote under the headline figures. */}
        <View style={[styles.footnote, { borderColor: tokens.colors.borderLight }]}>
          <Text allowFontScaling={false} style={[styles.footnoteLabel, { color: tokens.colors.text.dim }]}>
            {t('savings.totalLiters')}
          </Text>
          <Text allowFontScaling={false} style={[styles.footnoteValue, { color: tokens.colors.text.primary }]}>
            {savings.totalLiters.toFixed(0)}{t('common.liter')}
          </Text>
        </View>

        {/* Monthly savings breakdown */}
        {savings.monthly.length > 0 && (
          <View style={[styles.section, { borderColor: tokens.colors.borderLight }]}>
            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.primary }]}>
              {t('savings.monthlyBreakdown')}
            </Text>
            {savings.monthly.map((mb) => (
              <View key={mb.month} style={[styles.monthRow, { borderColor: tokens.colors.borderLight }]}>
                <Text allowFontScaling={false} style={[styles.monthLabel, { color: tokens.colors.text.primary }]}>
                  {mb.month}
                </Text>
                <View style={styles.monthStats}>
                  <Text allowFontScaling={false} style={[styles.monthStat, { color: tokens.colors.success }]}>
                    +{formatMoney(mb.saved)}
                  </Text>
                  <Text allowFontScaling={false} style={[styles.monthStat, { color: tokens.colors.text.dim }]}>
                    {formatMoney(mb.paid)}
                  </Text>
                  <Text allowFontScaling={false} style={[styles.monthStat, { color: tokens.colors.accent }]}>
                    {mb.liters.toFixed(0)}{t('common.liter')}
                  </Text>
                </View>
              </View>
            ))}
          </View>
        )}

        <Text allowFontScaling={false} style={[styles.disclaimer, { color: tokens.colors.text.dim }]}>
          {t('savings.disclaimer')}
        </Text>
      </ScrollView>
    </GridPageLayout>
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
  summaryLabel: { fontFamily: 'Inter-Black', fontSize: 8, letterSpacing: 1.5, textTransform: 'uppercase', textAlign: 'center' },
  section: { borderWidth: 1, borderRadius: 2, padding: 16, marginBottom: 16 },
  sectionTitle: { fontFamily: 'Rajdhani-SemiBold', fontSize: 12, letterSpacing: 4, textTransform: 'uppercase', marginBottom: 12 },
  monthRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingVertical: 10, borderBottomWidth: 1 },
  monthLabel: { fontFamily: 'Rajdhani-Bold', fontSize: 16, letterSpacing: 0.5 },
  monthStats: { flexDirection: 'row', gap: 12, alignItems: 'center' },
  monthStat: { fontFamily: 'Inter-Black', fontSize: 11, letterSpacing: 0.5 },
  footnote: {
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center',
    borderWidth: 1, borderRadius: 2, padding: 16, marginBottom: 16,
  },
  footnoteLabel: { fontFamily: 'Inter-Black', fontSize: 9, letterSpacing: 1.5, textTransform: 'uppercase' },
  footnoteValue: { fontFamily: 'Rajdhani-Bold', fontSize: 18, letterSpacing: -0.5 },
  disclaimer: { fontFamily: 'Inter', fontSize: 10, letterSpacing: 0.3, lineHeight: 15, opacity: 0.8 },
});
