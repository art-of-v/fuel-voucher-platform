import { View, Text, Animated } from 'react-native';
import { Fuel } from 'lucide-react-native';
import type { Order, Voucher } from '../../../core/types/api';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import type { ListControls } from '../lib/listControls';
import { brandColorFor } from '../lib/display';
import { OrderCard } from './OrderCard';
// Through the barrel, like the screen imported them: that is the module its test replaces.
import { VoucherCard, VoucherListControlsBar } from '.';
import { useMyCodesStyles } from './myCodesStyles';

type WorkerIssuanceSectionProps = {
  /** Every voucher the worker holds, for the controls' own counts and facets. */
  vouchers: Voucher[];
  controls: ListControls;
  onControlsChange: (controls: ListControls) => void;
  /** The worker's receipts, already narrowed to the vouchers the controls leave visible. */
  issuanceOrders: Order[];
  /** Fuel predating issuance orders (or arriving another way), already narrowed the same way. */
  looseIssued: Voucher[];
  expandedOrders: Set<string>;
  onToggle: (orderId: string) => void;
  onVoucherPress: (voucher: Voucher) => void;
  userId?: string;
  pulseAnim: Animated.Value;
  onSelectVoucher: (voucher: Voucher) => void;
};

/**
 * The worker's own wallet section, extracted from `app/my-codes.tsx` unchanged (planning #807).
 *
 * Each company handover is a real order now, so it renders as a receipt with its vouchers inside
 * it, exactly like the owner sees their fulfilled orders. Only fuel predating issuance orders (or
 * arriving another way) stays a flat list below. The screen owns the sorting/filtering; this is
 * the presentation and its two section headers.
 */
export function WorkerIssuanceSection({
  vouchers,
  controls,
  onControlsChange,
  issuanceOrders,
  looseIssued,
  expandedOrders,
  onToggle,
  onVoucherPress,
  userId,
  pulseAnim,
  onSelectVoucher,
}: WorkerIssuanceSectionProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const styles = useMyCodesStyles(tokens);
  return (
    <View style={{ gap: tokens.spacing.md }}>
      <VoucherListControlsBar
        vouchers={vouchers}
        controls={controls}
        onChange={onControlsChange}
        countLabel={t('codes.stock.issuedToYou')}
      />

      {issuanceOrders.length > 0 && (
        <>
          <View style={styles.sectionHeader}>
            <Fuel size={14} color={tokens.colors.accent} />
            <View
              style={{
                flex: 1,
                height: 1,
                backgroundColor: `${tokens.colors.accent}1A`,
                marginHorizontal: tokens.spacing.sm,
              }}
            />
            <Text
              allowFontScaling={false}
              style={[styles.sectionLabel, { color: tokens.colors.accent, marginBottom: 0 }]}
            >
              {t('codes.stock.issuedReceipts')} · {issuanceOrders.length}
            </Text>
          </View>
          {issuanceOrders.map((order) => (
            <OrderCard
              key={order.id}
              order={order}
              isExpanded={expandedOrders.has(order.id)}
              onToggle={onToggle}
              onVoucherPress={onVoucherPress}
              brandColor={brandColorFor(order.brand, tokens)}
            />
          ))}
        </>
      )}

      {looseIssued.length > 0 && (
        <>
          {issuanceOrders.length > 0 && (
            <View style={styles.sectionHeader}>
              <Fuel size={14} color={tokens.colors.accent} />
              <View
                style={{
                  flex: 1,
                  height: 1,
                  backgroundColor: `${tokens.colors.accent}1A`,
                  marginHorizontal: tokens.spacing.sm,
                }}
              />
              <Text
                allowFontScaling={false}
                style={[styles.sectionLabel, { color: tokens.colors.accent, marginBottom: 0 }]}
              >
                {t('codes.stock.issuedToYou')} · {looseIssued.length}
              </Text>
            </View>
          )}
          {looseIssued.map((voucher) => (
            <VoucherCard
              key={voucher.id}
              voucher={voucher}
              userId={userId}
              pulseAnim={pulseAnim}
              onSelect={onSelectVoucher}
            />
          ))}
        </>
      )}
    </View>
  );
}
