import { useEffect, useState } from 'react';
import { useRouter } from 'expo-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from '../../../core/api/apiClient';
import { logout as apiLogout } from '../../../core/api/logout';
import { getLegalProfile, updateLegalProfile } from '../api/updateLegalProfile';
import { updateUserProfile } from '../api/updateProfile';
import { getMyInvitations } from '../../company/api/companyApi';
import { useAuth } from '../../auth/hooks/useAuth';
import { useI18n } from '../../../core/i18n';
import { useToastStore } from '../../../core/feedback/toastStore';
import { useStore } from '../../../core/state/appStore';
import { Haptics } from '../../../core/utils/haptics';

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
  const { user, isAuthenticated, isLoading } = useAuth();
  const showToast = useToastStore((s) => s.show);

  const [isDeleting, setIsDeleting] = useState(false);

  const legalProfileQuery = useQuery({
    queryKey: ['legal-profile'],
    queryFn: getLegalProfile,
    enabled: isAuthenticated,
  });
  const legalProfile = legalProfileQuery.data;

  // Account type detection:
  // Mutually exclusive: business if userType is LEGAL_ENTITY or legalProfile exists
  const isBusiness = user?.userType === 'LEGAL_ENTITY' || !!legalProfile;

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
      showToast({ kind: 'danger', message: err.message || t('common.error') });
    },
  });

  const updateCompanyMutation = useMutation({
    mutationFn: async (data: CompanyProfileForm) => {
      return updateLegalProfile(data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['/api/auth/user/me'] });
      queryClient.invalidateQueries({ queryKey: ['legal-profile'] });
      callbacks?.onCompanyUpdated?.();
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
        message: String(err) || t('profile.deleteAccountError'),
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
