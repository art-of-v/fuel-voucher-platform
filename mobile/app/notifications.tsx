import React from 'react';
import { View, RefreshControl } from 'react-native';
import { Redirect } from 'expo-router';
import { Bell } from 'lucide-react-native';
import {
  PageLayout,
  ScreenHeader,
  Card,
  ListItem,
  EmptyState,
  LoadingState,
  ErrorState,
  Button,
  Text,
} from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { useStore } from '../src/core/state/appStore';
import { useNotifications } from '../src/features/notifications/hooks/useNotifications';
import { formatNotificationTime } from '../src/features/notifications/utils/formatNotificationTime';
import { Haptics } from '../src/core/utils/haptics';

export default function NotificationsScreen() {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
  const storeAuth = useStore((s) => s.isAuthenticated);
  const isAuthenticated = storeAuth || hookAuth;

  const {
    notifications,
    unreadCount,
    isLoading,
    isError,
    isRefetching,
    refetch,
    markAsRead,
    markAllAsRead,
    isMarkingAll,
  } = useNotifications();

  const Header = (
    <ScreenHeader
      title={t('notifications.title')}
      actions={
        unreadCount > 0 ? (
          <Button
            variant="ghost"
            size="sm"
            label={t('notifications.markAllRead')}
            loading={isMarkingAll}
            onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
              markAllAsRead();
            }}
          />
        ) : undefined
      }
    />
  );

  // Auth guard mirrors the other secondary screens: redirect once we know the
  // user is unauthenticated, but not while the auth check is still in flight.
  if (!isAuthenticated && !authLoading) {
    return <Redirect href="/landing" />;
  }

  if (isLoading) {
    return (
      <PageLayout header={Header} scroll={false}>
        <LoadingState fullScreen />
      </PageLayout>
    );
  }

  if (isError) {
    return (
      <PageLayout header={Header} scroll={false}>
        <ErrorState fullScreen onRetry={() => refetch()} />
      </PageLayout>
    );
  }

  if (notifications.length === 0) {
    return (
      <PageLayout header={Header} scroll={false}>
        <EmptyState
          icon={<Bell />}
          title={t('notifications.empty')}
          description={t('notifications.emptyHint')}
        />
      </PageLayout>
    );
  }

  return (
    <PageLayout
      header={Header}
      padding="none"
      refreshControl={
        <RefreshControl
          refreshing={isRefetching}
          onRefresh={() => refetch()}
          tintColor={tokens.colors.primary}
          colors={[tokens.colors.primary]}
        />
      }
    >
      <Card
        padding="none"
        style={{
          marginHorizontal: tokens.spacing.containerPadding,
          backgroundColor: tokens.colors.surface,
        }}
      >
        {notifications.map((n, i) => {
          const unread = !n.isRead;
          return (
            <ListItem
              key={n.id}
              leading={
                <Bell
                  size={20}
                  color={unread ? tokens.colors.primary : tokens.colors.text.muted}
                />
              }
              title={n.title}
              subtitle={n.message}
              trailing={
                <View style={{ alignItems: 'flex-end', gap: 4, maxWidth: 76 }}>
                  <Text role="caption" tone="muted" numberOfLines={1}>
                    {formatNotificationTime(n.createdAt, t)}
                  </Text>
                  {unread ? (
                    <View
                      accessibilityLabel={t('notifications.unread')}
                      style={{
                        width: 8,
                        height: 8,
                        borderRadius: 4,
                        backgroundColor: tokens.colors.primary,
                      }}
                    />
                  ) : null}
                </View>
              }
              // Rows carry no navigation target (the backend emits no order id),
              // so a read row is inert; an unread row's only action is to clear.
              onPress={
                unread
                  ? () => {
                      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                      markAsRead(n.id);
                    }
                  : undefined
              }
              divider={i < notifications.length - 1}
            />
          );
        })}
      </Card>
    </PageLayout>
  );
}
