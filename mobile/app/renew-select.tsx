import { useEffect, useMemo, useRef, useState } from 'react';
import { View } from 'react-native';
import { router } from 'expo-router';
import { Check, Circle } from 'lucide-react-native';
import {
  PageLayout,
  ScreenHeader,
  Card,
  Button,
  Text,
  LoadingState,
  EmptyState,
} from '../src/core/ui';
import { useI18n } from '../src/core/i18n';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { formatExpirationDate } from '../src/core/utils/formatters';
import { useMyCodes } from '../src/features/vouchers/hooks/useMyCodes';
import { getRenewalConfig, type RenewalConfig } from '../src/features/vouchers/renewal/api/renewal';
import { isRenewableVoucher } from '../src/features/vouchers/renewal/eligibility';

/**
 * Multi-select entry for voucher renewal (planning #95).
 *
 * The discoverable counterpart to the near-expiry banner on the wallet: it lists
 * every renewable (near-expiry / expired) voucher, pre-selects them all, and lets
 * the user trim the batch before handing a `voucherIds` CSV to the existing
 * `/renew` screen — which quotes per-term prices and mints one Monobank invoice.
 *
 * Eligibility is decided by the shared `isRenewableVoucher` predicate (the same
 * one that gates the banner and the per-voucher shortcut); the backend
 * quote/checkout stays the authority, this only chooses what to offer.
 */
export default function RenewSelectScreen() {
  const t = useI18n((s) => s.t);
  const tokens = useDesignTokens();
  const { vouchers, user, loading } = useMyCodes();

  // The feature gate + threshold. A read (no device signature); a failure just
  // leaves the list empty (nothing renewable), which surfaces the empty state.
  const [cfg, setCfg] = useState<RenewalConfig | null>(null);
  const [cfgLoading, setCfgLoading] = useState(true);
  useEffect(() => {
    let cancelled = false;
    getRenewalConfig()
      .then((c) => {
        if (!cancelled) setCfg(c);
      })
      .catch(() => {})
      .finally(() => {
        if (!cancelled) setCfgLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const candidates = useMemo(
    () => vouchers.filter((v) => isRenewableVoucher(v, user?.id, cfg)),
    [vouchers, user?.id, cfg],
  );

  const [selected, setSelected] = useState<Set<string>>(new Set());

  // Pre-select every candidate once the data resolves; the user can trim from
  // there. Runs once (guarded) so a background refresh never clobbers edits.
  const initRef = useRef(false);
  useEffect(() => {
    if (initRef.current || cfgLoading || loading || candidates.length === 0) return;
    initRef.current = true;
    setSelected(new Set(candidates.map((v) => v.id)));
  }, [cfgLoading, loading, candidates]);

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const allSelected = candidates.length > 0 && selected.size === candidates.length;
  const toggleAll = () =>
    setSelected(allSelected ? new Set() : new Set(candidates.map((v) => v.id)));

  const onContinue = () => {
    const ids = candidates.filter((v) => selected.has(v.id)).map((v) => v.id);
    if (ids.length === 0) return;
    router.push(`/renew?voucherIds=${ids.join(',')}`);
  };

  const header = <ScreenHeader title={t('renew.select.title')} subtitle={t('renew.select.subtitle')} />;

  if (loading || cfgLoading) {
    return (
      <PageLayout header={header} scroll={false}>
        <LoadingState fullScreen />
      </PageLayout>
    );
  }

  if (cfg && !cfg.enabled) {
    return (
      <PageLayout header={header} scroll={false}>
        <EmptyState title={t('renew.error.disabled')} />
      </PageLayout>
    );
  }

  if (candidates.length === 0) {
    return (
      <PageLayout header={header} scroll={false}>
        <EmptyState title={t('renew.select.empty')} />
      </PageLayout>
    );
  }

  const footer = (
    <Button
      label={t('renew.select.continue', String(selected.size))}
      onPress={onContinue}
      disabled={selected.size === 0}
      fullWidth
    />
  );

  return (
    <PageLayout header={header} footer={footer}>
      <View style={{ gap: tokens.spacing.md }}>
        <View
          style={{
            flexDirection: 'row',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
        >
          <Text role="secondary" tone="muted">
            {selected.size} / {candidates.length}
          </Text>
          <Button
            size="sm"
            variant="ghost"
            label={allSelected ? t('renew.select.clear') : t('renew.select.selectAll')}
            onPress={toggleAll}
          />
        </View>

        {candidates.map((v) => {
          const isSel = selected.has(v.id);
          const days = v.expirationDate
            ? Math.ceil((new Date(v.expirationDate).getTime() - Date.now()) / 86400000)
            : null;
          const expired = days !== null && days < 0;
          return (
            <Card key={v.id} selected={isSel} onPress={() => toggle(v.id)}>
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.md }}>
                {isSel ? (
                  <Check size={22} color={tokens.colors.primary} />
                ) : (
                  <Circle size={22} color={tokens.colors.text.muted} />
                )}
                <View style={{ flex: 1, gap: 2 }}>
                  <Text role="bodyStrong">
                    {v.provider} · {v.fuelName || v.fuelType}
                  </Text>
                  <Text role="secondary" tone="muted">
                    {v.amount} {t('common.liter')}
                  </Text>
                  <Text role="caption" tone={expired ? 'danger' : 'warning'}>
                    {expired
                      ? t('renew.select.expired')
                      : v.expirationDate
                        ? `${t('codes.expires')}: ${formatExpirationDate(v.expirationDate)}`
                        : ''}
                  </Text>
                </View>
              </View>
            </Card>
          );
        })}
      </View>
    </PageLayout>
  );
}
