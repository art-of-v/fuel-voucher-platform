/// <reference types="nativewind/types" />
import { useState, useEffect, useRef } from "react";
import { View, Text, Pressable, TextInput, ActivityIndicator, Image, StyleSheet, Animated, Platform, Keyboard } from "react-native";
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
import DateTimePicker from '@react-native-community/datetimepicker';
import { z } from "zod";
import { Modal } from "react-native";

const GLOBAL_PADDING = tokens.spacing.containerPadding;

export default function ProfileScreen() {
    const router = useRouter();
    const queryClient = useQueryClient();
    const { user, isLoading, isAuthenticated } = useAuth();
    const logout = useStore(state => state.logout);
    const { language, setLanguage, t } = useI18n();
    const saveScale = useRef(new Animated.Value(1)).current;
    const logoutScale = useRef(new Animated.Value(1)).current;

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

    const [errors, setErrors] = useState<{ email?: boolean; birthdate?: boolean }>({});
    const [showDatePicker, setShowDatePicker] = useState(false);
    const [tempDate, setTempDate] = useState(new Date());

    const emailSchema = z.string().email();

    const getSafeDate = (dateStr: string) => {
        if (!dateStr) return new Date();

        // Handle format like 21091982 or 21.09.1982
        const cleaned = dateStr.replace(/\D/g, '');
        if (cleaned.length === 8) {
            const d = parseInt(cleaned.substring(0, 2), 10);
            const m = parseInt(cleaned.substring(2, 4), 10);
            const y = parseInt(cleaned.substring(4, 8), 10);
            const dt = new Date(y, m - 1, d);
            if (!isNaN(dt.getTime())) return dt;
        }

        const d = new Date(dateStr);
        return isNaN(d.getTime()) ? new Date() : d;
    };

    useEffect(() => {
        if (!isLoading && !isAuthenticated) {
            router.replace("/landing");
        }
    }, [isLoading, isAuthenticated]);

    useEffect(() => {
        if (user) {
            setPersonalForm({
                firstName: user.firstName || "",
                lastName: user.lastName || "",
                email: user.email || "",
                birthdate: user.birthdate || ""
            });
        }
    }, [user]);

    const updateProfileMutation = useMutation({
        mutationFn: async (data: any) => {
            // Validation
            const newErrors: { email?: boolean; } = {};

            if (data.email) {
                const emailResult = emailSchema.safeParse(data.email);
                if (!emailResult.success) {
                    newErrors.email = true;
                }
            }

            if (Object.keys(newErrors).length > 0) {
                setErrors(newErrors);
                Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
                throw new Error("Validation failed");
            }

            setErrors({});
            const res = await apiRequest("POST", `/api/users/update`, data);
            return res.json();
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        }
    });

    const handleLogout = async () => {
        try {
            logout();
            queryClient.clear();
            try {
                await apiRequest("POST", "/api/auth/phone/logout");
            } catch (e) { }
            router.replace("/landing");
        } catch (err) {
            console.error("Logout failed:", err);
            router.replace("/landing");
        }
    };

    const Header = (
        <View style={styles.headerContainer}>
            <Text allowFontScaling={false} style={styles.headerTitle}>{t('profile.title')}</Text>
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
                        {user?.firstName && (
                            <Text allowFontScaling={false} style={styles.userName}>{user.firstName}</Text>
                        )}
                        <Text allowFontScaling={false} style={styles.userPhone}>{user?.phone || "+380"}</Text>
                    </View>
                </View>

                {/* Content Block */}
                <View style={{ gap: 24 }}>
                    {/* Personal Data Section */}
                    <View style={styles.sectionCard}>
                        <View style={styles.sectionHeader}>
                            <User size={18} color={tokens.colors.primary} />
                            <Text allowFontScaling={false} style={styles.sectionTitle}>{t('profile.personalInfo')}</Text>
                        </View>

                        <View style={{ gap: 16 }}>
                            <View style={{ flexDirection: 'row', gap: 16 }}>
                                <View style={{ flex: 1 }}>
                                    <Text allowFontScaling={false} style={styles.inputLabel}>{t('profile.firstName')}</Text>
                                    <TextInput
                                        value={personalForm.firstName}
                                        onChangeText={(text) => setPersonalForm(v => ({ ...v, firstName: text }))}
                                        style={styles.textInput}
                                        placeholderTextColor="#333"
                                    />
                                </View>
                                <View style={{ flex: 1 }}>
                                    <Text allowFontScaling={false} style={styles.inputLabel}>{t('profile.lastName')}</Text>
                                    <TextInput
                                        value={personalForm.lastName}
                                        onChangeText={(text) => setPersonalForm(v => ({ ...v, lastName: text }))}
                                        style={styles.textInput}
                                        placeholderTextColor="#333"
                                    />
                                </View>
                            </View>

                            <View>
                                <Text allowFontScaling={false} style={styles.inputLabel}>{t('profile.email')}</Text>
                                <TextInput
                                    value={personalForm.email}
                                    onChangeText={(text) => {
                                        setPersonalForm(v => ({ ...v, email: text }));
                                        if (errors.email) setErrors(e => ({ ...e, email: false }));
                                    }}
                                    style={[styles.textInput, errors.email && { borderColor: '#EF4444', borderWidth: 1 }]}
                                    keyboardType="email-address"
                                    placeholderTextColor="#333"
                                    autoCapitalize="none"
                                />
                                {errors.email && (
                                    <Text style={{ color: '#EF4444', fontSize: 10, marginTop: 4, fontFamily: tokens.typography.fonts.bodyBold }}>
                                        {t('profile.invalidEmail')}
                                    </Text>
                                )}
                            </View>

                            <View>
                                <Text allowFontScaling={false} style={styles.inputLabel}>{t('profile.birthdate')}</Text>
                                <Pressable
                                    onPress={() => {
                                        Keyboard.dismiss();
                                        setTempDate(getSafeDate(personalForm.birthdate));
                                        setShowDatePicker(true);
                                    }}
                                    style={({ pressed }) => [
                                        styles.textInput,
                                        { paddingRight: 44, flexDirection: 'row', alignItems: 'center' },
                                        pressed && { opacity: 0.7 }
                                    ]}
                                >
                                    <Text style={{ color: personalForm.birthdate ? '#FFF' : '#333', fontFamily: tokens.typography.fonts.bodyBold, fontSize: 14 }}>
                                        {personalForm.birthdate ? (
                                            (() => {
                                                const d = getSafeDate(personalForm.birthdate);
                                                const day = String(d.getDate()).padStart(2, '0');
                                                const month = String(d.getMonth() + 1).padStart(2, '0');
                                                const year = d.getFullYear();
                                                return `${day}.${month}.${year}`;
                                            })()
                                        ) : "dd.mm.yyyy"}
                                    </Text>
                                    <View style={{ position: 'absolute', right: 12 }}>
                                        <View style={{ width: 16, height: 16, borderWidth: 1.5, borderColor: tokens.colors.primary, borderRadius: 2, alignItems: 'center', justifyContent: 'center' }}>
                                            <View style={{ width: 10, height: 2, backgroundColor: tokens.colors.primary, position: 'absolute', top: 2 }} />
                                        </View>
                                    </View>
                                </Pressable>

                                {showDatePicker && Platform.OS === 'android' && (
                                    <DateTimePicker
                                        value={getSafeDate(personalForm.birthdate)}
                                        mode="date"
                                        display="default"
                                        onChange={(event, selectedDate) => {
                                            setShowDatePicker(false);
                                            if (event.type === 'set' && selectedDate) {
                                                const day = String(selectedDate.getDate()).padStart(2, '0');
                                                const month = String(selectedDate.getMonth() + 1).padStart(2, '0');
                                                const year = selectedDate.getFullYear();
                                                setPersonalForm(v => ({ ...v, birthdate: `${day}.${month}.${year}` }));
                                            }
                                        }}
                                    />
                                )}
                            </View>
                        </View>
                    </View>

                    {/* Language Settings Section */}
                    <View style={styles.sectionCard}>
                        <View style={styles.sectionHeader}>
                            <Globe size={18} color={tokens.colors.primary} />
                            <Text allowFontScaling={false} style={styles.sectionTitle}>{t('profile.language')}</Text>
                        </View>
                        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 12 }}>
                            {languages.map((lang) => {
                                const active = language === lang.code;
                                return (
                                    <Pressable
                                        key={lang.code}
                                        onPress={() => {
                                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                            setLanguage(lang.code);
                                        }}
                                        style={[
                                            styles.langBtn,
                                            { width: '48%' },
                                            active ? styles.langBtnActive : styles.langBtnInactive
                                        ]}
                                    >
                                        <Text allowFontScaling={false} style={styles.langFlag}>{lang.flag}</Text>
                                        <View style={{ flex: 1 }}>
                                            <Text
                                                allowFontScaling={false}
                                                numberOfLines={1}
                                                adjustsFontSizeToFit
                                                minimumFontScale={0.8}
                                                style={[styles.langText, active ? styles.langTextActive : styles.langTextInactive]}
                                            >
                                                {lang.name}
                                            </Text>
                                        </View>
                                        {active && (
                                            <View style={styles.activeIndicator} />
                                        )}
                                    </Pressable>
                                );
                            })}
                        </View>
                    </View>

                    {/* Action Buttons: Save Updates & Sign Out */}
                    <View style={{ gap: 16, marginTop: 12 }}>
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
                                <Text allowFontScaling={false} style={styles.saveBtnText}>{t('common.save') || 'ЗБЕРЕГТИ'}</Text>
                            </Pressable>
                        </Animated.View>

                        <Animated.View style={{ transform: [{ scale: logoutScale }] }}>
                            <Pressable
                                onPressIn={() => btnPressIn(logoutScale)}
                                onPressOut={() => btnPressOut(logoutScale)}
                                onPress={() => {
                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                    handleLogout();
                                }}
                                style={[
                                    styles.logoutBtn
                                ]}
                            >
                                <LogOut size={18} color="#000" />
                                <Text allowFontScaling={false} style={styles.logoutBtnText}>
                                    {t('profile.signOut')}
                                </Text>
                            </Pressable>
                        </Animated.View>
                    </View>
                </View>

                {/* Scroll Bottom Clearance */}
                <View style={{ height: 160 }} />
            </View>

            {/* iOS Premium Date Picker Modal - Moved to root for visibility */}
            <Modal
                visible={showDatePicker && Platform.OS === 'ios'}
                transparent={true}
                animationType="slide"
            >
                <View style={{ flex: 1, justifyContent: 'flex-end', backgroundColor: 'rgba(0,0,0,0.8)' }}>
                    <View style={{ backgroundColor: '#1A1A1A', borderTopWidth: 1, borderColor: tokens.colors.borderLight, paddingBottom: 40 }}>
                        <View style={{ flexDirection: 'row', justifyContent: 'space-between', padding: 20, borderBottomWidth: 1, borderColor: 'rgba(255,255,255,0.05)' }}>
                            <Pressable onPress={() => setShowDatePicker(false)} style={{ padding: 10 }}>
                                <Text style={{ color: '#999', fontFamily: tokens.typography.fonts.bodyBold, fontSize: 16 }}>СКАСУВАТИ</Text>
                            </Pressable>
                            <Pressable onPress={() => {
                                const day = String(tempDate.getDate()).padStart(2, '0');
                                const month = String(tempDate.getMonth() + 1).padStart(2, '0');
                                const year = tempDate.getFullYear();
                                setPersonalForm(v => ({ ...v, birthdate: `${day}.${month}.${year}` }));
                                setShowDatePicker(false);
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                            }} style={{ padding: 10 }}>
                                <Text style={{ color: tokens.colors.primary, fontFamily: tokens.typography.fonts.bodyBlack, fontSize: 16 }}>ГОТОВО</Text>
                            </Pressable>
                        </View>
                        <DateTimePicker
                            value={tempDate}
                            mode="date"
                            display="spinner"
                            textColor="#FFFFFF"
                            onChange={(event, date) => {
                                if (date) setTempDate(date);
                            }}
                        />
                    </View>
                </View>
            </Modal>
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
        fontSize: 32,
        lineHeight: 32,
        letterSpacing: -1,
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
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: tokens.colors.primary,
        fontSize: 18,
        letterSpacing: 1.5,
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
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 8,
        paddingVertical: 14,
        borderWidth: 1.5,
        borderRadius: tokens.effects.radius.sm,
        gap: 6,
    },
    langBtnActive: {
        backgroundColor: 'rgba(0, 255, 106, 0.08)',
        borderColor: tokens.colors.primary,
    },
    langBtnInactive: {
        borderColor: 'rgba(255, 255, 255, 0.05)',
        backgroundColor: 'rgba(255, 255, 255, 0.02)',
    },
    activeIndicator: {
        width: 4,
        height: 4,
        borderRadius: 2,
        backgroundColor: tokens.colors.primary,
    },
    langFlag: {
        fontSize: 18,
    },
    langText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        fontSize: 10,
        textTransform: 'uppercase',
        letterSpacing: 1,
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
        backgroundColor: '#FF0000',
        paddingVertical: 18,
        borderRadius: tokens.effects.radius.lg,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
    },
    logoutBtnText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: '#000',
        fontSize: 14,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
});
