/// <reference types="nativewind/types" />
import { useState, useEffect, useRef } from "react";
import { View, Text, Pressable, TextInput, ActivityIndicator, StyleSheet, Animated, Platform, Keyboard, Modal, Alert } from "react-native";
import { useRouter } from "expo-router";
import { User, LogOut, Phone, Globe, Save } from "lucide-react-native";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useI18n, languages } from "../src/lib/i18n";
import { apiRequest, logout as apiLogout, getLegalProfile, updateLegalProfile, Company as DbCompany } from "../src/lib/api";
import { useAuth } from "../src/hooks/useAuth";
import { PageLayout } from "../src/components/page-layout";
import { useDesignTokens } from "../src/lib/design-tokens";
import { Building2, ChevronRight, FileSignature } from "lucide-react-native";
import { useStore } from "../src/lib/store";
import { themeOptions } from "../src/lib/themes";
import { Haptics } from "../src/lib/haptics";
import DateTimePicker from '@react-native-community/datetimepicker';
import { z } from "zod";

export default function ProfileScreen() {
    const router = useRouter();
    const queryClient = useQueryClient();
    const { t, language, setLanguage } = useI18n();
    const { logout, theme, setTheme } = useStore();
    const { user, isAuthenticated, isLoading } = useAuth();
    const tokens = useDesignTokens();
    const saveScale = useRef(new Animated.Value(1)).current;
    const logoutScale = useRef(new Animated.Value(1)).current;

    const GLOBAL_PADDING = tokens.spacing.containerPadding;

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

    const [isLegalEntity, setIsLegalEntity] = useState(false);
    const [companyForm, setCompanyForm] = useState({
        name: "",
        edrpou: "",
        vatNumber: "",
        address: "",
        directorName: "",
        phone: "",
        email: ""
    });

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
            setIsLegalEntity(user.userType === 'LEGAL_ENTITY');
        }
    }, [user]);

    useEffect(() => {
        if (isLegalEntity) {
            getLegalProfile().then(data => {
                if (data.company) {
                    setCompanyForm({
                        name: data.company.name || "",
                        edrpou: data.company.edrpou || "",
                        vatNumber: data.company.vatNumber || "",
                        address: data.company.address || "",
                        directorName: data.company.directorName || "",
                        phone: data.company.phone || "",
                        email: data.company.email || ""
                    });
                }
            }).catch(console.error);
        }
    }, [isLegalEntity]);

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
            queryClient.invalidateQueries({ queryKey: ["/api/auth/user/me"] });
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        }
    });

    const updateCompanyMutation = useMutation({
        mutationFn: async (data: any) => {
            return updateLegalProfile(data);
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: ["/api/auth/user/me"] });
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        }
    });

    const handleLogout = async () => {
        try {
            await apiLogout();
            logout();
            queryClient.clear();
            router.replace("/");
        } catch (err) {
            console.error("Logout failed:", err);
            logout();
            queryClient.clear();
            router.replace("/");
        }
    };

    const Header = (
        <View style={[styles.headerContainer, { paddingHorizontal: GLOBAL_PADDING }]}>
            <Text allowFontScaling={false} style={[styles.headerTitle, { color: tokens.colors.primary }]}>{t('profile.title')}</Text>
        </View>
    );

    if (isLoading) {
        return (
            <View style={[styles.centerContainer, { backgroundColor: tokens.colors.background }]}>
                <ActivityIndicator size="large" color={tokens.colors.primary} />
            </View>
        );
    }

    if (!isAuthenticated) {
        return (
            <PageLayout header={Header}>
                <View style={[styles.centerContainer, { paddingHorizontal: GLOBAL_PADDING, backgroundColor: tokens.colors.background }]}>
                    <ActivityIndicator size="large" color={tokens.colors.primary} />
                    <Text style={{ color: tokens.colors.text.primary, marginTop: 16 }}>SECURITY REDIRECT...</Text>
                </View>
            </PageLayout>
        );
    }

    return (
        <PageLayout header={Header}>
            <View style={{ paddingHorizontal: GLOBAL_PADDING }}>
                <View style={styles.profileHeader}>
                    <View style={[styles.avatarBox, { borderColor: tokens.colors.primary }]}>
                        <View style={[styles.avatarInner, { backgroundColor: `${tokens.colors.primary}11` }]}>
                            <User size={24} color={tokens.colors.primary} />
                        </View>
                    </View>
                    <View>
                        {user?.firstName && (
                            <Text allowFontScaling={false} style={[styles.userName, { color: tokens.colors.text.primary }]}>{user.firstName}</Text>
                        )}
                        <Text allowFontScaling={false} style={[styles.userPhone, { color: tokens.colors.primary }]}>{user?.phone || "+380"}</Text>
                    </View>
                </View>

                {/* Content Block */}
                <View style={{ gap: 24 }}>
                    {/* Personal Data Section */}
                    <View style={[styles.sectionCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                        <View style={styles.sectionHeader}>
                            <User size={18} color={tokens.colors.primary} />
                            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.primary }]}>{t('profile.personalInfo')}</Text>
                        </View>

                        <View style={{ gap: 16 }}>
                            <View style={{ flexDirection: 'row', gap: 16 }}>
                                <View style={{ flex: 1 }}>
                                    <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>{t('profile.firstName')}</Text>
                                    <TextInput
                                        value={personalForm.firstName}
                                        onChangeText={(text) => setPersonalForm(v => ({ ...v, firstName: text }))}
                                        style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                        placeholderTextColor={tokens.colors.text.dim}
                                    />
                                </View>
                                <View style={{ flex: 1 }}>
                                    <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>{t('profile.lastName')}</Text>
                                    <TextInput
                                        value={personalForm.lastName}
                                        onChangeText={(text) => setPersonalForm(v => ({ ...v, lastName: text }))}
                                        style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                        placeholderTextColor={tokens.colors.text.dim}
                                    />
                                </View>
                            </View>

                            <View>
                                <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>{t('profile.email')}</Text>
                                <TextInput
                                    value={personalForm.email}
                                    onChangeText={(text) => {
                                        setPersonalForm(v => ({ ...v, email: text }));
                                        if (errors.email) setErrors(e => ({ ...e, email: false }));
                                    }}
                                    style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }, errors.email && { borderColor: tokens.colors.error, borderWidth: 1 }]}
                                    keyboardType="email-address"
                                    placeholderTextColor={tokens.colors.text.dim}
                                    autoCapitalize="none"
                                />
                                {errors.email && (
                                    <Text style={{ color: tokens.colors.error, fontSize: 10, marginTop: 4, fontFamily: 'Inter-Bold' }}>
                                        {t('profile.invalidEmail')}
                                    </Text>
                                )}
                            </View>

                            <View>
                                <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>{t('profile.birthdate')}</Text>
                                <Pressable
                                    onPress={() => {
                                        Keyboard.dismiss();
                                        setTempDate(getSafeDate(personalForm.birthdate));
                                        setShowDatePicker(true);
                                    }}
                                    style={({ pressed }) => [
                                        styles.textInput,
                                        { backgroundColor: tokens.colors.background, borderColor: tokens.colors.borderLight, paddingRight: 44, flexDirection: 'row', alignItems: 'center' },
                                        pressed && { opacity: 0.7 }
                                    ]}
                                >
                                    <Text style={{ color: personalForm.birthdate ? tokens.colors.text.primary : tokens.colors.text.dim, fontFamily: 'Inter-Bold', fontSize: 14 }}>
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
                    <View style={[styles.sectionCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                        <View style={styles.sectionHeader}>
                            <Globe size={18} color={tokens.colors.primary} />
                            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.primary }]}>{t('profile.language')}</Text>
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
                                            { width: '48%', backgroundColor: active ? tokens.colors.primaryDim : tokens.colors.background, borderColor: active ? tokens.colors.primary : tokens.colors.borderLight },
                                            active && { borderWidth: 1.5 }
                                        ]}
                                    >
                                        <Text allowFontScaling={false} style={styles.langFlag}>{lang.flag}</Text>
                                        <View style={{ flex: 1 }}>
                                            <Text
                                                allowFontScaling={false}
                                                numberOfLines={1}
                                                adjustsFontSizeToFit
                                                minimumFontScale={0.8}
                                                style={[styles.langText, { color: active ? tokens.colors.primary : tokens.colors.text.dim }]}
                                            >
                                                {lang.name}
                                            </Text>
                                        </View>
                                        {active && (
                                            <View style={[styles.activeIndicator, { backgroundColor: tokens.colors.primary }]} />
                                        )}
                                    </Pressable>
                                );
                            })}
                        </View>
                    </View>

                    {/* Theme Settings Section */}
                    <View style={[styles.sectionCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                        <View style={styles.sectionHeader}>
                            <View style={{ width: 18, height: 18, borderRadius: 9, borderWidth: 2, borderColor: tokens.colors.primary, alignItems: 'center', justifyContent: 'center' }}>
                                <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: tokens.colors.primary }} />
                            </View>
                            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.primary }]}>{t('profile.theme')}</Text>
                        </View>
                        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 12 }}>
                            {themeOptions.map((opt) => {
                                const active = theme === opt.id;
                                return (
                                    <Pressable
                                        key={opt.id}
                                        onPress={() => {
                                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                            setTheme(opt.id);
                                        }}
                                        style={[
                                            styles.langBtn,
                                            { width: '48%', backgroundColor: active ? tokens.colors.primaryDim : tokens.colors.background, borderColor: active ? tokens.colors.primary : tokens.colors.borderLight },
                                            active && { borderWidth: 1.5 }
                                        ]}
                                    >
                                        <View style={{ width: 12, height: 12, borderRadius: 6, backgroundColor: opt.color, marginRight: 8, borderWidth: 1, borderColor: 'rgba(255,255,255,0.2)' }} />
                                        <View style={{ flex: 1 }}>
                                            <Text
                                                allowFontScaling={false}
                                                style={[
                                                    styles.langText,
                                                    { color: active ? tokens.colors.primary : tokens.colors.text.dim }
                                                ]}
                                            >
                                                {t(opt.label)}
                                            </Text>
                                        </View>
                                    </Pressable>
                                );
                            })}
                        </View>
                    </View>

                    {/* Legal Entity Toggle */}
                    <View style={[styles.sectionCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                        <View style={styles.sectionHeader}>
                            <Building2 size={18} color={tokens.colors.primary} />
                            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.primary }]}>ЮРИДИЧНА ОСОБА</Text>
                        </View>
                        
                        <Pressable 
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                setIsLegalEntity(!isLegalEntity);
                            }}
                            style={styles.toggleRow}
                        >
                            <Text style={{ flex: 1, color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16, marginRight: 16 }}>Я ПРЕДСТАВНИК ЮРИДИЧНОЇ ОСОБИ</Text>
                            <View style={[styles.toggleSwitch, { backgroundColor: isLegalEntity ? tokens.colors.primary : tokens.colors.borderLight }]}>
                                <View style={[styles.toggleDot, { transform: [{ translateX: isLegalEntity ? 20 : 0 }] }]} />
                            </View>
                        </Pressable>

                        {isLegalEntity && (
                            <View style={{ marginTop: 24, gap: 16 }}>
                                <View>
                                    <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>НАЗВА КОМПАНІЇ</Text>
                                    <TextInput
                                        value={companyForm.name}
                                        onChangeText={(text) => setCompanyForm(v => ({ ...v, name: text }))}
                                        style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                    />
                                </View>
                                <View style={{ flexDirection: 'row', gap: 16 }}>
                                    <View style={{ flex: 1 }}>
                                        <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>ЄДРПОУ</Text>
                                        <TextInput
                                            value={companyForm.edrpou}
                                            onChangeText={(text) => setCompanyForm(v => ({ ...v, edrpou: text }))}
                                            style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                            keyboardType="numeric"
                                        />
                                    </View>
                                    <View style={{ flex: 1 }}>
                                        <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>ІПН (ЗА НАЯВНОСТІ)</Text>
                                        <TextInput
                                            value={companyForm.vatNumber}
                                            onChangeText={(text) => setCompanyForm(v => ({ ...v, vatNumber: text }))}
                                            style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                            keyboardType="numeric"
                                        />
                                    </View>
                                </View>
                                <View>
                                    <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>ЮРИДИЧНА АДРЕСА</Text>
                                    <TextInput
                                        value={companyForm.address}
                                        onChangeText={(text) => setCompanyForm(v => ({ ...v, address: text }))}
                                        style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                    />
                                </View>
                                <View>
                                    <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>ПІБ ДИРЕКТОРА</Text>
                                    <TextInput
                                        value={companyForm.directorName}
                                        onChangeText={(text) => setCompanyForm(v => ({ ...v, directorName: text }))}
                                        style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                    />
                                </View>

                                <Pressable 
                                    onPress={() => {
                                        if (!companyForm.name || !companyForm.edrpou) {
                                            Alert.alert("Попередження", "Спочатку заповніть та збережіть дані компанії");
                                            return;
                                        }
                                        router.push('/contracts');
                                    }}
                                    style={({ pressed }) => [
                                        styles.contractsBtn,
                                        { backgroundColor: `${tokens.colors.primary}11`, borderColor: tokens.colors.primary },
                                        pressed && { opacity: 0.7 }
                                    ]}
                                >
                                    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12, flex: 1 }}>
                                        <FileSignature size={20} color={tokens.colors.primary} />
                                        <Text style={{ color: tokens.colors.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16 }} numberOfLines={1}>ПІДПИСАТИ ДОГОВОРИ</Text>
                                    </View>
                                    <ChevronRight size={16} color={tokens.colors.primary} />
                                </Pressable>
                            </View>
                        )}
                    </View>

                    {/* Action Buttons: Save Updates & Sign Out */}
                    <View style={{ gap: 16, marginTop: 12 }}>
                        <Animated.View style={{ transform: [{ scale: saveScale }] }}>
                            <Pressable
                                onPressIn={() => btnPressIn(saveScale)}
                                onPressOut={() => btnPressOut(saveScale)}
                                onPress={() => {
                                    console.log('[DEBUG] Save button pressed');
                                    console.log('[DEBUG] isLegalEntity:', isLegalEntity);
                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                    updateProfileMutation.mutate(personalForm);
                                    if (isLegalEntity) {
                                        console.log('[DEBUG] Triggering company profile update...');
                                        updateCompanyMutation.mutate(companyForm);
                                    } else {
                                        console.log('[DEBUG] Company update skipped (isLegalEntity is false)');
                                    }
                                }}
                                disabled={updateProfileMutation.isPending}
                                style={[
                                    styles.saveBtn,
                                    { backgroundColor: tokens.colors.primary },
                                    updateProfileMutation.isPending && { opacity: 0.5 }
                                ]}
                            >
                                <Save size={18} color={tokens.colors.isDark ? "#000" : "#FFF"} />
                                <Text allowFontScaling={false} style={[styles.saveBtnText, { color: tokens.colors.isDark ? "#000" : "#FFF" }]}>{t('common.save') || 'ЗБЕРЕГТИ'}</Text>
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
                                    styles.logoutBtn,
                                    { backgroundColor: tokens.colors.error }
                                ]}
                            >
                                <LogOut size={18} color={tokens.colors.isDark ? "#000" : "#FFF"} />
                                <Text allowFontScaling={false} style={[styles.logoutBtnText, { color: tokens.colors.isDark ? "#000" : "#FFF" }]}>
                                    {t('profile.signOut')}
                                </Text>
                            </Pressable>
                        </Animated.View>
                    </View>
                </View>

                {/* Scroll Bottom Clearance */}
                <View style={{ height: 200 }} />
            </View>

            {/* iOS Premium Date Picker Modal */}
            <Modal
                visible={showDatePicker && Platform.OS === 'ios'}
                transparent={true}
                animationType="slide"
            >
                <View style={{ flex: 1, justifyContent: 'flex-end', backgroundColor: 'rgba(0,0,0,0.8)' }}>
                    <View style={{ backgroundColor: tokens.colors.background, borderTopWidth: 1, borderColor: tokens.colors.borderLight, paddingBottom: 40 }}>
                        <View style={{ flexDirection: 'row', justifyContent: 'space-between', padding: 20, borderBottomWidth: 1, borderColor: tokens.colors.borderLight }}>
                            <Pressable onPress={() => setShowDatePicker(false)} style={{ padding: 10 }}>
                                <Text style={{ color: tokens.colors.text.dim, fontFamily: 'Inter-Bold', fontSize: 16 }}>СКАСУВАТИ</Text>
                            </Pressable>
                            <Pressable onPress={() => {
                                const day = String(tempDate.getDate()).padStart(2, '0');
                                const month = String(tempDate.getMonth() + 1).padStart(2, '0');
                                const year = tempDate.getFullYear();
                                setPersonalForm(v => ({ ...v, birthdate: `${day}.${month}.${year}` }));
                                setShowDatePicker(false);
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                            }} style={{ padding: 10 }}>
                                <Text style={{ color: tokens.colors.primary, fontFamily: 'Inter-Black', fontSize: 16 }}>ГОТОВО</Text>
                            </Pressable>
                        </View>
                        <DateTimePicker
                            value={tempDate}
                            mode="date"
                            display="spinner"
                            textColor={tokens.colors.text.primary}
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
        alignItems: 'center',
        justifyContent: 'center',
    },
    headerContainer: {
        paddingBottom: 24,
        alignItems: 'center',
    },
    headerTitle: {
        fontFamily: 'Rajdhani-Bold',
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
        borderWidth: StyleSheet.hairlineWidth,
        padding: 2,
        borderRadius: 2,
    },
    avatarInner: {
        flex: 1,
        alignItems: 'center',
        justifyContent: 'center',
    },
    userName: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 32,
        textTransform: 'uppercase',
    },
    userPhone: {
        fontFamily: 'Inter-Black',
        fontSize: 18,
        letterSpacing: 1.5,
    },
    sectionCard: {
        padding: 20,
        borderRadius: 2,
        borderWidth: StyleSheet.hairlineWidth,
        marginBottom: 20,
    },
    sectionHeader: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
        marginBottom: 20,
    },
    sectionTitle: {
        fontFamily: 'Rajdhani-SemiBold',
        fontSize: 12,
        letterSpacing: 4,
        textTransform: 'uppercase',
    },
    inputLabel: {
        fontFamily: 'Rajdhani-SemiBold',
        fontSize: 10,
        letterSpacing: 2,
        textTransform: 'uppercase',
        marginBottom: 8,
        marginLeft: 4,
    },
    textInput: {
        borderRadius: 2,
        paddingHorizontal: 16,
        paddingVertical: 12,
        fontFamily: 'Inter-Bold',
        fontSize: 14,
        borderWidth: 1,
    },
    langBtn: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 8,
        paddingVertical: 14,
        borderWidth: 1.5,
        borderRadius: 4,
        gap: 6,
    },
    activeIndicator: {
        width: 4,
        height: 4,
        borderRadius: 2,
    },
    langFlag: {
        fontSize: 18,
    },
    langText: {
        fontFamily: 'Inter-Black',
        fontSize: 10,
        textTransform: 'uppercase',
        letterSpacing: 1,
    },
    saveBtn: {
        width: '100%',
        paddingVertical: 18,
        borderRadius: 12,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
    },
    saveBtnText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    logoutBtn: {
        width: '100%',
        paddingVertical: 18,
        borderRadius: 12,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
    },
    logoutBtnText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    toggleRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    toggleSwitch: {
        width: 44,
        height: 24,
        borderRadius: 12,
        padding: 2,
    },
    toggleDot: {
        width: 20,
        height: 20,
        borderRadius: 10,
        backgroundColor: '#FFF',
    },
    contractsBtn: {
        marginTop: 8,
        padding: 16,
        borderRadius: 2,
        borderWidth: 1,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'space-between',
    }
});
