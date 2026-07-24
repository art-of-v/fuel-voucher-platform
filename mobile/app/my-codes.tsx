import { useState, useEffect, useRef, useMemo } from "react";
import { View, Text, Pressable, ActivityIndicator, Modal, StyleSheet, ScrollView, Animated, Easing, Image, Alert } from "react-native";
import { X, QrCode as QrIcon, Clock, Copy, ShieldCheck, CheckCircle } from "lucide-react-native";
import { getMyVouchers, getMyOrders } from "../src/features/vouchers/api/getVouchers";
import { markVoucherAsUsed, restoreVoucher } from "../src/features/vouchers/api/updateVoucher";
import type { Voucher, Order } from "../src/core/types/api";
import { PageLayout } from "../src/components/page-layout";
import { GridBackground } from "../src/components/grid-background";
import { useDesignTokens } from "../src/core/hooks/useTheme";
import QRCode from "qrcode";
import Svg, { Rect, Defs, RadialGradient, Stop, Path, Polygon, Pattern, LinearGradient } from "react-native-svg";
import * as Clipboard from "expo-clipboard";
import { useI18n } from "../src/core/i18n";
import { Haptics } from "../src/core/utils/haptics";
import { BlurView } from "expo-blur";
import { GlowText } from "../src/components/glow-text";
import { useAuth } from "../src/features/auth/hooks/useAuth";
import { Redirect } from "expo-router";
import { useStore } from "../src/core/state/appStore";
import { OrderCard } from "../src/components/OrderCard";

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
                {
                    position: 'absolute',
                    top: 12,
                    left: 12,
                    right: 12,
                    height: 2,
                    backgroundColor: '#DC2626',
                    zIndex: 10,
                    opacity: 0.8,
                },
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

    const segments: any = isWog
        ? [{ data, mode: 'byte' }]
        : data;

    const qr = QRCode.create(segments, options);
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





export default function MyCodesScreen() {
    const tokens = useDesignTokens();
    const [vouchers, setVouchers] = useState<Voucher[]>([]);
    const [orders, setOrders] = useState<Order[]>([]);
    const [loading, setLoading] = useState(true);
    const [selectedVoucher, setSelectedVoucher] = useState<Voucher | null>(null);
    const [debugInfo, setDebugInfo] = useState<string>('');
    const pulseAnim = useRef(new Animated.Value(1)).current;
    const { t } = useI18n();
    const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
    const storeAuth = useStore(state => state.isAuthenticated);
    const isAuthenticated = storeAuth || hookAuth;
    useEffect(() => {
        if (isAuthenticated) {
            loadData();
        }
    }, [isAuthenticated]);

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
            setDebugInfo('Fetching...');
            const [vouchersData, ordersData] = await Promise.all([
                getMyVouchers(),
                getMyOrders()
            ]);
            setDebugInfo(`vouchers=${JSON.stringify(vouchersData).slice(0,100)} orders=${JSON.stringify(ordersData).slice(0,100)}`);
            setVouchers(Array.isArray(vouchersData) ? vouchersData : []);
            setOrders(Array.isArray(ordersData) ? ordersData : []);
        } catch (error: any) {
            setDebugInfo(`ERR: ${error.message}`);
            console.log("Data fetch failed - likely connection or auth issue:", error.message);
        } finally {
            setLoading(false);
        }
    };

    const refreshVouchers = async () => {
        try {
            const [vouchersData, ordersData] = await Promise.all([
                getMyVouchers(),
                getMyOrders()
            ]);
            const newVouchers = Array.isArray(vouchersData) ? vouchersData : [];
            setVouchers(newVouchers);
            setOrders(Array.isArray(ordersData) ? ordersData : []);
            setSelectedVoucher(prev => {
                if (!prev) return null;
                return newVouchers.find(v => v.id === prev.id) || prev;
            });
        } catch (error: any) {
            console.log("Background refresh failed:", error.message);
        }
    };

    const toggleUsed = async (voucher: Voucher) => {
        const newStatus = voucher.status === 'used' ? 'active' : 'used';
        setSelectedVoucher(prev => prev?.id === voucher.id ? { ...prev, status: newStatus } : prev);
        setVouchers(prev => prev.map(v => v.id === voucher.id ? { ...v, status: newStatus } : v));
        try {
            if (voucher.status === 'used') {
                await restoreVoucher(voucher.id);
            } else {
                await markVoucherAsUsed(voucher.id);
            }
            await refreshVouchers();
        } catch (error: any) {
            await refreshVouchers();
            console.error('Failed to update status:', error);
            Alert.alert('Error', error.message || 'Failed to update voucher status');
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



    const [expandedOrders, setExpandedOrders] = useState<Set<string>>(new Set());

    const toggleOrderExpand = (orderId: string) => {
      setExpandedOrders(prev => {
        const next = new Set(prev);
        if (next.has(orderId)) {
          next.delete(orderId);
        } else {
          next.add(orderId);
        }
        return next;
      });
    };

    const pendingOrders = orders.filter(o => o.status === 'PENDING_FULFILLMENT');
    const fulfilledOrders = orders.filter(o => o.status === 'FULFILLED');

    const assignedVoucherIds = useMemo(() => {
      const ids = new Set<string>();
      orders.forEach(order => {
        (order.vouchers || []).forEach(v => ids.add(v.id));
      });
      return ids;
    }, [orders]);

    const unassignedVouchers = vouchers.filter(v => !assignedVoucherIds.has(v.id));

    if (loading) {
        return (
            <View style={[styles.centerContainer, { backgroundColor: tokens.colors.background }]}>
                <ActivityIndicator size="large" color={tokens.colors.primary} />
            </View>
        );
    }

    const SummaryBar = (
        <View style={{ flexDirection: 'row', gap: 8, marginBottom: 16 }}>
            <View style={{ flex: 1, backgroundColor: 'rgba(255,255,255,0.05)', borderRadius: 8, padding: 10, borderWidth: 1, borderColor: 'rgba(255,255,255,0.08)' }}>
                <Text allowFontScaling={false} style={{ fontSize: 16, fontWeight: '800', color: tokens.colors.primary, textAlign: 'center' }}>{orders.length}</Text>
                <Text allowFontScaling={false} style={{ fontSize: 9, color: tokens.colors.text.muted, textAlign: 'center', marginTop: 2 }}>{t('codes.orders')}</Text>
            </View>
            <View style={{ flex: 1, backgroundColor: 'rgba(34,197,94,0.08)', borderRadius: 8, padding: 10, borderWidth: 1, borderColor: 'rgba(34,197,94,0.2)' }}>
                <Text allowFontScaling={false} style={{ fontSize: 16, fontWeight: '800', color: '#22c55e', textAlign: 'center' }}>{fulfilledOrders.length}</Text>
                <Text allowFontScaling={false} style={{ fontSize: 9, color: '#22c55e', textAlign: 'center', marginTop: 2 }}>{t('codes.fulfilled')}</Text>
            </View>
            <View style={{ flex: 1, backgroundColor: 'rgba(255,165,0,0.08)', borderRadius: 8, padding: 10, borderWidth: 1, borderColor: 'rgba(255,165,0,0.2)' }}>
                <Text allowFontScaling={false} style={{ fontSize: 16, fontWeight: '800', color: '#FFA500', textAlign: 'center' }}>{pendingOrders.length}</Text>
                <Text allowFontScaling={false} style={{ fontSize: 9, color: '#FFA500', textAlign: 'center', marginTop: 2 }}>{t('codes.pending')}</Text>
            </View>
        </View>
    );

    return (
        <PageLayout header={Header} background={<GridBackground />} disableScroll={true}>
            <ScrollView contentContainerStyle={{ paddingHorizontal: GLOBAL_PADDING, paddingBottom: 150 }}>
                {pendingOrders.length === 0 && fulfilledOrders.length === 0 && unassignedVouchers.length === 0 ? (
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
                        {debugInfo ? (
                            <Text allowFontScaling={false} style={{ color: '#FF6B6B', fontSize: 9, marginTop: 20, textAlign: 'center' }}>
                                DEBUG: {debugInfo}
                            </Text>
                        ) : null}
                        <Pressable onPress={loadData} style={{ marginTop: 24, padding: 12, borderWidth: 1, borderColor: tokens.colors.primary }}>
                            <Text style={{ color: tokens.colors.primary, fontSize: 12 }}>⟳ REFRESH</Text>
                        </Pressable>
                    </View>
                ) : (
                    <View style={{ gap: 24 }}>
                        {SummaryBar}

                        {/* PENDING ORDERS */}
                                {pendingOrders.length > 0 && (
                                    <View style={{ gap: 12 }}>
                                        <View style={styles.sectionHeader}>
                                            <Clock size={14} color="#FFA500" />
                                            <View style={{ flex: 1, height: 1, backgroundColor: 'rgba(255, 165, 0, 0.1)', marginLeft: 8 }} />
                                            <Text allowFontScaling={false} style={[styles.sectionLabel, { color: '#FFA500', marginBottom: 0 }]}>
                                                {t('codes.processingPurchases')}
                                            </Text>
                                        </View>
                                        {pendingOrders.map((order) => (
                                            <OrderCard
                                                key={order.id}
                                                order={order}
                                                isExpanded={expandedOrders.has(order.id)}
                                                onToggle={toggleOrderExpand}
                                                onVoucherPress={(v) => {
                                                    const fullVoucher = vouchers.find(v2 => v2.id === v.id) || v;
                                                    setSelectedVoucher(fullVoucher);
                                                }}
                                                onVoucherLongPress={(v) => {
                                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                                    const fullVoucher = vouchers.find(v2 => v2.id === v.id) || v;
                                                    setSelectedVoucher(fullVoucher);
                                                }}
                                                brandColor={getBrandColor(order.provider)}
                                            />
                                        ))}
                                    </View>
                                )}

                        {/* FULFILLED ORDERS WITH VOUCHERS */}
                                {fulfilledOrders.length > 0 && (
                                    <View style={{ gap: 12 }}>
                                        <View style={styles.sectionHeader}>
                                            <CheckCircle size={14} color="#22c55e" />
                                            <View style={{ flex: 1, height: 1, backgroundColor: 'rgba(34, 197, 94, 0.1)', marginLeft: 8 }} />
                                            <Text allowFontScaling={false} style={[styles.sectionLabel, { color: '#22c55e', marginBottom: 0 }]}>
                                                {t('codes.fulfilledOrders')}
                                            </Text>
                                        </View>
                                        {fulfilledOrders.map((order) => (
                                            <OrderCard
                                                key={order.id}
                                                order={order}
                                                isExpanded={expandedOrders.has(order.id)}
                                                onToggle={toggleOrderExpand}
                                                onVoucherPress={(v) => {
                                                    const fullVoucher = vouchers.find(v2 => v2.id === v.id) || v;
                                                    setSelectedVoucher(fullVoucher);
                                                }}
                                                onVoucherLongPress={(v) => {
                                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                                    const fullVoucher = vouchers.find(v2 => v2.id === v.id) || v;
                                                    setSelectedVoucher(fullVoucher);
                                                }}
                                                brandColor={getBrandColor(order.provider)}
                                            />
                                        ))}
                                    </View>
                                )}

                        {/* UNASSIGNED VOUCHERS — not linked to any order */}
                        {unassignedVouchers.length > 0 && (
                            <View style={{ gap: 12 }}>
                                <View style={styles.sectionHeader}>
                                    <View style={{ flex: 1, height: 1, backgroundColor: 'rgba(168,85,247,0.1)', marginRight: 8 }} />
                                    <Text allowFontScaling={false} style={[styles.sectionLabel, { color: '#a855f7', marginBottom: 0 }]}>
                                        {t('codes.availablePayloads')}
                                    </Text>
                                </View>
                                {unassignedVouchers.map((voucher) => {
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
                                                                 {voucher.fuelName || voucher.fuelType}
                                                            </Text>
                                                            <Text allowFontScaling={false} style={[styles.amountTextInline, { color: isUsed ? tokens.colors.text.dim : bColor }]}>
                                                                | {voucher.amount}L
                                                            </Text>
                                                        </View>
                                                    </View>
                                                    <View style={{ alignItems: 'flex-end' }}>
                                                        {!isUsed ? (
                                                            <View style={[styles.statusPillActive, { backgroundColor: tokens.colors.primaryDim, borderColor: tokens.colors.primary }]}>
                                                                <Animated.View style={[styles.dotActive, { backgroundColor: tokens.colors.primary, opacity: pulseAnim }]} />
                                                                <Text allowFontScaling={false} style={[styles.statusPillTextActive, { color: tokens.colors.primary }]}>ACTIVE</Text>
                                                            </View>
                                                        ) : (
                                                            <View style={[styles.statusPillUsed, { backgroundColor: tokens.colors.primaryDim, borderColor: tokens.colors.borderLight }]}>
                                                                <Text allowFontScaling={false} style={[styles.statusPillTextUsed, { color: tokens.colors.text.dim }]}>{t('codes.used')}</Text>
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
                                    {selectedVoucher && (() => {
                                        const bColor = getBrandColor(selectedVoucher.provider);
                                        const isUsed = selectedVoucher.status === 'used';
                                        const qrData = selectedVoucher.qrCodeData || (selectedVoucher as any).qr_code_data || selectedVoucher.externalId || 'EMPTY';
                                        const isWog = selectedVoucher.provider?.toLowerCase().includes('wog');
                                        const imageUrl = selectedVoucher.imageUrl || (selectedVoucher as any).image_url;
                                        return (
                                            <View style={[styles.modalContent, { backgroundColor: tokens.colors.background, borderColor: tokens.colors.borderLight }]}>
                                                <MeshBackground color={bColor} intensity={0.04} />
                                                <View style={[styles.modalAccent, { backgroundColor: isUsed ? tokens.colors.text.dim : bColor }]} />

                                                <View style={styles.modalBody}>
                                                    <View style={styles.modalTopRow}>
                                                        <View style={styles.modalInfo}>
                                                            <Text allowFontScaling={false} style={[styles.modalProvider, { color: tokens.colors.text.secondary }]}>
                                                                {selectedVoucher.provider}
                                                            </Text>
                                                            <Text allowFontScaling={false} style={[styles.modalFuel, { color: tokens.colors.text.primary }]}>
                                                                {selectedVoucher.fuelName || selectedVoucher.fuelType}
                                                            </Text>
                                                            <Text allowFontScaling={false} style={[styles.modalAmount, { color: isUsed ? tokens.colors.text.dim : bColor }]}>
                                                                {selectedVoucher.amount}
                                                                <Text style={[styles.modalUnit, { color: tokens.colors.text.muted }]}> {selectedVoucher.unit || 'L'}</Text>
                                                            </Text>
                                                        </View>
                                                        <View style={[styles.modalStatusPill, { backgroundColor: isUsed ? 'rgba(255,255,255,0.05)' : `${bColor}18` }]}>
                                                            <View style={[styles.modalStatusDot, { backgroundColor: isUsed ? tokens.colors.text.dim : bColor }]} />
                                                            <Text allowFontScaling={false} style={[styles.modalStatusText, { color: isUsed ? tokens.colors.text.dim : bColor }]}>
                                                                {isUsed ? 'REDEEMED' : 'READY'}
                                                            </Text>
                                                        </View>
                                                    </View>

                                                    <View style={[styles.modalQrWrap, { borderColor: tokens.colors.borderLight }]}>
                                                        <View style={styles.modalQrBox}>
                                                            {imageUrl ? (
                                                                <Image
                                                                    source={{ uri: imageUrl }}
                                                                    style={{ width: 220, height: 220 }}
                                                                    resizeMode="contain"
                                                                />
                                                            ) : (
                                                                <QrSync
                                                                    value={qrData}
                                                                    size={220}
                                                                    color="#000"
                                                                    isWog={isWog}
                                                                    imageUrl={imageUrl}
                                                                />
                                                            )}
                                                            <QrScannerOverlay />
                                                        </View>
                                                {isUsed && (
                                                    <BlurView intensity={40} tint={tokens.colors.isDark ? "dark" : "light"} style={styles.modalQrOverlay} />
                                                )}
                                            </View>

                                            <View style={[styles.modalSep, { backgroundColor: tokens.colors.borderLight }]} />

                                            <Pressable
                                                onPress={() => {
                                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
                                                    toggleUsed(selectedVoucher);
                                                }}
                                                style={[
                                                    styles.modalActionBtn,
                                                    {
                                                        backgroundColor: isUsed ? 'rgba(255,255,255,0.05)' : bColor,
                                                        borderColor: isUsed ? 'rgba(255,255,255,0.1)' : 'transparent',
                                                        borderWidth: isUsed ? 1 : 0,
                                                    },
                                                ]}
                                            >
                                                {!isUsed && <ShieldCheck size={20} color={tokens.colors.isDark ? '#000' : '#FFF'} />}
                                                <Text
                                                    allowFontScaling={false}
                                                    style={[
                                                        styles.modalActionText,
                                                        { color: isUsed ? tokens.colors.text.primary : (tokens.colors.isDark ? '#000' : '#FFF') },
                                                    ]}
                                                >
                                                    {isUsed ? t('codes.restoreCode') : t('codes.markAsUsed')}
                                                </Text>
                                            </Pressable>

                                            <Pressable
                                                onPress={() => {
                                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                                    copyToClipboard(selectedVoucher.externalId || selectedVoucher.id);
                                                }}
                                                style={styles.modalIdRow}
                                            >
                                                <Copy size={12} color={tokens.colors.text.dim} />
                                                <Text allowFontScaling={false} style={[styles.modalIdText, { color: tokens.colors.text.dim }]}>
                                                    ID: {selectedVoucher.externalId}
                                                </Text>
                                            </Pressable>
                                        </View>

                                        <Pressable
                                            onPress={() => setSelectedVoucher(null)}
                                            style={[styles.modalCloseBtn, { borderColor: tokens.colors.borderLight }]}
                                        >
                                            <X size={18} color={tokens.colors.text.primary} />
                                        </Pressable>
                                    </View>
                                )
                            })()}
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
        backgroundColor: 'rgba(0,0,0,0.92)',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 32,
    },
    modalContent: {
        width: '100%',
        maxWidth: 380,
        borderWidth: 1,
        borderRadius: 2,
        overflow: 'hidden',
        position: 'relative',
    },
    modalAccent: {
        position: 'absolute',
        left: 0,
        top: 0,
        bottom: 0,
        width: 8,
        zIndex: 2,
    },
    modalBody: {
        padding: 24,
        paddingLeft: 24 + 8 + 12,
        gap: 20,
    },
    modalTopRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
    },
    modalInfo: {
        gap: 4,
    },
    modalProvider: {
        fontFamily: 'Inter-Black',
        fontSize: 11,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    modalFuel: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 26,
        letterSpacing: -0.5,
        textTransform: 'uppercase',
    },
    modalAmount: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 20,
        letterSpacing: -0.5,
    },
    modalUnit: {
        fontFamily: 'Rajdhani-SemiBold',
        fontSize: 14,
    },
    modalStatusPill: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 6,
        paddingHorizontal: 12,
        paddingVertical: 6,
        borderRadius: 2,
    },
    modalStatusDot: {
        width: 6,
        height: 6,
        borderRadius: 3,
    },
    modalStatusText: {
        fontFamily: 'Inter-Black',
        fontSize: 9,
        letterSpacing: 1,
    },
    modalQrWrap: {
        alignItems: 'center',
        justifyContent: 'center',
        padding: 4,
        borderWidth: 1,
        borderRadius: 2,
        position: 'relative',
    },
    modalQrBox: {
        padding: 8,
        backgroundColor: '#FFFFFF',
        borderRadius: 2,
        overflow: 'hidden',
        position: 'relative',
    },
    modalQrOverlay: {
        ...StyleSheet.absoluteFillObject,
        zIndex: 5,
    },
    modalSep: {
        height: 1,
        borderRadius: 1,
    },
    modalActionBtn: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 10,
        height: 56,
        borderRadius: 2,
    },
    modalActionText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    modalIdRow: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 6,
        opacity: 0.5,
    },
    modalIdText: {
        fontFamily: 'Inter',
        fontSize: 11,
        fontWeight: '700',
        letterSpacing: 2,
        textTransform: 'uppercase',
        textAlign: 'center',
    },
    modalCloseBtn: {
        position: 'absolute',
        top: 12,
        right: 12,
        width: 32,
        height: 32,
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 2,
        zIndex: 10,
    },
});