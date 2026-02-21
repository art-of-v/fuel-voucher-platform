/// <reference types="nativewind/types" />
import { useState, useEffect } from "react";
import { View, Text, Pressable, ActivityIndicator, Modal, StyleSheet, ScrollView, Animated, Easing, ImageBackground } from "react-native";
import { X, QrCode as QrIcon, Clock, Wallet, Copy, ShieldCheck, CheckCircle } from "lucide-react-native";
import { getMyVouchers, Voucher, markVoucherAsUsed, restoreVoucher, getMyOrders, Order } from "../src/lib/api";
import { PageLayout } from "../src/components/page-layout";
import { GridBackground } from "../src/components/grid-background";
import { tokens } from "../src/lib/design-tokens";
import qrcodeEngineRaw from "qr.js";
const qrcodeEngine = qrcodeEngineRaw as any;
import Svg, { Rect } from "react-native-svg";
import * as Clipboard from "expo-clipboard";
import { useI18n } from "../src/lib/i18n";
import { Haptics } from "../src/lib/haptics";

const GLOBAL_PADDING = tokens.spacing.containerPadding;

const QrScannerOverlay = () => {
    const [scanAnim] = useState(new Animated.Value(0));

    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(scanAnim, {
                    toValue: 200,
                    duration: 2000,
                    easing: Easing.linear,
                    useNativeDriver: true,
                }),
                Animated.timing(scanAnim, {
                    toValue: 0,
                    duration: 2000,
                    easing: Easing.linear,
                    useNativeDriver: true,
                }),
            ])
        ).start();
    }, []);

    return (
        <Animated.View
            style={[
                styles.scanLine,
                { transform: [{ translateY: scanAnim }] }
            ]}
        />
    );
};

const QrSync = ({ value, size }: { value: string, size: number }) => {
    const qr = qrcodeEngine(value, { errorCorrectLevel: qrcodeEngine.ErrorCorrectLevel.L });
    const cells = qr.modules;
    const tileW = size / cells.length;

    return (
        <Svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
            {cells.map((row: boolean[], rowIndex: number) =>
                row.map((cell: boolean, colIndex: number) => (
                    cell ? (
                        <Rect
                            key={`${rowIndex}-${colIndex}`}
                            x={colIndex * tileW}
                            y={rowIndex * tileW}
                            width={tileW + 0.1} // overlap to avoid subpixel lines
                            height={tileW + 0.1}
                            fill="black"
                        />
                    ) : null
                ))
            )}
        </Svg>
    );
};

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

    const getBrandColor = (provider: string = "") => {
        const p = provider.toLowerCase();
        if (p.includes('okko')) return tokens.colors.text.brand.okko;
        if (p.includes('wog')) return tokens.colors.text.brand.wog;
        if (p.includes('upg')) return tokens.colors.text.brand.upg;
        if (p.includes('klo')) return tokens.colors.text.brand.klo;
        if (p.includes('shell')) return '#FF0000';
        if (p.includes('socar')) return '#C0C0C0';
        return tokens.colors.primary;
    };

    const Header = (
        <View style={styles.headerContainer}>
            <View style={styles.headerTopRow}>
                <View style={{ width: 44 }} />
                <View style={styles.headerCenter}>
                    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12 }}>
                        <Wallet size={28} color={tokens.colors.primary} />
                        <Text allowFontScaling={false} style={styles.headerTitle}>МОЇ АКТИВИ</Text>
                    </View>
                </View>
                <View style={{ width: 44 }} />
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

    const pendingOrders = orders.filter(o => o.status === 'PENDING_FULFILLMENT');

    return (
        <PageLayout header={Header} background={<GridBackground />} disableScroll={true}>
            <ScrollView contentContainerStyle={{ paddingHorizontal: GLOBAL_PADDING, paddingBottom: 150 }}>
                {vouchers.length === 0 && pendingOrders.length === 0 ? (
                    <View style={styles.emptyContainer}>
                        <QrIcon size={64} color="rgba(255,255,255,0.05)" style={{ marginBottom: 24 }} />
                        <Text allowFontScaling={false} style={styles.emptyTitle}>{t('codes.noAssets')}</Text>
                    </View>
                ) : (
                    <View style={{ gap: 40 }}>
                        {/* PENDING SECTION */}
                        {pendingOrders.length > 0 && (
                            <View style={{ gap: 12 }}>
                                <View style={styles.sectionHeader}>
                                    <Clock size={14} color="#FFA500" />
                                    <Text allowFontScaling={false} style={[styles.sectionLabel, { color: '#FFA500' }]}>
                                        {t('codes.processingPurchases')}
                                    </Text>
                                </View>
                                {pendingOrders.map((order) => {
                                    const bColor = getBrandColor(order.provider);
                                    return (
                                        <View key={order.id} style={[styles.premiumCard, styles.pendingPremiumCard]}>
                                            <View style={[styles.cardAccentLine, { backgroundColor: '#FFA500' }]} />
                                            <View style={styles.cardMainContent}>
                                                <View style={styles.cardHeaderRow}>
                                                    <View style={{ flex: 1 }}>
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                                                            <Text allowFontScaling={false} style={[styles.stationName, { color: bColor }]}>
                                                                {order.provider}
                                                            </Text>
                                                            <View style={[styles.stationDot, { backgroundColor: bColor }]} />
                                                        </View>
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6, marginTop: 2 }}>
                                                            <Text allowFontScaling={false} style={styles.fuelSpec}>
                                                                {order.fuelType.includes('ЄВРО') ? order.fuelType : `ДП ЄВРО`}
                                                            </Text>
                                                            <Text allowFontScaling={false} style={[styles.amountTextInline, { color: bColor }]}>
                                                                {order.liters}L
                                                            </Text>
                                                        </View>
                                                        <Text allowFontScaling={false} style={styles.assetIdText}>ID: {(order.id || "").slice(0, 12)}</Text>
                                                    </View>
                                                    <View style={{ alignItems: 'flex-end' }}>
                                                        <View style={styles.statusPillPending}>
                                                            <View style={styles.dotPending} />
                                                            <Text allowFontScaling={false} style={styles.statusPillTextPending}>PENDING</Text>
                                                        </View>
                                                        <Text allowFontScaling={false} style={styles.dateTextTop}>
                                                            {new Date(order.createdAt).toLocaleDateString()}
                                                        </Text>
                                                    </View>
                                                </View>
                                            </View>
                                        </View>
                                    );
                                })}
                            </View>
                        )}

                        {/* ACTIVE SECTION */}
                        {vouchers.length > 0 && (
                            <View style={{ gap: 16 }}>
                                <View style={styles.sectionHeader}>
                                    <Text allowFontScaling={false} style={styles.sectionLabel}>
                                        {t('codes.availablePayloads')}
                                    </Text>
                                </View>
                                {vouchers.map((voucher) => {
                                    const isUsed = voucher.status === 'used';
                                    const bColor = getBrandColor(voucher.provider);
                                    return (
                                        <Pressable
                                            key={voucher.id}
                                            onPress={() => {
                                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                                setSelectedVoucher(voucher);
                                            }}
                                            style={({ pressed }) => [
                                                styles.premiumCard,
                                                isUsed ? { opacity: 0.5 } : styles.activePremiumCard,
                                                pressed && { transform: [{ scale: 0.98 }] }
                                            ]}
                                        >
                                            <View style={[styles.cardAccentLine, isUsed ? { backgroundColor: '#333' } : { backgroundColor: bColor }]} />
                                            <View style={styles.cardMainContent}>
                                                <View style={styles.cardHeaderRow}>
                                                    <View style={{ flex: 1 }}>
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                                                            <Text allowFontScaling={false} style={[styles.stationName, { color: isUsed ? tokens.colors.text.dim : bColor }]}>
                                                                {voucher.provider}
                                                            </Text>
                                                            <View style={[styles.stationDot, { backgroundColor: isUsed ? tokens.colors.text.dim : bColor }]} />
                                                        </View>
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6, marginTop: 2 }}>
                                                            <Text allowFontScaling={false} style={styles.fuelSpec}>
                                                                {voucher.fuelType}
                                                            </Text>
                                                            {!isUsed && (
                                                                <Text allowFontScaling={false} style={[styles.amountTextInline, { color: bColor }]}>
                                                                    {voucher.amount}L
                                                                </Text>
                                                            )}
                                                        </View>
                                                        <Text allowFontScaling={false} style={styles.assetIdText}>ID: {voucher.externalId}</Text>
                                                    </View>
                                                    <View style={{ alignItems: 'flex-end' }}>
                                                        {!isUsed ? (
                                                            <View style={styles.statusPillActive}>
                                                                <View style={styles.dotActive} />
                                                                <Text allowFontScaling={false} style={styles.statusPillTextActive}>ACTIVE</Text>
                                                            </View>
                                                        ) : (
                                                            <View style={[styles.statusPillActive, { borderColor: 'rgba(255,255,255,0.1)', backgroundColor: 'transparent' }]}>
                                                                <Text allowFontScaling={false} style={[styles.statusPillTextActive, { color: 'rgba(255,255,255,0.2)' }]}>USED</Text>
                                                            </View>
                                                        )}
                                                        <QrIcon size={12} color={isUsed ? "rgba(255,255,255,0.1)" : bColor} />
                                                    </View>
                                                </View>
                                                {!isUsed && (
                                                    <View style={styles.cardFooterInfo}>
                                                        <Text allowFontScaling={false} style={styles.instructionText}>
                                                            Натисніть, щоб відкрити QR-код.
                                                        </Text>
                                                    </View>
                                                )}
                                            </View>
                                        </Pressable>
                                    );
                                })}
                            </View>
                        )}
                    </View>
                )}
            </ScrollView>

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
                                    <Text allowFontScaling={false} style={[styles.modalTitle, { textShadowColor: 'rgba(255,255,255,0.4)', textShadowOffset: { width: 0, height: 0 }, textShadowRadius: 10 }]}>{selectedVoucher.provider}</Text>
                                    <View style={{ flexDirection: 'row', gap: 12, marginTop: 4 }}>
                                        <Text allowFontScaling={false} style={styles.modalSubtitle}>FUEL: <Text style={{ color: tokens.colors.primary }}>{selectedVoucher.fuelType}</Text></Text>
                                        <Text allowFontScaling={false} style={styles.modalSubtitle}>VOLUME: <Text style={{ color: '#FFF' }}>{selectedVoucher.amount} L</Text></Text>
                                    </View>
                                </View>
                                <Pressable
                                    onPress={() => setSelectedVoucher(null)}
                                    style={styles.modalCloseBtn}
                                >
                                    <X size={20} color="#FFF" />
                                </Pressable>
                            </View>

                            <View style={styles.qrContainer}>
                                <View style={styles.qrGlowBorder}>
                                    <View style={styles.qrBox}>
                                        <QrSync
                                            value={selectedVoucher.qrCodeData || (selectedVoucher as any).qr_code_data || selectedVoucher.externalId || "EMPTY"}
                                            size={220}
                                        />
                                        <QrScannerOverlay />
                                    </View>
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
                                    <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'center' }}>
                                        {selectedVoucher.status !== 'used' && <CheckCircle size={18} color="black" style={{ marginRight: 8 }} />}
                                        <Text allowFontScaling={false} style={selectedVoucher.status === 'used' ? styles.restoreBtnText : styles.markUsedBtnText}>
                                            {selectedVoucher.status === 'used' ? 'RESTORE ASSET' : 'MARK AS USED'}
                                        </Text>
                                    </View>
                                </Pressable>
                                <Pressable
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                        copyToClipboard(selectedVoucher.externalId || selectedVoucher.id);
                                    }}
                                    style={styles.idCopyRow}
                                >
                                    <Copy size={14} color="rgba(255,255,255,0.3)" />
                                    <Text allowFontScaling={false} style={styles.nodeIdText}>ID: {selectedVoucher.externalId}</Text>
                                </Pressable>
                            </View>
                        </View>
                    )}
                </View>
            </Modal >
        </PageLayout >
    );
}

const styles = StyleSheet.create({
    centerContainer: {
        flex: 1,
        backgroundColor: '#000',
        alignItems: 'center',
        justifyContent: 'center',
    },
    headerContainer: {
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
        fontFamily: 'Inter-Bold',
        color: tokens.colors.primary,
        fontSize: 10,
        letterSpacing: 2,
    },
    sectionHeader: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
    },
    sectionLabel: {
        fontFamily: tokens.typography.fonts.headingSemi,
        color: tokens.colors.text.dim,
        fontSize: 12,
        letterSpacing: 6,
        textTransform: 'uppercase',
        marginBottom: 8,
    },
    premiumCard: {
        width: '100%',
        backgroundColor: '#0a0a0a',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.05)',
        flexDirection: 'row',
        position: 'relative',
        overflow: 'hidden',
        borderRadius: 2,
    },
    pendingPremiumCard: {
        borderColor: 'rgba(255, 165, 0, 0.2)',
    },
    cardAccentLine: {
        width: 4,
        height: '100%',
        position: 'absolute',
        left: 0,
        top: 0,
    },
    cardMainContent: {
        flex: 1,
        padding: 16,
        paddingLeft: 20,
    },
    cardHeaderRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
    },
    stationName: {
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 20,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    stationDot: {
        width: 4,
        height: 4,
        borderRadius: 2,
    },
    fuelSpec: {
        fontFamily: 'Inter-Bold',
        color: 'rgba(255,255,255,0.4)',
        fontSize: 10,
        marginTop: 2,
    },
    assetIdText: {
        fontFamily: 'Inter-Bold',
        color: 'rgba(255,255,255,0.6)',
        fontSize: 11,
        marginTop: 4,
        letterSpacing: 1,
    },
    amountTextInline: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 14,
    },
    statusPillPending: {
        flexDirection: 'row',
        alignItems: 'center',
        backgroundColor: 'rgba(255, 165, 0, 0.1)',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
        borderColor: 'rgba(255, 165, 0, 0.2)',
        marginBottom: 8,
    },
    dotPending: {
        width: 6,
        height: 6,
        borderRadius: 3,
        backgroundColor: '#FFA500',
        marginRight: 6,
    },
    statusPillTextPending: {
        color: '#FFA500',
        fontSize: 8,
        fontFamily: 'Inter-Black',
        letterSpacing: 0.5,
    },
    dateTextTop: {
        fontFamily: 'Inter-Bold',
        color: 'rgba(255,255,255,0.15)',
        fontSize: 10,
        marginTop: 4,
    },
    statusPillActive: {
        flexDirection: 'row',
        alignItems: 'center',
        backgroundColor: 'rgba(0, 255, 106, 0.1)',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 106, 0.2)',
        marginBottom: 8,
    },
    dotActive: {
        width: 6,
        height: 6,
        borderRadius: 3,
        backgroundColor: tokens.colors.primary,
        marginRight: 6,
    },
    statusPillTextActive: {
        color: tokens.colors.primary,
        fontSize: 8,
        fontFamily: 'Inter-Black',
        letterSpacing: 0.5,
    },
    activePremiumCard: {
        borderColor: 'rgba(0, 255, 106, 0.2)',
    },
    cardFooterInfo: {
        marginTop: 12,
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-end',
        borderTopWidth: 1,
        borderTopColor: 'rgba(255,255,255,0.03)',
        paddingTop: 12,
    },
    instructionText: {
        fontFamily: 'Inter',
        color: 'rgba(255,255,255,0.3)',
        fontSize: 10,
        maxWidth: '80%',
    },
    emptyContainer: {
        marginTop: 80,
        alignItems: 'center',
        justifyContent: 'center',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
        borderStyle: 'dashed',
        paddingVertical: 80,
        borderRadius: 4,
    },
    emptyTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: 'rgba(255,255,255,0.2)',
        fontSize: 24,
        letterSpacing: 4,
        textTransform: 'uppercase',
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
        borderWidth: 1.5,
        borderColor: tokens.colors.primary,
        borderRadius: 4,
        overflow: 'hidden',
    },
    modalHeader: {
        padding: 24,
        borderBottomWidth: 1,
        borderBottomColor: 'rgba(255,255,255,0.05)',
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    modalTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 36,
        letterSpacing: -1,
        textTransform: 'uppercase',
    },
    modalSubtitle: {
        fontFamily: 'Inter-Bold',
        color: tokens.colors.primary,
        fontSize: 10,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    modalCloseBtn: {
        width: 36,
        height: 36,
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.2)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 18,
    },
    qrContainer: {
        padding: 32,
        alignItems: 'center',
        justifyContent: 'center',
        position: 'relative',
    },
    qrBox: {
        padding: 12,
        backgroundColor: '#FFF',
        borderRadius: 2,
        overflow: 'hidden',
    },
    qrGlowBorder: {
        padding: 2,
        backgroundColor: tokens.colors.primary,
        borderRadius: 4,
        // Neon Glow matching web
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 1,
        shadowRadius: 15,
        elevation: 10,
    },
    scanLine: {
        position: 'absolute',
        top: 24,
        left: 24,
        right: 24,
        height: 2,
        backgroundColor: '#DC2626',
        zIndex: 10,
        opacity: 0.8,
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
        borderRadius: 8,
        alignItems: 'center',
        justifyContent: 'center',
    },
    restoreBtn: {
        backgroundColor: 'rgba(255,255,255,0.05)',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
    },
    markUsedBtn: {
        backgroundColor: tokens.colors.primary,
    },
    restoreBtnText: {
        fontFamily: 'Inter-Black',
        color: 'rgba(255,255,255,0.4)',
        fontSize: 14,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    markUsedBtnText: {
        fontFamily: 'Inter-Black',
        color: '#000',
        fontSize: 14,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    idCopyRow: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 8,
        opacity: 0.6,
    },
    nodeIdText: {
        fontFamily: 'Inter',
        color: 'rgba(255,255,255,0.2)',
        fontSize: 8,
        fontWeight: '700',
        letterSpacing: 2,
        textTransform: 'uppercase',
        textAlign: 'center',
    },
});