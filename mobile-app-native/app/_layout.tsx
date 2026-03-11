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
import { sendAppLog } from '../src/lib/api';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { useStore } from '../src/lib/store';
import { useAuth } from '../src/hooks/useAuth';
import { getTokens } from '../src/lib/design-tokens';

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
    const { isAuthenticated: hookAuth, isLoading, isFetching } = useAuth();
    const storeAuth = useStore(state => state.isAuthenticated);
    const logout = useStore(state => state.logout);
    const router = useRouter();
    const pathname = usePathname();

    useEffect(() => {
        // Only run when NOT actively checking auth
        if (!isLoading && !isFetching) {
            // Case: Server definitively says no session, but Store thinks we are auth
            if (!hookAuth && storeAuth) {
                logout();
                if (pathname !== '/landing') {
                    router.replace('/landing');
                }
            }
            // Case: Neither has auth, ensure we are on landing
            else if (!hookAuth && !storeAuth && pathname !== '/landing') {
                router.replace('/landing');
            }
        }
    }, [isLoading, isFetching, hookAuth, storeAuth, pathname]);

    return null;
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
                    </View>
                    <StatusBar style={tokens.colors.isDark ? "light" : "dark"} />
                </QueryClientProvider>
            </GestureHandlerRootView>
        </SafeAreaProvider>
    );
}
