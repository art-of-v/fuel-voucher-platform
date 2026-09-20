import { useState, useEffect, useRef } from "react";
import { View, Text, Pressable, StyleSheet, ScrollView, Animated, Alert, RefreshControl } from "react-native";
import { QrCode as QrIcon, Clock, Copy, CheckCircle, AlertTriangle, Ban } from "lucide-react-native";
import type { Order } from "../src/core/types/api";
import { classifyVoucher } from "../src/core/types/api";
import { useMyCodes } from "../src/features/vouchers/hooks/useMyCodes";
import { GridBackground, GridPageLayout, LoadingState, ScreenHeader, useContentInsets } from "../src/core/ui";
import { useDesignTokens } from "../src/core/hooks/useTheme";
import { MeshBackground } from "../src/core/ui";
import { formatExpirationDate } from "../src/core/utils/formatters";
import { VoucherBadge } from "../src/components/VoucherBadge";

import * as Linking from 'expo-linking';
import { useI18n } from "../src/core/i18n";
import { Haptics } from "../src/core/utils/haptics";
import { GlowText } from "../src/components/glow-text";
import { Redirect } from "expo-router";
import { OrderCard } from "../src/components/OrderCard";
import { VoucherDetailModal } from "../src/components/VoucherDetailModal";

const GLOBAL_PADDING = 24;



export default function MyCodesScreen() {
    const tokens = useDesignTokens();
    const contentInsets = useContentInsets();
    const [refreshing, setRefreshing] = useState(false);
    const pulseAnim = useRef(new Animated.Value(1)).current;
    const { t } = useI18n();
    const {
        isAuthenticated,
        authLoading,
        user,
        vouchers,
        orders,
        loading,
        error,
        selectedVoucher,
        setSelectedVoucher,
        pendingOrders,
        fulfilledOrders,
        unassignedVouchers,
        loadData,
        toggleUsed,
        deleteOrder,
    } = useMyCodes();

    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(pulseAnim, { toValue: 0.6, duration: 2000, useNativeDriver: true }),
                Animated.timing(pulseAnim, { toValue: 1, duration: 2000, useNativeDriver: true }),
            ])
        ).start();
    }, []);

    // The lock handles empty state now

    const handlePay = async (order: Order) => {
        if (order.monobankPaymentUrl) {
            await Linking.openURL(order.monobankPaymentUrl);
        }
    };

    const handleDeleteOrder = (order: Order) => {
        // Only allow deleting unpaid orders (PENDING_PAYMENT)
        // PENDING_FULFILLMENT orders have been paid and cannot be deleted by user
        if (order.status === 'PENDING_FULFILLMENT') {
            Alert.alert(t('codes.cannotDeletePaidOrder'));
            return;
        }
        Alert.alert(
            t('codes.deleteOrder'),
            t('codes.deleteOrderConfirm'),
            [
                { text: t('common.cancel'), style: 'cancel' },
                {
                    text: t('common.delete'),
                    style: 'destructive',
                    onPress: async () => {
                        try {
                            await deleteOrder(order.id);
                        } catch {
                            Alert.alert(t('common.error'), t('codes.deleteFailed'));
                        }
                    },
                },
            ],
        );
    };

    const getBrandColor = (provider: string = "") => {
        const p = provider.toLowerCase();
        const brandTokens = tokens.colors.text.brand as any;
        if (p.includes('okko')) return brandTokens.okko;
        if (p.includes('wog')) return brandTokens.wog;
        if (p.includes('upg')) return brandTokens.upg;
        if (p.includes('klo')) return brandTokens.klo;
        if (p.includes('shell')) return brandTokens.shell;
        if (p.includes('socar')) return brandTokens.socar;
        return tokens.colors.primary;
    };

    // Tab root: no back affordance, because there is nothing to pop to.
    const Header = <ScreenHeader title={t('codes.title')} hideBack />;

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

    // Auth guard runs after all hooks (incl. useMyCodes and the effects above) so
    // hook order stays stable across renders (react-hooks/rules-of-hooks).
    if (!isAuthenticated && !authLoading) {
        return <Redirect href="/landing" />;
    }

    if (loading && !refreshing) {
        // Inside `PageLayout`, not instead of it: the previous bare centred `View`
        // dropped the header, the background and the safe-area handling for the
        // duration of the load, so the wallet visibly re-assembled itself.
        return (
            <GridPageLayout header={Header} background={<GridBackground />} disableScroll>
                <LoadingState fullScreen />
            </GridPageLayout>
        );
    }

    const SummaryBar = (
        <View style={{ flexDirection: 'row', gap: 8, marginBottom: 16 }}>
            <View style={{ flex: 1, backgroundColor: tokens.colors.surfaceSunken, borderRadius: 8, padding: 10, borderWidth: 1, borderColor: tokens.colors.borderSubtle }}>
                <Text allowFontScaling={false} style={{ fontSize: 16, fontWeight: '800', color: tokens.colors.primary, textAlign: 'center' }}>{orders.length}</Text>
                <Text allowFontScaling={false} style={{ fontSize: 9, color: tokens.colors.text.muted, textAlign: 'center', marginTop: 2 }}>{t('codes.orders')}</Text>
            </View>
            <View style={{ flex: 1, backgroundColor: `${tokens.colors.primary}14`, borderRadius: 8, padding: 10, borderWidth: 1, borderColor: `${tokens.colors.primary}33` }}>
                <Text allowFontScaling={false} style={{ fontSize: 16, fontWeight: '800', color: tokens.colors.primary, textAlign: 'center' }}>{fulfilledOrders.length}</Text>
                <Text allowFontScaling={false} style={{ fontSize: 9, color: tokens.colors.primary, textAlign: 'center', marginTop: 2 }}>{t('codes.fulfilled')}</Text>
            </View>
            <View style={{ flex: 1, backgroundColor: `${tokens.colors.accent}14`, borderRadius: 8, padding: 10, borderWidth: 1, borderColor: `${tokens.colors.accent}33` }}>
                <Text allowFontScaling={false} style={{ fontSize: 16, fontWeight: '800', color: tokens.colors.accent, textAlign: 'center' }}>{pendingOrders.length}</Text>
                <Text allowFontScaling={false} style={{ fontSize: 9, color: tokens.colors.accent, textAlign: 'center', marginTop: 2 }}>{t('codes.pending')}</Text>
            </View>
        </View>
    );

    return (
        <GridPageLayout header={Header} background={<GridBackground />} disableScroll={true}>
            <ScrollView contentContainerStyle={{ paddingHorizontal: GLOBAL_PADDING, paddingBottom: contentInsets.bottom }}
                refreshControl={
                    <RefreshControl
                        refreshing={refreshing}
                        onRefresh={() => {
                            setRefreshing(true);
                            loadData().finally(() => setRefreshing(false));
                        }}
                        tintColor={tokens.colors.primary}
                        colors={[tokens.colors.primary]}
                    />
                }
            >
                {!loading && error && vouchers.length === 0 && orders.length === 0 ? (
                    <View style={styles.errorContainer}>
                        <AlertTriangle size={40} color={tokens.colors.error} />
                        <Text allowFontScaling={false} style={[styles.errorTitle, { color: tokens.colors.text.primary }]}>
                            {t('codes.failedToLoad')}
                        </Text>
                        <Pressable
                            onPress={loadData}
                            style={({ pressed }) => [{ marginTop: 24, paddingHorizontal: 24, paddingVertical: 12, borderWidth: 1, borderColor: tokens.colors.primary, backgroundColor: pressed ? tokens.colors.primaryDim : 'transparent' }]}
                        >
                            <Text style={{ color: tokens.colors.primary, fontSize: 12, letterSpacing: 1.5 }}>{t('codes.retry')}</Text>
                        </Pressable>
                    </View>
                ) : pendingOrders.length === 0 && fulfilledOrders.length === 0 && unassignedVouchers.length === 0 ? (
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
                                            <Clock size={14} color={tokens.colors.accent} />
                                            <View style={{ flex: 1, height: 1, backgroundColor: `${tokens.colors.accent}1A`, marginLeft: 8 }} />
                                            <Text allowFontScaling={false} style={[styles.sectionLabel, { color: tokens.colors.accent, marginBottom: 0 }]}>
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
                                                onPay={handlePay}
                                                onDelete={handleDeleteOrder}
                                                brandColor={getBrandColor(order.provider)}
                                            />
                                        ))}
                                    </View>
                                )}

                        {/* FULFILLED ORDERS WITH VOUCHERS */}
                                {fulfilledOrders.length > 0 && (
                                    <View style={{ gap: 12 }}>
                                        <View style={styles.sectionHeader}>
                                            <CheckCircle size={14} color={tokens.colors.primary} />
                                            <View style={{ flex: 1, height: 1, backgroundColor: `${tokens.colors.primary}1A`, marginLeft: 8 }} />
                                            <Text allowFontScaling={false} style={[styles.sectionLabel, { color: tokens.colors.primary, marginBottom: 0 }]}>
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
                                    <View style={{ flex: 1, height: 1, backgroundColor: `${tokens.colors.text.neon}1A`, marginRight: 8 }} />
                                    <Text allowFontScaling={false} style={[styles.sectionLabel, { color: tokens.colors.text.neon, marginBottom: 0 }]}>
                                        {t('codes.availablePayloads')}
                                    </Text>
                                </View>
                                {unassignedVouchers.map((voucher) => {
                                    const isUsed = voucher.status === 'used';
                                    const kind = classifyVoucher(voucher, user?.id);
                                    const isBlocked = kind === 'blocked';
                                    const workerName = [voucher.workerFirstName, voucher.workerLastName].filter(Boolean).join(' ').trim();
                                    const bColor = getBrandColor(voucher.provider);
                                    const expDays = voucher.expirationDate
                                        ? Math.ceil((new Date(voucher.expirationDate).getTime() - Date.now()) / 86400000)
                                        : null;
                                    const isExpiringSoon = expDays !== null && expDays <= 30;
                                    return (
                                        <Pressable
                                            key={voucher.id}
                                            onPress={() => {
                                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                                                setSelectedVoucher(voucher);
                                            }}
                                            style={({ pressed }) => [
                                                {
                                                    width: '100%',
                                                    borderRadius: 18,
                                                    borderWidth: 1,
                                                    overflow: 'hidden',
                                                    position: 'relative',
                                                    backgroundColor: isUsed ? tokens.colors.surfaceSunken : tokens.colors.surface,
                                                    borderColor: isUsed ? tokens.colors.borderLight : (pressed ? bColor : tokens.colors.borderLight),
                                                    opacity: isUsed ? 0.5 : 1,
                                                    transform: pressed ? [{ scale: 0.97 }] : [],
                                                },
                                            ]}
                                        >
                                            <MeshBackground color={isUsed ? tokens.colors.text.dim : bColor} intensity={0.07} variant="honeycomb" />
                                            <View style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: 5, backgroundColor: isUsed ? tokens.colors.text.dim : bColor }} />

                                            <View style={{ padding: 22, paddingLeft: 22 + 5 + 16, gap: 14 }}>
                                                <View style={{ flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                                    <View style={{ flex: 1, gap: 4, marginRight: 16 }}>
                                                        <Text allowFontScaling={false} style={{ fontSize: 18, fontFamily: 'Rajdhani-Bold', letterSpacing: 1.5, textTransform: 'uppercase', color: isUsed ? tokens.colors.text.dim : tokens.colors.text.primary }} numberOfLines={1}>
                                                            {voucher.provider}
                                                        </Text>
                                                        <Text allowFontScaling={false} style={{ fontSize: 12, fontFamily: 'Inter-Bold', letterSpacing: 1.5, textTransform: 'uppercase', color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted }} numberOfLines={1}>
                                                            {voucher.fuelName || voucher.fuelType}
                                                        </Text>
                                                        <VoucherBadge kind={kind} />
                                                        {kind === 'gifted_to_worker' && workerName ? (
                                                            <Text allowFontScaling={false} style={{ fontSize: 11, fontFamily: 'Inter-Medium', color: tokens.colors.text.dim }} numberOfLines={1}>
                                                                → {workerName}
                                                            </Text>
                                                        ) : null}
                                                    </View>
                                                    {isBlocked ? (
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', paddingHorizontal: 14, paddingVertical: 6, borderRadius: 20, backgroundColor: `${tokens.colors.error}14`, gap: 6 }}>
                                                            <Ban size={12} color={tokens.colors.error} />
                                                            <Text allowFontScaling={false} style={{ fontSize: 11, fontFamily: 'Inter-Black', letterSpacing: 0.8, color: tokens.colors.error }}>{t('voucher.badge.blocked')}</Text>
                                                        </View>
                                                    ) : !isUsed ? (
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', paddingHorizontal: 14, paddingVertical: 6, borderRadius: 20, backgroundColor: tokens.colors.primaryDim, gap: 6 }}>
                                                            <Animated.View style={{ width: 7, height: 7, borderRadius: 3.5, backgroundColor: tokens.colors.primary, opacity: pulseAnim }} />
                                                            <Text allowFontScaling={false} style={{ fontSize: 11, fontFamily: 'Inter-Black', letterSpacing: 0.8, color: tokens.colors.primary }}>READY</Text>
                                                        </View>
                                                    ) : (
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', paddingHorizontal: 14, paddingVertical: 6, borderRadius: 20, backgroundColor: tokens.colors.primaryDim, gap: 6 }}>
                                                            <Text allowFontScaling={false} style={{ fontSize: 11, fontFamily: 'Inter-Black', letterSpacing: 0.8, color: tokens.colors.text.dim }}>{t('codes.used')}</Text>
                                                        </View>
                                                    )}
                                                </View>

                                                <View style={{ flexDirection: 'row', alignItems: 'baseline' }}>
                                                    <Text allowFontScaling={false} style={{ fontSize: 36, fontFamily: 'Rajdhani-Bold', letterSpacing: -1, lineHeight: 38, color: isUsed ? tokens.colors.text.dim : tokens.colors.text.primary }}>
                                                        {voucher.amount}
                                                        <Text allowFontScaling={false} style={{ fontSize: 18, fontFamily: 'Rajdhani-SemiBold', letterSpacing: 0, color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted }}>
                                                            {' '}{voucher.unit || t('common.liter')}
                                                        </Text>
                                                    </Text>
                                                </View>

                                                <View style={{ flexDirection: 'row', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
                                                    {voucher.expirationDate && (
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                                                            <Text allowFontScaling={false} style={{ fontSize: 12, fontFamily: 'Inter', letterSpacing: 0.5, color: isExpiringSoon && !isUsed ? tokens.colors.error : tokens.colors.text.dim }}>
                                                                {t('codes.expires')}: {formatExpirationDate(voucher.expirationDate)}
                                                            </Text>
                                                            {isExpiringSoon && !isUsed && <AlertTriangle size={12} color={tokens.colors.error} />}
                                                        </View>
                                                    )}
                                                    {voucher.externalId && (
                                                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 5, opacity: 0.5 }}>
                                                            <Copy size={10} color={tokens.colors.text.dim} />
                                                            <Text allowFontScaling={false} style={{ fontSize: 10, fontFamily: 'Inter', letterSpacing: 1, textTransform: 'uppercase', color: tokens.colors.text.dim }} numberOfLines={1}>
                                                                {voucher.externalId}
                                                            </Text>
                                                        </View>
                                                    )}
                                                </View>
                                            </View>

                                            {isUsed && (
                                                <View style={styles.diagonalStampContainer}>
                                                    {/*
                                                      The "USED" watermark. Its three
                                                      colours were raw translucent
                                                      whites plus a black plaque, so on
                                                      the light themes it was a dark box
                                                      with near-invisible text. "Used"
                                                      is precisely what the neutral
                                                      status role is for.
                                                    */}
                                                    <View style={[styles.diagonalStamp, { borderColor: tokens.colors.status.neutral.border }]}>
                                                        <View
                                                            style={[
                                                                styles.diagonalStampInner,
                                                                {
                                                                    borderColor: tokens.colors.status.neutral.border,
                                                                    backgroundColor: tokens.colors.status.neutral.subtle,
                                                                },
                                                            ]}
                                                        >
                                                            <Text style={[styles.diagonalStampText, { color: tokens.colors.status.neutral.base }]}>{t('codes.used')}</Text>
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

                            <VoucherDetailModal
                                visible={!!selectedVoucher}
                                voucher={selectedVoucher}
                                user={user}
                                onClose={() => setSelectedVoucher(null)}
                                onToggleUsed={toggleUsed}
                                brandColor={getBrandColor(selectedVoucher?.provider)}
                            />
                    </GridPageLayout>
                    );
                }

                const styles = StyleSheet.create({
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
        padding: 2,
        transform: [{ rotate: '-12deg' }],
    },
    diagonalStampInner: {
        borderWidth: 2,
        paddingHorizontal: 20,
        paddingVertical: 6,
    },
    diagonalStampText: {
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
    errorContainer: {
        marginTop: 80,
        alignItems: 'center',
        justifyContent: 'center',
        paddingVertical: 80,
        borderRadius: 4,
        gap: 16,
    },
    errorTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 20,
        letterSpacing: 3,
        textTransform: 'uppercase',
        textAlign: 'center',
    },

});





