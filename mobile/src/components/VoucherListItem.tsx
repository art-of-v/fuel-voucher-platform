import { View, StyleSheet } from 'react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { Voucher } from '../core/types/api';
import { Card } from '../core/ui';
import { Text } from '../core/ui';
import { useI18n } from '../core/i18n';
import { ChevronRight, TriangleAlert } from 'lucide-react-native';
import { formatExpirationDate } from '../core/utils/formatters';
import { VoucherBadge } from './VoucherBadge';
import { classifyVoucher } from '../core/types/api';

export interface VoucherListItemProps {
  voucher: Voucher;
  /** Brand hue for this voucher, resolved by the parent. */
  brandColor: string;
  onPress: (voucher: Voucher) => void;
  currentUserId?: string | null;
}

/**
 * The wallet's voucher representation.
 *
 * One row, three columns: identity (provider + fuel + ID), amount, and a
 * status block. The amount is the dominant number — it is what the user
 * looks for when they are about to drive to the pump. The status block is
 * the only coloured element so a usable voucher stands out from
 * used/expired ones without the screen feeling like a list of traffic
 * lights.
 *
 * On the shared `Card` primitive. Replacing three competing implementations
 * (the standalone `VoucherCard` next to `OrderCard`, the inline card in
 * `my-codes.tsx`, and the detail-modal's open-on-tap variant) means there
 * is exactly one place that decides how a voucher looks in the wallet.
 */
export function VoucherListItem({ voucher, brandColor, onPress, currentUserId }: VoucherListItemProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const c = tokens.colors;

  const isUsed = voucher.status === 'used';
  const isExpired =
    !!voucher.expirationDate &&
    new Date(voucher.expirationDate).getTime() < Date.now();
  const kind = classifyVoucher(voucher, currentUserId);
  const isBlocked = kind === 'blocked';
  const expDays = voucher.expirationDate
    ? Math.ceil((new Date(voucher.expirationDate).getTime() - Date.now()) / 86400000)
    : null;
  const isExpiringSoon = expDays !== null && expDays <= 30 && expDays >= 0;
  const isInert = isUsed || isExpired || isBlocked;

  let accent: 'success' | 'warning' | 'danger' | 'neutral' | undefined;
  if (isBlocked) accent = 'danger';
  else if (isUsed) accent = 'neutral';
  else if (isExpired) accent = 'danger';
  else if (isExpiringSoon) accent = 'warning';
  else accent = 'success';

  const stateLabel = isBlocked
    ? t('voucher.badge.blocked')
    : isUsed
      ? t('codes.used')
      : isExpired
        ? t('voucher.status.expired')
        : isExpiringSoon
          ? t('redemption.expiringSoon')
          : t('redemption.ready');

  return (
    <Card
      onPress={() => onPress(voucher)}
      accent={accent}
      padding="md"
      accessibilityLabel={`${voucher.provider} ${voucher.amount} ${voucher.unit || 'L'} ${voucher.fuelName || voucher.fuelType}, ${stateLabel}`}
    >
      <View style={styles.row}>
        <View style={styles.identity}>
          <Text
            role="caption"
            tone="muted"
            style={{ textTransform: 'uppercase', letterSpacing: 1 }}
            numberOfLines={1}
          >
            {voucher.provider}
          </Text>
          <Text role="bodyStrong" numberOfLines={1}>
            {voucher.fuelName || voucher.fuelType}
          </Text>
          <Text role="caption" tone="muted" numberOfLines={1}>
            {voucher.externalId || ''}
          </Text>
        </View>

        <View style={styles.amount}>
          <Text role="numericLarge" tone={isInert ? 'muted' : 'primary'}>
            {voucher.amount}
          </Text>
          <Text role="caption" tone="muted">
            {voucher.unit || 'L'}
          </Text>
        </View>

        <View style={styles.status}>
          <View style={styles.stateRow}>
            {isExpiringSoon && !isUsed && !isExpired && !isBlocked ? (
              <TriangleAlert size={12} color={c.status.warning.base} />
            ) : null}
            <Text
              role="label"
              tone={isInert ? 'muted' : 'accent'}
              style={!isInert ? { color: brandColor } : undefined}
            >
              {stateLabel}
            </Text>
          </View>
          {kind !== 'personal' && !isBlocked ? <VoucherBadge kind={kind} /> : null}
          {voucher.expirationDate ? (
            <Text
              role="caption"
              tone="muted"
              numberOfLines={1}
              style={
                isExpiringSoon && !isUsed && !isExpired && !isBlocked
                  ? { color: c.status.warning.base }
                  : undefined
              }
            >
              {formatExpirationDate(voucher.expirationDate)}
            </Text>
          ) : null}
          <ChevronRight size={14} color={c.text.disabled} />
        </View>
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
  },
  identity: {
    flex: 1.2,
    gap: 2,
    minWidth: 0,
  },
  amount: {
    flex: 0.8,
    alignItems: 'flex-end',
    justifyContent: 'center',
  },
  status: {
    flex: 1.1,
    alignItems: 'flex-end',
    gap: 4,
  },
  stateRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
  },
});