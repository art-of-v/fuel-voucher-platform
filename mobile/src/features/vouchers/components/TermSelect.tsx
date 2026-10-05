import { useEffect, useRef } from 'react';
import { Animated, Pressable, View } from 'react-native';
import { Haptics } from '../../../core/utils/haptics';
import { useI18n } from '../../../core/i18n';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { Text } from '../../../core/ui';
import type { TermQuote } from '../api/termQuote';

interface Props {
  quote: TermQuote | null;
  /** Currently chosen term code, or undefined when nothing is chosen yet. */
  value?: string;
  onChange: (termCode?: string) => void;
}

/**
 * Picks how long the fuel stays valid.
 *
 * A row of chips rather than a dropdown, for three reasons that all came out of using it:
 *
 *  - The ladder is the point. A dropdown hides the shorter, cheaper options behind a tap, and this is the
 *    one screen where the customer compares them against the price, so the whole ladder belongs in view.
 *  - A dropdown had no room here. It rendered as one collapsed row and truncated its own placeholder,
 *    because the card around it was laid out for a price and a title.
 *  - Chips are real targets. Each is at least 44 px tall, matching the touch-target rule the app now
 *    enforces (#846), instead of a row of small tappable words.
 *
 * The default is the LONGEST term the manager enabled and priced, not "no choice". "Full remaining term"
 * is the absence of a decision and reads like a fallback; the customer's default should be the product they
 * would have bought anyway, with the ladder offered to whoever deliberately commits to less. A basket line
 * arriving with no term is still honoured — a stale cart, or an API client that omits it, must keep
 * working — so nothing here depends on the customer having chosen.
 *
 * Renders nothing when short-term selling is off, when the ladder is empty, or when no tier can be sold:
 * then the card shows its normal price, exactly as it did before the feature existed.
 */
export function TermSelect({ quote, value, onChange }: Props) {
  const { t } = useI18n();
  const tokens = useDesignTokens();

  // A selection that no longer exists falls back to the default rather than silently reading as something
  // the customer chose. The ladder can change underneath them: a manager repricing a term, or a tier being
  // switched off while the screen is open.
  useEffect(() => {
    if (!quote?.enabled) return;
    const stillSellable = quote.terms.some((term) => term.term === value && term.available);
    if (!stillSellable) onChange(defaultTermCode(quote));
    // `onChange` is deliberately not a dependency: it is a new closure every render of the parent, and
    // this must fire when the ladder or the value changes, not when the callback identity does.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, quote]);

  if (!quote?.enabled) return null;

  const sellable = quote.terms.filter((term) => term.available);
  if (!sellable.length) return null;

  const chosen = value ?? defaultTermCode(quote);

  return (
    <View style={{ marginTop: tokens.spacing.md, gap: tokens.spacing.sm }}>
      <Text role="caption" tone="muted">
        {t('term.label')}
      </Text>

      <View style={styles.row}>
        {sellable.map((term) => (
          <TermChip
            key={term.term}
            label={t(`term.short.${term.term}`)}
            selected={term.term === chosen}
            onPress={() => onChange(term.term)}
          />
        ))}
      </View>
    </View>
  );
}

/** One term. Springs on press so a choice feels like a choice, not a page redraw. */
function TermChip({
  label,
  selected,
  onPress,
}: {
  label: string;
  selected: boolean;
  onPress: () => void;
}) {
  const tokens = useDesignTokens();
  const scale = useRef(new Animated.Value(1)).current;

  return (
    <Animated.View style={{ transform: [{ scale }] }}>
      <Pressable
        onPressIn={() => {
          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
          Animated.spring(scale, { toValue: 0.94, useNativeDriver: true, speed: 40 }).start();
        }}
        onPressOut={() => {
          Animated.spring(scale, {
            toValue: 1,
            useNativeDriver: true,
            speed: 20,
            bounciness: 6,
          }).start();
        }}
        onPress={onPress}
        style={[
          styles.chip,
          {
            backgroundColor: selected ? tokens.colors.primary : tokens.colors.surfaceSunken,
            borderColor: selected ? tokens.colors.primary : tokens.colors.borderLight,
          },
        ]}
      >
        <Text
          allowFontScaling={false}
          style={[
            styles.chipLabel,
            { color: selected ? tokens.colors.text.onPrimary : tokens.colors.text.primary },
          ]}
        >
          {label}
        </Text>
      </Pressable>
    </Animated.View>
  );
}

/**
 * The longest term the manager enabled and priced — the customer's default.
 *
 * Longest rather than cheapest, deliberately: the default should be the product they would have bought
 * anyway, with the discount ladder there for whoever wants to commit to less fuel time. Defaulting to the
 * cheapest would hand a customer the shortest validity on the shelf and let the price frame it.
 */
export function defaultTermCode(quote: TermQuote): string | undefined {
  const sellable = quote.terms.filter((term) => term.available);
  if (!sellable.length) return undefined;

  // The server returns terms shortest-first, so the last sellable one is the longest.
  return sellable[sellable.length - 1].term;
}

/**
 * The price a quote implies for one package line, or the package price when no term applies.
 *
 * Kept next to the picker so the card and the chips can never disagree about what a term costs: the card
 * renders the line price from here and the saving line comes from the same quote.
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

const styles = {
  row: {
    flexDirection: 'row' as const,
    flexWrap: 'wrap' as const,
    gap: 8,
  },
  chip: {
    minHeight: 44,
    minWidth: 64,
    paddingHorizontal: 14,
    alignItems: 'center' as const,
    justifyContent: 'center' as const,
    borderWidth: 1,
    borderRadius: 4,
  },
  chipLabel: {
    fontFamily: 'Inter-Black',
    fontSize: 12,
    letterSpacing: 1,
    textTransform: 'uppercase' as const,
  },
};
