import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { useI18n } from '../../../core/i18n';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { Select, type SelectOption } from '../../../core/ui';
import { Text } from '../../../core/ui';
import { getTermQuote, type TermQuote, type TermQuoteItem } from '../../vouchers/api/termQuote';
import { formatMoney } from '../../../core/utils/currency';
import type { CartItem } from '../../cart/types';

interface Props {
  item: CartItem;
  /** Persists the chosen term onto the cart line. */
  onTermChange: (termCode?: string) => void;
}

/**
 * Term picker for one cart line: how long the customer wants the fuel to be valid for.
 *
 * The shorter the term, the bigger the discount - committing to less up front is what earns the lower
 * price, and a customer who cannot use the fuel in time buys the difference back later.
 *
 * Renders nothing at all when short-term selling is off, when the ladder has no usable tier, or when the
 * quote failed: a failed preview must never block buying fuel.
 */
export function TermPicker({ item, onTermChange }: Props) {
  const { t } = useI18n();
  const tokens = useDesignTokens();
  const [quote, setQuote] = useState<TermQuote | null>(null);

  const stationId = item.station.id;
  const fuelTypeId = item.fuel.id;
  const liters = item.package.liters;

  useEffect(() => {
    let cancelled = false;
    getTermQuote(stationId, fuelTypeId, liters)
      .then((q) => {
        if (!cancelled) setQuote(q);
      })
      .catch(() => {
        // Silent: the line still sells at the full term, so there is nothing to tell the user.
        if (!cancelled) setQuote(null);
      });
    return () => {
      cancelled = true;
    };
  }, [stationId, fuelTypeId, liters]);

  if (!quote?.enabled) return null;

  const options = buildOptions(quote.terms, item.quantity);
  if (!options.length) return null;

  return (
    <View style={{ marginTop: tokens.spacing.sm, gap: tokens.spacing.xs }}>
      <Text role="caption" tone="muted">
        {t('checkout.termLabel')}
      </Text>
      <Select<string>
        label={t('checkout.termLabel')}
        placeholder={t('checkout.termPlaceholder')}
        value={item.termCode ?? null}
        onChange={(term) => onTermChange(term === '' ? undefined : term)}
        options={options}
      />
      {item.termCode && (
        <Text role="caption" tone="muted">
          {t('checkout.termHint')}
        </Text>
      )}
    </View>
  );
}

/**
 * Turns the quote into picker options. Unconfigured tiers stay visible but disabled — the same treatment
 * the renewal picker gives them — so the customer can see the ladder exists rather than wondering where
 * the shorter terms went.
 */
function buildOptions(terms: TermQuoteItem[], quantity: number): SelectOption<string>[] {
  const { t } = useI18n();
  return terms.map((term) => ({
    value: term.term,
    label: t(`checkout.term.${term.term}`),
    description: term.available
      ? formatMoney(term.linePriceUah * quantity)
      : t('checkout.termUnavailable'),
    disabled: !term.available,
  }));
}