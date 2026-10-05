import { Pressable, Text, View } from 'react-native';
import { Clock, X } from 'lucide-react-native';
import type { useCompany } from '../hooks/useCompany';
import type { CompanyInvitationDto } from '../types';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { styles } from './styles';

function invitationStatusKey(status: string): string {
  switch ((status || '').toLowerCase()) {
    case 'pending':
      return 'company.status.pending';
    case 'accepted':
      return 'company.status.accepted';
    case 'declined':
      return 'company.status.declined';
    case 'cancelled':
    case 'canceled':
      return 'company.status.cancelled';
    default:
      return 'company.status.pending';
  }
}

type Data = ReturnType<typeof useCompany>;

type PendingInvitesProps = Pick<Data, 'pendingInvites' | 'cancelInvite' | 'isCancelling'> & {
  invitationName: (inv: CompanyInvitationDto) => string;
};

/** Invitations sent but not yet answered (planning #155). */
export function PendingInvites(props: PendingInvitesProps) {
  const { pendingInvites, cancelInvite, isCancelling, invitationName } = props;
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
        <Clock size={18} color={tokens.colors.primary} />
        <Text style={[styles.sectionTitle, { color: tokens.colors.primary }]}>
          {t('company.sent.section')}
          {pendingInvites.length > 0 ? ` · ${pendingInvites.length}` : ''}
        </Text>
      </View>
      {pendingInvites.length === 0 ? (
        <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>
          {t('company.sent.empty')}
        </Text>
      ) : (
        <View style={{ gap: 12 }}>
          {pendingInvites.map((inv) => (
            <View key={inv.id} style={[styles.row, { borderColor: tokens.colors.borderLight }]}>
              <View style={{ flex: 1 }}>
                <Text
                  style={{
                    color: tokens.colors.text.primary,
                    fontFamily: 'Rajdhani-Bold',
                    fontSize: 16,
                  }}
                  numberOfLines={1}
                >
                  {invitationName(inv)}
                </Text>
                <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }}>
                  {t(invitationStatusKey(inv.status))}
                </Text>
              </View>
              <Pressable
                disabled={isCancelling}
                onPress={() => cancelInvite(inv.id)}
                style={[
                  styles.smallBtn,
                  { borderColor: tokens.colors.error },
                  isCancelling && { opacity: 0.5 },
                ]}
              >
                <X size={14} color={tokens.colors.error} />
                <Text
                  style={{
                    color: tokens.colors.error,
                    fontFamily: 'Inter-Black',
                    fontSize: 11,
                    letterSpacing: 0.8,
                  }}
                >
                  {t('company.sent.cancel')}
                </Text>
              </Pressable>
            </View>
          ))}
        </View>
      )}
    </View>
  );
}
