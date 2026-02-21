import { useState, useEffect, useRef, useMemo } from "react";
import { View, Text, Pressable, ActivityIndicator, Modal, StyleSheet, ScrollView, Animated, Easing, ImageBackground } from "react-native";
import { X, QrCode as QrIcon, Clock, Wallet, Copy, ShieldCheck, CheckCircle, Shield } from "lucide-react-native";
import { getMyVouchers, Voucher, markVoucherAsUsed, restoreVoucher, getMyOrders, Order } from "../src/lib/api";
import { PageLayout } from "../src/components/page-layout";
import { GridBackground } from "../src/components/grid-background";
import { tokens } from "../src/lib/design-tokens";
// @ts-ignore
import qrcodeEngineRaw from "qr.js";
const qrcodeEngine = qrcodeEngineRaw as any;
import Svg, { Rect, Defs, RadialGradient, Stop, Path, Polygon, Pattern, LinearGradient } from "react-native-svg";
import * as Clipboard from "expo-clipboard";
import { useI18n } from "../src/lib/i18n";
import { Fuel } from "lucide-react-native";
import { Haptics } from "../src/lib/haptics";
import { BlurView } from "expo-blur";
import { GlowText } from "../src/components/glow-text";
import { useAuth } from "../src/hooks/useAuth";
import { useRouter, Redirect } from "expo-router";
import { useStore } from "../src/lib/store";

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

const MeshBackground = ({ color, intensity = 0.15 }: { color: string; intensity?: number }) => (
    <View style={StyleSheet.absoluteFill}>
        <Svg height="100%" width="100%">
            <Defs>
                <Pattern
                    id="honeycomb-mesh"
                    patternUnits="userSpaceOnUse"
                    width="34.64"
                    height="30"
                    viewBox="0 0 34.64 30"
                >
                    <Path
                        d="M8.66 0 L25.98 0 L34.64 15 L25.98 30 L8.66 30 L0 15 Z"
                        fill="transparent"
                        stroke={color}
                        strokeWidth="0.5"
                        opacity={intensity}
                    />
                </Pattern>

                {/* Technical Rim Light */}
                <LinearGradient id="rim-light" x1="0" y1="0" x2="1" y2="1">
                    <Stop offset="0" stopColor={color} stopOpacity="0.1" />
                    <Stop offset="0.5" stopColor="transparent" stopOpacity="0" />
                    <Stop offset="1" stopColor={color} stopOpacity="0.05" />
                </LinearGradient>

                {/* Tactical Gloss */}
                <RadialGradient id="gloss" cx="20%" cy="20%" r="50%">
                    <Stop offset="0" stopColor="#FFF" stopOpacity="0.05" />
                    <Stop offset="1" stopColor="transparent" stopOpacity="0" />
                </RadialGradient>
            </Defs>

            <Rect width="100%" height="100%" fill="url(#honeycomb-mesh)" />
            <Rect width="100%" height="100%" fill="url(#rim-light)" />
            <Rect width="100%" height="100%" fill="url(#gloss)" />
        </Svg>
    </View>
);

export default function MyCodesScreen() {
    const [vouchers, setVouchers] = useState<Voucher[]>([]);
    const [orders, setOrders] = useState<Order[]>([]);
    const [loading, setLoading] = useState(true);
    const [selectedVoucher, setSelectedVoucher] = useState<Voucher | null>(null);
    const pulseAnim = useRef(new Animated.Value(1)).current;
    const { t } = useI18n();
    const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
    const storeAuth = useStore(state => state.isAuthenticated);
    const isAuthenticated = storeAuth || hookAuth;

    useEffect(() => {
        if (isAuthenticated) {
            loadData();
        }

        Animated.loop(
            Animated.sequence([
                Animated.timing(pulseAnim, { toValue: 0.6, duration: 2000, useNativeDriver: true }),
                Animated.timing(pulseAnim, { toValue: 1, duration: 2000, useNativeDriver: true }),
            ])
        ).start();
    }, [isAuthenticated]);

    if (!isAuthenticated && !authLoading) {
        return <Redirect href="/landing" />;
    }

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
            console.log("Data fetch failed - likely connection or auth issue:", error.message);
            // Don't throw, just allow empty state to show or keep previous data
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
                    <GlowText
                        intensity="high"
                        align="center"
                        animation="pulse"
                        animatedValue={pulseAnim}
                        style={styles.headerTitle}
                    >
                        {t('codes.title')}
                    </GlowText>
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
                        <View style={styles.emptyIconBox}>
                            <QrIcon size={40} color={tokens.colors.primary} />
                        </View>
                        <GlowText
                            intensity="low"
                            align="center"
                            animation="pulse"
                            animatedValue={pulseAnim}
                            style={styles.emptyTitle}
                        >
                            {t('codes.noAssets')}
                        </GlowText>
                        <Text allowFontScaling={false} style={styles.emptySubtitle}>
                            {t('codes.purchaseFuel')}
                        </Text>
                    </View>
                ) : (
                    <View style={{ gap: 40 }}>
                        {/* PENDING SECTION */}
                        {pendingOrders.length > 0 && (
                            <View style={{ gap: 12 }}>
                                <View style={styles.sectionHeader}>
                                    <Clock size={14} color="#FFA500" />
                                    <View style={{ flex: 1, height: 1, backgroundColor: 'rgba(255, 165, 0, 0.1)', marginLeft: 8 }} />
                                    <Text allowFontScaling={false} style={[styles.sectionLabel, { color: '#FFA500', marginBottom: 0 }]}>
                                        {t('codes.processingPurchases')}
                                    </Text>
                                </View>
                                {pendingOrders.map((order) => {
                                    const bColor = getBrandColor(order.provider);
                                    return (
                                        <View key={order.id} style={[styles.premiumCard, styles.pendingPremiumCard]}>
                                            <MeshBackground color="#FFA500" intensity={0.05} />
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
                                                        <View style={{ flexDirection: 'row', alignItems: 'baseline', gap: 6, marginTop: 2 }}>
                                                            <Text allowFontScaling={false} style={styles.fuelSpec}>
                                                                {order.fuelType.includes('ЄВРО') ? order.fuelType : `ДП ЄВРО`}
                                                            </Text>
                                                            <Text allowFontScaling={false} style={[styles.amountTextInline, { color: bColor }]}>
                                                                | {order.liters}L
                                                            </Text>
                                                        </View>

                                                        {/* Bottom Row: ID */}
                                                        <Text allowFontScaling={false} style={styles.highContrastId}>ID: {(order.id || "").slice(0, 12).toUpperCase()}</Text>
                                                    </View>
                                                    <View style={{ alignItems: 'flex-end' }}>
                                                        <View style={styles.statusPillPending}>
                                                            <Animated.View style={[styles.dotPending, { opacity: pulseAnim }]} />
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
                                    <View style={{ flex: 1, height: 1, backgroundColor: 'rgba(255,255,255,0.05)', marginRight: 12 }} />
                                    <Text allowFontScaling={false} style={[styles.sectionLabel, { marginBottom: 0 }]}>
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
                                                isUsed ? styles.usedPremiumCard : styles.activePremiumCard,
                                                pressed && { transform: [{ scale: 0.985 }] }
                                            ]}
                                        >
                                            <MeshBackground color={isUsed ? 'rgba(255,255,255,0.2)' : bColor} intensity={isUsed ? 0.03 : 0.08} />
                                            <View style={[styles.cardAccentLine, isUsed ? { backgroundColor: 'rgba(255,255,255,0.1)' } : { backgroundColor: bColor }]} />
                                            <View style={styles.cardMainContent}>
                                                <View style={styles.cardHeaderRow}>
                                                    <View style={{ flex: 1 }}>
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                                                            <Text allowFontScaling={false} style={[styles.stationName, { color: isUsed ? tokens.colors.text.dim : bColor }]}>
                                                                {voucher.provider}
                                                            </Text>
                                                            <View style={[styles.stationDot, { backgroundColor: isUsed ? tokens.colors.text.dim : bColor }]} />
                                                        </View>
                                                        <View style={{ flexDirection: 'row', alignItems: 'baseline', gap: 6, marginTop: 2 }}>
                                                            <Text allowFontScaling={false} style={styles.fuelSpec}>
                                                                {voucher.fuelType}
                                                            </Text>
                                                            <Text allowFontScaling={false} style={[styles.amountTextInline, { color: isUsed ? 'rgba(255,255,255,0.2)' : bColor }]}>
                                                                | {voucher.amount}L
                                                            </Text>
                                                        </View>

                                                        {/* Bottom Row: ID */}
                                                        <Text allowFontScaling={false} style={[styles.highContrastId, isUsed && { color: 'rgba(255,255,255,0.4)', opacity: 0.5 }]}>ID: {voucher.externalId}</Text>
                                                    </View>
                                                    <View style={{ alignItems: 'flex-end' }}>
                                                        {!isUsed ? (
                                                            <View style={styles.statusPillActive}>
                                                                <Animated.View style={[styles.dotActive, { opacity: pulseAnim }]} />
                                                                <Text allowFontScaling={false} style={styles.statusPillTextActive}>ACTIVE</Text>
                                                            </View>
                                                        ) : (
                                                            <View style={styles.statusPillUsed}>
                                                                <Text allowFontScaling={false} style={styles.statusPillTextUsed}>REDEEMED</Text>
                                                            </View>
                                                        )}
                                                        {!isUsed && <QrIcon size={12} color={bColor} />}
                                                    </View>
                                                </View>
                                            </View>

                                            {isUsed && (
                                                <View style={styles.diagonalStampContainer}>
                                                    <View style={styles.diagonalStamp}>
                                                        <View style={styles.diagonalStampInner}>
                                                            <Text style={styles.diagonalStampText}>{t('codes.used')}</Text>
                                                        </View>
                                                    </View>
                                                </View>
                                            )}
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
                            <View style={[styles.modalHeader, { borderBottomWidth: 0, padding: 12 }]}>
                                <View style={styles.modalBrandBox}>
                                    <MeshBackground color={(tokens.colors.text.brand as any)[selectedVoucher.provider.toLowerCase()] || tokens.colors.primary} intensity={0.1} />
                                    <View style={{ width: 8, backgroundColor: (tokens.colors.text.brand as any)[selectedVoucher.provider.toLowerCase()] || tokens.colors.primary }} />
                                    <View style={{ flex: 1, position: 'relative', padding: 16, justifyContent: 'center' }}>
                                        <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                                            <View style={[styles.modalProviderIconBox, { backgroundColor: (tokens.colors.text.brand as any)[selectedVoucher.provider.toLowerCase()] || tokens.colors.primary }]}>
                                                <Fuel size={24} color="#000" />
                                            </View>
                                            <View style={{ flex: 1 }}>
                                                <Text allowFontScaling={false} style={styles.modalProviderName}>{selectedVoucher.provider}</Text>
                                                <View style={{ flexDirection: 'row', gap: 6, alignItems: 'baseline' }}>
                                                    <Text allowFontScaling={false} style={[styles.modalFuelTitle, { textShadowColor: (tokens.colors.text.brand as any)[selectedVoucher.provider.toLowerCase()] || tokens.colors.primary }]}>
                                                        {selectedVoucher.fuelType}
                                                    </Text>
                                                    <Text allowFontScaling={false} style={styles.modalAmountText}>| {selectedVoucher.amount} л.</Text>
                                                </View>
                                            </View>
                                        </View>
                                    </View>
                                </View>
                                <Pressable
                                    onPress={() => setSelectedVoucher(null)}
                                    style={[styles.modalCloseBtn, { position: 'absolute', top: 20, right: 20, zIndex: 10 }]}
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
                                    <BlurView intensity={40} tint="dark" style={styles.qrUsedOverlay} />
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
                                        {selectedVoucher.status !== 'used' && <ShieldCheck size={20} color="black" style={{ marginRight: 10 }} />}
                                        <Text
                                            allowFontScaling={false}
                                            style={selectedVoucher.status === 'used' ? styles.restoreBtnText : styles.markUsedBtnText}
                                        >
                                            {selectedVoucher.status === 'used' ? t('codes.restoreCode') : t('codes.markAsUsed')}
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
            </Modal>
        </PageLayout>
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
        paddingHorizontal: GLOBAL_PADDING,
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
    activePremiumCard: {
        borderColor: 'rgba(0, 255, 106, 0.2)',
    },
    pendingPremiumCard: {
        borderColor: 'rgba(255, 165, 0, 0.2)',
    },
    usedPremiumCard: {
        opacity: 0.6,
        borderColor: 'rgba(255,255,255,0.1)',
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
        padding: 12,
        paddingLeft: 16,
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
    amountTextInline: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 14,
    },
    highContrastId: {
        color: 'rgba(255,255,255,0.4)',
        fontSize: 14,
        fontFamily: 'Rajdhani-Bold',
        marginTop: 14,
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
    statusPillUsed: {
        backgroundColor: 'rgba(255,255,255,0.05)',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
        marginBottom: 8,
    },
    statusPillTextUsed: {
        color: 'rgba(255,255,255,0.3)',
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
    diagonalStampContainer: {
        position: 'absolute',
        top: 0,
        bottom: 0,
        left: 0,
        right: 0,
        alignItems: 'center',
        justifyContent: 'center',
        overflow: 'hidden',
    },
    diagonalStamp: {
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
        padding: 2,
        transform: [{ rotate: '-12deg' }],
    },
    diagonalStampInner: {
        borderWidth: 2,
        borderColor: 'rgba(255,255,255,0.15)',
        paddingHorizontal: 20,
        paddingVertical: 6,
        backgroundColor: 'rgba(0,0,0,0.6)',
    },
    diagonalStampText: {
        color: 'rgba(255,255,255,0.25)',
        fontSize: 22,
        fontFamily: 'Rajdhani-Bold',
        letterSpacing: 6,
        textTransform: 'uppercase',
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
    emptyIconBox: {
        width: 80,
        height: 80,
        borderRadius: 40,
        backgroundColor: 'rgba(0, 255, 106, 0.05)',
        alignItems: 'center',
        justifyContent: 'center',
        marginBottom: 24,
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 106, 0.1)',
    },
    emptyTitle: {
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 24,
        letterSpacing: 4,
        textTransform: 'uppercase',
    },
    emptySubtitle: {
        color: 'rgba(255,255,255,0.3)',
        fontSize: 12,
        fontFamily: 'Inter-Bold',
        marginTop: 8,
        letterSpacing: 1,
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
        borderColor: 'rgba(0, 255, 106, 0.2)',
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
    modalBrandBox: {
        width: '100%',
        backgroundColor: '#0a0a0a',
        borderRadius: 4,
        overflow: 'hidden',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.05)',
        flexDirection: 'row',
        height: 100
    },
    modalProviderIconBox: {
        width: 48,
        height: 48,
        borderRadius: 4,
        justifyContent: 'center',
        alignItems: 'center',
        marginRight: 16
    },
    modalProviderName: {
        fontFamily: 'Inter-Black',
        color: 'rgba(255,255,255,0.6)',
        fontSize: 20,
        textTransform: 'uppercase',
        marginBottom: 2
    },
    modalFuelTitle: {
        fontFamily: 'Rajdhani-Bold',
        color: '#FFF',
        fontSize: 28,
        textShadowRadius: 10
    },
    modalAmountText: {
        fontFamily: 'Rajdhani-Bold',
        color: 'rgba(255,255,255,0.3)',
        fontSize: 16
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
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 1,
        shadowRadius: 15,
        elevation: 10,
    },
    qrUsedOverlay: {
        ...StyleSheet.absoluteFillObject,
        zIndex: 5,
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
    modalFooter: {
        padding: 24,
        gap: 16,
    },
    statusToggleBtn: {
        width: '100%',
        height: 60,
        borderRadius: 4,
        alignItems: 'center',
        justifyContent: 'center',
    },
    markUsedBtn: {
        backgroundColor: tokens.colors.primary,
    },
    restoreBtn: {
        backgroundColor: 'rgba(255,255,255,0.05)',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
    },
    markUsedBtnText: {
        fontFamily: 'Inter-Black',
        color: '#000',
        fontSize: 14,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    restoreBtnText: {
        fontFamily: 'Inter-Black',
        color: 'rgba(255,255,255,0.4)',
        fontSize: 14,
        letterSpacing: 1.5,
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
        fontSize: 12,
        fontWeight: '700',
        letterSpacing: 2,
        textTransform: 'uppercase',
        textAlign: 'center',
    },
});