import { useState, useEffect, useRef, useMemo, useCallback } from "react";
import { View, Text, Pressable, ActivityIndicator, Modal, StyleSheet, ScrollView, Animated, Easing, ImageBackground, Image } from "react-native";
import { X, QrCode as QrIcon, Clock, Wallet, Copy, ShieldCheck, CheckCircle, Shield, ScanFace } from "lucide-react-native";
import { getMyVouchers, Voucher, markVoucherAsUsed, restoreVoucher, getMyOrders, Order } from "../src/lib/api";
import { PageLayout } from "../src/components/page-layout";
import { GridBackground } from "../src/components/grid-background";
import { useDesignTokens } from "../src/lib/design-tokens";
import QRCode from "qrcode";
import Svg, { Rect, Defs, RadialGradient, Stop, Path, Polygon, Pattern, LinearGradient } from "react-native-svg";
import * as Clipboard from "expo-clipboard";
import { useI18n } from "../src/lib/i18n";
import { Fuel } from "lucide-react-native";
import { Haptics } from "../src/lib/haptics";
import { BlurView } from "expo-blur";
import { GlowText } from "../src/components/glow-text";
import { useAuth } from "../src/hooks/useAuth";
import { useRouter, Redirect, useFocusEffect } from "expo-router";
import * as LocalAuthentication from 'expo-local-authentication';
import { useStore } from "../src/lib/store";

const GLOBAL_PADDING = 24;

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

const QrSync = ({ value, size, color = "black", isWog = false, imageUrl }: { value: string, size: number, color?: string, isWog?: boolean, imageUrl?: string | null }) => {
    // Priority 1: Stored image from PDF (pixel-perfect for ALL providers)
    if (imageUrl) {
        return (
            <Image
                source={{ uri: imageUrl }}
                style={{ width: size, height: size }}
                resizeMode="contain"
            />
        );
    }

    // Priority 2: QR regeneration (fallback)
    // WOG fallback: Byte mode, ECC H, Mask 0 (best effort if no stored image)
    const data = isWog ? value.trim() : value;
    const options: any = { errorCorrectionLevel: isWog ? 'H' : 'L' };

    if (isWog) {
        options.maskPattern = 0;
    }

    const segments = isWog
        ? [{ data, mode: 'byte' }] as QRCode.QRCodeSegment[]
        : data;

    const qr = QRCode.create(segments as any, options);
    const cells = qr.modules.data;
    const count = qr.modules.size;
    const tileW = size / count;

    const rows = [];
    for (let i = 0; i < count; i++) {
        const row = [];
        for (let j = 0; j < count; j++) {
            row.push(cells[i * count + j]);
        }
        rows.push(row);
    }

    return (
        <Svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
            {rows.map((row: number[], rowIndex: number) =>
                row.map((cell: number, colIndex: number) => (
                    cell ? (
                        <Rect
                            key={`${rowIndex}-${colIndex}`}
                            x={colIndex * tileW}
                            y={rowIndex * tileW}
                            width={tileW + 0.1}
                            height={tileW + 0.1}
                            fill={color}
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

                <LinearGradient id="rim-light" x1="0" y1="0" x2="1" y2="1">
                    <Stop offset="0" stopColor={color} stopOpacity="0.1" />
                    <Stop offset="0.5" stopColor="transparent" stopOpacity="0" />
                    <Stop offset="1" stopColor={color} stopOpacity="0.05" />
                </LinearGradient>

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

const FaceIdAnimation = ({ color }: { color: string }) => {
    const scaleAnim = useRef(new Animated.Value(0.95)).current;
    const opacityAnim = useRef(new Animated.Value(0.5)).current;

    useEffect(() => {
        Animated.loop(
            Animated.parallel([
                Animated.sequence([
                    Animated.timing(scaleAnim, { toValue: 1.05, duration: 1500, easing: Easing.inOut(Easing.ease), useNativeDriver: true }),
                    Animated.timing(scaleAnim, { toValue: 0.95, duration: 1500, easing: Easing.inOut(Easing.ease), useNativeDriver: true })
                ]),
                Animated.sequence([
                    Animated.timing(opacityAnim, { toValue: 1, duration: 1500, easing: Easing.linear, useNativeDriver: true }),
                    Animated.timing(opacityAnim, { toValue: 0.5, duration: 1500, easing: Easing.linear, useNativeDriver: true })
                ])
            ])
        ).start();
    }, []);

    return (
        <View style={{ alignItems: 'center', justifyContent: 'center', width: 120, height: 120, marginBottom: 24 }}>
            <Animated.View style={{
                position: 'absolute',
                width: 100,
                height: 100,
                borderRadius: 20,
                borderWidth: 2,
                borderColor: color,
                opacity: opacityAnim,
                transform: [{ scale: scaleAnim }]
            }} />
            <ScanFace size={56} color={color} strokeWidth={1.5} />
        </View>
    );
};



export default function MyCodesScreen() {
    const tokens = useDesignTokens();
    const [vouchers, setVouchers] = useState<Voucher[]>([]);
    const [orders, setOrders] = useState<Order[]>([]);
    const [loading, setLoading] = useState(true);
    const [selectedVoucher, setSelectedVoucher] = useState<Voucher | null>(null);
    const pulseAnim = useRef(new Animated.Value(1)).current;
    const { t } = useI18n();
    const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
    const storeAuth = useStore(state => state.isAuthenticated);
    const isAuthenticated = storeAuth || hookAuth;
    const [isUnlocked, setIsUnlocked] = useState(false);

    useFocusEffect(
        useCallback(() => {
            let isActive = true;

            const authenticate = async () => {
                if (isActive) setIsUnlocked(false);
                
                const hasHardware = await LocalAuthentication.hasHardwareAsync();
                const isEnrolled = await LocalAuthentication.isEnrolledAsync();
                
                if (hasHardware && isEnrolled) {
                    const authResult = await LocalAuthentication.authenticateAsync({
                        promptMessage: 'Підтвердіть особу для доступу до талонів',
                        cancelLabel: 'Скасувати',
                        fallbackLabel: 'Пароль'
                    });

                    if (isActive && authResult.success) {
                        setIsUnlocked(true);
                        loadData();
                    }
                } else {
                    alert('Біометричний захист не налаштовано на цьому пристрої. Його необхідно увімкнути.');
                }
            };
            
            if (isAuthenticated) {
                authenticate();
            }

            return () => {
                isActive = false;
                setIsUnlocked(false);
            };
        }, [isAuthenticated])
    );

    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(pulseAnim, { toValue: 0.6, duration: 2000, useNativeDriver: true }),
                Animated.timing(pulseAnim, { toValue: 1, duration: 2000, useNativeDriver: true }),
            ])
        ).start();
    }, []);

    // The lock handles empty state now

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
        const brandTokens = tokens.colors.text.brand as any;
        if (p.includes('okko')) return brandTokens.okko;
        if (p.includes('wog')) return brandTokens.wog;
        if (p.includes('upg')) return brandTokens.upg;
        if (p.includes('klo')) return brandTokens.klo;
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
                        style={[styles.headerTitle, { color: tokens.colors.text.primary }]}
                    >
                        {t('codes.title')}
                    </GlowText>
                </View>
                <View style={{ width: 44 }} />
            </View>
        </View>
    );

    if (!isAuthenticated && !authLoading) {
        return <Redirect href="/landing" />;
    }

    if (!isUnlocked) {
        return (
            <PageLayout header={Header}>
                <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', paddingBottom: 100 }}>
                    <FaceIdAnimation color={tokens.colors.primary} />
                    <Text allowFontScaling={false} style={{ fontFamily: 'Rajdhani-Bold', fontSize: 28, textTransform: 'uppercase', color: tokens.colors.text.primary, marginTop: 8 }}>
                        Доступ захищено
                    </Text>
                    <Text allowFontScaling={false} style={{ fontFamily: 'Inter-Medium', fontSize: 14, color: tokens.colors.text.dim, marginTop: 8 }}>
                        Розблокуйте за допомогою Face ID / Touch ID
                    </Text>
                    <Pressable
                        onPress={async () => {
                            const authResult = await LocalAuthentication.authenticateAsync({
                                promptMessage: 'Підтвердіть особу для доступу до талонів',
                                cancelLabel: 'Скасувати',
                                fallbackLabel: 'Пароль'
                            });
                            if (authResult.success) {
                                setIsUnlocked(true);
                                loadData();
                            }
                        }}
                        style={{ marginTop: 24, paddingHorizontal: 24, paddingVertical: 12, backgroundColor: tokens.colors.primary, borderRadius: 8 }}
                    >
                        <Text allowFontScaling={false} style={{ fontFamily: 'Inter-Bold', color: tokens.colors.isDark ? '#000' : '#fff', textTransform: 'uppercase' }}>Розблокувати</Text>
                    </Pressable>
                </View>
            </PageLayout>
        );
    }

    if (loading) {
        return (
            <View style={[styles.centerContainer, { backgroundColor: tokens.colors.background }]}>
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
                            style={[styles.emptyTitle, { color: tokens.colors.text.primary }]}
                        >
                            {t('codes.noAssets')}
                        </GlowText>
                        <Text allowFontScaling={false} style={[styles.emptySubtitle, { color: tokens.colors.text.muted }]}>
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
                                        <View key={order.id} style={[styles.premiumCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                                            <MeshBackground color="#FFA500" intensity={0.05} />
                                            <View style={[styles.cardAccentLine, { backgroundColor: '#FFA500' }]} />
                                            <View style={styles.cardMainContent}>
                                                <View style={styles.cardHeaderRow}>
                                                    <View style={{ flex: 1 }}>
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                                                            <Text allowFontScaling={false} style={[styles.stationName, { color: tokens.colors.text.primary }]}>
                                                                {order.provider}
                                                            </Text>
                                                            <View style={[styles.stationDot, { backgroundColor: bColor }]} />
                                                        </View>
                                                        <View style={{ flexDirection: 'row', alignItems: 'baseline', gap: 6, marginTop: 2 }}>
                                                            <Text allowFontScaling={false} style={[styles.fuelSpec, { color: tokens.colors.text.muted }]}>
                                                                {order.fuelType}
                                                            </Text>
                                                            <Text allowFontScaling={false} style={[styles.amountTextInline, { color: bColor }]}>
                                                                | {order.liters}L
                                                            </Text>
                                                        </View>

                                                        {/* Bottom Row: ID */}
                                                        <Text allowFontScaling={false} style={[styles.highContrastId, { color: tokens.colors.text.muted }]}>ID: {(order.id || "").slice(0, 12).toUpperCase()}</Text>
                                                    </View>
                                                    <View style={{ alignItems: 'flex-end' }}>
                                                        <View style={[styles.statusPillPending, { backgroundColor: 'rgba(255, 165, 0, 0.1)', borderColor: 'rgba(255, 165, 0, 0.2)' }]}>
                                                            <Animated.View style={[styles.dotPending, { backgroundColor: '#FFA500', opacity: pulseAnim }]} />
                                                            <Text allowFontScaling={false} style={[styles.statusPillTextPending, { color: '#FFA500' }]}>{t('codes.pending')}</Text>
                                                        </View>
                                                        <Text allowFontScaling={false} style={[styles.dateTextTop, { color: tokens.colors.text.dim }]}>
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
                                                { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
                                                isUsed ? styles.usedPremiumCard : (pressed ? { borderColor: bColor } : { borderColor: tokens.colors.borderLight }),
                                                pressed && { transform: [{ scale: 0.985 }] }
                                            ]}
                                        >
                                            <MeshBackground color={isUsed ? tokens.colors.text.dim : bColor} intensity={isUsed ? 0.03 : 0.08} />
                                            <View style={[styles.cardAccentLine, isUsed ? { backgroundColor: tokens.colors.text.dim } : { backgroundColor: bColor }]} />
                                            <View style={styles.cardMainContent}>
                                                <View style={styles.cardHeaderRow}>
                                                    <View style={{ flex: 1 }}>
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                                                            <Text allowFontScaling={false} style={[styles.stationName, { color: isUsed ? tokens.colors.text.dim : tokens.colors.text.primary }]}>
                                                                {voucher.provider}
                                                            </Text>
                                                            <View style={[styles.stationDot, { backgroundColor: isUsed ? tokens.colors.text.dim : bColor }]} />
                                                        </View>
                                                        <View style={{ flexDirection: 'row', alignItems: 'baseline', gap: 6, marginTop: 2 }}>
                                                            <Text allowFontScaling={false} style={[styles.fuelSpec, { color: tokens.colors.text.muted }]}>
                                                                {voucher.fuelType}
                                                            </Text>
                                                            <Text allowFontScaling={false} style={[styles.amountTextInline, { color: isUsed ? tokens.colors.text.dim : bColor }]}>
                                                                | {voucher.amount}L
                                                            </Text>
                                                        </View>

                                                        {/* Bottom Row: ID */}
                                                        <Text allowFontScaling={false} style={[styles.highContrastId, { color: tokens.colors.text.muted }, isUsed && { opacity: 0.5 }]}>ID: {voucher.externalId}</Text>
                                                    </View>
                                                    <View style={{ alignItems: 'flex-end' }}>
                                                        {!isUsed ? (
                                                            <View style={[styles.statusPillActive, { backgroundColor: tokens.colors.primaryDim, borderColor: tokens.colors.primary }]}>
                                                                <Animated.View style={[styles.dotActive, { backgroundColor: tokens.colors.primary, opacity: pulseAnim }]} />
                                                                <Text allowFontScaling={false} style={[styles.statusPillTextActive, { color: tokens.colors.primary }]}>ACTIVE</Text>
                                                            </View>
                                                        ) : (
                                                            <View style={[styles.statusPillUsed, { backgroundColor: tokens.colors.primaryDim, borderColor: tokens.colors.borderLight }]}>
                                                                <Text allowFontScaling={false} style={[styles.statusPillTextUsed, { color: tokens.colors.text.dim }]}>REDEEMED</Text>
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
                        <View style={[styles.modalContent, { backgroundColor: tokens.colors.background, borderColor: tokens.colors.primary }]}>
                            <View style={[styles.modalHeader, { borderBottomWidth: 0, padding: 12 }]}>
                                <View style={[styles.modalBrandBox, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                                    <MeshBackground color={(tokens.colors.text.brand as any)[selectedVoucher.provider.toLowerCase()] || tokens.colors.primary} intensity={0.1} />
                                    <View style={{ width: 8, backgroundColor: (tokens.colors.text.brand as any)[selectedVoucher.provider.toLowerCase()] || tokens.colors.primary }} />
                                    <View style={{ flex: 1, position: 'relative', padding: 16, justifyContent: 'center' }}>
                                        <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                                            <View style={[styles.modalProviderIconBox, { backgroundColor: (tokens.colors.text.brand as any)[selectedVoucher.provider.toLowerCase()] || tokens.colors.primary }]}>
                                                <Fuel size={24} color={tokens.colors.isDark ? "#000" : "#FFF"} />
                                            </View>
                                            <View style={{ flex: 1 }}>
                                                <Text allowFontScaling={false} style={[styles.modalProviderName, { color: tokens.colors.text.secondary }]}>{selectedVoucher.provider}</Text>
                                                <View style={{ flexDirection: 'row', gap: 6, alignItems: 'baseline' }}>
                                                    <Text allowFontScaling={false} style={[styles.modalFuelTitle, { color: tokens.colors.text.primary, textShadowColor: (tokens.colors.text.brand as any)[selectedVoucher.provider.toLowerCase()] || tokens.colors.primary }]}>
                                                        {selectedVoucher.fuelType}
                                                    </Text>
                                                    <Text allowFontScaling={false} style={[styles.modalAmountText, { color: tokens.colors.text.muted }]}>| {selectedVoucher.amount} л.</Text>
                                                </View>
                                            </View>
                                        </View>
                                    </View>
                                </View>
                                <Pressable
                                    onPress={() => setSelectedVoucher(null)}
                                    style={[styles.modalCloseBtn, { position: 'absolute', top: 20, right: 20, zIndex: 10, borderColor: tokens.colors.borderLight }]}
                                >
                                    <X size={20} color={tokens.colors.text.primary} />
                                </Pressable>
                            </View>

                            <View style={styles.qrContainer}>
                                <View style={[styles.qrGlowBorder, { backgroundColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]}>
                                    <View style={[styles.qrBox, { backgroundColor: tokens.colors.isDark ? '#FFF' : '#F0F0F0' }]}>
                                        <QrSync
                                            value={selectedVoucher.qrCodeData || (selectedVoucher as any).qr_code_data || selectedVoucher.externalId || "EMPTY"}
                                            size={220}
                                            color="#000"
                                            isWog={selectedVoucher.provider?.toLowerCase().includes('wog')}
                                            imageUrl={selectedVoucher.imageUrl || (selectedVoucher as any).image_url}
                                        />
                                        <QrScannerOverlay />
                                    </View>
                                </View>

                                {selectedVoucher.status === 'used' && (
                                    <BlurView intensity={40} tint={tokens.colors.isDark ? "dark" : "light"} style={styles.qrUsedOverlay} />
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
                                        { borderRadius: 2, paddingVertical: 14 },
                                        selectedVoucher.status === 'used' ? styles.restoreBtn : [styles.markUsedBtn, { backgroundColor: tokens.colors.primary }]
                                    ]}
                                >
                                    <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'center' }}>
                                        {selectedVoucher.status !== 'used' && <ShieldCheck size={20} color={tokens.colors.isDark ? "black" : "white"} style={{ marginRight: 10 }} />}
                                        <Text
                                            allowFontScaling={false}
                                            style={[
                                                selectedVoucher.status === 'used' ? styles.restoreBtnText : styles.markUsedBtnText,
                                                { color: selectedVoucher.status === 'used' ? tokens.colors.text.primary : (tokens.colors.isDark ? "black" : "white") }
                                            ]}
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
                                    <Copy size={14} color={tokens.colors.text.dim} />
                                    <Text allowFontScaling={false} style={[styles.nodeIdText, { color: tokens.colors.text.dim }]}>ID: {selectedVoucher.externalId}</Text>
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
        fontFamily: 'Rajdhani-Bold',
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
        fontFamily: 'Rajdhani-SemiBold',
        fontSize: 12,
        letterSpacing: 6,
        textTransform: 'uppercase',
        marginBottom: 8,
    },
    premiumCard: {
        width: '100%',
        borderWidth: 1,
        flexDirection: 'row',
        position: 'relative',
        overflow: 'hidden',
        borderRadius: 2,
    },
    activePremiumCard: {
    },
    pendingPremiumCard: {
    },
    usedPremiumCard: {
        opacity: 0.6,
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
        fontFamily: 'Rajdhani-Bold',
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
        fontSize: 10,
        marginTop: 2,
    },
    amountTextInline: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 14,
    },
    highContrastId: {
        fontSize: 14,
        fontFamily: 'Rajdhani-Bold',
        marginTop: 14,
    },
    statusPillPending: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
        marginBottom: 8,
    },
    dotPending: {
        width: 6,
        height: 6,
        borderRadius: 3,
        marginRight: 6,
    },
    statusPillTextPending: {
        fontSize: 8,
        fontFamily: 'Inter-Black',
        letterSpacing: 0.5,
    },
    statusPillActive: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
        marginBottom: 8,
    },
    dotActive: {
        width: 6,
        height: 6,
        borderRadius: 3,
        marginRight: 6,
    },
    statusPillTextActive: {
        fontSize: 8,
        fontFamily: 'Inter-Black',
        letterSpacing: 0.5,
    },
    statusPillUsed: {
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 2,
        borderWidth: 1,
        marginBottom: 8,
    },
    statusPillTextUsed: {
        fontSize: 8,
        fontFamily: 'Inter-Black',
        letterSpacing: 0.5,
    },
    dateTextTop: {
        fontFamily: 'Inter-Bold',
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
        paddingVertical: 80,
        borderRadius: 4,
    },
    emptyIconBox: {
        width: 80,
        height: 80,
        borderRadius: 40,
        alignItems: 'center',
        justifyContent: 'center',
        marginBottom: 24,
    },
    emptyTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 24,
        letterSpacing: 4,
        textTransform: 'uppercase',
    },
    emptySubtitle: {
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
        borderWidth: 1.5,
        borderRadius: 4,
        overflow: 'hidden',
    },
    modalHeader: {
        padding: 24,
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    modalBrandBox: {
        width: '100%',
        borderRadius: 4,
        overflow: 'hidden',
        borderWidth: 1,
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
        fontSize: 20,
        textTransform: 'uppercase',
        marginBottom: 2
    },
    modalFuelTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 28,
        textShadowRadius: 10
    },
    modalAmountText: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 16
    },
    modalCloseBtn: {
        width: 36,
        height: 36,
        borderWidth: 1,
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
        borderRadius: 2,
        overflow: 'hidden',
    },
    qrGlowBorder: {
        padding: 2,
        borderRadius: 4,
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
    },
    restoreBtn: {
        backgroundColor: 'rgba(255,255,255,0.05)',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
    },
    markUsedBtnText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    restoreBtnText: {
        fontFamily: 'Inter-Black',
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
        fontSize: 12,
        fontWeight: '700',
        letterSpacing: 2,
        textTransform: 'uppercase',
        textAlign: 'center',
    },
});