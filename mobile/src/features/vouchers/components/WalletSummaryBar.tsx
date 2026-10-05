import { View, Text } from 'react-native';
import type { Order } from '../../../core/types/api';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';

type WalletSummaryBarProps = {
  orders: Order[];
  fulfilledOrders: Order[];
  pendingOrders: Order[];
};

/**
 * The three counts across the top of the wallet: all orders, fulfilled, pending.
 * Purely presentational — it takes the lists because that is what the screen already
 * has, and reads only their lengths.
 */
export function WalletSummaryBar({
  orders,
  fulfilledOrders,
  pendingOrders,
}: WalletSummaryBarProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  return (
    <View style={{ flexDirection: 'row', gap: 8, marginBottom: 16 }}>
      <View
        style={{
          flex: 1,
          backgroundColor: tokens.colors.surfaceSunken,
          borderRadius: 8,
          padding: 10,
          borderWidth: 1,
          borderColor: tokens.colors.borderSubtle,
        }}
      >
        <Text
          allowFontScaling={false}
          style={{
            fontSize: 16,
            fontWeight: '800',
            color: tokens.colors.primary,
            textAlign: 'center',
          }}
        >
          {orders.length}
        </Text>
        <Text
          allowFontScaling={false}
          style={{
            fontSize: 9,
            color: tokens.colors.text.muted,
            textAlign: 'center',
            marginTop: 2,
          }}
        >
          {t('codes.orders')}
        </Text>
      </View>
      <View
        style={{
          flex: 1,
          backgroundColor: `${tokens.colors.primary}14`,
          borderRadius: 8,
          padding: 10,
          borderWidth: 1,
          borderColor: `${tokens.colors.primary}33`,
        }}
      >
        <Text
          allowFontScaling={false}
          style={{
            fontSize: 16,
            fontWeight: '800',
            color: tokens.colors.primary,
            textAlign: 'center',
          }}
        >
          {fulfilledOrders.length}
        </Text>
        <Text
          allowFontScaling={false}
          style={{ fontSize: 9, color: tokens.colors.primary, textAlign: 'center', marginTop: 2 }}
        >
          {t('codes.fulfilled')}
        </Text>
      </View>
      <View
        style={{
          flex: 1,
          backgroundColor: `${tokens.colors.accent}14`,
          borderRadius: 8,
          padding: 10,
          borderWidth: 1,
          borderColor: `${tokens.colors.accent}33`,
        }}
      >
        <Text
          allowFontScaling={false}
          style={{
            fontSize: 16,
            fontWeight: '800',
            color: tokens.colors.accent,
            textAlign: 'center',
          }}
        >
          {pendingOrders.length}
        </Text>
        <Text
          allowFontScaling={false}
          style={{ fontSize: 9, color: tokens.colors.accent, textAlign: 'center', marginTop: 2 }}
        >
          {t('codes.pending')}
        </Text>
      </View>
    </View>
  );
}
