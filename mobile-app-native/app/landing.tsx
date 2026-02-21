import { useState, useRef } from "react";
import { View, Text, Pressable, StyleSheet, Animated } from "react-native";
import { useRouter } from "expo-router";
import { User, Phone } from "lucide-react-native";
import { PhoneAuth } from "@/components/phone-auth";
import { PageLayout } from "@/components/page-layout";
import { useQueryClient } from "@tanstack/react-query";
import { GridBackground } from "@/components/grid-background";
import { tokens } from "@/lib/design-tokens";
import { useStore } from "@/lib/store";
import { Haptics } from "@/lib/haptics";

export default function LandingScreen() {
    const router = useRouter();
    const queryClient = useQueryClient();
    const login = useStore(state => state.login);
    const [showPhoneAuth, setShowPhoneAuth] = useState(false);
    const authScale = useRef(new Animated.Value(1)).current;

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
                <View style={styles.authIconBox}>
                    <User size={32} color={tokens.colors.primary} />
                </View>

                <View style={styles.authTitleBlock}>
                    <Text allowFontScaling={false} style={styles.authTitle}>ПОТРІБНА</Text>
                    <Text allowFontScaling={false} style={styles.authTitle}>АВТОРИЗАЦІЯ</Text>
                </View>

                <Text allowFontScaling={false} style={styles.authSubtitle}>
                    Увійдіть для доступу до системи
                </Text>

                <Animated.View style={{ transform: [{ scale: authScale }], width: '100%', alignItems: 'center' }}>
                    <Pressable
                        onPressIn={() => btnPressIn(authScale)}
                        onPressOut={() => btnPressOut(authScale)}
                        onPress={() => setShowPhoneAuth(true)}
                        style={styles.primaryBtn}
                    >
                        <Phone size={20} color="#000" />
                        <Text allowFontScaling={false} style={styles.primaryBtnText}>ВХІД ЗА ТЕЛЕФОНОМ</Text>
                    </Pressable>
                </Animated.View>

                <Text allowFontScaling={false} style={styles.authSafetyText}>
                    ПОТРІБНЕ СМС ПІДТВЕРДЖЕННЯ
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
        borderColor: tokens.colors.primary,
        alignItems: 'center',
        justifyContent: 'center',
        marginBottom: 24,
        backgroundColor: 'rgba(0,0,0,0.3)',
    },
    authTitleBlock: {
        alignItems: 'center',
        marginBottom: 16,
    },
    authTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 28,
        lineHeight: 32,
        letterSpacing: 2,
        textAlign: 'center',
    },
    authSubtitle: {
        fontFamily: 'Inter',
        color: tokens.colors.text.muted,
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
        backgroundColor: tokens.colors.primary, // Single source of truth
        borderRadius: 2,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
        marginBottom: 12,
    },
    primaryBtnText: {
        fontFamily: 'Inter-Black',
        color: '#000',
        fontSize: 14,
        lineHeight: 20, // Explicit line height
        letterSpacing: 0.5,
        textTransform: 'uppercase',
    },
    authSafetyText: {
        fontFamily: 'Inter-Bold',
        color: 'rgba(255,255,255,0.2)',
        fontSize: 8,
        lineHeight: 12, // Explicit line height
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    cancelBtn: {
        width: '100%',
        alignItems: 'center',
        paddingVertical: 32,
    },
    cancelBtnText: {
        fontFamily: tokens.typography.fonts.bodyBold,
        color: tokens.colors.text.dim,
        fontSize: 10,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
});
