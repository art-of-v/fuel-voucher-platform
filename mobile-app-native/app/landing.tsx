import { useState, useRef } from "react";
import { View, Text, Pressable, StyleSheet, Animated } from "react-native";
import { useRouter, Redirect } from "expo-router";
import { User, Phone } from "lucide-react-native";
import { PhoneAuth } from "@/components/phone-auth";
import { PageLayout } from "@/components/page-layout";
import { useQueryClient } from "@tanstack/react-query";
import { GridBackground } from "@/components/grid-background";
import { useDesignTokens } from "@/lib/design-tokens";
import { useStore } from "@/lib/store";
import { Haptics } from "@/lib/haptics";
import { useAuth } from "@/hooks/useAuth";
import { useI18n } from "@/lib/i18n";

export default function LandingScreen() {
    const router = useRouter();
    const queryClient = useQueryClient();
    const tokens = useDesignTokens();
    const { t } = useI18n();
    const login = useStore(state => state.login);
    const storeAuth = useStore(state => state.isAuthenticated);
    const { isAuthenticated: hookAuth, isLoading } = useAuth();
    const isAuthenticated = storeAuth || hookAuth;
    const [showPhoneAuth, setShowPhoneAuth] = useState(false);
    const authScale = useRef(new Animated.Value(1)).current;

    if (isAuthenticated && !isLoading) {
        return <Redirect href="/" />;
    }

    const btnPressIn = (val: Animated.Value) => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        Animated.spring(val, { toValue: 0.99, useNativeDriver: true, friction: 12, tension: 40 }).start();
    };

    const btnPressOut = (val: Animated.Value) => {
        Animated.spring(val, { toValue: 1, useNativeDriver: true, friction: 12, tension: 100 }).start();
    };

    const handlePhoneAuthSuccess = () => {
        login();
        queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
        router.replace("/");
    };

    if (showPhoneAuth) {
        return (
            <PageLayout background={<GridBackground />}>
                <View style={{ marginTop: 60 }}>
                    <PhoneAuth
                        onSuccess={handlePhoneAuthSuccess}
                        onBack={() => setShowPhoneAuth(false)}
                    />
                </View>
            </PageLayout>
        );
    }

    return (
        <PageLayout background={<GridBackground />} disableScroll={true}>
            <View style={styles.authContainer}>
                <View style={[styles.authIconBox, { borderColor: tokens.colors.primary }]}>
                    <User size={32} color={tokens.colors.primary} />
                </View>

                <View style={styles.authTitleBlock}>
                    <Text allowFontScaling={false} style={[styles.authTitle, { color: tokens.colors.text.primary }]}>{t('profile.accessRequired')}</Text>
                </View>

                <Text allowFontScaling={false} style={[styles.authSubtitle, { color: tokens.colors.text.muted }]}>
                    {t('profile.signInDesc')}
                </Text>

                <Animated.View style={{ transform: [{ scale: authScale }], width: '100%', alignItems: 'center' }}>
                    <Pressable
                        onPressIn={() => btnPressIn(authScale)}
                        onPressOut={() => btnPressOut(authScale)}
                        onPress={() => setShowPhoneAuth(true)}
                        style={[styles.primaryBtn, { backgroundColor: tokens.colors.primary }]}
                    >
                        <Phone size={20} color={tokens.colors.isDark ? "#000" : "#FFF"} />
                        <Text allowFontScaling={false} style={[styles.primaryBtnText, { color: tokens.colors.isDark ? "#000" : "#FFF" }]}>{t('phoneAuth.orPhone')}</Text>
                    </Pressable>
                </Animated.View>

                <Text allowFontScaling={false} style={[styles.authSafetyText, { color: tokens.colors.text.dim }]}>
                    {t('phoneAuth.verificationRequired')}
                </Text>
            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    authContainer: {
        flex: 1,
        alignItems: 'center',
        justifyContent: 'center',
        paddingHorizontal: 20,
    },
    authIconBox: {
        width: 80,
        height: 80,
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
        marginBottom: 24,
        backgroundColor: 'rgba(0,0,0,0.1)',
    },
    authTitleBlock: {
        alignItems: 'center',
        marginBottom: 16,
    },
    authTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 28,
        lineHeight: 32,
        letterSpacing: 2,
        textAlign: 'center',
        textTransform: 'uppercase',
    },
    authSubtitle: {
        fontFamily: 'Inter',
        fontSize: 12,
        lineHeight: 18,
        letterSpacing: 0.5,
        textAlign: 'center',
        marginBottom: 32,
        maxWidth: 240,
    },
    primaryBtn: {
        width: '100%',
        maxWidth: 320,
        height: 56, // Force height
        borderRadius: 2,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
        marginBottom: 12,
    },
    primaryBtnText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        lineHeight: 20, // Explicit line height
        letterSpacing: 0.5,
        textTransform: 'uppercase',
    },
    authSafetyText: {
        fontFamily: 'Inter-Bold',
        fontSize: 8,
        lineHeight: 12, // Explicit line height
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
});
