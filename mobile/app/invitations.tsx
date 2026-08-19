import { View, Text, Pressable, ScrollView, ActivityIndicator, StyleSheet, Alert } from 'react-native';
import { useRouter, Redirect } from 'expo-router';
import { ChevronLeft, Mail, Check, X, Building2 } from 'lucide-react-native';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getMyInvitations,
  acceptInvitation,
  declineInvitation,
  companyErrorKey,
} from '../src/features/company/api/companyApi';
import type { MyCompanyInvitationDto } from '../src/features/company/types';
import { PageLayout } from '../src/components/page-layout';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { Haptics } from '../src/core/utils/haptics';
import { formatExpirationDate } from '../src/core/utils/formatters';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { useStore } from '../src/core/state/appStore';

export default function InvitationsScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
  const storeAuth = useStore(state => state.isAuthenticated);
  const isAuthenticated = storeAuth || hookAuth;

  const { data: invitations, isLoading } = useQuery({
    queryKey: ['company', 'my-invitations'],
    queryFn: getMyInvitations,
    enabled: isAuthenticated,
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['company', 'my-invitations'] });
  };

  const showError = (err: unknown) => {
    Alert.alert(t('common.error'), t(companyErrorKey(err)));
  };

  const acceptMutation = useMutation({
    mutationFn: (id: string) => acceptInvitation(id),
    onSuccess: () => {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      invalidate();
      Alert.alert(t('company.invitations.acceptedTitle'), t('company.invitations.acceptedDesc'));
    },
    onError: showError,
  });

  const declineMutation = useMutation({
    mutationFn: (id: string) => declineInvitation(id),
    onSuccess: () => {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
      invalidate();
    },
    onError: showError,
  });

  const isBusy = acceptMutation.isPending || declineMutation.isPending;

  const Header = (
    <View style={styles.header}>
      <Pressable onPress={() => router.back()} style={styles.backBtn}>
        <ChevronLeft size={24} color={tokens.colors.primary} />
      </Pressable>
      <Text style={[styles.title, { color: tokens.colors.primary }]}>{t('company.invitationsTitle')}</Text>
      <View style={{ width: 24 }} />
    </View>
  );

  if (!isAuthenticated && !authLoading) {
    return <Redirect href="/landing" />;
  }

  if (isLoading) {
    return (
      <View style={[styles.center, { backgroundColor: tokens.colors.background }]}>
        <ActivityIndicator size="large" color={tokens.colors.primary} />
      </View>
    );
  }

  const list = invitations ?? [];

  const ownerName = (inv: MyCompanyInvitationDto) =>
    [inv.ownerFirstName, inv.ownerLastName].filter(Boolean).join(' ').trim() || inv.ownerPhoneNumber;

  return (
    <PageLayout header={Header}>
      <ScrollView style={{ flex: 1 }} contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 10, paddingBottom: 120 }}>
        {list.length === 0 ? (
          <View style={styles.emptyState}>
            <Mail size={48} color={tokens.colors.borderLight} />
            <Text style={{ color: tokens.colors.text.dim, marginTop: 16, textAlign: 'center' }}>
              {t('company.invitations.empty')}
            </Text>
          </View>
        ) : (
          <View style={{ gap: 16 }}>
            {list.map((inv) => (
              <View
                key={inv.id}
                style={[styles.card, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}
              >
                <View style={styles.cardTop}>
                  <View style={[styles.iconBox, { backgroundColor: `${tokens.colors.primary}18`, borderColor: `${tokens.colors.primary}55` }]}>
                    <Building2 size={20} color={tokens.colors.primary} />
                  </View>
                  <View style={{ flex: 1 }}>
                    <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 18 }} numberOfLines={1}>
                      {inv.legalEntityName}
                    </Text>
                    <Text style={{ color: tokens.colors.text.dim, fontSize: 12 }} numberOfLines={1}>
                      {t('company.invitations.from')}: {ownerName(inv)}
                    </Text>
                    <Text style={{ color: tokens.colors.text.dim, fontSize: 11, marginTop: 2 }}>
                      {formatExpirationDate(inv.createdAtUtc)}
                    </Text>
                  </View>
                </View>

                <View style={styles.actionRow}>
                  <Pressable
                    disabled={isBusy}
                    onPress={() => {
                      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                      declineMutation.mutate(inv.id);
                    }}
                    style={[styles.declineBtn, { borderColor: tokens.colors.borderLight }, isBusy && { opacity: 0.5 }]}
                  >
                    <X size={16} color={tokens.colors.text.muted} />
                    <Text style={{ color: tokens.colors.text.muted, fontFamily: 'Inter-Black', fontSize: 12, letterSpacing: 1 }}>
                      {t('company.invitations.decline')}
                    </Text>
                  </Pressable>
                  <Pressable
                    disabled={isBusy}
                    onPress={() => {
                      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
                      acceptMutation.mutate(inv.id);
                    }}
                    style={[styles.acceptBtn, { backgroundColor: tokens.colors.primary }, isBusy && { opacity: 0.5 }]}
                  >
                    <Check size={16} color={tokens.colors.isDark ? '#000' : '#FFF'} />
                    <Text style={{ color: tokens.colors.isDark ? '#000' : '#FFF', fontFamily: 'Inter-Black', fontSize: 12, letterSpacing: 1 }}>
                      {t('company.invitations.accept')}
                    </Text>
                  </Pressable>
                </View>
              </View>
            ))}
          </View>
        )}
      </ScrollView>
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingBottom: 16,
  },
  backBtn: { padding: 8 },
  title: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 24,
    textTransform: 'uppercase',
  },
  center: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  emptyState: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 80,
  },
  card: {
    padding: 16,
    borderRadius: 12,
    borderWidth: 1,
    gap: 16,
  },
  cardTop: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 14,
  },
  iconBox: {
    width: 44,
    height: 44,
    borderRadius: 12,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  actionRow: {
    flexDirection: 'row',
    gap: 12,
  },
  declineBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 14,
    borderRadius: 10,
    borderWidth: 1,
  },
  acceptBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 14,
    borderRadius: 10,
  },
});
