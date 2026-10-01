import { useEffect, useState } from 'react';
import { useRouter } from 'expo-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from '../../../core/api/apiClient';
import { logout as apiLogout } from '../../../core/api/logout';
import { getLegalProfile, updateLegalProfile } from '../api/updateLegalProfile';
import { getMyLegalEntities, updateLegalEntity } from '../../company/api/legalEntityApi';
import { resolveCurrentCompany } from '../../company/lib/context';
import { updateUserProfile } from '../api/updateProfile';
import { getMyInvitations } from '../../company/api/companyApi';
import { useAuth } from '../../auth/hooks/useAuth';
import { useI18n } from '../../../core/i18n';
import { useToastStore } from '../../../core/feedback/toastStore';
import { useStore } from '../../../core/state/appStore';
import { Haptics } from '../../../core/utils/haptics';
import { reportError } from '../../../core/observability/sentry';

export interface PersonalProfileForm {
  firstName: string;
  lastName: string;
  birthdate: string;
}

export interface CompanyProfileForm {
  name: string;
  edrpou: string;
  vatNumber: string;
  directorName: string;
  address: string;
  phone: string;
  email: string;
}

/**
 * Data layer for the profile screen: the legal-profile and pending-invitation
 * queries, the personal/company update mutations, and the logout + account-delete
 * flows. Form state, bottom-sheet visibility, and the date picker stay in the
 * screen; the three points where a completed mutation must close a sheet or dialog
 * are bridged through callbacks.
 */
export function useProfile(callbacks?: {
  onProfileUpdated?: () => void;
  onCompanyUpdated?: () => void;
  onDeleteError?: () => void;
}) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const { logout } = useStore();
  const currentLegalEntityId = useStore((s) => s.currentLegalEntityId);
  const { user, isAuthenticated, isLoading } = useAuth();
  const showToast = useToastStore((s) => s.show);

  const [isDeleting, setIsDeleting] = useState(false);

  const legalProfileQuery = useQuery({
    queryKey: ['legal-profile'],
    queryFn: getLegalProfile,
    enabled: isAuthenticated,
  });
  const legalProfile = legalProfileQuery.data;

  // All legal entities the user owns (0..N). Drives the context switcher and,
  // together with the active context, which company the profile screen shows.
  const companiesQuery = useQuery({
    queryKey: ['legal-entities', 'mine'],
    queryFn: getMyLegalEntities,
    enabled: isAuthenticated,
  });
  const companies = companiesQuery.data ?? [];

  // Active context (multi-company epic #103, S1). `null` = personal root, which
  // is the default on a fresh open — a legal entity existing no longer locks the
  // profile into "business". A stale/foreign id (persisted from a deleted company
  // or another account) resolves to `null`, safely falling back to personal.
  const currentCompany = resolveCurrentCompany(currentLegalEntityId, companies);

  // Business iff a company context is active — no longer sticky on userType.
  const isBusiness = currentCompany != null;

  const myInvitationsQuery = useQuery({
    queryKey: ['company', 'my-invitations'],
    queryFn: getMyInvitations,
    enabled: isAuthenticated,
  });
  const pendingInvitationCount = myInvitationsQuery.data?.length ?? 0;

  useEffect(() => {
    if (!isLoading && !isAuthenticated) {
      router.replace('/landing');
    }
  }, [isLoading, isAuthenticated, router]);

  const updateProfileMutation = useMutation({
    mutationFn: async (data: PersonalProfileForm) => updateUserProfile(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['/api/auth/user/me'] });
      callbacks?.onProfileUpdated?.();
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      showToast({ kind: 'success', message: t('common.saved') });
    },
    onError: (err: any) => {
      // Unexpected failure — forward the raw cause to Sentry, show localized copy only.
      if ((err?.status ?? 500) >= 500) {
        reportError(err);
      }
      showToast({ kind: 'danger', message: t('common.error') });
    },
  });

  const updateCompanyMutation = useMutation({
    mutationFn: async (data: CompanyProfileForm) => {
      // Scope the edit to the active company (epic #103 S3a): with a company
      // context, PUT the chosen entity by id so companies 2..N are editable;
      // falling back to the legacy upsert only for the personal/default context
      // (which edits the owner's first entity, matching pre-S3a behaviour).
      if (currentCompany != null) {
        return updateLegalEntity(currentCompany.id, data);
      }
      return updateLegalProfile(data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['/api/auth/user/me'] });
      queryClient.invalidateQueries({ queryKey: ['legal-profile'] });
      queryClient.invalidateQueries({ queryKey: ['legal-entities', 'mine'] });
      callbacks?.onCompanyUpdated?.();
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      showToast({ kind: 'success', message: t('common.saved') });
    },
    onError: (err: any) => {
      // Unexpected failure — forward the raw cause to Sentry, show localized copy only.
      if ((err?.status ?? 500) >= 500) {
        reportError(err);
      }
      showToast({ kind: 'danger', message: t('common.error') });
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
        // Keep the raw server body in the logs; show the user localized copy only.
        console.error('Delete account failed:', res.status, errBody);
        showToast({
          kind: 'danger',
          message: t('profile.deleteAccountError'),
        });
        setIsDeleting(false);
        callbacks?.onDeleteError?.();
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
        message: t('profile.deleteAccountError'),
      });
      setIsDeleting(false);
      callbacks?.onDeleteError?.();
    }
  };

  return {
    // auth (re-exposed so the screen does not call useAuth twice)
    user,
    isAuthenticated,
    isLoading,
    // data
    legalProfile,
    isBusiness,
    companies,
    currentCompany,
    currentLegalEntityId,
    pendingInvitationCount,
    // actions
    updateProfile: (data: PersonalProfileForm) => updateProfileMutation.mutate(data),
    updateCompany: (data: CompanyProfileForm) => updateCompanyMutation.mutate(data),
    logout: handleLogout,
    deleteAccount: handleDeleteAccount,
    // pending flags
    isUpdatingProfile: updateProfileMutation.isPending,
    isUpdatingCompany: updateCompanyMutation.isPending,
    isDeleting,
  };
}
