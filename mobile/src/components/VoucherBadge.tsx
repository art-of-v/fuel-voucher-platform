import { useI18n } from '../core/i18n';
import { Badge, type BadgeStatus } from '../core/ui';
import type { VoucherKind } from '../core/types/api';

// Ownership classification chip (Company pool / Gifted to me / Gifted to worker).
// `blocked` and `personal` render nothing here: blocked is surfaced by the
// status pill, and personal vouchers need no extra tag. See spec §8/§5.
//
// This used to return a raw hex per kind (`#22c55e`, `#a855f7`, `#3b82f6`) and
// paint its own container with `${color}18` / `${color}55`. Ownership is a state,
// so it now names a semantic status and lets `Badge` resolve it per theme.
export function voucherKindMeta(
  kind: VoucherKind,
): { key: string; status: BadgeStatus } | null {
  switch (kind) {
    case 'gifted_to_me':
      return { key: 'voucher.badge.giftedToMe', status: 'success' };
    case 'gifted_to_worker':
      return { key: 'voucher.badge.giftedToWorker', status: 'info' };
    case 'company_pool':
      return { key: 'voucher.badge.companyPool', status: 'primary' };
    default:
      return null;
  }
}

export function VoucherBadge({ kind }: { kind: VoucherKind }) {
  const { t } = useI18n();
  const meta = voucherKindMeta(kind);
  if (!meta) return null;
  return <Badge label={t(meta.key)} status={meta.status} />;
}
