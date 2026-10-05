import { View } from 'react-native';
import { useI18n } from '../../../core/i18n';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { Select, type SelectOption } from '../../../core/ui';
import { formatMoney } from '../../../core/utils/currency';
import type { TermQuote } from '../api/termQuote';

interface Props {
  quote: TermQuote | null;
  /** Currently chosen term code, or undefined for the full remaining term. */
  value?: string;
  quantity: number;
  onChange: (termCode?: string) => void;
}

/**
 * Picks how long the fuel stays valid, and shows what that choice costs.
 *
 * Lives on the package card rather than at checkout, next to the price it changes. The customer decides
 * whether the discount is worth the shorter term at the moment they read the price — asking again on the
 * payment screen, after the total is already shown, is asking too late and reads as a surcharge.
 *
 * The label and the hint go through the Select's own `label` and `helper` slots. Rendering a caption above
 * it as well printed "Valid until" twice, which is exactly what an earlier version did: the field already
 * draws its label, and a second copy is a bug that reads as a design mistake.
 *
 * Renders nothing when short-term selling is off, when the ladder is empty, or when no tier can be sold:
 * then the card shows its normal price and nothing is lost, which is how the feature looked before it
 * existed.
 */
export function TermSelect({ quote, value, quantity, onChange }: Props) {
  const { t } = useI18n();
  const tokens = useDesignTokens();

  if (!quote?.enabled || quote.terms.length === 0) return null;

  const options: SelectOption<string>[] = [
    {
      value: '',
      label: t('term.fullTerm'),
      description: t('term.fullTermHint'),
    },
    ...quote.terms.map((term) => ({
      value: term.term,
      label: t(`term.${term.term}`),
      description: term.available
        ? formatMoney(term.linePriceUah * quantity)
        : t('term.unavailable'),
      disabled: !term.available,
    })),
  ];

  return (
    <View style={{ marginTop: tokens.spacing.md }}>
      <Select<string>
        label={t('term.label')}
        helper={value ? t('term.shortenHint') : undefined}
        placeholder={t('term.placeholder')}
        value={value ?? ''}
        onChange={(next) => onChange(next === '' ? undefined : next)}
        options={options}
      />
    </View>
  );
}

/**
 * The price a quote implies for one package line, or the package price when no term applies.
 *
 * Kept next to the select so the card and the picker can never disagree about what a term costs: the card
 * renders the line price from here and the picker's option descriptions come from the same quote.
 */
export function linePriceForTerm(
  quote: TermQuote | null,
  termCode: string | undefined,
  fallbackPrice: number,
): number {
  if (!termCode || !quote) return fallbackPrice;
  const term = quote.terms.find((candidate) => candidate.term === termCode);
  return term?.available ? term.linePriceUah : fallbackPrice;
}
