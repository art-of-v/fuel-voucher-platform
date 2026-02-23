import { Stack, useRouter, usePathname } from 'expo-router';
import { View } from 'react-native';
import '../global.css';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StatusBar } from 'expo-status-bar';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { BottomTabs } from '../src/components/bottom-tabs';
import { useFonts, Inter_400Regular, Inter_700Bold, Inter_900Black } from '@expo-google-fonts/inter';
import { Rajdhani_400Regular, Rajdhani_600SemiBold, Rajdhani_700Bold } from '@expo-google-fonts/rajdhani';
import * as SplashScreen from 'expo-splash-screen';
import { useEffect } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { useStore } from '../src/lib/store';
import { useAuth } from '../src/hooks/useAuth';
import { getTokens } from '../src/lib/design-tokens';

SplashScreen.preventAutoHideAsync();

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

    useEffect(() => {
        if (loaded || error) {
            SplashScreen.hideAsync();
        }
    }, [loaded, error]);

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
