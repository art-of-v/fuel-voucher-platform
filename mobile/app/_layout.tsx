import { Stack, useRouter, usePathname } from 'expo-router';
import { View, Linking } from 'react-native';
import { Download, Lock } from 'lucide-react-native';
import '../global.css';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StatusBar } from 'expo-status-bar';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import * as SplashScreen from 'expo-splash-screen';
import { useEffect, useRef, useState } from 'react';
import {
  useFonts,
  Inter_400Regular,
  Inter_500Medium,
  Inter_700Bold,
  Inter_900Black,
} from '@expo-google-fonts/inter';
import {
  Rajdhani_400Regular,
  Rajdhani_600SemiBold,
  Rajdhani_700Bold,
} from '@expo-google-fonts/rajdhani';

import { BottomTabs } from '../src/components/bottom-tabs';
import {
  Button,
  ErrorBoundary,
  ErrorState,
  LoadingState,
  Text as UIText,
  ToastHost,
} from '../src/core/ui';
import { useI18n } from '../src/core/i18n';
import { useStore } from '../src/core/state/appStore';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { apiFetch } from '../src/core/api/apiClient';
import { SecurityService } from '../src/core/api/securityService';
import { ensureFreshInstallReset } from '../src/core/api/firstLaunchReset';
import {
  fetchAppVersion,
  isVersionBelow,
  getCurrentAppVersion,
  getStoreUrl,
  type AppVersionInfo,
} from '../src/core/utils/versionCheck';

SplashScreen.preventAutoHideAsync();

const queryClient = new QueryClient();

function AuthSync() {
  const { isAuthenticated: hookAuth, isLoading, isFetching, isFetched, isError } = useAuth();
  const { isAuthenticated: storeAuth, login, logout } = useStore();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (!isLoading && isFetched) {
      if (hookAuth && !storeAuth) {
        login();
      }
      if (!hookAuth && storeAuth && !isFetching) {
        logout();
      }
      if (!hookAuth && !storeAuth && pathname !== '/landing' && !isFetching && !isError) {
        router.replace('/landing');
      }
    }
  }, [isLoading, isFetching, isFetched, isError, hookAuth, storeAuth, pathname]);

  return null;
}

function AppLockGuard({ children, tokens }: { children: React.ReactNode; tokens: any }) {
  const { isAuthenticated, isAppUnlocked, unlockApp } = useStore();
  const { t } = useI18n();
  const router = useRouter();
  const pathname = usePathname();

  const isPromptingRef = useRef(false);
  const [isPrompting, setIsPrompting] = useState(false);
  const isLanding = pathname === '/landing';

  useEffect(() => {
    if (isAuthenticated && !isAppUnlocked && !isLanding) {
      handleBiometric();
    }
  }, [isAuthenticated, isAppUnlocked, isLanding]);

  const handleBiometric = async () => {
    if (isPromptingRef.current) return;
    isPromptingRef.current = true;
    setIsPrompting(true);
    try {
      const hasKeys = await SecurityService.hasKeys();

      if (hasKeys) {
        const resp = await apiFetch('/api/auth/user/me', {
          headers: { 'x-force-signature': 'true' },
        });
        if (resp.ok) {
          unlockApp();
        } else if (resp.status === 401) {
          useStore.getState().logout();
        }
      } else {
        const resp = await apiFetch('/api/auth/user/me');
        if (resp.ok) {
          unlockApp();
        } else {
          useStore.getState().logout();
        }
      }
    } catch (e: any) {
      console.error('[AppLock] Biometric error:', e);
      if (e.message === 'IDENTITY_MISSING') {
        useStore.getState().logout();
        router.replace('/landing');
      }
    } finally {
      isPromptingRef.current = false;
      setIsPrompting(false);
    }
  };

  if (isAuthenticated && !isAppUnlocked && !isLanding) {
    /*
     * The lock screen used to hardcode three Ukrainian strings, so a German or
     * Spanish user met a language they had not chosen at the one point they
     * cannot get past. It also drew its own glowing button with a rotated
     * diamond wordmark and no accessible name.
     *
     * The lockup is kept -- this is the app's front door and brand belongs here
     * -- but it is now upright and unglowed, and the action is the shared Button
     * so its pressed and loading states match every other primary action.
     */
    return (
      <View style={{ flex: 1, backgroundColor: tokens.colors.background }}>
        <View
          style={{
            flex: 1,
            justifyContent: 'center',
            alignItems: 'center',
            paddingHorizontal: tokens.spacing.containerPadding,
            gap: tokens.spacing['3xl'],
          }}
        >
          <View style={{ alignItems: 'center', gap: tokens.spacing.sm }}>
            <UIText role="display" style={{ color: tokens.colors.primary }}>
              FUELFLOW
            </UIText>
            <UIText role="title" center>
              {t('appLock.title')}
            </UIText>
            <UIText role="secondary" tone="muted" center>
              {t('appLock.description')}
            </UIText>
          </View>

          {isPrompting ? (
            <LoadingState message={t('appLock.verifying')} />
          ) : (
            <Button
              label={t('appLock.unlock')}
              onPress={handleBiometric}
              icon={<Lock />}
              hapticStyle="medium"
              fullWidth={false}
            />
          )}
        </View>
      </View>
    );
  }

  return <>{children}</>;
}

export default function RootLayout() {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  const [loaded, error] = useFonts({
    Inter: Inter_400Regular,
    // Registered in Phase 2: `Inter-Medium` was referenced by components
    // (`VoucherBadge`, worker rows) but never loaded, so it silently fell back
    // to the OS system font.
    'Inter-Medium': Inter_500Medium,
    'Inter-Bold': Inter_700Bold,
    'Inter-Black': Inter_900Black,
    Rajdhani: Rajdhani_400Regular,
    'Rajdhani-SemiBold': Rajdhani_600SemiBold,
    'Rajdhani-Bold': Rajdhani_700Bold,
  });

  const [errorState, setErrorState] = useState<Error | null>(null);
  const [updateRequired, setUpdateRequired] = useState<AppVersionInfo | null>(null);
  const [checkingVersion, setCheckingVersion] = useState(true);
  // Gate the app until the fresh-install check has run, so no token is read before a
  // reinstall's stale Keychain session is wiped (see ensureFreshInstallReset).
  const [initializing, setInitializing] = useState(true);

  useEffect(() => {
    if (loaded || error) {
      SplashScreen.hideAsync();
    }
    if (error) {
      setErrorState(error);
    }
  }, [loaded, error]);

  useEffect(() => {
    (async () => {
      try {
        await ensureFreshInstallReset();
      } catch (e) {
        // Never block launch on this; worst case the wipe is retried next launch.
        console.error('[FirstLaunch] reset failed:', e);
      } finally {
        setInitializing(false);
      }
    })();
  }, []);

  useEffect(() => {
    (async () => {
      try {
        const info = await fetchAppVersion();
        if (info && info.minimumVersion) {
          const current = getCurrentAppVersion();
          if (isVersionBelow(current, info.minimumVersion)) {
            setUpdateRequired(info);
          }
        }
      } catch {
      } finally {
        setCheckingVersion(false);
      }
    })();
  }, []);

  if (errorState) {
    // Font loading failed. `ErrorState` keeps the stack behind `__DEV__`; the
    // previous version rendered `errorState.stack` to whoever hit it.
    return (
      <SafeAreaProvider>
        <View style={{ flex: 1, backgroundColor: tokens.colors.background }}>
          <ErrorState
            fullScreen
            onRetry={() => setErrorState(null)}
            detail={[errorState.message, errorState.stack].filter(Boolean).join('\n\n')}
          />
        </View>
      </SafeAreaProvider>
    );
  }

  if (checkingVersion || initializing || (!loaded && !error)) {
    return <View style={{ flex: 1, backgroundColor: tokens.colors.background }} />;
  }

  if (updateRequired) {
    const storeUrl = getStoreUrl(updateRequired);
    /*
     * Forced-update wall. It used to paint itself on a hardcoded `#000` (so the
     * light themes showed dark-theme copy), size its wordmark with
     * `fontWeight: '900'` and no `fontFamily` (so it rendered in the OS system
     * font while Rajdhani sat loaded), fall back to a literal `#888`, and print
     * English only.
     */
    return (
      <SafeAreaProvider>
        <View
          style={{
            flex: 1,
            backgroundColor: tokens.colors.background,
            justifyContent: 'center',
            alignItems: 'center',
            paddingHorizontal: tokens.spacing.containerPadding,
          }}
        >
          <View style={{ alignItems: 'center', maxWidth: 340, gap: tokens.spacing.md }}>
            <UIText role="display" style={{ color: tokens.colors.primary }}>
              FUELFLOW
            </UIText>
            <UIText role="title" center>
              {t('update.title')}
            </UIText>
            <UIText
              role="body"
              tone="secondary"
              center
              style={{ marginBottom: tokens.spacing.lg }}
            >
              {t('update.description')}
            </UIText>
            {storeUrl ? (
              <Button
                label={t('update.action')}
                onPress={() => Linking.openURL(storeUrl)}
                icon={<Download />}
                fullWidth={false}
              />
            ) : (
              <UIText role="caption" tone="muted" center>
                {t('update.manualHint')}
              </UIText>
            )}
          </View>
        </View>
      </SafeAreaProvider>
    );
  }

  return (
    <SafeAreaProvider>
      <GestureHandlerRootView style={{ flex: 1 }}>
        <QueryClientProvider client={queryClient}>
          <View style={{ flex: 1, backgroundColor: tokens.colors.background }}>
            <AuthSync />
            <AppLockGuard tokens={tokens}>
              <ErrorBoundary>
                <Stack
                  screenOptions={{
                    headerShown: false,
                    contentStyle: { backgroundColor: 'transparent' },
                    animation: 'fade',
                  }}
                >
                  <Stack.Screen name="index" />
                  <Stack.Screen name="landing" />
                  <Stack.Screen name="profile" />
                  <Stack.Screen name="basket" />
                  <Stack.Screen name="packages" />
                  <Stack.Screen name="checkout" />
                  <Stack.Screen name="my-codes" />
                  <Stack.Screen name="report" />
                  <Stack.Screen name="map" />
                  <Stack.Screen name="company" />
                  <Stack.Screen name="invitations" />
                </Stack>
                <BottomTabs />
              </ErrorBoundary>
            </AppLockGuard>
            {/*
              Mounted once, above the tab bar and outside the lock guard's
              children so an outcome reported during unlock is still visible.
            */}
            <ToastHost />
          </View>
          <StatusBar style={tokens.colors.isDark ? 'light' : 'dark'} />
        </QueryClientProvider>
      </GestureHandlerRootView>
    </SafeAreaProvider>
  );
}
