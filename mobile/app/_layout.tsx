import { Stack, useRouter, usePathname } from 'expo-router';
import { View, Text, Pressable, ScrollView, ActivityIndicator } from 'react-native';
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
  Inter_700Bold,
  Inter_900Black,
} from '@expo-google-fonts/inter';
import {
  Rajdhani_400Regular,
  Rajdhani_600SemiBold,
  Rajdhani_700Bold,
} from '@expo-google-fonts/rajdhani';

import { BottomTabs } from '../src/components/bottom-tabs';
import { GridBackground } from '../src/components/grid-background';
import { GlowText } from '../src/components/glow-text';
import { useStore } from '../src/core/state/appStore';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { apiFetch } from '../src/core/api/apiClient';

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
      const { SecurityService } = require('../src/core/api/securityService');
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
    return (
      <View style={{ flex: 1, backgroundColor: tokens.colors.background }}>
        <GridBackground color={tokens.colors.primary} />
        <View style={{ flex: 1, justifyContent: 'center', alignItems: 'center', padding: 24 }}>
          <View
            style={{
              width: 140,
              height: 140,
              borderWidth: 1,
              borderColor: `${tokens.colors.primary}40`,
              alignItems: 'center',
              justifyContent: 'center',
              marginBottom: 48,
              transform: [{ rotate: '45deg' }],
            }}
          >
            <View style={{ transform: [{ rotate: '-45deg' }], alignItems: 'center' }}>
              <GlowText
                intensity="high"
                style={{
                  fontFamily: 'Rajdhani-Bold',
                  fontSize: 18,
                  letterSpacing: 4,
                  color: tokens.colors.primary,
                }}
              >
                FUEL
              </GlowText>
              <GlowText
                intensity="high"
                style={{
                  fontFamily: 'Rajdhani-Bold',
                  fontSize: 18,
                  letterSpacing: 4,
                  color: tokens.colors.primary,
                }}
              >
                FLOW
              </GlowText>
            </View>
          </View>

          <Text
            style={{
              color: tokens.colors.text.primary,
              fontFamily: 'Rajdhani-Bold',
              fontSize: 28,
              marginBottom: 12,
              textTransform: 'uppercase',
              letterSpacing: 2,
            }}
          >
            Вхід захищено
          </Text>

          <View
            style={{
              height: 2,
              width: 40,
              backgroundColor: tokens.colors.primary,
              marginBottom: 48,
            }}
          />

          {isPrompting ? (
            <View style={{ alignItems: 'center' }}>
              <ActivityIndicator size="large" color={tokens.colors.primary} />
              <Text
                style={{
                  color: tokens.colors.primary,
                  fontFamily: 'Inter',
                  fontSize: 12,
                  marginTop: 16,
                  letterSpacing: 2,
                  opacity: 0.8,
                }}
              >
                ПЕРЕВІРКА...
              </Text>
            </View>
          ) : (
            <Pressable
              onPress={handleBiometric}
              style={({ pressed }) => ({
                backgroundColor: pressed ? `${tokens.colors.primary}cc` : tokens.colors.primary,
                paddingHorizontal: 48,
                paddingVertical: 18,
                borderRadius: 4,
                borderWidth: 1,
                borderColor: tokens.colors.primary,
                shadowColor: tokens.colors.primary,
                shadowOffset: { width: 0, height: 0 },
                shadowOpacity: 0.5,
                shadowRadius: 10,
                elevation: 5,
              })}
            >
              <Text
                style={{
                  color: tokens.colors.isDark ? '#000' : '#FFF',
                  fontFamily: 'Inter-Black',
                  fontSize: 14,
                  letterSpacing: 2,
                }}
              >
                РОЗБЛОКУВАТИ
              </Text>
            </Pressable>
          )}
        </View>
      </View>
    );
  }

  return <>{children}</>;
}

export default function RootLayout() {
  const theme = useStore(state => state.theme);
  const tokens = useDesignTokens();

  const [loaded, error] = useFonts({
    Inter: Inter_400Regular,
    'Inter-Bold': Inter_700Bold,
    'Inter-Black': Inter_900Black,
    Rajdhani: Rajdhani_400Regular,
    'Rajdhani-SemiBold': Rajdhani_600SemiBold,
    'Rajdhani-Bold': Rajdhani_700Bold,
  });

  const [errorState, setErrorState] = useState<Error | null>(null);

  useEffect(() => {
    if (loaded || error) {
      SplashScreen.hideAsync();
    }
    if (error) {
      setErrorState(error);
    }
  }, [loaded, error]);

  if (errorState) {
    return (
      <SafeAreaProvider>
        <View style={{ flex: 1, backgroundColor: tokens.colors.background, padding: 20, justifyContent: 'center' }}>
          <Text style={{ color: tokens.colors.primary, fontSize: 24, fontWeight: 'bold', marginBottom: 20 }}>
            CRITICAL APP ERROR
          </Text>
          <ScrollView style={{ backgroundColor: '#1a1a1a', padding: 10, borderRadius: 8, maxHeight: 300 }}>
            <Text style={{ color: '#ff4444', fontSize: 12 }}>{errorState.message}</Text>
            <Text style={{ color: '#888', fontSize: 10, marginTop: 10 }}>{errorState.stack}</Text>
          </ScrollView>
          <Pressable
            onPress={() => setErrorState(null)}
            style={{
              marginTop: 20,
              backgroundColor: tokens.colors.primary,
              padding: 15,
              borderRadius: 8,
              alignItems: 'center',
            }}
          >
            <Text style={{ color: '#000', fontWeight: 'bold' }}>RETRY LOAD</Text>
          </Pressable>
        </View>
      </SafeAreaProvider>
    );
  }

  if (!loaded && !error) {
    return <View style={{ flex: 1, backgroundColor: tokens.colors.background }} />;
  }

  return (
    <SafeAreaProvider>
      <GestureHandlerRootView style={{ flex: 1 }}>
        <QueryClientProvider client={queryClient}>
          <View style={{ flex: 1, backgroundColor: tokens.colors.background }}>
            <AuthSync />
            <AppLockGuard tokens={tokens}>
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
                <Stack.Screen name="map" />
              </Stack>
              <BottomTabs />
            </AppLockGuard>
          </View>
          <StatusBar style={tokens.colors.isDark ? 'light' : 'dark'} />
        </QueryClientProvider>
      </GestureHandlerRootView>
    </SafeAreaProvider>
  );
}
