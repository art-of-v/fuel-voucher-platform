import { Stack, useRouter, usePathname } from 'expo-router';
import { View, Text, Pressable, ScrollView } from 'react-native';
import '../global.css';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StatusBar } from 'expo-status-bar';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { BottomTabs } from '../src/components/bottom-tabs';
import { useFonts, Inter_400Regular, Inter_700Bold, Inter_900Black } from '@expo-google-fonts/inter';
import { Rajdhani_400Regular, Rajdhani_600SemiBold, Rajdhani_700Bold } from '@expo-google-fonts/rajdhani';
import * as SplashScreen from 'expo-splash-screen';
import { useEffect, useState } from 'react';
import { sendAppLog, apiFetch } from '../src/lib/api';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { useStore } from '../src/lib/store';
import { useAuth } from '../src/hooks/useAuth';
import { getTokens } from '../src/lib/design-tokens';
import * as LocalAuthentication from 'expo-local-authentication';

console.log("[RootLayout] Native start triggered (Entry Point)");
SplashScreen.preventAutoHideAsync();

// Catch unexpected global errors that are NOT caught by the React lifecycle
if (!__DEV__) {
    const defaultHandler = (ErrorUtils as any).getGlobalHandler();
    (ErrorUtils as any).setGlobalHandler((error: any, isFatal: any) => {
        // We can't use state here since it's outside the component, 
        // but we can at least log it to the console or a persistent store if needed.
        console.error('GLOBAL ERROR:', error);
        defaultHandler(error, isFatal);
    });
}

const queryClient = new QueryClient();

function AuthSync() {
    const { isAuthenticated: hookAuth, isLoading, isFetching, isFetched } = useAuth();
    const { isAuthenticated: storeAuth, login, logout } = useStore();
    const router = useRouter();
    const pathname = usePathname();

    useEffect(() => {
        // Only run synchronization when the query has actually finished at least once
        if (!isLoading && isFetched) {
            // Case 1: Silent Sync - Server has session but Store doesn't
            if (hookAuth && !storeAuth) {
                console.log("[AuthSync] Restoring store session from hook");
                login();
            }
            
            // Case 2: Logout Sync - Store has session but Server says No
            // We only do this if we are NOT fetching (to avoid killing session during transitions)
            if (!hookAuth && storeAuth && !isFetching) {
                console.log("[AuthSync] Clearing store session (server rejection)");
                logout();
            }

            // Case 3: Strict Authentication Gate
            // Redirect to landing if no session detected, unless already there
            if (!hookAuth && !storeAuth && pathname !== '/landing' && !isFetching) {
                console.log("[AuthSync] Strict Gate: Redirecting to landing from", pathname);
                router.replace('/landing');
            }
        }
    }, [isLoading, isFetching, isFetched, hookAuth, storeAuth, pathname]);

    return null;
}

function AppLockGuard({ children, tokens }: { children: React.ReactNode, tokens: any }) {
    const { isAuthenticated, isAppUnlocked, unlockApp } = useStore();
    const router = useRouter();
    const pathname = usePathname();
    const [isPrompting, setIsPrompting] = useState(false);

    // Don't lock on landing page
    const isLanding = pathname === '/landing';

    const { refetch } = useAuth();

    useEffect(() => {
        if (isAuthenticated && !isAppUnlocked && !isPrompting && !isLanding) {
            handleBiometric();
        }
    }, [isAuthenticated, isAppUnlocked, isPrompting, isLanding]);

    const handleBiometric = async () => {
        if (isPrompting) return;
        setIsPrompting(true);
        try {
            // 1. Explicit UX Prompt - This fixes the iOS Simulator quirk where disabled Face ID 
            //    silently allows crypto signing without any visible UI.
            const { SecurityService } = require('../src/lib/security.service');
            const { available } = await SecurityService.isBiometricAvailable();
            if (available) {
                const authenticated = await SecurityService.authenticate('Розблокувати FuelFlow');
                if (!authenticated) {
                    setIsPrompting(false);
                    return; // user cancelled logic
                }
            }

            // 2. Server validation
            const resp = await apiFetch("/api/auth/user/me", {
                headers: { 'x-force-signature': 'true' }
            });

            if (resp.ok) {
                unlockApp();
            } else if (resp.status === 401) {
                const { logout: storeLogout } = useStore.getState();
                storeLogout();
            }
        } catch (e: any) {
            console.error("[AppLock] Biometric error:", e);
            if (e.message === 'IDENTITY_MISSING') {
                const { logout: storeLogout } = useStore.getState();
                storeLogout();
                router.replace('/landing');
            }
        } finally {
            setIsPrompting(false);
        }
    };

    if (isAuthenticated && !isAppUnlocked && !isLanding) {
        return (
            <View style={{ flex: 1, backgroundColor: tokens.colors.background, justifyContent: 'center', alignItems: 'center' }}>
                <View style={{ width: 80, height: 80, borderWidth: 1, borderColor: tokens.colors.primary, alignItems: 'center', justifyContent: 'center', marginBottom: 24 }}>
                   <View style={{ width: 40, height: 40, backgroundColor: tokens.colors.primary, opacity: 0.2, position: 'absolute' }} />
                   <Text style={{ color: tokens.colors.primary, fontSize: 32 }}>🔒</Text>
                </View>
                <Text style={{ color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 24, marginBottom: 8, textTransform: 'uppercase' }}>Доступ обмежено</Text>
                <Text style={{ color: tokens.colors.text.dim, fontFamily: 'Inter', fontSize: 14, marginBottom: 32, textAlign: 'center', maxWidth: 200 }}>Будь ласка, підтвердіть особу для входу в додаток</Text>
                
                <Pressable 
                    onPress={handleBiometric}
                    style={{ backgroundColor: tokens.colors.primary, paddingHorizontal: 32, paddingVertical: 16, borderRadius: 2 }}
                >
                    <Text style={{ color: tokens.colors.isDark ? '#000' : '#FFF', fontFamily: 'Inter-Black', fontSize: 14 }}>РОЗБЛОКУВАТИ</Text>
                </Pressable>
            </View>
        );
    }

    return <>{children}</>;
}

export default function RootLayout() {
    const theme = useStore(state => state.theme);
    const tokens = getTokens(theme);

    const [loaded, error] = useFonts({
        'Inter': Inter_400Regular,
        'Inter-Bold': Inter_700Bold,
        'Inter-Black': Inter_900Black,
        'Rajdhani': Rajdhani_400Regular,
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
            sendAppLog('crash', 'Font loading failed', { error: error.message });
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
                        <Text style={{ color: '#ff4444', fontSize: 12 }}>
                            {errorState.message}
                        </Text>
                        <Text style={{ color: '#888', fontSize: 10, marginTop: 10 }}>
                            {errorState.stack}
                        </Text>
                    </ScrollView>
                    <Pressable 
                        onPress={() => setErrorState(null)} 
                        style={{ marginTop: 20, backgroundColor: tokens.colors.primary, padding: 15, borderRadius: 8, alignItems: 'center' }}
                    >
                        <Text style={{ color: '#000', fontWeight: 'bold' }}>RETRY LOAD</Text>
                    </Pressable>
                    <Pressable 
                        onPress={() => sendAppLog('error', 'Manual error report', { message: errorState.message, stack: errorState.stack })} 
                        style={{ marginTop: 12, borderWidth: 1, borderColor: tokens.colors.primary, padding: 15, borderRadius: 8, alignItems: 'center' }}
                    >
                        <Text style={{ color: tokens.colors.primary, fontWeight: '600' }}>SEND LOG TO SERVER</Text>
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
                    <StatusBar style={tokens.colors.isDark ? "light" : "dark"} />
                </QueryClientProvider>
            </GestureHandlerRootView>
        </SafeAreaProvider>
    );
}
