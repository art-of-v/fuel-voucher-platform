import { View, Text, StyleSheet } from 'react-native';
import { useI18n } from '../core/i18n';
import type { VoucherKind } from '../core/types/api';

// Ownership classification chip (Company pool / Gifted to me / Gifted to worker).
// `blocked` and `personal` render nothing here: blocked is surfaced by the
// status pill, and personal vouchers need no extra tag. See spec §8/§5.
export function voucherKindMeta(
  kind: VoucherKind,
): { key: string; color: string } | null {
  switch (kind) {
    case 'gifted_to_me':
      return { key: 'voucher.badge.giftedToMe', color: '#22c55e' };
    case 'gifted_to_worker':
      return { key: 'voucher.badge.giftedToWorker', color: '#a855f7' };
    case 'company_pool':
      return { key: 'voucher.badge.companyPool', color: '#3b82f6' };
    default:
      return null;
  }
}

export function VoucherBadge({ kind }: { kind: VoucherKind }) {
  const { t } = useI18n();
  const meta = voucherKindMeta(kind);
  if (!meta) return null;
  return (
    <View style={[styles.badge, { backgroundColor: `${meta.color}18`, borderColor: `${meta.color}55` }]}>
      <Text allowFontScaling={false} style={[styles.badgeText, { color: meta.color }]}>
        {t(meta.key)}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: {
    alignSelf: 'flex-start',
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
    borderWidth: 1,
  },
  badgeText: {
    fontSize: 9,
    fontFamily: 'Inter-Black',
    letterSpacing: 0.8,
    textTransform: 'uppercase',
  },
});
