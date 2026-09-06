/// <reference types="nativewind/types" />
import React, { useState, useEffect } from 'react';
import {
  View,
  ScrollView,
  Platform,
  Keyboard,
  Modal,
  Pressable,
} from 'react-native';
import { useRouter } from 'expo-router';
import {
  User,
  Building2,
  FileSignature,
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
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import DateTimePicker from '@react-native-community/datetimepicker';
import { z } from 'zod';

import { useI18n, languages } from '../src/core/i18n';
import { apiFetch } from '../src/core/api/apiClient';
import { logout as apiLogout } from '../src/core/api/logout';
import {
  getLegalProfile,
  updateLegalProfile,
} from '../src/features/profile/api/updateLegalProfile';
import { updateUserProfile } from '../src/features/profile/api/updateProfile';
import { getMyInvitations } from '../src/features/company/api/companyApi';
import { useAuth } from '../src/features/auth/hooks/useAuth';
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
  Select,
  ConfirmDialog,
  LoadingState,
  Text,
} from '../src/core/ui';
import { useToastStore } from '../src/core/feedback/toastStore';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useStore } from '../src/core/state/appStore';
import { themeOptions, ThemeType } from '../src/core/design/themes';
import { Language } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';

const emailSchema = z.string().email();

export default function ProfileScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { t, language, setLanguage } = useI18n();
  const { logout, theme, setTheme } = useStore();
  const { user, isAuthenticated, isLoading } = useAuth();
  const tokens = useDesignTokens();
  const showToast = useToastStore((s) => s.show);

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
  const [isDeleting, setIsDeleting] = useState(false);

  // Field validation error states
  const [emailError, setEmailError] = useState('');

  // Date picker state for birthdate
  const [showDatePicker, setShowDatePicker] = useState(false);
  const [tempDate, setTempDate] = useState(new Date());

  // Company profile query
  const { data: legalProfile } = useQuery({
    queryKey: ['legal-profile'],
    queryFn: getLegalProfile,
    enabled: isAuthenticated,
  });

  // Account type detection:
  // Mutually exclusive: business if userType is LEGAL_ENTITY or legalProfile exists
  const isBusiness = user?.userType === 'LEGAL_ENTITY' || !!legalProfile;

  // Pending worker invitations
  const { data: myInvitations } = useQuery({
    queryKey: ['company', 'my-invitations'],
    queryFn: getMyInvitations,
    enabled: isAuthenticated,
  });
  const pendingInvitationCount = myInvitations?.length ?? 0;

  useEffect(() => {
    if (!isLoading && !isAuthenticated) {
      router.replace('/landing');
    }
  }, [isLoading, isAuthenticated, router]);

  useEffect(() => {
    if (user) {
      setPersonalForm({
        firstName: user.firstName || '',
        lastName: user.lastName || '',
        email: user.email || '',
        birthdate: user.birthdate || '',
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
    if (!dateStr) return new Date();
    const cleaned = dateStr.replace(/\D/g, '');
    if (cleaned.length === 8) {
      const d = parseInt(cleaned.substring(0, 2), 10);
      const m = parseInt(cleaned.substring(2, 4), 10);
      const y = parseInt(cleaned.substring(4, 8), 10);
      const dt = new Date(y, m - 1, d);
      if (!isNaN(dt.getTime())) return dt;
    }
    const d = new Date(dateStr);
    return isNaN(d.getTime()) ? new Date() : d;
  };

  // Mutations
  const updateProfileMutation = useMutation({
    mutationFn: async (data: typeof personalForm) => {
      if (data.email) {
        const emailCheck = emailSchema.safeParse(data.email);
        if (!emailCheck.success) {
          setEmailError(t('auth.invalidEmail') || 'Invalid email');
          throw new Error('Validation failed');
        }
      }
      setEmailError('');
      return updateUserProfile(data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['/api/auth/user/me'] });
      setEditPersonalVisible(false);
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      showToast({ kind: 'success', message: t('common.saved') });
    },
    onError: (err: any) => {
      if (err.message !== 'Validation failed') {
        showToast({ kind: 'danger', message: err.message || t('common.error') });
      }
    },
  });

  const updateCompanyMutation = useMutation({
    mutationFn: async (data: typeof companyForm) => {
      return updateLegalProfile(data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['/api/auth/user/me'] });
      queryClient.invalidateQueries({ queryKey: ['legal-profile'] });
      setEditCompanyVisible(false);
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      showToast({ kind: 'success', message: t('common.saved') });
    },
    onError: (err: any) => {
      showToast({ kind: 'danger', message: err.message || t('common.error') });
    },
  });

  const handleLogout = async () => {
    try {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
      await apiLogout();
      logout();
      queryClient.clear();
      router.replace('/');
    } catch (err) {
      console.error('Logout failed:', err);
      logout();
      queryClient.clear();
      router.replace('/');
    }
  };

  const handleDeleteAccount = async () => {
    setIsDeleting(true);
    try {
      const res = await apiFetch('/api/users/me', { method: 'DELETE' });
      if (!res.ok) {
        const errBody = await res.json().catch(() => ({}));
        showToast({
          kind: 'danger',
          message: errBody.message || t('profile.deleteAccountError'),
        });
        setIsDeleting(false);
        setDeleteConfirmVisible(false);
        return;
      }
      await apiLogout();
      logout();
      queryClient.clear();
      router.replace('/');
    } catch (err) {
      console.error('Delete account failed:', err);
      showToast({
        kind: 'danger',
        message: String(err) || t('profile.deleteAccountError'),
      });
      setIsDeleting(false);
      setDeleteConfirmVisible(false);
    }
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
              <>
                <ListItem
                  leading={<User size={20} color={tokens.colors.text.muted} />}
                  title={t('profile.personalInfo')}
                  subtitle={personalSubtitle}
                  onPress={() => {
                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                    setEmailError('');
                    setEditPersonalVisible(true);
                  }}
                  showChevron
                  divider
                />

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
              </>
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
              />
            )}
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
            onPress={handleLogout}
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
        onClose={() => setEditPersonalVisible(false)}
        title={t('profile.editPersonalTitle')}
        footer={
          <Button
            label={t('common.save')}
            variant="primary"
            size="lg"
            fullWidth
            loading={updateProfileMutation.isPending}
            onPress={() => updateProfileMutation.mutate(personalForm)}
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

          <View style={{ gap: tokens.spacing.xs }}>
            <Text role="label" tone="muted">
              {t('profile.birthdate')}
            </Text>
            <Pressable
              onPress={() => {
                Keyboard.dismiss();
                setTempDate(parseSafeDate(personalForm.birthdate));
                setShowDatePicker(true);
              }}
              style={{
                height: tokens.control.md,
                backgroundColor: tokens.colors.surfaceSunken,
                borderRadius: tokens.radius.md,
                borderWidth: 1,
                borderColor: tokens.colors.border,
                paddingHorizontal: tokens.spacing.lg,
                flexDirection: 'row',
                alignItems: 'center',
                justifyContent: 'space-between',
              }}
            >
              <Text role="body" tone={personalForm.birthdate ? 'primary' : 'muted'}>
                {personalForm.birthdate || 'ДД.ММ.РРРР'}
              </Text>
              <Calendar size={18} color={tokens.colors.text.muted} />
            </Pressable>
          </View>
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
            loading={updateCompanyMutation.isPending}
            onPress={() => updateCompanyMutation.mutate(companyForm)}
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
        onConfirm={handleDeleteAccount}
        onCancel={() => setDeleteConfirmVisible(false)}
      />

      {/* ============================================================
          DATE PICKER MODAL (iOS Spinner / Android Native)
          ============================================================ */}
      {Platform.OS === 'ios' ? (
        <Modal visible={showDatePicker} transparent animationType="slide">
          <View
            style={{
              flex: 1,
              justifyContent: 'flex-end',
              backgroundColor: tokens.colors.overlay,
            }}
          >
            <View
              style={{
                backgroundColor: tokens.colors.surfaceElevated,
                borderTopLeftRadius: tokens.radius.xl,
                borderTopRightRadius: tokens.radius.xl,
                paddingBottom: tokens.spacing.xl,
              }}
            >
              <View
                style={{
                  flexDirection: 'row',
                  justifyContent: 'space-between',
                  padding: tokens.spacing.lg,
                  borderBottomWidth: 1,
                  borderColor: tokens.colors.borderSubtle,
                }}
              >
                <Pressable onPress={() => setShowDatePicker(false)} hitSlop={12}>
                  <Text role="bodyStrong" tone="muted">
                    {t('common.cancel')}
                  </Text>
                </Pressable>
                <Pressable
                  onPress={() => {
                    const day = String(tempDate.getDate()).padStart(2, '0');
                    const month = String(tempDate.getMonth() + 1).padStart(2, '0');
                    const year = tempDate.getFullYear();
                    setPersonalForm((v) => ({ ...v, birthdate: `${day}.${month}.${year}` }));
                    setShowDatePicker(false);
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
                textColor={tokens.colors.text.primary}
                onChange={(_, date) => {
                  if (date) setTempDate(date);
                }}
              />
            </View>
          </View>
        </Modal>
      ) : (
        showDatePicker && (
          <DateTimePicker
            value={tempDate}
            mode="date"
            display="default"
            onChange={(_, date) => {
              setShowDatePicker(false);
              if (date) {
                const day = String(date.getDate()).padStart(2, '0');
                const month = String(date.getMonth() + 1).padStart(2, '0');
                const year = date.getFullYear();
                setPersonalForm((v) => ({ ...v, birthdate: `${day}.${month}.${year}` }));
              }
            }}
          />
        )
      )}
    </PageLayout>
  );
}
