import { Pressable, Text, View } from 'react-native';
import { Ban, Unlock } from 'lucide-react-native';
import type { useCompany } from '../hooks/useCompany';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { styles } from './styles';

type Data = ReturnType<typeof useCompany>;

type BlockedVouchersProps = Pick<Data, 'blocked' | 'unblock' | 'isUnblocking'>;

/** Frozen vouchers, with unfreeze. Only the owner sees this section. */
export function BlockedVouchers(props: BlockedVouchersProps) {
  const { blocked, unblock, isUnblocking } = props;
  const tokens = useDesignTokens();
  const { t } = useI18n();
  return (
    <View
      style={[
        styles.card,
        { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
      ]}
    >
      <View style={styles.sectionHeader}>
        <Ban size={18} color={tokens.colors.error} />
        <Text style={[styles.sectionTitle, { color: tokens.colors.error }]}>
          {t('company.block.section')}
        </Text>
      </View>
      <View style={{ gap: 12 }}>
        {blocked.map((v) => {
          const workerName = [v.workerFirstName, v.workerLastName].filter(Boolean).join(' ').trim();
          return (
            <View key={v.id} style={[styles.row, { borderColor: tokens.colors.borderLight }]}>
              <View style={{ flex: 1 }}>
                <Text
                  style={{
                    color: tokens.colors.text.primary,
                    fontFamily: 'Rajdhani-Bold',
                    fontSize: 16,
                  }}
                  numberOfLines={1}
                >
                  {v.provider} · {v.amount} {v.unit || t('common.liter')}
                </Text>
                <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }} numberOfLines={1}>
                  {v.fuelName || v.fuelType}
                  {workerName ? ` → ${workerName}` : ''}
                </Text>
              </View>
              <Pressable
                disabled={isUnblocking}
                onPress={() => unblock(v.id)}
                style={[
                  styles.smallBtn,
                  { borderColor: tokens.colors.primary },
                  isUnblocking && { opacity: 0.5 },
                ]}
              >
                <Unlock size={14} color={tokens.colors.primary} />
                <Text
                  style={{
                    color: tokens.colors.primary,
                    fontFamily: 'Inter-Black',
                    fontSize: 11,
                    letterSpacing: 0.8,
                  }}
                >
                  {t('company.block.unblockAction')}
                </Text>
              </Pressable>
            </View>
          );
        })}
      </View>
    </View>
  );
}
