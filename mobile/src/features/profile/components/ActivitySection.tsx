import React from 'react';
import { Linking, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Bell, FileText, Mail, PiggyBank } from 'lucide-react-native';

import { Badge, Card, ListItem, SectionHeader } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';

/**
 * "Activity & Invitations" — notifications, savings, and the invitation row,
 * which only appears when an invitation is actually pending.
 *
 * The privacy-policy row is here rather than in the account-actions section
 * because App Review Guideline 5.1.1 requires it to be reachable from inside the
 * app, not only from App Store metadata — it is a compliance obligation, not a
 * preference, so it should not be easy to drop in a redesign.
 */
export interface ActivitySectionProps {
  unreadNotifications: number;
  pendingInvitationCount: number;
}

export function ActivitySection({
  unreadNotifications,
  pendingInvitationCount,
}: ActivitySectionProps) {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { t } = useI18n();

  return (
    <View style={{ gap: tokens.spacing.xs }}>
      <SectionHeader title={t('profile.activitySection')} />

      <Card padding="none" style={{ backgroundColor: tokens.colors.surface }}>
        <ListItem
          leading={<Bell size={20} color={tokens.colors.text.muted} />}
          title={t('notifications.title')}
          trailing={
            unreadNotifications > 0 ? (
              <Badge status="primary" emphasis="solid" label={String(unreadNotifications)} />
            ) : undefined
          }
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            router.push('/notifications');
          }}
          showChevron
          divider
        />

        <ListItem
          leading={<PiggyBank size={20} color={tokens.colors.text.muted} />}
          title={t('profile.savings')}
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            router.push('/savings');
          }}
          showChevron
          divider={pendingInvitationCount > 0}
        />

        {pendingInvitationCount > 0 && (
          <ListItem
            leading={<Mail size={20} color={tokens.colors.text.muted} />}
            title={t('company.invitationsTitle')}
            trailing={
              <Badge status="primary" emphasis="solid" label={String(pendingInvitationCount)} />
            }
            onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
              router.push('/invitations');
            }}
            showChevron
            divider
          />
        )}

        {/* App Review Guideline 5.1.1: the privacy policy must be reachable
            from inside the app, not only from App Store metadata. */}
        <ListItem
          leading={<FileText size={20} color={tokens.colors.text.muted} />}
          title={t('profile.privacyPolicy')}
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            void Linking.openURL('https://palne.shop/privacy/');
          }}
          showChevron
        />
      </Card>
    </View>
  );
}
