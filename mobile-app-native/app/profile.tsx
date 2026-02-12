import { useState, useEffect } from "react";
import { View, Text, Pressable, TextInput, ScrollView, ActivityIndicator, Image } from "react-native";
import { useRouter } from "expo-router";
import { User, LogIn, LogOut, Mail, Phone, Zap, Car, Gift, Bell, Globe, Save } from "lucide-react-native";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useI18n, languages } from "../src/lib/i18n";
import { PhoneAuth } from "../src/components/phone-auth";
import { apiRequest } from "../src/lib/utils";
import { useAuth } from "../src/hooks/useAuth";
import { PageLayout } from "../src/components/page-layout";

export default function ProfileScreen() {
    const router = useRouter();
    const queryClient = useQueryClient();
    const { user, isLoading, isAuthenticated, authType } = useAuth();
    const { t, language, setLanguage } = useI18n();
    const [showPhoneAuth, setShowPhoneAuth] = useState(false);
    const [referralInput, setReferralInput] = useState("");

    const [personalForm, setPersonalForm] = useState({
        firstName: "",
        lastName: "",
        email: "",
        birthdate: ""
    });

    const [vehicleForm, setVehicleForm] = useState({
        vehicleMake: "",
        vehicleModel: "",
        vehiclePlate: "",
        vehicleFuelType: ""
    });

    useEffect(() => {
        if (user) {
            setPersonalForm({
                firstName: user.firstName || "",
                lastName: user.lastName || "",
                email: user.email || "",
                birthdate: user.birthdate || ""
            });
            setVehicleForm({
                vehicleMake: user.vehicleMake || "",
                vehicleModel: user.vehicleModel || "",
                vehiclePlate: user.vehiclePlate || "",
                vehicleFuelType: user.vehicleFuelType || ""
            });
        }
    }, [user]);

    const { data: notifications = [] } = useQuery<any[]>({
        queryKey: ["/api/notifications"],
        enabled: !!user,
    });

    const updateProfileMutation = useMutation({
        mutationFn: async (data: any) => {
            const res = await apiRequest("POST", `/api/users/update`, data);
            return res.json();
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: ["/api/auth/user"] });
        }
    });

    const handleLogout = async () => {
        try {
            await apiRequest("POST", "/api/auth/phone/logout");
            queryClient.clear();
            router.push("/");
        } catch (err) {
            console.error("Logout failed:", err);
            router.push("/");
        }
    };

    const handlePhoneAuthSuccess = async () => {
        await queryClient.invalidateQueries({ queryKey: ["/api/auth/user"] });
        setShowPhoneAuth(false);
        router.push("/");
    };

    if (isLoading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
                <Text className="text-[#00FF80] font-bold mt-4 uppercase tracking-widest">{t('common.loading')}</Text>
            </View>
        );
    }

    if (!isAuthenticated && !showPhoneAuth) {
        return (
            <PageLayout>
                <View className="flex-1 items-center justify-center p-8 py-20">
                    <View className="w-24 h-24 bg-[#00FF8010] border-2 border-[#00FF8030] items-center justify-center mb-8 rounded">
                        <User size={48} color="#00FF80" />
                    </View>
                    <Text className="text-3xl font-black text-white uppercase text-center mb-4">{t('profile.accessRequired')}</Text>
                    <Text className="text-gray-500 text-center mb-10 uppercase font-bold text-xs tracking-widest px-4">{t('profile.signInDesc')}</Text>

                    <Pressable
                        onPress={() => setShowPhoneAuth(true)}
                        className="w-full bg-[#00FF80] py-4 rounded flex-row items-center justify-center gap-3 active:scale-95"
                    >
                        <Phone size={24} color="#000" />
                        <Text className="text-black font-black text-lg uppercase tracking-widest">{t('phoneAuth.title')}</Text>
                    </Pressable>

                    <Text className="text-[10px] text-gray-700 font-bold uppercase tracking-widest mt-6">
                        {t('phoneAuth.verificationRequired')}
                    </Text>
                </View>
            </PageLayout>
        );
    }

    if (showPhoneAuth) {
        return (
            <PageLayout scrollClassName="p-6 pt-12">
                <PhoneAuth onSuccess={handlePhoneAuthSuccess} />
                <Pressable
                    onPress={() => setShowPhoneAuth(false)}
                    className="w-full items-center py-6"
                >
                    <Text className="text-gray-500 font-bold uppercase tracking-widest">{t('common.back')}</Text>
                </Pressable>
            </PageLayout>
        );
    }

    const Header = (
        <View className="p-6 pt-12 border-b border-white/5 bg-zinc-950">
            <Text className="text-3xl font-black text-white uppercase">{t('profile.title')}</Text>
            <Text className="text-[10px] text-[#00FF80] font-bold tracking-[0.2em] uppercase mt-1">
                {t('profile.subtitle')}
            </Text>
        </View>
    );

    return (
        <PageLayout header={Header} scrollClassName="p-4 space-y-4">
            {/* User Info Card */}
            <View className="bg-zinc-900/80 border border-[#00FF8030] p-6 rounded-lg">
                <View className="flex-row items-center gap-4 mb-6">
                    <View className="w-20 h-20 bg-[#00FF8020] border border-[#00FF80] items-center justify-center rounded">
                        {user?.profileImageUrl ? (
                            <Image source={{ uri: user.profileImageUrl }} className="w-full h-full rounded" />
                        ) : (
                            <User size={40} color="#00FF80" />
                        )}
                    </View>
                    <View className="flex-1">
                        <Text className="text-2xl font-black text-white uppercase">
                            {user?.firstName || user?.phone || "OPERATOR"}
                        </Text>
                        <Text className="text-gray-400 font-bold uppercase">{user?.lastName}</Text>
                    </View>
                </View>

                {user?.phone ? (
                    <View className="flex-row items-center gap-3 bg-white/5 p-3 rounded border border-white/10">
                        <Phone size={18} color="#00FF80" />
                        <Text className="text-white font-bold">{user.phone}</Text>
                    </View>
                ) : null}
            </View>

            {/* Personal Data */}
            <View className="bg-zinc-900 border border-[#00FF8030] p-6 rounded-lg">
                <View className="flex-row items-center gap-2 border-b border-white/10 pb-4 mb-6">
                    <User size={20} color="#00FF80" />
                    <Text className="text-lg font-black text-white uppercase">{t('profile.personalInfo')}</Text>
                </View>

                <View className="space-y-4">
                    <View>
                        <Text className="text-[10px] text-gray-500 font-bold uppercase mb-2">{t('profile.firstName')}</Text>
                        <TextInput
                            value={personalForm.firstName}
                            onChangeText={(text) => setPersonalForm(prev => ({ ...prev, firstName: text }))}
                            className="bg-black/50 border border-white/10 p-3 text-white rounded"
                        />
                    </View>
                    <View>
                        <Text className="text-[10px] text-gray-500 font-bold uppercase mb-2">{t('profile.lastName')}</Text>
                        <TextInput
                            value={personalForm.lastName}
                            onChangeText={(text) => setPersonalForm(prev => ({ ...prev, lastName: text }))}
                            className="bg-black/50 border border-white/10 p-3 text-white rounded"
                        />
                    </View>
                    <View>
                        <Text className="text-[10px] text-gray-500 font-bold uppercase mb-2">{t('profile.email')}</Text>
                        <TextInput
                            value={personalForm.email}
                            onChangeText={(text) => setPersonalForm(prev => ({ ...prev, email: text }))}
                            className="bg-black/50 border border-white/10 p-3 text-white rounded"
                            keyboardType="email-address"
                        />
                    </View>
                </View>

                <Pressable
                    onPress={() => updateProfileMutation.mutate({ ...personalForm, ...vehicleForm })}
                    disabled={updateProfileMutation.isPending}
                    className="w-full bg-[#00FF80] py-4 rounded flex-row items-center justify-center gap-2 mt-6 active:scale-95"
                >
                    {updateProfileMutation.isPending ? <ActivityIndicator color="#000" /> : <Save size={20} color="#000" />}
                    <Text className="text-black font-black uppercase tracking-widest">{t('common.save')}</Text>
                </Pressable>
            </View>

            {/* Language Settings */}
            <View className="bg-zinc-900 border border-[#00FF8030] p-6 rounded-lg">
                <View className="flex-row items-center gap-2 mb-6">
                    <Globe size={20} color="#00FF80" />
                    <Text className="text-lg font-black text-white uppercase">{t('profile.language')}</Text>
                </View>
                <View className="flex-row flex-wrap gap-2">
                    {languages.map((lang) => (
                        <Pressable
                            key={lang.code}
                            onPress={() => setLanguage(lang.code)}
                            className={`flex-1 min-w-[45%] p-4 border rounded-lg flex-row items-center gap-3 ${language === lang.code ? 'bg-[#00FF8020] border-[#00FF80]' : 'bg-white/5 border-white/10'
                                }`}
                        >
                            <Text className="text-2xl">{lang.flag}</Text>
                            <Text className={`font-bold uppercase text-xs ${language === lang.code ? 'text-[#00FF80]' : 'text-gray-400'}`}>
                                {lang.name}
                            </Text>
                        </Pressable>
                    ))}
                </View>
            </View>

            {/* Logout */}
            <Pressable
                onPress={handleLogout}
                className="w-full bg-red-500/10 border border-red-500/50 py-4 rounded flex-row items-center justify-center gap-2 mb-10 active:bg-red-500/20"
            >
                <LogOut size={20} color="#EF4444" />
                <Text className="text-red-500 font-black uppercase tracking-widest">{t('profile.signOut')}</Text>
            </Pressable>
        </PageLayout>
    );
}
