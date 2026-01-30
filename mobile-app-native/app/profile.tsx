
import { useState } from 'react';
import { View, Text, TouchableOpacity, ScrollView, Image, TextInput, ActivityIndicator, Alert } from 'react-native';
import * as Clipboard from 'expo-clipboard';
import { useRouter } from 'expo-router';
import { useAuth } from '@/hooks/use-auth';
import { useI18n } from '@/lib/i18n';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { User, LogOut, Phone, Zap, Car, Gift, Bell, Check, Mail } from 'lucide-react-native';
import { PhoneAuth } from '@/components/phone-auth';
import { apiRequest, apiFetch } from '@/lib/api';
import { cn } from '@/lib/utils';
import { ProtectedRoute } from '@/components/ProtectedRoute';

export default function ProfileScreen() {
    const { user: rawUser, isLoading, isAuthenticated, authType } = useAuth();
    const user = rawUser as any;
    const { t } = useI18n();
    const [showPhoneAuth, setShowPhoneAuth] = useState(false);
    const [referralInput, setReferralInput] = useState("");
    const queryClient = useQueryClient();
    const router = useRouter();

    const { data: notifications = [] } = useQuery<any[]>({
        queryKey: ["/api/notifications"],
        enabled: !!user,
        queryFn: async () => {
            const res = await apiFetch("/api/notifications");
            return res.json();
        }
    });

    const markReadMutation = useMutation({
        mutationFn: async (id: number) => {
            await apiFetch(`/api/notifications/${id}/read`, { method: "POST" });
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: ["/api/notifications"] });
        },
    });

    const redeemMutation = useMutation({
        mutationFn: async (code: string) => {
            const res = await apiFetch("/api/referral/redeem", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ code })
            });
            return res.json();
        },
        onSuccess: () => {
            queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
            queryClient.invalidateQueries({ queryKey: ["/api/notifications"] });
            setReferralInput("");
            Alert.alert("Success", "Referral code redeemed! Bonus applied.");
        },
        onError: (err: any) => {
            Alert.alert("Error", err.message || "Failed to redeem code");
        }
    });

    const handleLogout = async () => {
        try {
            await apiFetch("/api/auth/phone/logout", { method: "POST" });
            queryClient.clear();
            router.replace("/");
        } catch (err) {
            console.error("Logout failed:", err);
            router.replace("/");
        }
    };

    const handlePhoneAuthSuccess = async () => {
        await queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
        setShowPhoneAuth(false);
        router.replace("/");
    };

    const handleUpdateUser = async (field: string, value: string) => {
        try {
            await apiFetch("/api/users/update", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ [field]: value })
            });
            queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
        } catch (e) {
            console.error("Update failed", e);
        }
    };

    if (isLoading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
            </View>
        );
    }

    if (!isAuthenticated && !showPhoneAuth) {
        return (
            <View className="flex-1 bg-[#050505] items-center justify-center p-[32px] relative">
                {/* Background glow - MECHANICAL REPLICATION */}
                <View
                    className="absolute top-1/3 left-1/2 -ml-[128px] w-[256px] h-[256px] bg-[#00FF80]/20 rounded-full opacity-20"
                    style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                />

                <View className="items-center w-full relative z-10">
                    <View className="w-[96px] h-[96px] bg-[#00FF80]/10 border-4 border-[#00FF80]/30 items-center justify-center mb-8">
                        <User size={48} color="#00FF80" />
                    </View>
                    <Text className="text-[36px] font-black text-white font-heading uppercase text-center mb-4 tracking-wider">
                        {t('profile.accessRequired')}
                    </Text>
                    <Text className="text-gray-400 font-mono text-sm mb-8 text-center max-w-xs leading-relaxed">
                        {t('profile.signInDesc')}
                    </Text>
                    <TouchableOpacity
                        onPress={() => setShowPhoneAuth(true)}
                        className="w-full bg-[#00FF80] py-[16px] flex-row items-center justify-center gap-3 shadow-[0_0_40px_rgba(0,255,128,0.5)] active:scale-[0.98]"
                    >
                        <Phone size={24} color="black" />
                        <Text className="text-black font-black text-[18px] font-heading uppercase tracking-wider">
                            {t('phoneAuth.title')}
                        </Text>
                    </TouchableOpacity>
                    <Text className="text-[10px] text-gray-600 font-mono mt-6 uppercase tracking-wider">
                        SMS VERIFICATION REQUIRED
                    </Text>
                </View>
            </View>
        );
    }

    if (showPhoneAuth) {
        return (
            <View className="flex-1 bg-[#050505] p-[24px] pt-[40px] items-center relative">
                <View
                    className="absolute top-0 right-0 w-[256px] h-[256px] bg-[#00FF80]/10 rounded-full opacity-10"
                    style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                />
                <PhoneAuth onSuccess={handlePhoneAuthSuccess} />
                <TouchableOpacity
                    onPress={() => setShowPhoneAuth(false)}
                    className="w-full py-[16px] items-center"
                >
                    <Text className="text-gray-500 font-mono text-sm">{t('common.back')}</Text>
                </TouchableOpacity>
            </View>
        );
    }

    return (
        <ProtectedRoute>
            <View className="flex-1 bg-[#050505]">
                <View
                    className="absolute top-0 right-0 w-[256px] h-[256px] bg-[#00FF80]/10 rounded-full opacity-10"
                    style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                />

                <ScrollView className="flex-1 p-[24px] z-10" contentContainerStyle={{ paddingBottom: 150 }}>
                    <View className="mt-[32px] mb-[32px]">
                        <Text className="text-[36px] font-black text-white font-heading uppercase tracking-wider">{t('profile.title')}</Text>
                        <Text className="text-xs text-[#00FF80] font-mono tracking-[0.2em] uppercase mt-1">
                            {t('profile.subtitle')}
                        </Text>
                    </View>

                    <View className="gap-[24px]">
                        {/* Notifications */}
                        {notifications.length > 0 && (
                            <View className="bg-black/80 border-2 border-[#00FF80]/30 p-[24px] space-y-[16px]">
                                <View className="flex-row items-center gap-2 mb-4">
                                    <Bell size={20} color="#00FF80" />
                                    <Text className="text-[18px] font-black text-white font-heading uppercase">Updates</Text>
                                </View>
                                <View className="gap-[12px]">
                                    {notifications.map((n) => (
                                        <TouchableOpacity
                                            key={n.id}
                                            onPress={() => !n.read && markReadMutation.mutate(n.id)}
                                            className={cn("p-[12px] border rounded-[8px] transition-all",
                                                n.read ? 'bg-white/5 border-white/5 opacity-50' : 'bg-[#00FF80]/5 border-[#00FF80]/20'
                                            )}
                                        >
                                            <View className="flex-row justify-between items-start mb-1">
                                                <Text className={cn("font-bold text-sm uppercase", n.read ? 'text-gray-400' : 'text-[#00FF80]')}>{n.title}</Text>
                                                {!n.read && <View className="w-2 h-2 rounded-full bg-[#00FF80] animate-pulse" />}
                                            </View>
                                            <Text className="text-xs text-gray-300 font-mono">{n.message}</Text>
                                        </TouchableOpacity>
                                    ))}
                                </View>
                            </View>
                        )}

                        {/* User Card */}
                        <View className="bg-black/80 border-2 border-[#00FF80]/30 p-[24px]">
                            <View className="flex-row items-center gap-4 mb-6">
                                <View className="w-[80px] h-[80px] bg-[#00FF80]/20 border-2 border-[#00FF80] items-center justify-center">
                                    {user?.profileImageUrl ? (
                                        <Image source={{ uri: user.profileImageUrl }} className="w-full h-full" />
                                    ) : (
                                        <User size={40} color="#00FF80" />
                                    )}
                                </View>
                                <View>
                                    <Text className="text-[24px] font-black text-white font-heading uppercase tracking-wider">
                                        {user?.firstName || 'Operator'}
                                    </Text>
                                    {user?.lastName && (
                                        <Text className="text-gray-400 font-heading uppercase">{user.lastName}</Text>
                                    )}
                                </View>
                            </View>

                            {user?.phone && (
                                <View className="flex-row items-center gap-3 text-gray-400 bg-white/5 p-3 border border-white/10">
                                    <Phone size={20} color="#00FF80" />
                                    <Text className="font-mono text-sm text-gray-400">{user.phone}</Text>
                                </View>
                            )}
                        </View>

                        {/* Info Section: Personal */}
                        <View className="bg-black/80 border-2 border-[#00FF80]/30 p-[24px] gap-[16px]">
                            <View className="flex-row items-center gap-2 mb-2">
                                <User size={20} color="#00FF80" />
                                <Text className="text-[18px] font-black text-white font-heading uppercase">Personal Information</Text>
                            </View>
                            <View className="gap-[16px]">
                                <View className="flex-row gap-4">
                                    <View className="flex-1 gap-1">
                                        <Text className="text-xs text-gray-500 font-bold uppercase">First Name</Text>
                                        <TextInput
                                            className="w-full bg-white/5 border border-white/10 px-3 py-2 text-white font-mono text-sm"
                                            placeholder="John"
                                            placeholderTextColor="#333"
                                            defaultValue={user?.firstName}
                                            onBlur={(e) => handleUpdateUser('firstName', e.nativeEvent.text)}
                                        />
                                    </View>
                                    <View className="flex-1 gap-1">
                                        <Text className="text-xs text-gray-500 font-bold uppercase">Last Name</Text>
                                        <TextInput
                                            className="w-full bg-white/5 border border-white/10 px-3 py-2 text-white font-mono text-sm"
                                            placeholder="Doe"
                                            placeholderTextColor="#333"
                                            defaultValue={user?.lastName}
                                            onBlur={(e) => handleUpdateUser('lastName', e.nativeEvent.text)}
                                        />
                                    </View>
                                </View>
                                <View className="gap-1">
                                    <Text className="text-xs text-gray-500 font-bold uppercase">Email</Text>
                                    <TextInput
                                        className="w-full bg-white/5 border border-white/10 px-3 py-2 text-white font-mono text-sm"
                                        placeholder="john.doe@example.com"
                                        placeholderTextColor="#333"
                                        defaultValue={user?.email}
                                        onBlur={(e) => handleUpdateUser('email', e.nativeEvent.text)}
                                        keyboardType="email-address"
                                    />
                                </View>
                            </View>
                        </View>

                        {/* Info Section: Vehicle */}
                        <View className="bg-black/80 border-2 border-[#00FF80]/30 p-[24px] gap-[16px]">
                            <View className="flex-row items-center gap-2 mb-2">
                                <Car size={20} color="#00FF80" />
                                <Text className="text-[18px] font-black text-white font-heading uppercase">Vehicle Details</Text>
                            </View>
                            <View className="flex-row gap-4">
                                <View className="flex-1 gap-1">
                                    <Text className="text-xs text-gray-500 font-bold uppercase">Make</Text>
                                    <TextInput
                                        className="w-full bg-white/5 border border-white/10 px-3 py-2 text-white font-mono text-sm"
                                        placeholder="e.g. BMW"
                                        placeholderTextColor="#333"
                                        defaultValue={user?.vehicleMake}
                                        onBlur={(e) => handleUpdateUser('vehicleMake', e.nativeEvent.text)}
                                    />
                                </View>
                                <View className="flex-1 gap-1">
                                    <Text className="text-xs text-gray-500 font-bold uppercase">Model</Text>
                                    <TextInput
                                        className="w-full bg-white/5 border border-white/10 px-3 py-2 text-white font-mono text-sm"
                                        placeholder="e.g. X5"
                                        placeholderTextColor="#333"
                                        defaultValue={user?.vehicleModel}
                                        onBlur={(e) => handleUpdateUser('vehicleModel', e.nativeEvent.text)}
                                    />
                                </View>
                            </View>
                        </View>

                        {/* Referral System */}
                        <View className="bg-black/80 border-2 border-[#00FF80]/30" style={{ backgroundColor: 'rgba(0, 255, 128, 0.05)' }}>
                            <View className="p-[24px] gap-[16px]">
                                <View className="flex-row items-center gap-2">
                                    <Gift size={20} color="#00FF80" />
                                    <Text className="text-[18px] font-black text-white font-heading uppercase">Referral Program</Text>
                                </View>

                                <View className="flex-row items-center justify-between bg-black/40 p-[16px] border border-[#00FF80]/20 rounded-[8px]">
                                    <View>
                                        <Text className="text-xs text-gray-400 font-mono uppercase tracking-wider">Your Bonus Balance</Text>
                                        <Text className="text-[30px] font-black text-[#00FF80] font-heading tracking-tight">{user?.bonusBalance || 0} UAH</Text>
                                    </View>
                                    <Gift size={40} color="#00FF80" opacity={0.2} />
                                </View>

                                {user?.referralCode ? (
                                    <View className="gap-2">
                                        <Text className="text-xs text-gray-500 font-bold uppercase">Your Invite Code</Text>
                                        <View className="flex-row gap-2">
                                            <View className="flex-1 bg-white/5 border border-white/10 rounded-[8px] flex items-center justify-center p-[12px] border-dashed">
                                                <Text className="font-mono text-[20px] tracking-widest text-[#00FF80] font-black">{user.referralCode}</Text>
                                            </View>
                                            <TouchableOpacity
                                                onPress={() => Clipboard.setStringAsync(user.referralCode!)}
                                                className="bg-white/10 px-[16px] justify-center rounded-[8px]"
                                            >
                                                <Text className="text-white font-bold text-xs uppercase">COPY</Text>
                                            </TouchableOpacity>
                                        </View>
                                    </View>
                                ) : (
                                    <TouchableOpacity
                                        onPress={async () => {
                                            const code = Math.random().toString(36).substring(2, 8).toUpperCase();
                                            await apiFetch("/api/referral/create", {
                                                method: "POST",
                                                headers: { "Content-Type": "application/json" },
                                                body: JSON.stringify({ code })
                                            });
                                            queryClient.invalidateQueries({ queryKey: ["/api/auth/phone/user"] });
                                        }}
                                        className="w-full bg-[#00FF80] py-[12px] items-center rounded-[8px]"
                                    >
                                        <Text className="text-black font-black uppercase tracking-wider">Generate Invite Code</Text>
                                    </TouchableOpacity>
                                )}

                                {!user?.referredBy && (
                                    <View className="mt-4 pt-4 border-t border-[#00FF80]/20">
                                        <Text className="text-xs text-gray-500 font-bold uppercase block mb-2">Have a code?</Text>
                                        <View className="flex-row gap-2">
                                            <TextInput
                                                value={referralInput}
                                                onChangeText={(t) => setReferralInput(t.toUpperCase())}
                                                placeholder="CODE"
                                                placeholderTextColor="#333"
                                                className="flex-1 bg-white/5 border border-white/10 rounded-[8px] px-3 py-2 text-white font-mono tracking-widest uppercase"
                                            />
                                            <TouchableOpacity
                                                onPress={() => redeemMutation.mutate(referralInput)}
                                                disabled={!referralInput}
                                                className="bg-[#00FF80]/20 border border-[#00FF80]/50 rounded-[8px] px-4 justify-center"
                                            >
                                                <Text className="text-[#00FF80] font-bold uppercase text-[10px]">Redeem</Text>
                                            </TouchableOpacity>
                                        </View>
                                    </View>
                                )}
                            </View>
                        </View>

                        {/* Actions */}
                        <TouchableOpacity
                            onPress={handleLogout}
                            className="w-full bg-red-500/20 border-2 border-red-500/50 py-[16px] flex-row items-center justify-center gap-3 active:scale-[0.98] mb-[64px]"
                        >
                            <LogOut size={24} color="#F87171" />
                            <Text className="text-[#F87171] font-black text-[18px] font-heading uppercase tracking-wider">
                                {t('profile.signOut')}
                            </Text>
                        </TouchableOpacity>
                    </View>
                </ScrollView>
            </View>
        </ProtectedRoute>
    );
}
