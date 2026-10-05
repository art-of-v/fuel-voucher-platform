import { Text, View } from 'react-native';
import { Clock, Ticket, Users } from 'lucide-react-native';
import type { useCompany } from '../hooks/useCompany';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { styles } from './styles';

type Data = ReturnType<typeof useCompany>;

type CompanyStatsRowProps = Pick<Data, 'members' | 'gifted' | 'pendingInvites'>;

/** The three counts across the top: employees, awaiting reply, fuel issued. */
export function CompanyStatsRow(props: CompanyStatsRowProps) {
  const { members, gifted, pendingInvites } = props;
  const tokens = useDesignTokens();
  const { t } = useI18n();
  return (
    <View style={styles.statsRow}>
      <View
        style={[
          styles.statCard,
          { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
        ]}
      >
        <Users size={16} color={tokens.colors.primary} />
        <Text style={[styles.statValue, { color: tokens.colors.text.primary }]}>
          {members.length}
        </Text>
        <Text style={[styles.statLabel, { color: tokens.colors.text.dim }]}>
          {t('company.stats.members')}
        </Text>
      </View>
      <View
        style={[
          styles.statCard,
          { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
        ]}
      >
        <Clock size={16} color={tokens.colors.primary} />
        <Text style={[styles.statValue, { color: tokens.colors.text.primary }]}>
          {pendingInvites.length}
        </Text>
        <Text style={[styles.statLabel, { color: tokens.colors.text.dim }]}>
          {t('company.stats.pending')}
        </Text>
      </View>
      <View
        style={[
          styles.statCard,
          { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
        ]}
      >
        <Ticket size={16} color={tokens.colors.primary} />
        <Text style={[styles.statValue, { color: tokens.colors.text.primary }]}>
          {gifted.length}
        </Text>
        <Text style={[styles.statLabel, { color: tokens.colors.text.dim }]}>
          {t('company.stats.gifted')}
        </Text>
      </View>
    </View>
  );
}
