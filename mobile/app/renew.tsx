import { useEffect, useMemo, useState } from 'react';
import { Alert, View } from 'react-native';
import * as Linking from 'expo-linking';
import { router, useLocalSearchParams } from 'expo-router';
import {
  PageLayout,
  ScreenHeader,
  Card,
  Divider,
  Select,
  Button,
  Price,
  Text,
  LoadingState,
  ErrorState,
  EmptyState,
} from '../src/core/ui';
import type { SelectOption } from '../src/core/ui';
import { useI18n } from '../src/core/i18n';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import {
  quoteRenewal,
  createRenewalCheckout,
  renewalErrorKey,
  RenewalApiError,
  type RenewalQuote,
  type RenewalVoucherQuote,
} from '../src/features/vouchers/renewal/api/renewal';

/**
 * Voucher renewal / replacement screen (planning #80, slice 4).
 *
 * Reached from the wallet with `?voucherIds=<id>[,<id>…]`. Every voucher is
 * quoted up front so the term picker can DISABLE a tier the manager turned off
 * or that has no stock — shown as "temporarily unavailable" rather than hidden —
 * and the user only ever pays for a buyable term. Checkout creates ONE Monobank
 * invoice for the whole batch (reusing the purchases money endpoint) and hands
 * off to the browser, exactly like a first purchase.
 *
 * The authoritative stock/price gate stays server-side at checkout: the quote is
 * an optimistic preview, so a tier that passes here can still be rejected at pay
 * time — surfaced via RenewalApiError → localised alert.
 */
export default function RenewScreen() {
  const t = useI18n((s) => s.t);
  const tokens = useDesignTokens();
  const { voucherIds: raw } = useLocalSearchParams<{ voucherIds?: string }>();

  const voucherIds = useMemo(
    () =>
      (raw ?? '')
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean),
    [raw],
  );

  const [quote, setQuote] = useState<RenewalQuote | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  // voucherId → selected term code. Only eligible vouchers ever get an entry.
  const [selections, setSelections] = useState<Record<string, string>>({});
  const [paying, setPaying] = useState(false);

  const load = async () => {
    if (voucherIds.length === 0) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setLoadFailed(false);
    try {
      const q = await quoteRenewal(voucherIds);
      setQuote(q);
      // Default each eligible voucher to its first available term.
      const defaults: Record<string, string> = {};
      for (const v of q.vouchers) {
        if (!v.eligible) continue;
        const firstAvailable = v.terms.find((term) => term.available);
        if (firstAvailable) defaults[v.voucherId] = firstAvailable.term;
      }
      setSelections(defaults);
    } catch {
      setLoadFailed(true);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [raw]);
  const priceFor = (v: RenewalVoucherQuote, term: string | undefined): number => {
    if (!term) return 0;
    const q = v.terms.find((x) => x.term === term);
    return q?.available ? q.priceUah : 0;
  };

  const eligibleVouchers = quote?.vouchers.filter((v) => v.eligible) ?? [];

  const total = eligibleVouchers.reduce(
    (sum, v) => sum + priceFor(v, selections[v.voucherId]),
    0,
  );

  // Only eligible vouchers with a chosen term go to checkout.
  const items = eligibleVouchers
    .filter((v) => selections[v.voucherId])
    .map((v) => ({ voucherId: v.voucherId, termCode: selections[v.voucherId] }));

  const handlePay = async () => {
    if (items.length === 0) return;
    setPaying(true);
    try {
      const result = await createRenewalCheckout(items);
      if (result.paymentUrl) await Linking.openURL(result.paymentUrl);
      router.replace('/my-codes');
    } catch (err) {
      const code = err instanceof RenewalApiError ? err.code : undefined;
      Alert.alert(t('renew.errorTitle'), t(renewalErrorKey(code)));
    } finally {
      setPaying(false);
    }
  };

  const header = <ScreenHeader title={t('renew.title')} subtitle={t('renew.subtitle')} />;

  if (loading) {
    return (
      <PageLayout header={header} scroll={false}>
        <LoadingState fullScreen />
      </PageLayout>
    );
  }

  if (voucherIds.length === 0 || (quote && quote.vouchers.length === 0)) {
    return (
      <PageLayout header={header} scroll={false}>
        <EmptyState title={t('renew.empty')} />
      </PageLayout>
    );
  }
  if (loadFailed || !quote) {
    return (
      <PageLayout header={header} scroll={false}>
        <ErrorState title={t('renew.loadFailed')} onRetry={load} />
      </PageLayout>
    );
  }

  if (!quote.enabled) {
    return (
      <PageLayout header={header} scroll={false}>
        <EmptyState title={t('renew.error.disabled')} />
      </PageLayout>
    );
  }

  const footer = (
    <Button
      label={t('renew.pay', String(total))}
      onPress={handlePay}
      loading={paying}
      disabled={items.length === 0}
      fullWidth
    />
  );
  return (
    <PageLayout header={header} footer={footer}>
      <View style={{ gap: tokens.spacing.md }}>
        {quote.vouchers.map((v) => {
          if (!v.eligible) {
            // Ineligible vouchers stay visible with the reason, so a batch that
            // mixes eligible and stale ones is self-explanatory.
            const reasonKey =
              v.ineligibleReason === 'not_your_voucher'
                ? 'renew.ineligible.not_your_voucher'
                : 'renew.ineligible.not_renewable';
            return (
              <Card key={v.voucherId} accent="neutral">
                <Text role="bodyStrong">
                  {v.provider} · {v.fuelName}
                </Text>
                <Text role="secondary" tone="muted">
                  {t(reasonKey)}
                </Text>
              </Card>
            );
          }

          const options: SelectOption<string>[] = v.terms.map((term) => ({
            value: term.term,
            label: t(`renew.term.${term.term}`),
            description: term.available
              ? t('renew.priceUah', String(term.priceUah))
              : t('renew.unavailable'),
            disabled: !term.available,
          }));

          const branchKey =
            v.branch === 'replace' ? 'renew.branch.replace' : 'renew.branch.extend';

          return (
            <Card key={v.voucherId}>
              <View style={{ gap: tokens.spacing.xs }}>
                <Text role="bodyStrong">
                  {v.provider} · {v.fuelName}
                </Text>
                <Text role="secondary" tone="muted">
                  {v.liters} {t('common.liter')} · {t(branchKey)}
                </Text>
              </View>
              <Divider tone="subtle" style={{ marginVertical: tokens.spacing.md }} />
              <Select<string>
                label={t('renew.termLabel')}
                placeholder={t('renew.termPlaceholder')}
                value={selections[v.voucherId] ?? null}
                onChange={(term) =>
                  setSelections((prev) => ({ ...prev, [v.voucherId]: term }))
                }
                options={options}
              />
            </Card>
          );
        })}

        <View
          style={{
            flexDirection: 'row',
            justifyContent: 'space-between',
            alignItems: 'center',
            paddingTop: tokens.spacing.sm,
          }}
        >
          <Text role="sectionTitle" tone="muted">
            {t('renew.total')}
          </Text>
          <Price amount={total} size="lg" decimals={0} />
        </View>
      </View>
    </PageLayout>
  );
}




