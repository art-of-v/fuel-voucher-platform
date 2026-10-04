/// <reference types="nativewind/types" />
import React, { useState, useEffect } from 'react';
import { View, ScrollView, Platform, Keyboard, Pressable } from 'react-native';
import { Calendar } from 'lucide-react-native';
import DateTimePicker from '@react-native-community/datetimepicker';

import { useI18n } from '../src/core/i18n';
import { useProfile } from '../src/features/profile/hooks/useProfile';
import { useChangeEmail } from '../src/features/profile/hooks/useChangeEmail';
import { useUnreadNotificationCount } from '../src/features/notifications/hooks/useNotifications';
import {
  ProfileHeaderCard,
  ManagementSection,
  ActivitySection,
  PreferencesSection,
  AccountActions,
} from '../src/features/profile/components';
import {
  PageLayout,
  ScreenHeader,
  BottomSheet,
  Button,
  TextField,
  FieldShell,
  ConfirmDialog,
  LoadingState,
  Text,
} from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { Haptics } from '../src/core/utils/haptics';

/**
 * Birthdate picker bounds. An empty field anchors on 1990 rather than today,
 * because a spinner that opens on the current date makes the user scroll back
 * three decades before they reach a plausible year of birth.
 */
const BIRTHDATE_ANCHOR = new Date(1990, 0, 1);
const BIRTHDATE_MIN = new Date(1900, 0, 1);
/** Stable so the wheel is not handed a new upper bound on every re-render. */
const BIRTHDATE_MAX = new Date();

/** Date -> the DD.MM.YYYY form the profile form and API adapter both expect. */
const formatDateToDisplay = (date: Date) =>
  [
    String(date.getDate()).padStart(2, '0'),
    String(date.getMonth() + 1).padStart(2, '0'),
    date.getFullYear(),
  ].join('.');

export default function ProfileScreen() {
  const { t, language } = useI18n();
  const tokens = useDesignTokens();
  const unreadNotifications = useUnreadNotificationCount();

  // Forms state
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

  // Date picker state for birthdate
  const [showDatePicker, setShowDatePicker] = useState(false);
  const [tempDate, setTempDate] = useState(BIRTHDATE_ANCHOR);

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
    onProfileUpdated: () => {
      setEditPersonalVisible(false);
      setShowDatePicker(false);
    },
    onCompanyUpdated: () => setEditCompanyVisible(false),
    onDeleteError: () => setDeleteConfirmVisible(false),
  });

  const changeEmail = useChangeEmail({
    currentEmail: user?.email,
    phoneNumber: user?.phone,
    onConfirmed: () => setChangeEmailVisible(false),
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

  const parseSafeDate = (dateStr: string) => {
    if (!dateStr) return BIRTHDATE_ANCHOR;
    const trimmed = dateStr.trim();
    if (trimmed.includes('.')) {
      const parts = trimmed.split('.');
      if (parts.length === 3) {
        const d = parseInt(parts[0], 10);
        const m = parseInt(parts[1], 10);
        const y = parseInt(parts[2], 10);
        if (!isNaN(d) && !isNaN(m) && !isNaN(y)) {
          const dt = new Date(y, m - 1, d);
          if (!isNaN(dt.getTime())) return dt;
        }
      }
    }
    if (trimmed.includes('-')) {
      const parts = trimmed.split('-');
      if (parts.length === 3) {
        const y = parseInt(parts[0], 10);
        const m = parseInt(parts[1], 10);
        const d = parseInt(parts[2], 10);
        if (!isNaN(d) && !isNaN(m) && !isNaN(y)) {
          const dt = new Date(y, m - 1, d);
          if (!isNaN(dt.getTime())) return dt;
        }
      }
    }
    const d = new Date(trimmed);
    return isNaN(d.getTime()) ? BIRTHDATE_ANCHOR : d;
  };

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
            changeEmail.reset();
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

      {/* ============================================================
          EDIT PERSONAL INFORMATION BOTTOM SHEET
          ============================================================ */}
      <BottomSheet
        visible={editPersonalVisible}
        onClose={() => {
          setEditPersonalVisible(false);
          // The picker lives inside this sheet, so it has to close with it —
          // otherwise re-opening the sheet reveals a stale spinner.
          setShowDatePicker(false);
        }}
        title={t('profile.editPersonalTitle')}
        footer={
          <Button
            label={t('common.save')}
            variant="primary"
            size="lg"
            fullWidth
            loading={isUpdatingProfile}
            onPress={() => updateProfile(personalForm)}
          />
        }
      >
        <View style={{ gap: tokens.spacing.lg, paddingBottom: tokens.spacing.lg }}>
          <View style={{ flexDirection: 'row', gap: tokens.spacing.md }}>
            <View style={{ flex: 1 }}>
              <TextField
                label={t('profile.firstName')}
                value={personalForm.firstName}
                onChangeText={(text) => setPersonalForm((v) => ({ ...v, firstName: text }))}
                autoCapitalize="words"
              />
            </View>
            <View style={{ flex: 1 }}>
              <TextField
                label={t('profile.lastName')}
                value={personalForm.lastName}
                onChangeText={(text) => setPersonalForm((v) => ({ ...v, lastName: text }))}
                autoCapitalize="words"
              />
            </View>
          </View>

          <FieldShell
            label={t('profile.birthdate')}
            trailing={<Calendar size={18} color={tokens.colors.text.muted} />}
            onPress={() => {
              Keyboard.dismiss();
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
              if (showDatePicker) {
                setShowDatePicker(false);
                return;
              }
              setTempDate(parseSafeDate(personalForm.birthdate));
              setShowDatePicker(true);
            }}
          >
            <Text
              style={{
                color: personalForm.birthdate
                  ? tokens.colors.text.primary
                  : tokens.colors.text.muted,
                fontSize: 15,
              }}
            >
              {personalForm.birthdate || 'ДД.ММ.РРРР'}
            </Text>
          </FieldShell>

          {/*
            The picker renders inside the sheet, not beside it. BottomSheet is
            itself a Modal, and iOS refuses to present a second Modal over one
            that is already showing — so the old root-level <Modal> never
            appeared and the tap looked dead. Inline reveal has no such limit.
            Android is unaffected either way: its picker is a native dialog
            owned by the Activity, so it opens above the sheet regardless of
            where it sits in the tree.
          */}
          {showDatePicker &&
            (Platform.OS === 'ios' ? (
              <View
                style={{
                  borderRadius: tokens.radius.md,
                  borderWidth: 1,
                  borderColor: tokens.colors.border,
                  backgroundColor: tokens.colors.surfaceSunken,
                  overflow: 'hidden',
                }}
              >
                <View
                  style={{
                    flexDirection: 'row',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    paddingHorizontal: tokens.spacing.lg,
                    paddingVertical: tokens.spacing.md,
                    borderBottomWidth: 1,
                    borderBottomColor: tokens.colors.borderSubtle,
                  }}
                >
                  <Pressable onPress={() => setShowDatePicker(false)} hitSlop={12}>
                    <Text role="bodyStrong" tone="muted">
                      {t('common.cancel')}
                    </Text>
                  </Pressable>
                  <Pressable
                    onPress={() => {
                      setPersonalForm((v) => ({
                        ...v,
                        birthdate: formatDateToDisplay(tempDate),
                      }));
                      setShowDatePicker(false);
                      Haptics.selectionAsync();
                    }}
                    hitSlop={12}
                  >
                    <Text role="bodyStrong" tone="accent">
                      {t('common.done')}
                    </Text>
                  </Pressable>
                </View>
                <DateTimePicker
                  value={tempDate}
                  mode="date"
                  display="spinner"
                  locale={language}
                  minimumDate={BIRTHDATE_MIN}
                  maximumDate={BIRTHDATE_MAX}
                  textColor={tokens.colors.text.primary}
                  onChange={(_, date) => {
                    if (date) setTempDate(date);
                  }}
                />
              </View>
            ) : (
              <DateTimePicker
                value={tempDate}
                mode="date"
                display="default"
                minimumDate={BIRTHDATE_MIN}
                maximumDate={BIRTHDATE_MAX}
                onChange={(event, date) => {
                  setShowDatePicker(false);
                  if (event.type === 'dismissed' || !date) return;
                  setPersonalForm((v) => ({
                    ...v,
                    birthdate: formatDateToDisplay(date),
                  }));
                  Haptics.selectionAsync();
                }}
              />
            ))}
        </View>
      </BottomSheet>

      {/* ============================================================
          EDIT COMPANY INFORMATION BOTTOM SHEET
          ============================================================ */}
      <BottomSheet
        visible={editCompanyVisible}
        onClose={() => setEditCompanyVisible(false)}
        title={t('profile.editCompanyTitle')}
        footer={
          <Button
            label={t('common.save')}
            variant="primary"
            size="lg"
            fullWidth
            loading={isUpdatingCompany}
            onPress={() => updateCompany(companyForm)}
          />
        }
      >
        <View style={{ gap: tokens.spacing.lg, paddingBottom: tokens.spacing.lg }}>
          <TextField
            label={t('profile.companyName')}
            value={companyForm.name}
            onChangeText={(text) => setCompanyForm((v) => ({ ...v, name: text }))}
          />

          <View style={{ flexDirection: 'row', gap: tokens.spacing.md }}>
            <View style={{ flex: 1 }}>
              <TextField
                label={t('profile.edrpou')}
                value={companyForm.edrpou}
                onChangeText={(text) => setCompanyForm((v) => ({ ...v, edrpou: text }))}
                keyboardType="numeric"
              />
            </View>
            <View style={{ flex: 1 }}>
              <TextField
                label={t('profile.vatNumber')}
                value={companyForm.vatNumber}
                onChangeText={(text) => setCompanyForm((v) => ({ ...v, vatNumber: text }))}
                keyboardType="numeric"
              />
            </View>
          </View>

          <TextField
            label={t('profile.directorName')}
            value={companyForm.directorName}
            onChangeText={(text) => setCompanyForm((v) => ({ ...v, directorName: text }))}
          />

          <TextField
            label={t('profile.companyAddress')}
            value={companyForm.address}
            onChangeText={(text) => setCompanyForm((v) => ({ ...v, address: text }))}
          />
        </View>
      </BottomSheet>

      {/* ============================================================
          CHANGE EMAIL BOTTOM SHEET (step-up guarded)
          ============================================================ */}
      <BottomSheet
        visible={changeEmailVisible}
        onClose={() => {
          setChangeEmailVisible(false);
          changeEmail.reset();
        }}
        title={t('profile.changeEmail')}
        footer={
          changeEmail.step === 'email' ? (
            <Button
              label={t('profile.changeEmailSendCode')}
              variant="primary"
              size="lg"
              fullWidth
              loading={changeEmail.isSending}
              onPress={changeEmail.sendCode}
            />
          ) : (
            <Button
              label={t('profile.changeEmailConfirm')}
              variant="primary"
              size="lg"
              fullWidth
              loading={changeEmail.isConfirming}
              onPress={changeEmail.confirm}
            />
          )
        }
      >
        <View style={{ gap: tokens.spacing.lg, paddingBottom: tokens.spacing.lg }}>
          {changeEmail.step === 'email' ? (
            <>
              <Text role="secondary" tone="muted">
                {t('profile.changeEmailStepInfo')}
              </Text>
              <TextField
                label={t('profile.changeEmailNew')}
                value={changeEmail.newEmail}
                onChangeText={(text) => {
                  changeEmail.setNewEmail(text);
                  if (changeEmail.emailError) changeEmail.setEmailError('');
                }}
                keyboardType="email-address"
                autoCapitalize="none"
                autoFocus
                error={changeEmail.emailError || undefined}
              />
            </>
          ) : (
            <>
              <Text role="secondary" tone="muted">
                {t('profile.changeEmailCodeInfo')}
              </Text>
              <TextField
                label={t('phoneAuth.codeLabel')}
                value={changeEmail.code}
                onChangeText={(text) => {
                  changeEmail.setCode(text);
                  if (changeEmail.codeError) changeEmail.setCodeError('');
                }}
                placeholder="000000"
                keyboardType="number-pad"
                maxLength={6}
                codeStyle
                autoFocus
                textContentType="oneTimeCode"
                autoComplete="sms-otp"
                error={changeEmail.codeError || undefined}
              />
              <View
                style={{
                  flexDirection: 'row',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                }}
              >
                <Pressable onPress={changeEmail.backToEmail} hitSlop={8}>
                  <Text role="bodyStrong" tone="muted">
                    {t('profile.changeEmailBack')}
                  </Text>
                </Pressable>
                <Pressable
                  onPress={changeEmail.sendCode}
                  hitSlop={8}
                  disabled={changeEmail.isSending}
                >
                  <Text role="bodyStrong" tone="accent">
                    {t('profile.changeEmailResend')}
                  </Text>
                </Pressable>
              </View>
            </>
          )}
        </View>
      </BottomSheet>

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
