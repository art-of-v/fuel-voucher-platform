import { View, Text } from 'react-native';
import { Clock } from 'lucide-react-native';
import type { Order, Voucher } from '../../../core/types/api';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { OrderCard } from './OrderCard';
import { brandColorFor } from '../lib/display';
import { useMyCodesStyles } from './myCodesStyles';

type PendingOrdersSectionProps = {
  orders: Order[];
  expandedOrders: Set<string>;
  onToggle: (orderId: string) => void;
  onVoucherPress: (voucher: Voucher) => void;
  onPay: (order: Order) => void;
  onDelete: (order: Order) => void;
};

/**
 * The wallet's pending purchases, extracted from `app/my-codes.tsx` unchanged (planning #807).
 * The screen decides whether the section exists at all.
 */
export function PendingOrdersSection({
  orders,
  expandedOrders,
  onToggle,
  onVoucherPress,
  onPay,
  onDelete,
}: PendingOrdersSectionProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const styles = useMyCodesStyles(tokens);
  return (
    <View style={{ gap: tokens.spacing.md }}>
      <View style={styles.sectionHeader}>
        <Clock size={14} color={tokens.colors.accent} />
        <View
          style={{
            flex: 1,
            height: 1,
            backgroundColor: `${tokens.colors.accent}1A`,
            marginLeft: tokens.spacing.sm,
          }}
        />
        <Text
          allowFontScaling={false}
          style={[styles.sectionLabel, { color: tokens.colors.accent, marginBottom: 0 }]}
        >
          {t('codes.processingPurchases')}
        </Text>
      </View>
      {orders.map((order) => (
        <OrderCard
          key={order.id}
          order={order}
          isExpanded={expandedOrders.has(order.id)}
          onToggle={onToggle}
          onVoucherPress={onVoucherPress}
          onPay={onPay}
          onDelete={onDelete}
          brandColor={brandColorFor(order.brand, tokens)}
        />
      ))}
    </View>
  );
}
