
import { useState, useEffect } from 'react';
import { View, Text, TouchableOpacity, ScrollView, Modal, Image, ActivityIndicator, Alert, Dimensions } from 'react-native';
import { useRouter } from 'expo-router';
import { useI18n } from '@/lib/i18n';
import { getMyVouchers, Voucher, markVoucherAsUsed, restoreVoucher, getMyOrders, Order } from '@/lib/api';
import { X, Copy, QrCode as QrIcon, Zap, Wallet, ShieldCheck, AlertTriangle, CheckCircle2, RotateCcw, Clock } from 'lucide-react-native';
import QRCode from 'react-native-qrcode-svg';
import { cn } from '@/lib/utils';
import { ProtectedRoute } from '@/components/ProtectedRoute';
import Layout from '@/components/layout';

export default function MyCodesScreen() {
    const [vouchers, setVouchers] = useState<Voucher[]>([]);
    const [orders, setOrders] = useState<Order[]>([]);
    const [loading, setLoading] = useState(true);
    const [selectedVoucher, setSelectedVoucher] = useState<Voucher | null>(null);
    const { t } = useI18n();
    const router = useRouter();

    useEffect(() => {
        loadData();
    }, []);

    const loadData = async () => {
        try {
            setLoading(true);
            const [vouchersData, ordersData] = await Promise.all([
                getMyVouchers(),
                getMyOrders()
            ]);
            setVouchers(Array.isArray(vouchersData) ? vouchersData : []);
            setOrders(Array.isArray(ordersData) ? ordersData : []);
        } catch (error: any) {
            console.error("Failed to load data:", error);
            if (!error.message?.includes('401')) {
                Alert.alert("Error", "Failed to load your vouchers");
            }
        } finally {
            setLoading(false);
        }
    };

    const toggleUsed = async (voucher: Voucher) => {
        try {
            const isCurrentlyUsed = voucher.status === 'used';
            if (isCurrentlyUsed) {
                await restoreVoucher(voucher.id);
            } else {
                await markVoucherAsUsed(voucher.id);
            }
            await loadData();

            // Update the local selected voucher state as well
            if (selectedVoucher && selectedVoucher.id === voucher.id) {
                setSelectedVoucher({ ...voucher, status: isCurrentlyUsed ? 'active' : 'used' });
            }
        } catch (error: any) {
            console.error('Failed to toggle voucher status:', error);
            Alert.alert("Error", error.message || 'Failed to update voucher status');
        }
    };

    if (loading) {
        return (
            <View className="flex-1 bg-[#050505] items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
                <Text className="text-[#00FF80] font-mono mt-4 text-xs tracking-[0.2em] uppercase animate-pulse">PROTOCOL_SYNC_ACTIVE</Text>
            </View>
        );
    }

    return (
        <ProtectedRoute>
            <Layout>
                <View className="flex-1 bg-[#050505] relative">
                    {/* Background Atmospheric Glow */}
                    <View
                        className="absolute top-0 right-0 w-[256px] h-[256px] bg-[#00FF80]/10 rounded-full opacity-10"
                        style={{ shadowColor: '#00FF80', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 100 }}
                    />

                    <ScrollView className="flex-1 p-[24px] relative z-10" contentContainerStyle={{ paddingBottom: 160 }}>
                        <View className="mb-[32px]">
                            <View className="flex-row items-center gap-4 mb-2">
                                <Wallet size={32} color="#00FF80" />
                                <View>
                                    <Text className="text-[36px] font-black text-white font-heading uppercase tracking-tighter">
                                        {t('codes.myAssets')}
                                    </Text>
                                    <View className="flex-row items-center gap-2 mt-1">
                                        <View className="h-[2px] w-6 bg-[#00FF80]/50" />
                                        <Text className="text-[#00FF80] text-[10px] font-mono tracking-[0.3em] uppercase">
                                            {t('codes.secureVault')}
                                        </Text>
                                    </View>
                                </View>
                            </View>
                        </View>

                        {vouchers.length === 0 && orders.length === 0 ? (
                            <View className="items-center justify-center py-[40px] border-2 border-dashed border-white/10 bg-black/50">
                                <QrIcon size={64} color="#1F1F1F" className="mb-4" />
                                <Text className="font-mono text-[18px] uppercase tracking-widest text-gray-500 mb-2">{t('codes.noActiveCodes')}</Text>
                                <Text className="text-[10px] text-gray-600 font-mono uppercase tracking-[0.2em]">{t('codes.purchaseToStart')}</Text>
                            </View>
                        ) : (
                            <View className="gap-[24px]">
                                {/* Pending Orders Section */}
                                {orders.filter(o => o.status === 'PENDING_FULFILLMENT').length > 0 && (
                                    <View className="gap-[12px]">
                                        <View className="flex-row items-center gap-2 mb-1">
                                            <Clock size={16} color="#EAB308" />
                                            <Text className="text-xs font-mono uppercase tracking-[0.2em] text-[#EAB308]">PENDING FULFILLMENT // LIVE</Text>
                                        </View>
                                        {orders.filter(o => o.status === 'PENDING_FULFILLMENT').map((order) => (
                                            <View key={order.id} className="bg-black/80 border-2 border-[#EAB308]/30 p-[20px] relative overflow-hidden">
                                                <View className="absolute top-0 left-0 right-0 h-[2px] bg-[#EAB308]" />
                                                <View className="flex-row items-center justify-between">
                                                    <View>
                                                        <Text className="font-black text-white text-[24px] font-heading uppercase tracking-tight">{order.provider}</Text>
                                                        <View className="flex-row items-center gap-2 mt-2">
                                                            <Text className="text-gray-500 font-mono text-[10px] uppercase tracking-wider">{order.fuelType}</Text>
                                                            <View className="w-1 h-1 rounded-full bg-gray-700" />
                                                            <Text className="text-[#EAB308] text-sm font-black font-mono">{order.liters}L</Text>
                                                        </View>
                                                    </View>
                                                    <View className="bg-[#EAB308]/10 border-2 border-[#EAB308]/30 px-3 py-1">
                                                        <Text className="text-[#EAB308] text-[10px] font-black uppercase font-mono tracking-widest">VERIFYING</Text>
                                                    </View>
                                                </View>
                                            </View>
                                        ))}
                                    </View>
                                )}

                                {/* Vouchers Section */}
                                <View className="gap-[16px]">
                                    <View className="flex-row items-center gap-2 mb-2">
                                        <ShieldCheck size={16} color="#00FF80" />
                                        <Text className="text-xs font-mono uppercase tracking-[0.2em] text-gray-500">ACTIVE ASSETS</Text>
                                    </View>
                                    {vouchers.map((voucher) => {
                                        const isUsed = voucher.status === 'used';
                                        return (
                                            <TouchableOpacity
                                                key={voucher.id}
                                                onPress={() => setSelectedVoucher(voucher)}
                                                className={cn("flex-row bg-black border-2 overflow-hidden active:scale-[0.98]",
                                                    isUsed ? 'border-gray-900 opacity-60' : 'border-white/10 shadow-[0_0_20px_rgba(255,255,255,0.05)]'
                                                )}
                                            >
                                                <View className={cn("w-[80px] border-r-2 items-center justify-center p-3",
                                                    isUsed ? 'bg-gray-950 border-gray-900' : 'bg-white/5 border-white/10'
                                                )}>
                                                    {voucher.qrCodeUrl ? (
                                                        <Image source={{ uri: voucher.qrCodeUrl }} className="w-full h-full opacity-40 grayscale" style={{ resizeMode: 'contain' }} />
                                                    ) : (
                                                        <QrIcon size={24} color={isUsed ? '#333' : '#00FF80'} opacity={isUsed ? 1 : 0.6} />
                                                    )}
                                                </View>
                                                <View className="flex-1 p-[20px] justify-center relative">
                                                    <View className="flex-row items-center justify-between">
                                                        <Text className={cn("font-black text-[24px] font-heading uppercase tracking-tight",
                                                            isUsed ? 'text-gray-600' : 'text-white'
                                                        )}>
                                                            {voucher.provider}
                                                        </Text>
                                                        <Text className={cn("font-black text-[18px] font-mono tracking-tight",
                                                            isUsed ? 'text-gray-700' : 'text-[#00FF80]'
                                                        )}>
                                                            {voucher.amount}L
                                                        </Text>
                                                    </View>
                                                    <Text className="text-gray-500 font-mono text-[10px] uppercase tracking-[0.2em] mt-1">{voucher.fuelType}</Text>
                                                    <View className="flex-row items-center gap-2 mt-4">
                                                        <View className={cn("w-1.5 h-1.5 rounded-full", isUsed ? 'bg-gray-800' : 'bg-[#00FF80] shadow-[0_0_8px_#00FF80]')} />
                                                        <Text className="text-[9px] text-gray-700 font-mono tracking-widest uppercase">
                                                            REF: FF-{voucher.externalId || voucher.id.substring(0, 8)}
                                                        </Text>
                                                    </View>
                                                </View>
                                                {isUsed && (
                                                    <View className="absolute inset-0 items-center justify-center bg-black/40">
                                                        <View className="border-4 border-white/10 px-8 py-3 -rotate-12">
                                                            <Text className="text-white/10 font-black text-5xl uppercase tracking-[0.3em]">
                                                                {t('codes.used')}
                                                            </Text>
                                                        </View>
                                                    </View>
                                                )}
                                            </TouchableOpacity>
                                        );
                                    })}
                                </View>
                            </View>
                        )}

                        <View className="mt-[48px] items-center">
                            <Text className="text-[10px] text-gray-700 font-mono tracking-[0.3em] uppercase">// SYSTEM STATUS: SECURE</Text>
                        </View>
                    </ScrollView>

                    {/* Voucher Modal */}
                    <Modal
                        visible={!!selectedVoucher}
                        transparent={true}
                        animationType="none"
                        onRequestClose={() => setSelectedVoucher(null)}
                    >
                        <View className="flex-1 bg-black/98 items-center justify-center p-[24px]">
                            {selectedVoucher && (
                                <View className="w-full bg-[#050505] border-2 border-white/20 shadow-[0_0_80px_rgba(0,255,128,0.2)]">
                                    {/* Header */}
                                    <View className="p-[32px] relative border-b-2 border-white/10 overflow-hidden">
                                        <View className={cn("absolute inset-0 opacity-10",
                                            selectedVoucher.provider === 'OKKO' ? 'bg-[#00FF80]' :
                                                selectedVoucher.provider === 'WOG' ? 'bg-[#00FF80]' :
                                                    'bg-[#EAB308]'
                                        )} />

                                        <TouchableOpacity
                                            onPress={() => setSelectedVoucher(null)}
                                            className="absolute top-6 right-6 z-30 w-[48px] h-[48px] bg-black border-2 border-white/10 items-center justify-center active:scale-[0.98]"
                                        >
                                            <X size={24} color="white" />
                                        </TouchableOpacity>

                                        <View className="items-center mt-6 relative z-10">
                                            <Text className="text-[60px] font-black text-white font-heading uppercase tracking-tighter mb-2">
                                                {selectedVoucher.provider}
                                            </Text>
                                            <View className="flex-row items-center justify-center gap-[48px] mt-10 w-full">
                                                <View className="items-center">
                                                    <Text className="text-[10px] text-gray-500 uppercase font-mono tracking-widest mb-1">GRADE</Text>
                                                    <Text className="text-[30px] font-black text-white font-heading">{selectedVoucher.fuelType}</Text>
                                                </View>
                                                <View className="w-[2px] h-10 bg-white/10" />
                                                <View className="items-center">
                                                    <Text className="text-[10px] text-gray-500 uppercase font-mono tracking-widest mb-1">VOLUME</Text>
                                                    <Text className="text-[30px] font-black text-[#00FF80] font-heading">{selectedVoucher.amount}L</Text>
                                                </View>
                                            </View>
                                        </View>
                                    </View>

                                    {/* QR Code Display Section */}
                                    <View className="p-[40px] items-center bg-black">
                                        <View className={cn("relative p-4 bg-white border-8 border-white", selectedVoucher.status === 'used' ? 'opacity-20 grayscale' : '')}>
                                            <View className="absolute -inset-6 border-2 border-[#00FF80]/30" />
                                            {selectedVoucher.qrCodeData ? (
                                                <QRCode
                                                    value={selectedVoucher.qrCodeData}
                                                    size={220}
                                                    color="black"
                                                    backgroundColor="white"
                                                />
                                            ) : (
                                                <View className="w-[220px] h-[220px] items-center justify-center bg-gray-100">
                                                    <Text className="text-black font-mono text-xs font-black uppercase tracking-widest">DATA UNAVAILABLE</Text>
                                                </View>
                                            )}

                                            {selectedVoucher.status !== 'used' && (
                                                <View className="absolute top-0 left-0 right-0 h-[2px] bg-[#00FF80] shadow-[0_0_15px_#00FF80] opacity-50" />
                                            )}
                                        </View>

                                        <TouchableOpacity
                                            onPress={() => toggleUsed(selectedVoucher)}
                                            className={cn("w-full mt-[48px] py-[20px] flex-row items-center justify-center gap-4 border-2 active:scale-[0.98]",
                                                selectedVoucher.status === 'used'
                                                    ? 'bg-transparent border-white/20'
                                                    : 'bg-[#00FF80] border-[#00FF80] shadow-[0_0_40px_rgba(0,255,128,0.4)]'
                                            )}
                                        >
                                            {selectedVoucher.status === 'used' ? (
                                                <>
                                                    <RotateCcw size={24} color="white" />
                                                    <Text className="text-white font-black text-[20px] font-heading uppercase tracking-widest">RESTORE PROTOCOL</Text>
                                                </>
                                            ) : (
                                                <>
                                                    <CheckCircle2 size={24} color="black" />
                                                    <Text className="text-black font-black text-[20px] font-heading uppercase tracking-widest">MARK AS USED</Text>
                                                </>
                                            )}
                                        </TouchableOpacity>

                                        <View className="mt-8 flex-row items-center gap-3 bg-white/5 border border-white/10 px-4 py-2">
                                            <Copy size={12} color="#666" />
                                            <Text className="text-gray-500 font-mono text-[10px] uppercase tracking-[0.2em] font-black">
                                                ID: {selectedVoucher.externalId || selectedVoucher.id}
                                            </Text>
                                        </View>
                                    </View>
                                </View>
                            )}
                        </View>
                    </Modal>
                </View>
            </Layout>
        </ProtectedRoute>
    );
}
