/// <reference types="nativewind/types" />
import { useState, useEffect } from "react";
import { View, Text, Pressable, ActivityIndicator, Modal, StyleSheet } from "react-native";
import { X, QrCode, RotateCcw } from "lucide-react-native";
import { getMyVouchers, Voucher, markVoucherAsUsed, restoreVoucher, getMyOrders, Order } from "../src/lib/api";
import { PageLayout } from "../src/components/page-layout";
import { GridBackground } from "../src/components/grid-background";
import { tokens } from "../src/lib/design-tokens";
import QRCode from "react-native-qrcode-svg";
import * as Clipboard from "expo-clipboard";
import { useI18n } from "../src/lib/i18n";
import { Haptics } from "../src/lib/haptics";

const GLOBAL_PADDING = tokens.spacing.containerPadding;

export default function MyCodesScreen() {
    const [vouchers, setVouchers] = useState<Voucher[]>([]);
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
            if (selectedVoucher && selectedVoucher.id === voucher.id) {
                setSelectedVoucher({ ...voucher, status: isCurrentlyUsed ? 'active' : 'used' });
            }
        } catch (error: any) {
            console.error('Failed to update status:', error);
        }
    };

    const copyToClipboard = async (text: string) => {
        await Clipboard.setStringAsync(text);
    };

    const Header = (
        <View style={styles.headerContainer}>
            <View style={styles.headerTopRow}>
                <View style={{ width: 44 }} />

                <View style={styles.headerCenter}>
                    <Text allowFontScaling={false} style={styles.headerTitle}>{t('nav.codes')}</Text>
                    <Text allowFontScaling={false} style={styles.headerSubtitle}>{t('codes.vaultStorage')} [ SECURE ]</Text>
                </View>

                <Pressable
                    onPress={() => {
                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                        loadData();
                    }}
                    style={styles.refreshBtn}
                >
                    <RotateCcw size={18} color={tokens.colors.primary} />
                </Pressable>
            </View>
        </View>
    );

    if (loading) {
        return (
            <View style={styles.centerContainer}>
                <ActivityIndicator size="large" color={tokens.colors.primary} />
            </View>
        );
    }

    return (
        <PageLayout header={Header} background={<GridBackground />} paddingHorizontal={GLOBAL_PADDING}>
            {vouchers.length === 0 ? (
                <View style={styles.emptyContainer}>
                    <QrCode size={48} color="#222" style={{ marginBottom: 16 }} />
                    <Text allowFontScaling={false} style={styles.emptyTitle}>{t('codes.noAssets')}</Text>
                </View>
            ) : (
                <View style={{ gap: tokens.spacing.cardGap }}>
                    <Text allowFontScaling={false} style={styles.sectionLabel}>{t('codes.availablePayloads')}</Text>

                    {vouchers.map((voucher) => {
                        const isUsed = voucher.status === 'used';
                        return (
                            <Pressable
                                key={voucher.id}
                                onPress={() => {
                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                    setSelectedVoucher(voucher);
                                }}
                                style={({ pressed }) => [
                                    styles.voucherCard,
                                    isUsed ? styles.voucherUsed : styles.voucherActive,
                                    pressed && { transform: [{ scale: 0.98 }] }
                                ]}
                            >
                                <View style={styles.voucherIconBox}>
                                    <QrCode size={24} color={isUsed ? "#333" : tokens.colors.primary} />
                                </View>
                                <View style={styles.voucherContent}>
                                    <View style={styles.voucherRow}>
                                        <View>
                                            <Text
                                                allowFontScaling={false}
                                                style={[styles.voucherProvider, { color: isUsed ? tokens.colors.text.dim : '#FFF' }]}
                                            >
                                                {voucher.provider}
                                            </Text>
                                            <Text
                                                allowFontScaling={false}
                                                style={[styles.voucherFuelType, { color: isUsed ? 'rgba(255,255,255,0.05)' : tokens.colors.text.muted }]}
                                            >
                                                {voucher.fuelType}
                                            </Text>
                                        </View>
                                        <View style={{ alignItems: 'flex-end' }}>
                                            <Text
                                                allowFontScaling={false}
                                                style={[styles.voucherAmount, { color: isUsed ? 'rgba(255,255,255,0.1)' : tokens.colors.primary }]}
                                            >
                                                {voucher.amount}L
                                            </Text>
                                            {!isUsed && (
                                                <View style={styles.activeTag}>
                                                    <Text allowFontScaling={false} style={styles.activeTagText}>ACTIVE</Text>
                                                </View>
                                            )}
                                        </View>
                                    </View>
                                </View>
                                {isUsed && (
                                    <View style={styles.usedOverlay}>
                                        <Text allowFontScaling={false} style={styles.usedStamp}>USED</Text>
                                    </View>
                                )}
                            </Pressable>
                        );
                    })}
                </View>
            )}

            <Modal
                visible={!!selectedVoucher}
                animationType="fade"
                transparent={true}
                onRequestClose={() => setSelectedVoucher(null)}
            >
                <View style={styles.modalBackdrop}>
                    {selectedVoucher && (
                        <View style={styles.modalContent}>
                            <View style={styles.modalHeader}>
                                <View>
                                    <Text allowFontScaling={false} style={styles.modalTitle}>{selectedVoucher.provider}</Text>
                                    <Text allowFontScaling={false} style={styles.modalSubtitle}>{selectedVoucher.fuelType} • {selectedVoucher.amount} ЛІТРІВ</Text>
                                </View>
                                <Pressable
                                    onPress={() => setSelectedVoucher(null)}
                                    style={styles.modalCloseBtn}
                                >
                                    <X size={20} color="#FFF" />
                                </Pressable>
                            </View>

                            <View style={styles.qrContainer}>
                                <View style={styles.qrBox}>
                                    <QRCode
                                        value={selectedVoucher.qrCodeData || "EMPTY"}
                                        size={200}
                                    />
                                </View>
                                {selectedVoucher.status === 'used' && (
                                    <View style={styles.qrUsedOverlay}>
                                        <Text allowFontScaling={false} style={styles.qrUsedStamp}>USED</Text>
                                    </View>
                                )}
                            </View>

                            <View style={styles.modalFooter}>
                                <Pressable
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
                                        toggleUsed(selectedVoucher);
                                    }}
                                    style={[
                                        styles.statusToggleBtn,
                                        selectedVoucher.status === 'used' ? styles.restoreBtn : styles.markUsedBtn
                                    ]}
                                >
                                    <Text allowFontScaling={false} style={selectedVoucher.status === 'used' ? styles.restoreBtnText : styles.markUsedBtnText}>
                                        {selectedVoucher.status === 'used' ? 'RESTORE ASSET' : 'MARK AS USED'}
                                    </Text>
                                </Pressable>
                                <Pressable
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                        copyToClipboard(selectedVoucher.id);
                                    }}
                                    style={{ padding: 8 }}
                                >
                                    <Text allowFontScaling={false} style={styles.nodeIdText}>NODE ID: {selectedVoucher.id}</Text>
                                </Pressable>
                            </View>
                        </View>
                    )}
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
        paddingHorizontal: 0,
        paddingTop: 8,
        paddingBottom: 24,
    },
    headerTopRow: {
        flexDirection: 'row',
        alignItems: 'center',
    },
    headerCenter: {
        flex: 1,
        alignItems: 'center',
    },
    headerTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 32,
        letterSpacing: -1,
        textTransform: 'uppercase',
    },
    headerSubtitle: {
        fontFamily: 'Inter-Black',
        color: tokens.colors.primary,
        fontSize: 8,
        letterSpacing: 4,
        textTransform: 'uppercase',
        marginTop: 2,
        opacity: 0.6,
    },
    refreshBtn: {
        width: 44,
        height: 44,
        backgroundColor: 'rgba(0,0,0,0.8)',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    emptyContainer: {
        marginTop: 80,
        alignItems: 'center',
        justifyContent: 'center',
        borderWidth: tokens.spacing.hairline,
        borderColor: tokens.colors.border,
        borderStyle: 'dashed',
        paddingVertical: 80,
        borderRadius: tokens.effects.radius.xs,
        backgroundColor: 'rgba(0,0,0,0.4)',
    },
    emptyTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: tokens.colors.text.dim,
        fontSize: 24,
        letterSpacing: 4,
        textTransform: 'uppercase',
    },
    sectionLabel: {
        fontFamily: tokens.typography.fonts.headingSemi,
        color: tokens.colors.text.dim,
        fontSize: tokens.typography.sizes.caption,
        letterSpacing: tokens.typography.letterSpacing.widest,
        textTransform: 'uppercase',
        marginBottom: 8,
    },
    voucherCard: {
        flexDirection: 'row',
        backgroundColor: '#000',
        borderWidth: 1,
        borderRadius: 2,
        overflow: 'hidden',
    },
    voucherActive: {
        borderColor: 'rgba(0, 255, 128, 0.4)',
        // Manual Glow
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 0.1,
        shadowRadius: 15,
        elevation: 5,
    },
    voucherUsed: {
        borderColor: 'rgba(255,255,255,0.05)',
        opacity: 0.4,
    },
    voucherIconBox: {
        width: 64,
        height: 64,
        backgroundColor: '#000',
        alignItems: 'center',
        justifyContent: 'center',
        borderRightWidth: tokens.spacing.hairline,
        borderRightColor: tokens.colors.borderLight,
    },
    voucherContent: {
        flex: 1,
        padding: 16,
        justifyContent: 'center',
    },
    voucherRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    voucherProvider: {
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 24,
        letterSpacing: -1,
        textTransform: 'uppercase',
    },
    voucherFuelType: {
        fontFamily: tokens.typography.fonts.body,
        fontSize: 9,
        fontWeight: '700',
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    voucherAmount: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        fontSize: 24,
    },
    activeTag: {
        backgroundColor: 'rgba(0, 255, 128, 0.1)',
        paddingHorizontal: 6,
        paddingVertical: 2,
        borderRadius: tokens.effects.radius.xs,
        borderWidth: tokens.spacing.hairline,
        borderColor: 'rgba(0, 255, 128, 0.3)',
        marginTop: 4,
    },
    activeTagText: {
        color: tokens.colors.primary,
        fontSize: 7,
        fontWeight: '900',
    },
    usedOverlay: {
        ...StyleSheet.absoluteFillObject,
        backgroundColor: 'rgba(0,0,0,0.4)',
        alignItems: 'center',
        justifyContent: 'center',
    },
    usedStamp: {
        fontFamily: tokens.typography.fonts.heading,
        color: 'rgba(255,255,255,0.1)',
        fontSize: 48,
        letterSpacing: 8,
        transform: [{ rotate: '-12deg' }],
    },
    modalBackdrop: {
        flex: 1,
        backgroundColor: 'rgba(0,0,0,0.95)',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 24,
    },
    modalContent: {
        width: '100%',
        backgroundColor: '#000',
        borderWidth: tokens.spacing.hairline,
        borderColor: 'rgba(0, 255, 128, 0.3)',
        borderRadius: tokens.effects.radius.xs,
        overflow: 'hidden',
        // Manual Glow
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 0.2,
        shadowRadius: 50,
        elevation: 20,
    },
    modalHeader: {
        padding: 24,
        borderBottomWidth: tokens.spacing.hairline,
        borderBottomColor: tokens.colors.borderLight,
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    modalTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 32,
        letterSpacing: -1,
        textTransform: 'uppercase',
    },
    modalSubtitle: {
        fontFamily: tokens.typography.fonts.bodyBold,
        color: tokens.colors.primary,
        fontSize: 10,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    modalCloseBtn: {
        width: 40,
        height: 40,
        borderWidth: tokens.spacing.hairline,
        borderColor: tokens.colors.border,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: tokens.effects.radius.xs,
    },
    qrContainer: {
        padding: 32,
        alignItems: 'center',
        justifyContent: 'center',
        position: 'relative',
    },
    qrBox: {
        padding: 24,
        backgroundColor: '#FFF',
        borderRadius: tokens.effects.radius.xs,
    },
    qrUsedOverlay: {
        ...StyleSheet.absoluteFillObject,
        backgroundColor: 'rgba(255,255,255,0.9)',
        alignItems: 'center',
        justifyContent: 'center',
    },
    qrUsedStamp: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#DC2626',
        fontSize: 48,
        borderWidth: 4,
        borderColor: '#DC2626',
        padding: 16,
        transform: [{ rotate: '-12deg' }],
    },
    modalFooter: {
        padding: 24,
        gap: 16,
    },
    statusToggleBtn: {
        width: '100%',
        paddingVertical: 16,
        borderRadius: tokens.effects.radius.md,
        alignItems: 'center',
        justifyContent: 'center',
    },
    restoreBtn: {
        backgroundColor: 'rgba(255,255,255,0.05)',
        borderWidth: tokens.spacing.hairline,
        borderColor: tokens.colors.borderLight,
    },
    markUsedBtn: {
        backgroundColor: tokens.colors.primary,
    },
    restoreBtnText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: tokens.colors.text.dim,
        fontSize: 14,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    markUsedBtnText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: '#000',
        fontSize: 14,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    nodeIdText: {
        fontFamily: tokens.typography.fonts.body,
        color: tokens.colors.text.dim,
        fontSize: 8,
        fontWeight: '700',
        letterSpacing: 2,
        textTransform: 'uppercase',
        textAlign: 'center',
    }
});
