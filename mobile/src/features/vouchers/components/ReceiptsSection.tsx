import { View, Text } from 'react-native';
import { CheckCircle } from 'lucide-react-native';
import type { Order, Voucher } from '../../../core/types/api';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { OrderCard } from './OrderCard';
import { brandColorFor } from '../lib/display';
import { useMyCodesStyles } from './myCodesStyles';

type ReceiptsSectionProps = {
  orders: Order[];
  expandedOrders: Set<string>;
  onToggle: (orderId: string) => void;
  onVoucherPress: (voucher: Voucher) => void;
  onReceipt: (order: Order) => void;
};

/**
 * The wallet's fulfilled receipts, extracted from `app/my-codes.tsx` unchanged (planning #807).
 *
 * Each card is a purchase the customer made, with its live vouchers inside it (one if they bought
 * one, many if they bought many). A renewal is not its own card: it is an event in a voucher's
 * History, and a replacement is re-parented onto the original order server-side. An owner sees
 * the company's receipts too, and nothing else (#161 H4): the hub owns who holds what, so this
 * list and the hub cannot disagree.
 */
export function ReceiptsSection({
  orders,
  expandedOrders,
  onToggle,
  onVoucherPress,
  onReceipt,
}: ReceiptsSectionProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const styles = useMyCodesStyles(tokens);
  return (
    <View style={{ gap: tokens.spacing.md }}>
      <View style={styles.sectionHeader}>
        <CheckCircle size={14} color={tokens.colors.primary} />
        <View
          style={{
            flex: 1,
            height: 1,
            backgroundColor: `${tokens.colors.primary}1A`,
            marginLeft: tokens.spacing.sm,
          }}
        />
        <Text
          allowFontScaling={false}
          style={[styles.sectionLabel, { color: tokens.colors.primary, marginBottom: 0 }]}
        >
          {t('codes.fulfilledOrders')}
        </Text>
      </View>
      {orders.map((order) => (
        <OrderCard
          key={order.id}
          order={order}
          isExpanded={expandedOrders.has(order.id)}
          onToggle={onToggle}
          onVoucherPress={onVoucherPress}
          // Orders money was actually paid for — the customer's own, or the company's
          // when an owner is looking at their company context. An issuance receipt carries
          // no money and so has no invoice behind it.
          onReceipt={onReceipt}
          brandColor={brandColorFor(order.brand, tokens)}
        />
      ))}
    </View>
  );
}
