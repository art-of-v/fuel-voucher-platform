import React from 'react';
import { View } from 'react-native';
import { User, Building2, Briefcase, Shield } from 'lucide-react-native';

import { Badge, Button, Card, Text } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';

/**
 * The card at the top of the profile: who the user is, which kind of account
 * they hold, and the one button that opens the matching edit sheet.
 *
 * Extracted from `app/profile.tsx`, which was one function of ~840 lines. The
 * screen decides *which* sheet opens — personal vs company, because that depends
 * on the same `isBusiness` flag it uses for the badge — so this takes `onEdit`
 * rather than branching itself.
 */
export interface ProfileHeaderCardProps {
  isBusiness: boolean;
  /** Resolved company name, or `'—'` when there is none. */
  companyName: string;
  /** `firstName lastName`, falling back to the screen title when both are empty. */
  fullName: string;
  userPhone: string;
  /** Empty string when the account has no email yet. */
  userEmail: string;
  onEdit: () => void;
}

export function ProfileHeaderCard({
  isBusiness,
  companyName,
  fullName,
  userPhone,
  userEmail,
  onEdit,
}: ProfileHeaderCardProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  return (
    <Card padding="md" style={{ backgroundColor: tokens.colors.surface }}>
      <View style={{ gap: tokens.spacing.md }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.md }}>
          {/* Avatar */}
          <View
            style={{
              width: 56,
              height: 56,
              borderRadius: tokens.radius.md,
              borderWidth: 1.5,
              borderColor: tokens.colors.borderAccent,
              backgroundColor: tokens.colors.surfaceSunken,
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            {isBusiness ? (
              <Building2 size={26} color={tokens.colors.primary} />
            ) : (
              <User size={26} color={tokens.colors.primary} />
            )}
          </View>

          {/* Identity & Status */}
          <View style={{ flex: 1, gap: 2 }}>
            <Text role="title" numberOfLines={1}>
              {isBusiness && companyName !== '—' ? companyName : fullName}
            </Text>

            <Text role="bodyStrong" tone="accent">
              {userPhone}
            </Text>

            {userEmail ? (
              <Text role="secondary" tone="muted" numberOfLines={1}>
                {userEmail}
              </Text>
            ) : null}
          </View>
        </View>

        {/* Read-only account type badge + Edit action */}
        <View
          style={{
            flexDirection: 'row',
            alignItems: 'center',
            justifyContent: 'space-between',
            paddingTop: tokens.spacing.xs,
            borderTopWidth: 1,
            borderTopColor: tokens.colors.borderSubtle,
          }}
        >
          <Badge
            status="primary"
            label={isBusiness ? t('profile.businessClient') : t('profile.individualClient')}
            icon={
              isBusiness ? (
                <Briefcase size={12} color={tokens.colors.primary} />
              ) : (
                <Shield size={12} color={tokens.colors.primary} />
              )
            }
          />

          <Button
            variant="ghost"
            size="sm"
            label={t('profile.edit')}
            onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
              onEdit();
            }}
          />
        </View>
      </View>
    </Card>
  );
}
