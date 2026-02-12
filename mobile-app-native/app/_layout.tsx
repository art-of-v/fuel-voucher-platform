import { Stack } from 'expo-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StatusBar } from 'expo-status-bar';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import '../global.css';

const queryClient = new QueryClient();

export default function RootLayout() {
    return (
        <GestureHandlerRootView style={{ flex: 1 }}>
            <QueryClientProvider client={queryClient}>
                <Stack
                    screenOptions={{
                        headerShown: false,
                        contentStyle: { backgroundColor: '#000000' },
                    }}
                >
                    <Stack.Screen name="index" />
                    <Stack.Screen name="map" />
                    <Stack.Screen name="profile" />
                    <Stack.Screen name="basket" />
                    <Stack.Screen name="packages" />
                    <Stack.Screen name="checkout" />
                    <Stack.Screen name="my-codes" />
                    <Stack.Screen name="success" />
                </Stack>
                <StatusBar style="light" />
            </QueryClientProvider>
        </GestureHandlerRootView>
    );
}
