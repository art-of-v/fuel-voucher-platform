import { useState, useEffect } from "react";
import { View, Text, Pressable, ScrollView, ActivityIndicator, Modal } from "react-native";
import { X, Copy, QrCode, Zap, Wallet, ShieldCheck, CheckCircle2, RotateCcw, Clock } from "lucide-react-native";
import { getMyVouchers, Voucher, markVoucherAsUsed, restoreVoucher, getMyOrders, Order } from "../src/lib/api";
import { useI18n } from "../src/lib/i18n";
import { PageLayout } from "../src/components/page-layout";
import QRCode from "react-native-qrcode-svg";
import * as Clipboard from "expo-clipboard";

export default function MyCodesScreen() {
    const [vouchers, setVouchers] = useState<Voucher[]>([]);
    const [orders, setOrders] = useState<Order[]>([]);
    const [loading, setLoading] = useState(true);
    const [selectedVoucher, setSelectedVoucher] = useState<Voucher | null>(null);
    const { t } = useI18n();

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
            // Refresh detail modal if open
            if (selectedVoucher && selectedVoucher.id === voucher.id) {
                setSelectedVoucher({ ...voucher, status: isCurrentlyUsed ? 'active' : 'used' });
            }
        } catch (error: any) {
            console.error('Failed to update voucher status:', error);
        }
    };

    const copyToClipboard = async (text: string) => {
        await Clipboard.setStringAsync(text);
    };

    const Header = (
        <View className="p-6 pt-12 border-b border-white/5 bg-zinc-950">
            <View className="flex-row items-center gap-3 mb-2">
                <Wallet size={32} color="#00FF80" />
                <Text className="text-3xl font-black text-white uppercase">{t('codes.myAssets')}</Text>
            </View>
            <View className="flex-row items-center justify-between">
                <View className="flex-row items-center gap-2">
                    <ShieldCheck size={14} color="#00FF80" />
                    <Text className="text-[10px] text-[#00FF80] font-bold uppercase tracking-widest">{t('codes.secureVault')}</Text>
                </View>
                <Pressable onPress={loadData} className="bg-zinc-800 px-3 py-1 rounded">
                    <Text className="text-[10px] text-white font-bold">RELOAD</Text>
                </Pressable>
            </View>
        </View>
    );

    if (loading) {
        return (
            <View className="flex-1 bg-black items-center justify-center">
                <ActivityIndicator size="large" color="#00FF80" />
                <Text className="text-[#00FF80] font-bold mt-4 uppercase">DECRYPTING ASSETS...</Text>
            </View>
        );
    }

    return (
        <PageLayout header={Header} scrollClassName="p-4 space-y-4">
            {vouchers.length === 0 && orders.length === 0 ? (
                <View className="py-20 items-center justify-center border-2 border-dashed border-white/10 rounded-lg">
                    <QrCode size={64} color="#333" className="mb-4" />
                    <Text className="text-white font-black uppercase text-center mb-2">{t('codes.noActiveCodes')}</Text>
                    <Text className="text-gray-500 text-[10px] uppercase font-bold">{t('codes.purchaseToStart')}</Text>
                </View>
            ) : (
                <View className="gap-4">
                    {/* Pending Orders */}
                    {orders.filter(o => o.status === 'PENDING_FULFILLMENT').map((order) => (
                        <View key={order.id} className="bg-zinc-950 border border-yellow-500/30 p-4 rounded-lg">
                            <View className="flex-row justify-between items-center">
                                <View>
                                    <Text className="text-white font-bold text-lg uppercase">{order.provider}</Text>
                                    <Text className="text-gray-400 text-xs uppercase">{order.fuelType} • {order.liters}L</Text>
                                </View>
                                <View className="bg-yellow-500/20 px-2 py-1 rounded">
                                    <Text className="text-yellow-500 text-[10px] font-bold uppercase">PENDING</Text>
                                </View>
                            </View>
                            <Text className="text-[10px] text-gray-600 mt-3 uppercase font-bold tracking-widest">Processing transaction...</Text>
                        </View>
                    ))}

                    {/* Vouchers */}
                    {vouchers.map((voucher) => {
                        const isUsed = voucher.status === 'used';
                        return (
                            <Pressable
                                key={voucher.id}
                                onPress={() => setSelectedVoucher(voucher)}
                                className={`flex-row bg-zinc-900 border border-white/10 rounded-lg overflow-hidden active:scale-95 ${isUsed ? 'opacity-50 grayscale' : ''}`}
                            >
                                <View className="w-20 bg-black items-center justify-center border-r border-white/10">
                                    <QrCode size={32} color={isUsed ? "#333" : "#00FF80"} opacity={0.5} />
                                </View>
                                <View className="flex-1 p-4">
                                    <View className="flex-row justify-between">
                                        <Text className="text-white font-black text-xl uppercase tracking-tighter">{voucher.provider}</Text>
                                        <Text className="text-[#00FF80] font-black text-lg">{voucher.amount}L</Text>
                                    </View>
                                    <View className="flex-row items-center gap-2">
                                        <Text className="text-gray-500 text-[10px] font-bold uppercase tracking-widest">{voucher.fuelType}</Text>
                                        {isUsed && <Text className="bg-white/10 text-white text-[8px] px-1 rounded">USED</Text>}
                                    </View>
                                </View>
                            </Pressable>
                        );
                    })}
                </View>
            )}

            {/* Voucher Detail Modal */}
            <Modal
                visible={!!selectedVoucher}
                animationType="slide"
                transparent={true}
                onRequestClose={() => setSelectedVoucher(null)}
            >
                <View className="flex-1 bg-black/95 items-center justify-center p-6">
                    {selectedVoucher && (
                        <View className="w-full bg-black border-2 border-[#00FF80] rounded-xl overflow-hidden">
                            <View className="p-6 items-center border-b border-white/10">
                                <Pressable
                                    onPress={() => setSelectedVoucher(null)}
                                    className="absolute top-4 right-4 p-2 bg-white/5 rounded-full"
                                >
                                    <X size={20} color="#FFF" />
                                </Pressable>

                                <Text className="text-4xl font-black text-white uppercase mb-1">{selectedVoucher.provider}</Text>
                                <View className="flex-row gap-4 bg-[#00FF8010] px-4 py-2 rounded-lg border border-[#00FF8030]">
                                    <View className="items-center">
                                        <Text className="text-[8px] text-gray-500 uppercase font-bold">TYPE</Text>
                                        <Text className="text-[#00FF80] font-bold text-lg uppercase">{selectedVoucher.fuelType}</Text>
                                    </View>
                                    <View className="w-[1px] bg-white/10" />
                                    <View className="items-center">
                                        <Text className="text-[8px] text-gray-500 uppercase font-bold">VOLUME</Text>
                                        <Text className="text-white font-bold text-lg uppercase">{selectedVoucher.amount} L</Text>
                                    </View>
                                </View>
                            </View>

                            <View className="p-10 items-center justify-center bg-white m-6 rounded-lg">
                                {selectedVoucher.qrCodeData ? (
                                    <QRCode
                                        value={selectedVoucher.qrCodeData}
                                        size={220}
                                        color="black"
                                        backgroundColor="white"
                                    />
                                ) : (
                                    <Text className="text-black font-bold uppercase">NO QR DATA</Text>
                                )}
                                {selectedVoucher.status === 'used' && (
                                    <View className="absolute inset-0 bg-white/80 items-center justify-center">
                                        <View className="border-4 border-red-500 p-4 rounded -rotate-12">
                                            <Text className="text-red-500 font-black text-4xl">USED</Text>
                                        </View>
                                    </View>
                                )}
                            </View>

                            <View className="p-6 items-center gap-4">
                                <Pressable
                                    onPress={() => toggleUsed(selectedVoucher)}
                                    className={`w-full py-4 rounded-lg flex-row items-center justify-center gap-3 ${selectedVoucher.status === 'used' ? 'bg-zinc-800' : 'bg-[#00FF80]'
                                        }`}
                                >
                                    {selectedVoucher.status === 'used' ? (
                                        <>
                                            <RotateCcw size={20} color="#666" />
                                            <Text className="text-gray-400 font-bold uppercase">RESTORE CODE</Text>
                                        </>
                                    ) : (
                                        <>
                                            <CheckCircle2 size={20} color="#000" />
                                            <Text className="text-black font-black uppercase tracking-widest">MARK AS USED</Text>
                                        </>
                                    )}
                                </Pressable>

                                <Pressable
                                    onPress={() => copyToClipboard(selectedVoucher.externalId || selectedVoucher.id)}
                                    className="flex-row items-center gap-2 py-2"
                                >
                                    <Copy size={14} color="#666" />
                                    <Text className="text-gray-600 font-bold text-[10px] uppercase tracking-widest">
                                        ID: {selectedVoucher.externalId || selectedVoucher.id}
                                    </Text>
                                </Pressable>
                            </View>
                        </View>
                    )}
                </View>
            </Modal>
        </PageLayout>
    );
}
