import { useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useStore } from '../../../core/state/appStore';
import { useAuth } from '../../auth/hooks/useAuth';
import { useI18n } from '../../../core/i18n';
import { useToastStore } from '../../../core/feedback/toastStore';
import { Haptics } from '../../../core/utils/haptics';
import { getNotifications, markNotificationRead } from '../api/notificationsApi';
import type { AppNotification } from '../types';

/** One key, shared by the list screen and the unread badge, so a mark-read
 * invalidation refreshes both. */
const NOTIFICATIONS_KEY = ['notifications'] as const;

/** Auth resolves from either source, matching the rest of the app. */
function useIsAuthenticated(): boolean {
  const storeAuth = useStore((s) => s.isAuthenticated);
  const { isAuthenticated: hookAuth } = useAuth();
  return storeAuth || hookAuth;
}

/**
 * The notifications list plus its mutations, for the notifications screen.
 *
 * Mark-read is invalidation-based (not optimistic): the PATCH returns and the
 * list refetches, keeping the unread count the single source of truth.
 */
export function useNotifications() {
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const showToast = useToastStore((s) => s.show);
  const isAuthenticated = useIsAuthenticated();

  const query = useQuery({
    queryKey: NOTIFICATIONS_KEY,
    queryFn: getNotifications,
    enabled: isAuthenticated,
    retry: false,
  });

  const notifications = query.data ?? [];
  const unreadCount = useMemo(
    () => notifications.filter((n) => !n.isRead).length,
    [notifications],
  );

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_KEY });

  const markReadMutation = useMutation({
    mutationFn: (id: string) => markNotificationRead(id),
    onSuccess: invalidate,
  });

  const markAllReadMutation = useMutation({
    mutationFn: async () => {
      const unread = (
        queryClient.getQueryData<AppNotification[]>(NOTIFICATIONS_KEY) ?? []
      ).filter((n) => !n.isRead);
      await Promise.all(unread.map((n) => markNotificationRead(n.id)));
    },
    onSuccess: () => {
      invalidate();
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      showToast({ kind: 'success', message: t('notifications.allRead') });
    },
    onError: () =>
      showToast({ kind: 'danger', message: t('common.error') }),
  });

  return {
    notifications,
    unreadCount,
    isLoading: query.isLoading,
    isError: query.isError,
    isRefetching: query.isRefetching,
    refetch: query.refetch,
    markAsRead: (id: string) => markReadMutation.mutate(id),
    markAllAsRead: () => markAllReadMutation.mutate(),
    isMarkingAll: markAllReadMutation.isPending,
  };
}

/**
 * Just the unread count, for the tab badge. Shares the list's query key (so it
 * costs no extra request when the screen is open) and polls in the background
 * so the badge stays roughly fresh without a manual refresh.
 */
export function useUnreadNotificationCount(): number {
  const isAuthenticated = useIsAuthenticated();
  const { data } = useQuery({
    queryKey: NOTIFICATIONS_KEY,
    queryFn: getNotifications,
    enabled: isAuthenticated,
    retry: false,
    refetchInterval: 60_000,
    select: (list: AppNotification[]) => list.filter((n) => !n.isRead).length,
  });
  return data ?? 0;
}
