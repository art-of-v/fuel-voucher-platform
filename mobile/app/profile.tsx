/// <reference types="nativewind/types" />
import React, { useState, useEffect } from 'react';
import {
  View,
  ScrollView,
  Platform,
  Keyboard,
  Pressable,
  Linking,
} from 'react-native';
import { useRouter } from 'expo-router';
import {
  User,
  Building2,
  FileSignature,
  FileText,
  TrendingUp,
  Mail,
  LogOut,
  Trash2,
  Calendar,
  Phone,
  Shield,
  Briefcase,
  Layers,
} from 'lucide-react-native';
import DateTimePicker from '@react-native-community/datetimepicker';

import { useI18n, languages } from '../src/core/i18n';
import { useProfile } from '../src/features/profile/hooks/useProfile';
import {
  PageLayout,
  ScreenHeader,
  SectionHeader,
  Card,
  ListItem,
  Badge,
  Button,
  BottomSheet,
  TextField,
  FieldShell,
  Select,
  ConfirmDialog,
  LoadingState,
  Text,
} from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useStore } from '../src/core/state/appStore';
import { themeOptions, ThemeType } from '../src/core/design/themes';
import { Language } from '../src/core/i18n';
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
  const router = useRouter();
  const { t, language, setLanguage } = useI18n();
  const { theme, setTheme } = useStore();
  const tokens = useDesignTokens();

  // Forms state
  const [personalForm, setPersonalForm] = useState({
    firstName: '',
    lastName: '',
    email: '',
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
    pendingInvitationCount,
    emailError,
    setEmailError,
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
        email: user.email || '',
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
  const fullName = [user?.firstName, user?.lastName].filter(Boolean).join(' ') || t('profile.title');
  const userPhone = user?.phone || '+380';
  const companyName = companyForm.name || legalProfile?.name || '—';
  const edrpou = companyForm.edrpou || legalProfile?.edrpou || '—';

  // Subtitle summaries for compact progressive disclosure rows
  const personalSubtitle = [
    fullName !== t('profile.title') ? fullName : null,
    user?.email,
    user?.birthdate,
  ]
    .filter(Boolean)
    .join(' · ') || t('profile.personalInfo');

  const companySubtitle = [
    companyName !== '—' ? companyName : null,
    edrpou !== '—' ? `ЄДРПОУ ${edrpou}` : null,
  ]
    .filter(Boolean)
    .join(' · ') || t('profile.companySection');

  // Language options
  const languageOptions = languages.map((l) => ({
    value: l.code,
    label: l.name,
    leading: <Text role="heading">{l.flag}</Text>,
  }));

  // Theme options
  const themeSelectOptions = themeOptions.map((opt) => ({
    value: opt.id,
    label: t(opt.label),
    leading: (
      <View
        style={{
          width: 14,
          height: 14,
          borderRadius: 7,
          backgroundColor: opt.color,
          borderWidth: 1,
          borderColor: tokens.colors.borderStrong,
        }}
      />
    ),
  }));

  return (
    <PageLayout header={Header} scroll={false} padding="none">
      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{
          paddingHorizontal: tokens.spacing.containerPadding,
          paddingTop: tokens.spacing.md,
          paddingBottom: tokens.spacing['3xl'] + tokens.chrome.tabBarHeight,
          gap: tokens.spacing.xl,
        }}
      >
        {/* ============================================================
            1. PROFILE HEADER: Who am I? What account type?
            ============================================================ */}
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

                {user?.email ? (
                  <Text role="secondary" tone="muted" numberOfLines={1}>
                    {user.email}
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
                  if (isBusiness) {
                    setEditCompanyVisible(true);
                  } else {
                    setEmailError('');
                    setEditPersonalVisible(true);
                  }
                }}
              />
            </View>
          </View>
        </Card>

        {/* ============================================================
            2. MANAGEMENT SECTION: What can I manage?
            ============================================================ */}
        <View style={{ gap: tokens.spacing.xs }}>
          <SectionHeader
            title={isBusiness ? t('profile.companySection') : t('profile.personalSection')}
          />

          <Card padding="none" style={{ backgroundColor: tokens.colors.surface }}>
            {/* Individual Client: Personal Information & Register Company Rows */}
            {!isBusiness && (
              <ListItem
                leading={<Building2 size={20} color={tokens.colors.primary} />}
                  title={t('profile.registerCompany')}
                  subtitle={t('profile.registerCompanySubtitle')}
                  onPress={() => {
                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                    setEditCompanyVisible(true);
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
                    setEditCompanyVisible(true);
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

        {/* ============================================================
            3. ACTIVITY SECTION: Activity & Invitations
            ============================================================ */}
        <View style={{ gap: tokens.spacing.xs }}>
          <SectionHeader title={t('profile.activitySection')} />

          <Card padding="none" style={{ backgroundColor: tokens.colors.surface }}>
            <ListItem
              leading={<TrendingUp size={20} color={tokens.colors.text.muted} />}
              title={t('profile.report')}
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                router.push('/report');
              }}
              showChevron
              divider={pendingInvitationCount > 0}
            />

            {pendingInvitationCount > 0 && (
              <ListItem
                leading={<Mail size={20} color={tokens.colors.text.muted} />}
                title={t('company.invitationsTitle')}
                trailing={
                  <Badge
                    status="primary"
                    emphasis="solid"
                    label={String(pendingInvitationCount)}
                  />
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

        {/* ============================================================
            4. PREFERENCES / SETTINGS SECTION: Language & Theme Pickers
            ============================================================ */}
        <View style={{ gap: tokens.spacing.xs }}>
          <SectionHeader title={t('profile.settingsSection')} />

          <Card padding="md" style={{ backgroundColor: tokens.colors.surface, gap: tokens.spacing.md }}>
            <Select<Language>
              label={t('profile.language')}
              options={languageOptions}
              value={language}
              onChange={(val) => {
                setLanguage(val);
                Haptics.selectionAsync();
              }}
            />

            <Select<ThemeType>
              label={t('profile.theme')}
              options={themeSelectOptions}
              value={theme}
              onChange={(val) => {
                setTheme(val);
                Haptics.selectionAsync();
              }}
            />
          </Card>
        </View>

        {/* ============================================================
            5. ACCOUNT ACTIONS: Secondary & Destructive
            ============================================================ */}
        <View style={{ gap: tokens.spacing.md, paddingTop: tokens.spacing.sm }}>
          <Button
            label={t('profile.signOut')}
            variant="secondary"
            size="md"
            icon={<LogOut size={18} />}
            onPress={logout}
            fullWidth
          />

          <Button
            label={t('profile.deleteAccount')}
            variant="destructive"
            size="md"
            icon={<Trash2 size={18} />}
            onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
              setDeleteConfirmVisible(true);
            }}
            fullWidth
          />
        </View>
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

          <TextField
            label={t('profile.email')}
            value={personalForm.email}
            onChangeText={(text) => {
              setPersonalForm((v) => ({ ...v, email: text }));
              if (emailError) setEmailError('');
            }}
            keyboardType="email-address"
            autoCapitalize="none"
            error={emailError}
          />
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
