
import { useEffect } from "react";
import { View, Text, ActivityIndicator, Platform } from "react-native";
import { useRouter } from "expo-router";
import { useAuth } from "@/hooks/use-auth";

interface ProtectedRouteProps {
    children: React.ReactNode;
}

export function ProtectedRoute({ children }: ProtectedRouteProps) {
    const { isAuthenticated, isLoading } = useAuth();
    const router = useRouter();

    useEffect(() => {
        if (!isLoading && !isAuthenticated) {
            router.replace("/profile");
        }
    }, [isAuthenticated, isLoading]);

    if (isLoading) {
        return (
            <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#050505' }}>
                <ActivityIndicator size="large" color="#00FF80" />
                <Text style={{ color: '#00FF80', fontFamily: Platform.OS === 'ios' ? 'Courier' : 'monospace', marginTop: 32, textTransform: 'uppercase', letterSpacing: 4 }}>
                    // SYNCING_PROTOCOL...
                </Text>
            </View>
        );
    }

    if (!isAuthenticated) {
        return (
            <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#7f1d1d' }}>
                <Text style={{ color: 'white', fontWeight: 'bold', fontSize: 18 }}>REDIRECTING TO PROFILE...</Text>
                <Text style={{ color: 'white', fontSize: 12, marginTop: 8 }}>{isLoading ? "Loading..." : "Not Auth"}</Text>
            </View>
        );
    }

    return <>{children}</>;
}
