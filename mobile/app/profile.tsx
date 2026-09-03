/// <reference types="nativewind/types" />
import { useState, useEffect } from "react";
import { View, Text, Pressable, TextInput, StyleSheet, Platform, Keyboard, Modal, Alert, ScrollView } from "react-native";
import { useRouter } from "expo-router";
import { User, LogOut, Globe, Save, Building2, FileSignature, TrendingUp, Trash2, Users, Mail } from "lucide-react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useI18n, languages } from "../src/core/i18n";
import { apiFetch } from "../src/core/api/apiClient";
import { logout as apiLogout } from "../src/core/api/logout";
import { getLegalProfile, updateLegalProfile } from "../src/features/profile/api/updateLegalProfile";
import { getMyInvitations } from "../src/features/company/api/companyApi";
import { useAuth } from "../src/features/auth/hooks/useAuth";
import { PageLayout } from "../src/components/page-layout";
import { Badge, Button, Card, ListItem, LoadingState, ScreenHeader } from "../src/core/ui";
import { useDesignTokens } from "../src/core/hooks/useTheme";
import { useStore } from "../src/core/state/appStore";
import { themeOptions } from "../src/core/design/themes";
import { Haptics } from "../src/core/utils/haptics";
import DateTimePicker from '@react-native-community/datetimepicker';
import { z } from "zod";

export default function ProfileScreen() {
    const router = useRouter();
    const queryClient = useQueryClient();
    const { t, language, setLanguage } = useI18n();
    const { logout, theme, setTheme } = useStore();
    const { user, isAuthenticated, isLoading } = useAuth();
    const tokens = useDesignTokens();

    const GLOBAL_PADDING = tokens.spacing.containerPadding;

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

    // Auto-detect company ownership: the user is an owner iff the legal-entity
    // profile endpoint returns a profile (null on 404). Drives the company
    // section and the "Company Management" entry.
    const { data: legalProfile } = useQuery({
        queryKey: ['legal-profile'],
        queryFn: getLegalProfile,
        enabled: isAuthenticated,
    });
    const hasCompany = !!legalProfile;

    // Pending worker invitations — surfaces the inbox entry with a badge.
    const { data: myInvitations } = useQuery({
        queryKey: ['company', 'my-invitations'],
        queryFn: getMyInvitations,
        enabled: isAuthenticated,
    });
    const pendingInvitationCount = myInvitations?.length ?? 0;

    const getSafeDate = (dateStr: string) => {
        if (!dateStr) return new Date();

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

    // When a legal-entity profile exists, enable the company section and
    // populate the form from it.
    useEffect(() => {
        if (legalProfile) {
            setIsLegalEntity(true);
            setCompanyForm({
                name: legalProfile.name || "",
                edrpou: legalProfile.edrpou || "",
                vatNumber: legalProfile.vatNumber || "",
                address: legalProfile.address || "",
                directorName: legalProfile.directorName || "",
                phone: legalProfile.phone || "",
                email: legalProfile.email || ""
            });
        }
    }, [legalProfile]);

    const updateProfileMutation = useMutation({
        mutationFn: async (data: any) => {
            const newErrors: { email?: boolean; } = {};
            if (data.email) {
                const emailResult = emailSchema.safeParse(data.email);
                if (!emailResult.success) newErrors.email = true;
            }
            if (Object.keys(newErrors).length > 0) {
                setErrors(newErrors);
                Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
                throw new Error("Validation failed");
            }
            setErrors({});
            const body: Record<string, any> = {};
            if (data.firstName) body.firstName = data.firstName;
            if (data.lastName) body.lastName = data.lastName;
            if (data.email) body.email = data.email;
            if (data.birthdate) {
                const [day, month, year] = data.birthdate.split('.');
                body.birthdate = `${year}-${month}-${day}`;
            }
            const res = await apiFetch(`/api/users/update`, {
                method: 'POST',
                body: JSON.stringify(body),
            });
            if (!res.ok) {
                const errBody = await res.json().catch(() => ({}));
                throw new Error(errBody.message || errBody.title || `Save failed (${res.status})`);
            }
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
            queryClient.invalidateQueries({ queryKey: ["legal-profile"] });
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

    const handleDeleteAccount = async () => {
        try {
            const res = await apiFetch('/api/users/me', { method: 'DELETE' });
            if (!res.ok) {
                const errBody = await res.json().catch(() => ({}));
                Alert.alert(t('profile.deleteAccountError'), errBody.message || `Delete failed (${res.status})`);
                return;
            }
            await apiLogout();
            logout();
            queryClient.clear();
            router.replace("/");
        } catch (err) {
            console.error("Delete account failed:", err);
            Alert.alert(t('profile.deleteAccountError'), String(err));
        }
    };

    // Tab root: no back affordance, because there is nothing to pop to.
    const Header = <ScreenHeader title={t('profile.title')} hideBack />;

    if (isLoading) {
        // Inside `PageLayout`, not instead of it: the previous bare centred `View`
        // dropped the header and the safe-area handling for the duration of the load.
        return (
            <PageLayout header={Header} disableScroll>
                <LoadingState fullScreen />
            </PageLayout>
        );
    }

    if (!isAuthenticated) {
        /*
         * The redirect interstitial. It previously rendered a hardcoded English
         * "SECURITY REDIRECT..." in a four-language app; the string is dropped
         * rather than translated because it is internal jargon on a view the user
         * passes through in milliseconds — the spinner alone says everything true
         * about the state.
         */
        return (
            <PageLayout header={Header} disableScroll>
                <LoadingState fullScreen />
            </PageLayout>
        );
    }

    return (
        <PageLayout header={Header} disableScroll>
            <ScrollView showsVerticalScrollIndicator={false} style={{ flex: 1, paddingHorizontal: GLOBAL_PADDING }}>
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
                                    />
                                </View>
                                <View style={{ flex: 1 }}>
                                    <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>{t('profile.lastName')}</Text>
                                    <TextInput
                                        value={personalForm.lastName}
                                        onChangeText={(text) => setPersonalForm(v => ({ ...v, lastName: text }))}
                                        style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
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
                                    autoCapitalize="none"
                                />
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
                                        {personalForm.birthdate ? personalForm.birthdate : "dd.mm.yyyy"}
                                    </Text>
                                </Pressable>
                            </View>
                        </View>
                    </View>

                    {/* Legal Entity Section */}
                    <View style={[styles.sectionCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                        <View style={styles.sectionHeader}>
                            <Building2 size={18} color={tokens.colors.primary} />
                            <Text allowFontScaling={false} style={[styles.sectionTitle, { color: tokens.colors.primary }]}>{t('profile.legalEntityTitle')}</Text>
                        </View>
                        
                        <Pressable 
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                setIsLegalEntity(!isLegalEntity);
                            }}
                            style={styles.toggleRow}
                        >
                            <Text style={{ flex: 1, color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold', fontSize: 16, marginRight: 16 }}>{t('profile.legalToggle')}</Text>
                            <View style={[styles.toggleSwitch, { backgroundColor: isLegalEntity ? tokens.colors.primary : tokens.colors.borderLight }]}>
                                {/*
                                  The knob has to contrast with whatever the track
                                  is. It used to be a static `#FFF`, which is a
                                  1.1:1 contrast against the light themes' `#EDECE7`
                                  off-track — the switch simply looked empty. On is
                                  `onPrimary` (the track is `primary`); off is
                                  `text.muted`, which also makes the off state read
                                  as inactive rather than merely displaced.
                                */}
                                <View
                                    style={[
                                        styles.toggleDot,
                                        {
                                            backgroundColor: isLegalEntity
                                                ? tokens.colors.text.onPrimary
                                                : tokens.colors.text.muted,
                                            transform: [{ translateX: isLegalEntity ? 20 : 0 }],
                                        },
                                    ]}
                                />
                            </View>
                        </Pressable>

                        {isLegalEntity ? (
                            <View style={{ marginTop: 20, gap: 16 }}>
                                {/*
                                  Was a hand-rolled row whose chevron wrapped onto
                                  a second line under the title and sat
                                  left-misaligned, on a 0.5px 8%-white hairline
                                  that is not perceptible on a black canvas — so a
                                  customer saw green text, not a row they could
                                  open. `ListItem` is the row primitive: 52pt
                                  minimum, title and subtitle in the type scale,
                                  and the chevron pinned to the trailing edge.
                                */}
                                <Card padding="none">
                                    <ListItem
                                        title={t('profile.documentsTitle')}
                                        subtitle={t('profile.documentsSubtitle')}
                                        leading={<FileSignature size={20} color={tokens.colors.primary} />}
                                        onPress={() => {
                                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                            router.push('/contracts');
                                        }}
                                    />
                                </Card>

                                <View style={{ height: 1, backgroundColor: tokens.colors.borderLight, marginVertical: 4 }} />
                                
                                <View style={{ gap: 16 }}>
                                    <View>
                                        <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>{t('profile.companyName')}</Text>
                                        <TextInput
                                            value={companyForm.name}
                                            onChangeText={(text) => setCompanyForm(v => ({ ...v, name: text }))}
                                            style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                        />
                                    </View>
                                    <View style={{ flexDirection: 'row', gap: 16 }}>
                                        <View style={{ flex: 1 }}>
                                            <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>{t('profile.edrpou')}</Text>
                                            <TextInput
                                                value={companyForm.edrpou}
                                                onChangeText={(text) => setCompanyForm(v => ({ ...v, edrpou: text }))}
                                                style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                                keyboardType="numeric"
                                            />
                                        </View>
                                        <View style={{ flex: 1 }}>
                                            <Text allowFontScaling={false} style={[styles.inputLabel, { color: tokens.colors.text.dim }]}>{t('profile.vatNumber')}</Text>
                                            <TextInput
                                                value={companyForm.vatNumber}
                                                onChangeText={(text) => setCompanyForm(v => ({ ...v, vatNumber: text }))}
                                                style={[styles.textInput, { backgroundColor: tokens.colors.background, color: tokens.colors.text.primary, borderColor: tokens.colors.borderLight }]}
                                                keyboardType="numeric"
                                            />
                                        </View>
                                    </View>
                                </View>
                            </View>
                        ) : (
                            <Text style={{ color: tokens.colors.text.dim, fontSize: 12, marginTop: 12, fontFamily: 'Inter-Medium', lineHeight: 18 }}>
                                {t('profile.legalDescription')}
                            </Text>
                        )}
                    </View>

                    {/* Company Management (owner only) */}
                    {hasCompany && (
                        <Card padding="none" style={{ marginBottom: 20 }}>
                            <ListItem
                                title={t('company.managementTitle')}
                                leading={<Users size={20} color={tokens.colors.primary} />}
                                onPress={() => {
                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                    router.push('/company');
                                }}
                            />
                        </Card>
                    )}

                    {/* Worker Invitations Inbox (shown when pending invites exist) */}
                    {pendingInvitationCount > 0 && (
                        <Card padding="none" style={{ marginBottom: 20 }}>
                            <ListItem
                                title={t('company.invitationsTitle')}
                                leading={<Mail size={20} color={tokens.colors.primary} />}
                                trailing={
                                    <Badge
                                        label={String(pendingInvitationCount)}
                                        status="primary"
                                        emphasis="solid"
                                    />
                                }
                                showChevron
                                onPress={() => {
                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                    router.push('/invitations');
                                }}
                            />
                        </Card>
                    )}

                    {/* Report Section */}
                    <Card padding="none" style={{ marginBottom: 20 }}>
                        <ListItem
                            title={t('profile.report')}
                            leading={<TrendingUp size={20} color={tokens.colors.primary} />}
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                router.push('/report');
                            }}
                        />
                    </Card>

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
                                            <Text allowFontScaling={false} style={[styles.langText, { color: active ? tokens.colors.primary : tokens.colors.text.dim }]}>{lang.name}</Text>
                                        </View>
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
                                        <View style={{ width: 12, height: 12, borderRadius: 6, backgroundColor: opt.color, marginRight: 8, borderWidth: 1, borderColor: tokens.colors.borderStrong }} />
                                        <View style={{ flex: 1 }}>
                                            <Text allowFontScaling={false} style={[styles.langText, { color: active ? tokens.colors.primary : tokens.colors.text.dim }]}>{t(opt.label)}</Text>
                                        </View>
                                    </Pressable>
                                );
                            })}
                        </View>
                    </View>

                    {/*
                      Action hierarchy. These three used to be `saveBtn`,
                      `logoutBtn` and `deleteAccountBtn` — the first two with
                      byte-identical geometry, one filled brand green and the other
                      filled `error` red. A filled red bar of exactly the same size
                      as the save button made "log out" the loudest thing on the
                      screen and left it indistinguishable in weight from deleting
                      the account outright.

                      Now: save is the single filled primary, signing out is a real
                      alternative (outlined), and deleting the account is the only
                      control in the danger role.
                    */}
                    <View style={{ gap: 16, marginTop: 12 }}>
                        <Button
                            label={t('common.save')}
                            onPress={() => {
                                updateProfileMutation.mutate(personalForm);
                                if (isLegalEntity) updateCompanyMutation.mutate(companyForm);
                            }}
                            loading={updateProfileMutation.isPending}
                            icon={<Save />}
                        />

                        <Button
                            label={t('profile.signOut')}
                            onPress={handleLogout}
                            variant="secondary"
                            size="md"
                            icon={<LogOut />}
                        />

                        <Button
                            label={t('profile.deleteAccount')}
                            onPress={() => {
                                Alert.alert(
                                    t('profile.deleteAccount'),
                                    t('profile.deleteAccountConfirm'),
                                    [
                                        { text: t('common.cancel'), style: 'cancel' },
                                        { text: t('profile.deleteAccount'), style: 'destructive', onPress: handleDeleteAccount },
                                    ]
                                );
                            }}
                            variant="destructive"
                            size="md"
                            hapticStyle="heavy"
                            icon={<Trash2 />}
                        />
                    </View>
                </View>
            </ScrollView>

            {/* iOS Date Picker Modal */}
            <Modal visible={showDatePicker && Platform.OS === 'ios'} transparent animationType="slide">
                <View style={{ flex: 1, justifyContent: 'flex-end', backgroundColor: tokens.colors.overlay }}>
                    <View style={{ backgroundColor: tokens.colors.background, borderTopWidth: 1, borderColor: tokens.colors.borderLight, paddingBottom: 40 }}>
                        <View style={{ flexDirection: 'row', justifyContent: 'space-between', padding: 20, borderBottomWidth: 1, borderColor: tokens.colors.borderLight }}>
                            <Pressable onPress={() => setShowDatePicker(false)} style={{ padding: 10 }}>
                                <Text style={{ color: tokens.colors.text.dim, fontFamily: 'Inter-Bold', fontSize: 16 }}>{t('common.cancel')}</Text>
                            </Pressable>
                            <Pressable onPress={() => {
                                const day = String(tempDate.getDate()).padStart(2, '0');
                                const month = String(tempDate.getMonth() + 1).padStart(2, '0');
                                const year = tempDate.getFullYear();
                                setPersonalForm(v => ({ ...v, birthdate: `${day}.${month}.${year}` }));
                                setShowDatePicker(false);
                            }} style={{ padding: 10 }}>
                                <Text style={{ color: tokens.colors.primary, fontFamily: 'Inter-Black', fontSize: 16 }}>{t('common.done')}</Text>
                            </Pressable>
                        </View>
                        <DateTimePicker
                            value={tempDate}
                            mode="date"
                            display="spinner"
                            textColor={tokens.colors.text.primary}
                            onChange={(event, date) => { if (date) setTempDate(date); }}
                        />
                    </View>
                </View>
            </Modal>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    profileHeader: { flexDirection: 'row', alignItems: 'center', gap: 20, marginBottom: 32, paddingHorizontal: 4 },
    avatarBox: { width: 64, height: 64, borderWidth: StyleSheet.hairlineWidth, padding: 2, borderRadius: 2 },
    avatarInner: { flex: 1, alignItems: 'center', justifyContent: 'center' },
    userName: { fontFamily: 'Rajdhani-Bold', fontSize: 32, textTransform: 'uppercase' },
    userPhone: { fontFamily: 'Inter-Black', fontSize: 18, letterSpacing: 1.5 },
    sectionCard: { padding: 20, borderRadius: 2, borderWidth: StyleSheet.hairlineWidth, marginBottom: 20 },
    sectionHeader: { flexDirection: 'row', alignItems: 'center', gap: 12, marginBottom: 20 },
    sectionTitle: { fontFamily: 'Rajdhani-SemiBold', fontSize: 12, letterSpacing: 4, textTransform: 'uppercase' },
    inputLabel: { fontFamily: 'Rajdhani-SemiBold', fontSize: 10, letterSpacing: 2, textTransform: 'uppercase', marginBottom: 8, marginLeft: 4 },
    textInput: { borderRadius: 2, paddingHorizontal: 16, paddingVertical: 12, fontFamily: 'Inter-Bold', fontSize: 14, borderWidth: 1 },
    langBtn: { flexDirection: 'row', alignItems: 'center', paddingHorizontal: 8, paddingVertical: 14, borderWidth: 1.5, borderRadius: 4, gap: 6 },
    langFlag: { fontSize: 18 },
    langText: { fontFamily: 'Inter-Black', fontSize: 10, textTransform: 'uppercase', letterSpacing: 1 },
    toggleRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
    toggleSwitch: { width: 44, height: 24, borderRadius: 12, padding: 2 },
    toggleDot: { width: 20, height: 20, borderRadius: 10 },
});
