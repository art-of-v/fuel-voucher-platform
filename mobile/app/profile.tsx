/// <reference types="nativewind/types" />
import React, { useState, useEffect } from 'react';
import { ScrollView } from 'react-native';

import { useI18n } from '../src/core/i18n';
import { useProfile } from '../src/features/profile/hooks/useProfile';
import { useUnreadNotificationCount } from '../src/features/notifications/hooks/useNotifications';
import {
  ProfileHeaderCard,
  ManagementSection,
  ActivitySection,
  PreferencesSection,
  AccountActions,
  EditPersonalSheet,
  EditCompanySheet,
  ChangeEmailSheet,
} from '../src/features/profile/components';
import { PageLayout, ScreenHeader, ConfirmDialog, LoadingState } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';

/**
 * Why the two edit sheets take their form state as props instead of owning it.
 *
 * `BottomSheet` renders a React Native `Modal`, and a `Modal` does not mount its
 * children while `visible` is false — verified rather than assumed, since the whole
 * design turns on it. Form state inside either sheet would therefore be discarded
 * on every dismiss, and a user who typed a name, closed the sheet and reopened it
 * would find the field empty.
 *
 * So `personalForm` and `companyForm` live here. The birthdate picker's own
 * open/closed state is the exception: it is always false on open and reset on
 * close, so there is nothing to preserve and it moved into the sheet.
 *
 * `ChangeEmailSheet` differs again — its flow state is *supposed* to start fresh
 * each time, which is why the old `changeEmail.reset()` call is gone rather than
 * relocated: the hook now unmounts with the sheet, so the reset is implicit.
 */
export default function ProfileScreen() {
  const { t } = useI18n();
  const tokens = useDesignTokens();
  const unreadNotifications = useUnreadNotificationCount();

  // Forms state — owned here, see the note above.
  const [personalForm, setPersonalForm] = useState({
    firstName: '',
    lastName: '',
    birthdate: '',
  });

  const [companyForm, setCompanyForm] = useState({
    name: '',
    edrpou: '',
    vatNumber: '',
    directorName: '',
    address: '',
    phone: '',
    email: '',
  });

  // Modal / Sheet visibility
  const [editPersonalVisible, setEditPersonalVisible] = useState(false);
  const [editCompanyVisible, setEditCompanyVisible] = useState(false);
  const [changeEmailVisible, setChangeEmailVisible] = useState(false);
  const [deleteConfirmVisible, setDeleteConfirmVisible] = useState(false);

  const {
    user,
    isAuthenticated,
    isLoading,
    legalProfile,
    isBusiness,
    isWorkerContext,
    currentCompany,
    companies,
    pendingInvitationCount,
    updateProfile,
    updateCompany,
    logout,
    deleteAccount,
    isUpdatingProfile,
    isUpdatingCompany,
    isDeleting,
  } = useProfile({
    onProfileUpdated: () => setEditPersonalVisible(false),
    onCompanyUpdated: () => setEditCompanyVisible(false),
    onDeleteError: () => setDeleteConfirmVisible(false),
  });

  const formatIsoToDisplay = (isoStr?: string) => {
    if (!isoStr) return '';
    const trimmed = isoStr.trim();
    if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
      const [y, m, d] = trimmed.split('-');
      return `${d}.${m}.${y}`;
    }
    return trimmed;
  };

  useEffect(() => {
    if (user) {
      setPersonalForm({
        firstName: user.firstName || '',
        lastName: user.lastName || '',
        birthdate: formatIsoToDisplay(user.birthdate || ''),
      });
    }
  }, [user]);

  useEffect(() => {
    if (legalProfile) {
      setCompanyForm({
        name: legalProfile.name || '',
        edrpou: legalProfile.edrpou || '',
        vatNumber: legalProfile.vatNumber || '',
        directorName: legalProfile.directorName || '',
        address: legalProfile.address || '',
        phone: legalProfile.phone || '',
        email: legalProfile.email || '',
      });
    }
  }, [legalProfile]);

  const Header = <ScreenHeader title={t('profile.title')} hideBack />;

  if (isLoading || !isAuthenticated) {
    return (
      <PageLayout header={Header} scroll={false}>
        <LoadingState fullScreen />
      </PageLayout>
    );
  }

  // Display values
  const fullName =
    [user?.firstName, user?.lastName].filter(Boolean).join(' ') || t('profile.title');
  const userPhone = user?.phone || '+380';
  const companyName = currentCompany?.name || companyForm.name || legalProfile?.name || '—';
  const edrpou = currentCompany?.edrpou || companyForm.edrpou || legalProfile?.edrpou || '—';
  // Label for the context-switcher row: the active company, else the personal root.
  // A worker context says so — the same company name means different rights, and the
  // switcher lists it under "companies I work for" (multi-company epic #103, S5).
  const contextLabel = isWorkerContext
    ? `${currentCompany?.name ?? ''} · ${t('context.workerSubtitle')}`.trim()
    : currentCompany
      ? currentCompany.name
      : t('context.personal');

  // Subtitle summary for the company row.
  //
  // `personalSubtitle` used to sit beside this and was never rendered — dead since
  // the progressive-disclosure rows were replaced by the management section, which
  // shows the account badge in the header instead. Removed rather than carried over.
  const companySubtitle =
    [companyName !== '—' ? companyName : null, edrpou !== '—' ? `ЄДРПОУ ${edrpou}` : null]
      .filter(Boolean)
      .join(' · ') || t('profile.companySection');

  return (
    <PageLayout header={Header} scroll={false} padding="none">
      {/*
        The screen is the composition and the state; each section below is a
        component in `features/profile/components/`. This file used to be one
        function of ~840 lines holding all nine of them, which meant it could not
        be searched, reviewed in one pass, or tested. See the section files for
        what each one owns.
      */}
      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{
          paddingHorizontal: tokens.spacing.containerPadding,
          paddingTop: tokens.spacing.md,
          paddingBottom: tokens.spacing['3xl'] + tokens.chrome.tabBarHeight,
          gap: tokens.spacing.xl,
        }}
      >
        <ProfileHeaderCard
          isBusiness={isBusiness}
          companyName={companyName}
          fullName={fullName}
          userPhone={userPhone}
          userEmail={user?.email || ''}
          onEdit={() => {
            if (isBusiness) {
              setEditCompanyVisible(true);
            } else {
              setEditPersonalVisible(true);
            }
          }}
        />

        <ManagementSection
          isBusiness={isBusiness}
          showRegisterCompany={!isBusiness && companies.length === 0}
          contextLabel={contextLabel}
          companySubtitle={companySubtitle}
          userEmail={user?.email || ''}
          onOpenChangeEmail={() => {
            // No `reset()` here any more: `ChangeEmailSheet` owns the
            // `useChangeEmail` hook, and because a `Modal` unmounts its children
            // when hidden, dismissing the sheet already discards the flow state.
            // Calling reset here would have reached a different hook instance.
            setChangeEmailVisible(true);
          }}
          onOpenCompanyEdit={() => setEditCompanyVisible(true)}
        />

        <ActivitySection
          unreadNotifications={unreadNotifications}
          pendingInvitationCount={pendingInvitationCount}
        />

        <PreferencesSection />

        <AccountActions onSignOut={logout} onDelete={() => setDeleteConfirmVisible(true)} />
      </ScrollView>

      <EditPersonalSheet
        visible={editPersonalVisible}
        form={personalForm}
        onChange={setPersonalForm}
        onClose={() => setEditPersonalVisible(false)}
        onSave={() => updateProfile(personalForm)}
        isSaving={isUpdatingProfile}
      />

      <EditCompanySheet
        visible={editCompanyVisible}
        form={companyForm}
        onChange={setCompanyForm}
        onClose={() => setEditCompanyVisible(false)}
        onSave={() => updateCompany(companyForm)}
        isSaving={isUpdatingCompany}
      />

      <ChangeEmailSheet
        visible={changeEmailVisible}
        currentEmail={user?.email}
        phoneNumber={user?.phone}
        onClose={() => setChangeEmailVisible(false)}
        onConfirmed={() => setChangeEmailVisible(false)}
      />

      {/* ============================================================
          DELETE ACCOUNT CONFIRM DIALOG
          ============================================================ */}
      <ConfirmDialog
        visible={deleteConfirmVisible}
        tone="destructive"
        title={t('profile.deleteAccount')}
        message={t('profile.deleteAccountConfirm')}
        confirmLabel={t('profile.deleteAccount')}
        cancelLabel={t('common.cancel')}
        loading={isDeleting}
        onConfirm={deleteAccount}
        onCancel={() => setDeleteConfirmVisible(false)}
      />
    </PageLayout>
  );
}
