/// <reference types="nativewind/types" />
import { useState, useEffect, useRef } from "react";
import { View, Text, Pressable, TextInput, ActivityIndicator, Image, StyleSheet, Animated } from "react-native";
import { useRouter } from "expo-router";
import { User, LogOut, Phone, Globe, Save } from "lucide-react-native";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useI18n, languages } from "../src/lib/i18n";
import { PhoneAuth } from "../src/components/phone-auth";
import { apiRequest } from "../src/lib/utils";
import { useAuth } from "../src/hooks/useAuth";
import { PageLayout } from "../src/components/page-layout";
import { GridBackground } from "../src/components/grid-background";
import { tokens } from "../src/lib/design-tokens";
import { useStore } from "../src/lib/store";
import { Haptics } from "../src/lib/haptics";
import AsyncStorage from "@react-native-async-storage/async-storage";

const GLOBAL_PADDING = tokens.spacing.containerPadding;

export default function ProfileScreen() {
    const router = useRouter();
    const queryClient = useQueryClient();
    const { user, isLoading, isAuthenticated } = useAuth();
    const logout = useStore(state => state.logout);
    const { t, language, setLanguage } = useI18n();
    const saveScale = useRef(new Animated.Value(1)).current;

    const btnPressIn = (val: Animated.Value) => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        Animated.spring(val, { toValue: 0.99, useNativeDriver: true, friction: 12, tension: 40 }).start();
    };

    const btnPressOut = (val: Animated.Value) => {
        Animated.spring(val, { toValue: 1, useNativeDriver: true, friction: 12, tension: 100 }).start();
    };

    const [personalForm, setPersonalForm] = useState({
        firstName: "",
        lastName: "",
        email: "",
        birthdate: ""
    });



    useEffect(() => {
        if (user) {
            setPersonalForm({
                firstName: user.firstName || "",
                lastName: user.lastName || "",
                email: user.email || "",
                birthdate: user.birthdate || ""
            });
        });
}
    }, [user]);

const updateProfileMutation = useMutation({
    mutationFn: async (data: any) => {
        const res = await apiRequest("POST", `/api/users/update`, data);
        return res.json();
    },
    onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
    }
});

const handleLogout = async () => {
    try {
        // 1. Clear Zustand Store
        logout();

        // 2. Force reset query cache to ensure clean state on re-login
        queryClient.clear();

        // 3. (Optional) Try to hit server logout to clear cookies
        try {
            await apiRequest("POST", "/api/auth/phone/logout");
        } catch (e) {
            // Ignore failure
        }

        // 5. Hard redirect
        router.replace("/landing");
    } catch (err) {
        console.error("Logout failed:", err);
        router.replace("/landing");
    }
};

const Header = (
    <View style={styles.headerContainer}>
        <Text allowFontScaling={false} style={styles.headerTitle}>{t('profile.title')}</Text>
        <Text allowFontScaling={false} style={styles.headerSubtitle}>{t('profile.terminalAccess')}</Text>
    </View>
);

if (isLoading) {
    return (
        <View style={styles.centerContainer}>
            <ActivityIndicator size="large" color={tokens.colors.primary} />
        </View>
    );
}

if (!isAuthenticated) {
    // Fallback for direct navigation attempts, but mainly handled by layout/index redirects
    return (
        <PageLayout header={Header}>
            <View style={[styles.centerContainer, { paddingHorizontal: GLOBAL_PADDING }]}>
                <ActivityIndicator size="large" color={tokens.colors.primary} />
                <Text style={{ color: '#FFF', marginTop: 16 }}>SECURITY REDIRECT...</Text>
            </View>
        </PageLayout>
    );
}

return (
    <PageLayout header={Header}>
        <View style={{ paddingHorizontal: GLOBAL_PADDING }}>
            <View style={styles.profileHeader}>
                <View style={styles.avatarBox}>
                    <View style={styles.avatarInner}>
                        <User size={24} color={tokens.colors.primary} />
                    </View>
                </View>
                <View>
                    <Text allowFontScaling={false} style={styles.userName}>{user?.firstName || "OPERATOR"}</Text>
                    <Text allowFontScaling={false} style={styles.userPhone}>{user?.phone || "+380"}</Text>
                </View>
            </View>

            {/* Sections */}
            <View style={{ gap: 24 }}>
                {/* Personal Info */}
                <View style={styles.sectionCard}>
                    <View style={styles.sectionHeader}>
                        <User size={18} color={tokens.colors.primary} />
                        <Text allowFontScaling={false} style={styles.sectionTitle}>PERSONAL DATA</Text>
                    </View>
                    <View style={{ gap: 16 }}>
                        <View>
                            <Text allowFontScaling={false} style={styles.inputLabel}>FIRST NAME</Text>
                            <TextInput
                                value={personalForm.firstName}
                                onChangeText={(text) => setPersonalForm(v => ({ ...v, firstName: text }))}
                                style={styles.textInput}
                                placeholderTextColor="#333"
                            />
                        </View>
                        <View>
                            <Text allowFontScaling={false} style={styles.inputLabel}>EMAIL</Text>
                            <TextInput
                                value={personalForm.email}
                                onChangeText={(text) => setPersonalForm(v => ({ ...v, email: text }))}
                                style={styles.textInput}
                                keyboardType="email-address"
                                placeholderTextColor="#333"
                            />
                        </View>
                    </View>
                </View>



                {/* Language */}
                <View style={styles.sectionCard}>
                    <View style={styles.sectionHeader}>
                        <Globe size={18} color={tokens.colors.primary} />
                        <Text allowFontScaling={false} style={styles.sectionTitle}>TERMINAL LANGUAGE</Text>
                    </View>
                    <View style={{ flexDirection: 'row', gap: 8 }}>
                        {languages.map((lang) => {
                            const active = language === lang.code;
                            return (
                                <Pressable
                                    key={lang.code}
                                    onPress={() => setLanguage(lang.code)}
                                    style={[
                                        styles.langBtn,
                                        active ? styles.langBtnActive : styles.langBtnInactive
                                    ]}
                                >
                                    <Text allowFontScaling={false} style={styles.langFlag}>{lang.flag}</Text>
                                    <Text allowFontScaling={false} style={[styles.langText, active ? styles.langTextActive : styles.langTextInactive]}>
                                        {lang.name}
                                    </Text>
                                </Pressable>
                            );
                        })}
                    </View>
                </View>

                <Animated.View style={{ transform: [{ scale: saveScale }] }}>
                    <Pressable
                        onPressIn={() => btnPressIn(saveScale)}
                        onPressOut={() => btnPressOut(saveScale)}
                        onPress={() => {
                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                            updateProfileMutation.mutate(personalForm);
                        }}
                        disabled={updateProfileMutation.isPending}
                        style={[
                            styles.saveBtn,
                            updateProfileMutation.isPending && { opacity: 0.5 }
                        ]}
                    >
                        <Save size={18} color="#000" />
                        <Text allowFontScaling={false} style={styles.saveBtnText}>SAVE UPDATES</Text>
                    </Pressable>
                </Animated.View>

                <Pressable
                    onPress={() => {
                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                        handleLogout();
                    }}
                    style={({ pressed }) => [
                        styles.logoutBtn,
                        pressed && styles.logoutBtnPressed
                    ]}
                >
                    <LogOut size={18} color="#EF4444" />
                    <Text allowFontScaling={false} style={styles.logoutBtnText}>TERMINATE SESSION</Text>
                </Pressable>

                {/* Hard Reset for Testing Auth */}
                <View style={[styles.sectionCard, { marginTop: 40, borderColor: 'rgba(239, 68, 68, 0.3)', borderStyle: 'dashed' }]}>
                    <Text style={[styles.sectionTitle, { color: '#EF4444', fontSize: 12, marginBottom: 8 }]}>DEBUG: LOCAL CACHE</Text>
                    <Text style={{ color: 'rgba(255,255,255,0.4)', fontSize: 10, marginBottom: 16 }}>
                        Wipe all local data, storage, and sessions. Use this to verify a clean authentication flow.
                    </Text>
                    <Pressable
                        onPress={async () => {
                            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);

                            // 1. Attempt to clear server session (cookies)
                            try {
                                await apiRequest("POST", "/api/auth/phone/logout");
                            } catch (e) {
                                console.log("Server logout failed, continuing with local wipe...");
                            }

                            // 2. Clear local states
                            logout();
                            queryClient.clear();
                            await AsyncStorage.clear();

                            // 3. Redirect to fresh landing
                            router.replace("/landing");
                        }}
                        style={{
                            backgroundColor: 'rgba(239, 68, 68, 0.1)',
                            padding: 12,
                            borderRadius: 4,
                            alignItems: 'center',
                            borderWidth: 1,
                            borderColor: '#EF4444'
                        }}
                    >
                        <Text style={{ color: '#EF4444', fontFamily: 'Inter-Black', fontSize: 10, letterSpacing: 2 }}>FORCE HARD RESET</Text>
                    </Pressable>
                </View>
            </View>

            <View style={styles.footerBranding}>
                <Image
                    source={require("../assets/original_lion_watermark.png")}
                    style={styles.footerLogo}
                    resizeMode="contain"
                />
            </View>
        </View>
    </PageLayout>
);
}

const styles = StyleSheet.create({
    centerContainer: {
        flex: 1,
        backgroundColor: tokens.colors.background,
        alignItems: 'center',
        justifyContent: 'center',
    },
    headerContainer: {
        paddingHorizontal: GLOBAL_PADDING,
        paddingBottom: 24,
        alignItems: 'center',
    },
    headerTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: tokens.colors.primary,
        fontSize: 48,
        lineHeight: 48,
        letterSpacing: -1,
        textTransform: 'uppercase',
    },
    headerSubtitle: {
        fontFamily: 'Inter-Black',
        color: tokens.colors.text.muted,
        fontSize: 8,
        letterSpacing: 4,
        textTransform: 'uppercase',
        opacity: 0.6,
        marginTop: 4,
    },
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
    profileHeader: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 20,
        marginBottom: 32,
        paddingHorizontal: 4,
    },
    avatarBox: {
        width: 64,
        height: 64,
        borderWidth: tokens.spacing.hairline,
        borderColor: tokens.colors.primary,
        padding: 2,
        borderRadius: tokens.effects.radius.xs,
    },
    avatarInner: {
        flex: 1,
        backgroundColor: 'rgba(0, 255, 128, 0.08)',
        alignItems: 'center',
        justifyContent: 'center',
    },
    userName: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 32,
        textTransform: 'uppercase',
    },
    userPhone: {
        fontFamily: tokens.typography.fonts.bodyBold,
        color: tokens.colors.primary,
        fontSize: 12,
        letterSpacing: 1,
    },
    sectionCard: {
        backgroundColor: '#000',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.08)',
        padding: 20,
        borderRadius: 2,
    },
    sectionHeader: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
        marginBottom: 24,
    },
    sectionTitle: {
        fontFamily: tokens.typography.fonts.headingReg,
        color: '#FFF',
        fontSize: 18,
        fontWeight: '700',
        textTransform: 'uppercase',
        letterSpacing: 0.5,
    },
    inputLabel: {
        fontFamily: tokens.typography.fonts.bodyBold,
        color: tokens.colors.text.dim,
        fontSize: 9,
        letterSpacing: 1,
        textTransform: 'uppercase',
        marginBottom: 8,
    },
    textInput: {
        backgroundColor: 'rgba(255,255,255,0.03)',
        borderWidth: tokens.spacing.hairline,
        borderColor: tokens.colors.borderLight,
        padding: 16,
        borderRadius: tokens.effects.radius.xs,
        color: '#FFF',
        fontFamily: tokens.typography.fonts.bodyBold,
        fontSize: 14,
    },
    langBtn: {
        flex: 1,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 8,
        paddingVertical: 14,
        borderWidth: tokens.spacing.hairline,
        borderRadius: tokens.effects.radius.xs,
    },
    langBtnActive: {
        backgroundColor: 'rgba(0, 255, 128, 0.08)',
        borderColor: tokens.colors.primary,
    },
    langBtnInactive: {
        borderColor: tokens.colors.borderLight,
        backgroundColor: 'rgba(0,0,0,0.2)',
    },
    langFlag: {
        fontSize: 18,
    },
    langText: {
        fontFamily: tokens.typography.fonts.bodyBold,
        fontSize: 10,
        textTransform: 'uppercase',
    },
    langTextActive: {
        color: tokens.colors.primary,
    },
    langTextInactive: {
        color: tokens.colors.text.dim,
    },
    saveBtn: {
        width: '100%',
        backgroundColor: tokens.colors.primary,
        paddingVertical: 18,
        borderRadius: tokens.effects.radius.lg,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
    },
    saveBtnText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: '#000',
        fontSize: 14,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    logoutBtn: {
        width: '100%',
        backgroundColor: 'rgba(239, 68, 68, 0.08)',
        borderWidth: tokens.spacing.hairline,
        borderColor: 'rgba(239, 68, 68, 0.2)',
        paddingVertical: 18,
        borderRadius: tokens.effects.radius.lg,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
        marginTop: 8,
    },
    logoutBtnPressed: {
        backgroundColor: 'rgba(239, 68, 68, 0.15)',
    },
    logoutBtnText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: '#EF4444',
        fontSize: 14,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    footerBranding: {
        paddingVertical: 64,
        alignItems: 'center',
        opacity: 0.1,
    },
    footerLogo: {
        width: 128,
        height: 128,
        tintColor: '#FFF',
    }
});
