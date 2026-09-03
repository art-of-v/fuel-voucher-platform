import React, { useState } from 'react';
import { View } from 'react-native';
import { Trash2 } from 'lucide-react-native';
import {
  Badge,
  Card,
  ConfirmDialog,
  IconButton,
  Price,
  QuantityStepper,
  Text,
} from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { formatMoney } from '../../../core/utils/currency';
import type { CartItem } from '../types';

interface CartItemCardProps {
  item: CartItem;
  onUpdateQuantity: (id: string, quantity: number) => void;
  onRemove: (id: string) => void;
}

/**
 * One line in the basket.
 *
 * The interaction this component exists to correct: the "−" control used to call
 * `updateQuantity(id, 0)`, and the store drops any line at `quantity <= 0`. So a
 * customer at quantity 1 who tapped the same glyph that had meant "one fewer" a
 * moment earlier lost the line outright — no dialog, no undo, and no change in
 * the control's appearance to warn them. `QuantityStepper` swaps the glyph for a
 * bin in the danger role at `min`, and the removal is confirmed.
 *
 * The other correction is which number the eye lands on. The stepper's quantity
 * was set at 24px against a 20px line total, so the count outranked the money.
 */
export function CartItemCard({ item, onUpdateQuantity, onRemove }: CartItemCardProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const [confirmRemove, setConfirmRemove] = useState(false);

  const quantity = item.quantity ?? 1;
  const unitPrice = item.package?.price ?? 0;

  return (
    <Card style={{ marginBottom: tokens.spacing.cardGap }}>
      <View
        style={{
          flexDirection: 'row',
          alignItems: 'flex-start',
          justifyContent: 'space-between',
          gap: tokens.spacing.md,
          marginBottom: tokens.spacing.lg,
        }}
      >
        <View style={{ flex: 1, gap: tokens.spacing.xs }}>
          <Text role="heading" numberOfLines={2}>
            {item.station?.name ?? 'Station'} — {item.fuel?.name ?? 'Fuel'}
          </Text>
          <Badge label={`${item.package?.liters ?? 0} L`} status="primary" />
        </View>

        {/*
          Was a 20px glyph inside `padding: 4` — a ~28pt target for a destructive
          action. `IconButton` is 36pt with slop out to the 44pt minimum.
        */}
        <IconButton
          icon={<Trash2 />}
          onPress={() => setConfirmRemove(true)}
          accessibilityLabel={t('cart.removeLabel')}
          variant="danger"
          size="sm"
        />
      </View>

      <View
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: tokens.spacing.md,
        }}
      >
        <QuantityStepper
          value={quantity}
          onChange={(next) => onUpdateQuantity(item.id, next)}
          min={1}
          onRemove={() => setConfirmRemove(true)}
          accessibilityLabel={t('cart.quantityLabel')}
        />

        <View style={{ alignItems: 'flex-end', gap: 2 }}>
          <Text role="caption" tone="muted">
            {quantity} × {formatMoney(unitPrice)}
          </Text>
          <Price amount={unitPrice * quantity} size="md" align="right" />
        </View>
      </View>

      <ConfirmDialog
        visible={confirmRemove}
        tone="destructive"
        title={t('cart.removeTitle')}
        message={t('cart.removeMessage')}
        confirmLabel={t('cart.removeConfirm')}
        cancelLabel={t('common.cancel')}
        onConfirm={() => {
          setConfirmRemove(false);
          onRemove(item.id);
        }}
        onCancel={() => setConfirmRemove(false)}
      />
    </Card>
  );
}
