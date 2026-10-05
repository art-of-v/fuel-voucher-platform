import { Pressable, Text, View } from 'react-native';
import { Ticket, UserMinus, Users } from 'lucide-react-native';
import type { useCompany } from '../hooks/useCompany';
import type { CompanyMemberDto } from '../types';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { styles } from './styles';
import { formatExpirationDate } from '../../../core/utils/formatters';

type Data = ReturnType<typeof useCompany>;

type WorkerListProps = Pick<Data, 'members' | 'isFiring'> & {
  memberName: (m: CompanyMemberDto) => string;
  confirmFire: (m: CompanyMemberDto) => void;
  openGift: (m: CompanyMemberDto) => void;
};

/** The employees table, with the fire and gift actions per row. */
export function WorkerList(props: WorkerListProps) {
  const { members, isFiring, memberName, confirmFire, openGift } = props;
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
        <Users size={18} color={tokens.colors.primary} />
        <Text style={[styles.sectionTitle, { color: tokens.colors.primary }]}>
          {t('company.members.section')}
        </Text>
      </View>
      {members.length === 0 ? (
        <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>
          {t('company.members.empty')}
        </Text>
      ) : (
        <View style={{ gap: 14 }}>
          {members.map((m) => (
            <View key={m.id} style={[styles.memberRow, { borderColor: tokens.colors.borderLight }]}>
              <View>
                <Text
                  style={{
                    color: tokens.colors.text.primary,
                    fontFamily: 'Rajdhani-Bold',
                    fontSize: 16,
                  }}
                  numberOfLines={1}
                >
                  {memberName(m)}
                </Text>
                <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>
                  {t('company.members.joined', formatExpirationDate(m.joinedAtUtc))} ·{' '}
                  {t('company.members.giftedCount', String(m.giftedVoucherCount ?? 0))}
                </Text>
              </View>
              <View style={{ flexDirection: 'row', gap: 8 }}>
                <Pressable
                  onPress={() => openGift(m)}
                  style={[styles.smallBtn, { borderColor: tokens.colors.primary }]}
                >
                  <Ticket size={14} color={tokens.colors.primary} />
                  <Text
                    style={{
                      color: tokens.colors.primary,
                      fontFamily: 'Inter-Black',
                      fontSize: 11,
                      letterSpacing: 0.8,
                    }}
                  >
                    {t('company.members.gift')}
                  </Text>
                </Pressable>
                <Pressable
                  disabled={isFiring}
                  onPress={() => confirmFire(m)}
                  style={[
                    styles.smallBtn,
                    { borderColor: tokens.colors.error },
                    isFiring && { opacity: 0.5 },
                  ]}
                >
                  <UserMinus size={14} color={tokens.colors.error} />
                  <Text
                    style={{
                      color: tokens.colors.error,
                      fontFamily: 'Inter-Black',
                      fontSize: 11,
                      letterSpacing: 0.8,
                    }}
                  >
                    {t('company.members.fire')}
                  </Text>
                </Pressable>
              </View>
            </View>
          ))}
        </View>
      )}
    </View>
  );
}
