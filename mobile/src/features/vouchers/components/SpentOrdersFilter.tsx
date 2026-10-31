import { View } from 'react-native';
import { Eye, EyeOff } from 'lucide-react-native';
import { Chip } from '../../../core/ui';
import { useI18n } from '../../../core/i18n';

type SpentOrdersFilterProps = {
  /** Fully spent orders, counted before the filter is switched on. */
  count: number;
  selected: boolean;
  onToggle: () => void;
};

/**
 * The spent-fuel filter chip, extracted from `app/my-codes.tsx` unchanged (planning #807).
 *
 * One switch covers both levels: it reveals the receipts whose every voucher is spent, and the
 * spent vouchers inside any receipt the wallet already shows. Purely presentational - whether the
 * chip is offered at all is the screen's decision.
 */
export function SpentOrdersFilter({ count, selected, onToggle }: SpentOrdersFilterProps) {
  const { t } = useI18n();
  return (
    <View style={{ flexDirection: 'row' }}>
      <Chip
        testID="spent-orders-toggle"
        label={t('codes.spentFilter')}
        count={count}
        selected={selected}
        icon={selected ? <Eye size={16} /> : <EyeOff size={16} />}
        onPress={onToggle}
      />
    </View>
  );
}
