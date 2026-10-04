import React from 'react';
import { View } from 'react-native';
import { useRouter } from 'expo-router';
import { ArrowLeftRight, Building2, FileSignature, Layers, Mail } from 'lucide-react-native';

import { Card, ListItem, SectionHeader } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';

/**
 * "What can I manage?" — the account-context switcher, the email row, and the
 * company rows that only exist for a business account.
 *
 * Rows that only navigate call `router` here rather than taking a callback: where
 * a row goes is a property of the row, and threading five `onPress` props
 * through the screen would make the screen's wiring longer than the section.
 * Rows that need screen state (opening a sheet, resetting the email flow) take a
 * callback, because that genuinely belongs to the screen.
 */
export interface ManagementSectionProps {
  isBusiness: boolean;
  /**
   * True for an individual account with no company yet. That is the only state
   * in which registering a first company is offered — once companies exist,
   * creating and switching live on the contexts screen.
   */
  showRegisterCompany: boolean;
  /** Active company name, or the worker subtitle / personal-root label. */
  contextLabel: string;
  /** `name · ЄДРПОУ …`, or a fallback when nothing is known yet. */
  companySubtitle: string;
  /** Empty string when the account has no email yet. */
  userEmail: string;
  onOpenChangeEmail: () => void;
  onOpenCompanyEdit: () => void;
}

export function ManagementSection({
  isBusiness,
  showRegisterCompany,
  contextLabel,
  companySubtitle,
  userEmail,
  onOpenChangeEmail,
  onOpenCompanyEdit,
}: ManagementSectionProps) {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { t } = useI18n();

  return (
    <View style={{ gap: tokens.spacing.xs }}>
      <SectionHeader
        title={isBusiness ? t('profile.companySection') : t('profile.personalSection')}
      />

      <Card padding="none" style={{ backgroundColor: tokens.colors.surface }}>
        {/* Account context switcher — personal root ↔ owned companies (#103). */}
        <ListItem
          leading={<ArrowLeftRight size={20} color={tokens.colors.primary} />}
          title={t('context.entryTitle')}
          subtitle={contextLabel}
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            router.push('/contexts');
          }}
          showChevron
          divider
        />

        {/* Login email — a step-up-guarded change: a fresh OTP to the current channel is
            required before the new address is staged, then confirmed by an emailed link. */}
        <ListItem
          leading={<Mail size={20} color={tokens.colors.text.muted} />}
          title={t('profile.emailRowTitle')}
          subtitle={userEmail || t('profile.emailAddPrompt')}
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            onOpenChangeEmail();
          }}
          showChevron
          divider
        />

        {/* Individual Client with no company yet: offer first-company registration.
            Once companies exist, creating/switching happens on the contexts screen. */}
        {showRegisterCompany && (
          <ListItem
            leading={<Building2 size={20} color={tokens.colors.primary} />}
            title={t('profile.registerCompany')}
            subtitle={t('profile.registerCompanySubtitle')}
            onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
              onOpenCompanyEdit();
            }}
            showChevron
          />
        )}

        {/* Business Client: Company Details Row */}
        {isBusiness && (
          <>
            <ListItem
              leading={<Building2 size={20} color={tokens.colors.text.muted} />}
              title={t('profile.companyName')}
              subtitle={companySubtitle}
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onOpenCompanyEdit();
              }}
              showChevron
              divider
            />

            <ListItem
              leading={<FileSignature size={20} color={tokens.colors.text.muted} />}
              title={t('profile.documentsTitle')}
              subtitle={t('profile.documentsSubtitle')}
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                router.push('/contracts');
              }}
              showChevron
              divider
            />

            <ListItem
              leading={<Layers size={20} color={tokens.colors.text.muted} />}
              title={t('company.managementTitle')}
              subtitle={t('company.members.section')}
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                router.push('/company');
              }}
              showChevron
            />
          </>
        )}
      </Card>
    </View>
  );
}
