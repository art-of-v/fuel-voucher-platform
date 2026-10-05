import { ActivityIndicator, Pressable, Text, TextInput, View } from 'react-native';
import { Send, UserPlus } from 'lucide-react-native';
import { Haptics } from '../../../core/utils/haptics';
import type { useCompany } from '../hooks/useCompany';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { styles } from './styles';

type Data = ReturnType<typeof useCompany>;

type InviteWorkerFormProps = Pick<Data, 'invite' | 'isInviting'> & {
  UA_DIAL_PREFIX: string;
  phone: string;
  setPhone: (value: string) => void;
};

/**
 * The phone input and send button.
 *
 * The number lives in the screen, not here, because the reset-after-send ordering is
 * the easy thing to get wrong (it is wired through useCompany's onInviteSuccess) and
 * that ordering is worth being able to see at the call site.
 */
export function InviteWorkerForm(props: InviteWorkerFormProps) {
  const { invite, isInviting, UA_DIAL_PREFIX, phone, setPhone } = props;
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
        <UserPlus size={18} color={tokens.colors.primary} />
        <Text style={[styles.sectionTitle, { color: tokens.colors.primary }]}>
          {t('company.invite.section')}
        </Text>
      </View>
      <View style={{ flexDirection: 'row', gap: 10 }}>
        <TextInput
          value={phone}
          onChangeText={setPhone}
          placeholder={t('company.invite.placeholder')}
          placeholderTextColor={tokens.colors.text.dim}
          keyboardType="phone-pad"
          autoCapitalize="none"
          style={[
            styles.input,
            {
              backgroundColor: tokens.colors.background,
              color: tokens.colors.text.primary,
              borderColor: tokens.colors.borderLight,
            },
          ]}
        />
        <Pressable
          disabled={phone.trim().length <= UA_DIAL_PREFIX.length || isInviting}
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
            invite(phone.trim());
          }}
          style={[
            styles.iconBtn,
            { backgroundColor: tokens.colors.primary },
            (phone.trim().length <= UA_DIAL_PREFIX.length || isInviting) && { opacity: 0.4 },
          ]}
        >
          {isInviting ? (
            <ActivityIndicator size="small" color={tokens.colors.text.onPrimary} />
          ) : (
            <Send size={18} color={tokens.colors.text.onPrimary} />
          )}
        </Pressable>
      </View>
    </View>
  );
}
