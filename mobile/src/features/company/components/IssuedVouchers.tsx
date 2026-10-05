import { Pressable, Text, View } from 'react-native';
import { Ban, RotateCcw, Ticket } from 'lucide-react-native';
import type { useCompany } from '../hooks/useCompany';

import type { Voucher } from '../../../core/types/api';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { styles } from './styles';
import { ownerActionsForVoucher } from '../lib/stock';

type Data = ReturnType<typeof useCompany>;

type IssuedVouchersProps = Pick<Data, 'gifted' | 'members' | 'isBlocking' | 'isRecalling'> & {
  confirmRecall: (v: Voucher) => void;
  confirmBlock: (v: Voucher) => void;
};

/** Fuel already issued to employees, with recall and block per voucher. */
export function IssuedVouchers(props: IssuedVouchersProps) {
  const { gifted, members, isBlocking, isRecalling, confirmRecall, confirmBlock } = props;
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
        <Ticket size={18} color={tokens.colors.primary} />
        <Text style={[styles.sectionTitle, { color: tokens.colors.primary }]}>
          {t('company.recall.section')}
        </Text>
      </View>
      {gifted.length === 0 ? (
        <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>
          {t('company.recall.empty')}
        </Text>
      ) : (
        <View style={{ gap: 12 }}>
          {gifted.map((v) => {
            // Attribution: an issued voucher must always say who holds it. A worker can
            // leave while a voucher is still linked to them — firing only blocks the
            // ones still Assigned — and the row then names a person who is no longer in
            // the roster, which left the owner guessing whose fuel this was.
            const workerName = [v.workerFirstName, v.workerLastName]
              .filter(Boolean)
              .join(' ')
              .trim();
            const isActiveWorker =
              !!v.workerUserId && members.some((m) => m.workerUserId === v.workerUserId);
            const actions = ownerActionsForVoucher(v.status);
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
                  {!isActiveWorker && (
                    <Text
                      style={{
                        color: tokens.colors.warning,
                        fontSize: 11,
                        fontFamily: 'Inter-Medium',
                      }}
                      numberOfLines={1}
                    >
                      {t('company.recall.formerWorker')}
                    </Text>
                  )}
                </View>
                {actions.canFreezeOrRecall ? (
                  <>
                    <Pressable
                      disabled={isBlocking}
                      onPress={() => confirmBlock(v)}
                      style={[
                        styles.smallBtn,
                        { borderColor: tokens.colors.error },
                        isBlocking && { opacity: 0.5 },
                      ]}
                    >
                      <Ban size={14} color={tokens.colors.error} />
                      <Text
                        style={{
                          color: tokens.colors.error,
                          fontFamily: 'Inter-Black',
                          fontSize: 11,
                          letterSpacing: 0.8,
                        }}
                      >
                        {t('company.block.action')}
                      </Text>
                    </Pressable>
                    <Pressable
                      disabled={isRecalling}
                      onPress={() => confirmRecall(v)}
                      style={[
                        styles.smallBtn,
                        { borderColor: tokens.colors.primary },
                        isRecalling && { opacity: 0.5 },
                      ]}
                    >
                      <RotateCcw size={14} color={tokens.colors.primary} />
                      <Text
                        style={{
                          color: tokens.colors.primary,
                          fontFamily: 'Inter-Black',
                          fontSize: 11,
                          letterSpacing: 0.8,
                        }}
                      >
                        {t('company.recall.action')}
                      </Text>
                    </Pressable>
                  </>
                ) : (
                  // Keep the row honest about why there is nothing to press.
                  <Text
                    style={{
                      color: tokens.colors.text.dim,
                      fontSize: 11,
                      fontFamily: 'Inter-Medium',
                    }}
                  >
                    {t('company.recall.spentAction')}
                  </Text>
                )}
              </View>
            );
          })}
        </View>
      )}
    </View>
  );
}
