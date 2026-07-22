import React from 'react';
import { View, Text, Pressable, StyleSheet } from 'react-native';
import { Minus, Plus, Trash2 } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import type { CartItem } from '../types';

interface CartItemCardProps {
  item: CartItem;
  onUpdateQuantity: (id: string, quantity: number) => void;
  onRemove: (id: string) => void;
}

export function CartItemCard({ item, onUpdateQuantity, onRemove }: CartItemCardProps) {
  const tokens = useDesignTokens();

  return (
    <View
      style={[
        styles.card,
        {
          backgroundColor: tokens.colors.card,
          borderColor: tokens.colors.borderLight,
        },
      ]}
    >
      <View style={styles.cardHeader}>
        <View style={{ flex: 1 }}>
          <Text style={[styles.cardTitle, { color: tokens.colors.text.primary }]}>
            {item.station?.name ?? 'Station'} - {item.fuel?.name ?? 'Fuel'}
          </Text>
          <Text style={[styles.cardBadge, { color: tokens.colors.primary }]}>
            {item.package?.liters ?? 0} LITERS
          </Text>
        </View>
        <Pressable onPress={() => onRemove(item.id)} style={{ padding: 4 }}>
          <Trash2 size={20} color={tokens.colors.error} />
        </Pressable>
      </View>

      <View style={styles.cardFooter}>
        <View style={styles.stepper}>
          <Pressable
            onPress={() => onUpdateQuantity(item.id, (item.quantity ?? 1) - 1)}
            style={[
              styles.stepperBtn,
              {
                backgroundColor: tokens.colors.background,
                borderColor: tokens.colors.borderLight,
              },
            ]}
          >
            <Minus size={20} color={tokens.colors.text.primary} />
          </Pressable>
          <Text style={[styles.stepperValue, { color: tokens.colors.primary }]}>
            {item.quantity ?? 1}
          </Text>
          <Pressable
            onPress={() => onUpdateQuantity(item.id, (item.quantity ?? 1) + 1)}
            style={[
              styles.stepperBtn,
              {
                backgroundColor: tokens.colors.background,
                borderColor: tokens.colors.borderLight,
              },
            ]}
          >
            <Plus size={20} color={tokens.colors.text.primary} />
          </Pressable>
        </View>
        <View style={{ alignItems: 'flex-end' }}>
          <Text style={[styles.itemMeta, { color: tokens.colors.text.dim }]}>
            {item.quantity ?? 0} x {item.package?.price ?? 0} ₴
          </Text>
          <Text style={[styles.itemTotal, { color: tokens.colors.text.primary }]}>
            {(item.package?.price ?? 0) * (item.quantity ?? 0)} ₴
          </Text>
        </View>
      </View>
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
