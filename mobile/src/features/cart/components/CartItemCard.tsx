import React from 'react';
import { View, Text, Pressable, StyleSheet } from 'react-native';
import { Minus, Plus, Trash2 } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { formatMoney } from '../../../core/utils/currency';
import { Haptics } from '../../../core/utils/haptics';
import type { CartItem } from '../types';

interface CartItemCardProps {
  item: CartItem;
  onUpdateQuantity: (id: string, quantity: number) => void;
  onRemove: (id: string) => void;
  /**
   * Whether this line's package still has stock. The basket re-reads the catalog rather than trusting
   * the copy of the package it was handed when the line was added: stock moves while a basket sits
   * open, and the alternative is discovering it as a rejected payment.
   *
   * `undefined` means "not known yet" (the catalog is still loading) and must not disable anything —
   * failing the whole basket on a slow fetch would be worse than letting the server have the final word.
   */
  isAvailable?: boolean;
}

export function CartItemCard({ item, onUpdateQuantity, onRemove, isAvailable }: CartItemCardProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const soft = tokens.surface.soft;
  const outOfStock = isAvailable === false;

  // The line is priced at whatever the customer was quoted when they chose a term, falling back to the
  // package price when they did not. Both totals in the basket already read from `termLinePrice`, so this
  // is the same number rather than a third opinion of it.
  const unitPrice = item.termLinePrice ?? item.package?.price ?? 0;
  const saved = item.termLinePrice ? (item.package?.price ?? 0) - item.termLinePrice : 0;

  return (
    <View
      style={[
        styles.card,
        {
          backgroundColor: tokens.colors.card,
          borderColor: outOfStock ? tokens.colors.error : tokens.colors.borderLight,
          opacity: outOfStock ? 0.6 : 1,
          borderRadius: soft ? tokens.surface.card : undefined,
        },
      ]}
    >
      <View style={styles.cardHeader}>
        <View style={{ flex: 1 }}>
          <Text style={[styles.cardTitle, { color: tokens.colors.text.primary }]}>
            {item.station?.name ?? 'Station'} - {item.fuel?.name ?? 'Fuel'}
          </Text>
          <Text style={[styles.cardBadge, { color: tokens.colors.primary }]}>
            {item.package?.liters ?? 0} {t('packages.liters')}
          </Text>
          {outOfStock && (
            <Text style={[styles.outOfStockText, { color: tokens.colors.error }]}>
              {t('packages.outOfStock')}
            </Text>
          )}
        </View>
        <Pressable
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            onRemove(item.id);
          }}
          // A 20pt glyph in a 28pt box: the target reaches 44 without moving the icon.
          hitSlop={8}
          style={{ padding: 4 }}
          accessibilityRole="button"
          accessibilityLabel={t('basket.remove')}
        >
          <Trash2 size={20} color={tokens.colors.error} />
        </Pressable>
      </View>

      {/* Quantity and the total are pointless on a line that cannot be paid for, so an emptied shelf
          shows only the way out. Leaving a stepper that changes a total nobody will be charged is how
          a basket starts contradicting the payment screen. */}
      {!outOfStock && (
        <View style={styles.cardFooter}>
          <View style={styles.stepper}>
            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onUpdateQuantity(item.id, (item.quantity ?? 1) - 1);
              }}
              // 44pt is the box; the slop only absorbs a thumb landing a few points off.
              hitSlop={4}
              accessibilityRole="button"
              accessibilityLabel={t('common.decrease')}
              style={[
                styles.stepperBtn,
                {
                  backgroundColor: tokens.colors.background,
                  borderColor: tokens.colors.borderLight,
                  borderRadius: soft ? tokens.surface.field : undefined,
                },
              ]}
            >
              <Minus size={20} color={tokens.colors.text.primary} />
            </Pressable>
            <Text style={[styles.stepperValue, { color: tokens.colors.primary }]}>
              {item.quantity ?? 1}
            </Text>
            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onUpdateQuantity(item.id, (item.quantity ?? 1) + 1);
              }}
              hitSlop={4}
              accessibilityRole="button"
              accessibilityLabel={t('common.increase')}
              style={[
                styles.stepperBtn,
                {
                  backgroundColor: tokens.colors.background,
                  borderColor: tokens.colors.borderLight,
                  borderRadius: soft ? tokens.surface.field : undefined,
                },
              ]}
            >
              <Plus size={20} color={tokens.colors.text.primary} />
            </Pressable>
          </View>
          <View style={{ alignItems: 'flex-end', gap: 2 }}>
            {/* The term price, not the package price: the total below this screen already used it, so
              leaving the line on the package price made the basket contradict its own total - 299.70 for
              the line against 284.70 to pay, with nothing explaining the difference. */}
            <Text style={[styles.itemMeta, { color: tokens.colors.text.dim }]}>
              {item.quantity ?? 0} × {formatMoney(unitPrice)}
            </Text>
            <Text style={[styles.itemTotal, { color: tokens.colors.text.primary }]}>
              {formatMoney(unitPrice * (item.quantity ?? 0))}
            </Text>
            {saved > 0 && (
              <Text style={[styles.itemMeta, { color: tokens.colors.primary }]}>
                {t('term.saves', formatMoney(saved))}
              </Text>
            )}
          </View>
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderWidth: 1,
    padding: 16,
    borderRadius: 2,
    marginBottom: 16,
  },
  cardHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    marginBottom: 16,
  },
  cardTitle: {
    fontWeight: '700',
    fontSize: 18,
    textTransform: 'uppercase',
  },
  cardBadge: {
    fontSize: 12,
    fontWeight: '700',
    textTransform: 'uppercase',
    marginTop: 2,
  },
  outOfStockText: {
    fontSize: 11,
    fontWeight: '800',
    textTransform: 'uppercase',
    marginTop: 4,
  },
  cardFooter: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  stepper: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 16,
  },
  stepperBtn: {
    width: 44,
    height: 44,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: 2,
  },
  stepperValue: {
    fontSize: 24,
    fontWeight: '900',
    minWidth: 40,
    textAlign: 'center',
    fontFamily: 'Rajdhani-Bold',
  },
  itemMeta: {
    fontSize: 10,
    fontWeight: '700',
    textTransform: 'uppercase',
  },
  itemTotal: {
    fontWeight: '700',
    fontSize: 20,
    marginTop: 2,
  },
});
