import { Stack } from 'expo-router';
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

SplashScreen.preventAutoHideAsync();

const queryClient = new QueryClient();

export default function RootLayout() {
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

    const isAuthenticated = useStore(state => state.isAuthenticated);

    if (!loaded && !error) {
        return null;
    }

    return (
        <SafeAreaProvider>
            <GestureHandlerRootView style={{ flex: 1 }}>
                <QueryClientProvider client={queryClient}>
                    <View style={{ flex: 1, backgroundColor: '#000000' }}>
                        <Stack
                            screenOptions={{
                                headerShown: false,
                                contentStyle: { backgroundColor: '#000000' },
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
                            <Stack.Screen name="payment" />
                        </Stack>

                        <BottomTabs />
                    </View>
                    <StatusBar style="light" />
                </QueryClientProvider>
            </GestureHandlerRootView>
        </SafeAreaProvider>
    );
}
