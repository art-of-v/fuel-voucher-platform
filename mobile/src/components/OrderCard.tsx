import { useRef, useEffect, useMemo } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { ChevronDown, ChevronRight, Clock, CheckCircle } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { Order, Voucher } from '../core/types/api';
import { VoucherCard } from './VoucherCard';
import { useI18n } from '../core/i18n';
import { Haptics } from '../core/utils/haptics';

interface OrderCardProps {
    order: Order;
    isExpanded: boolean;
    onToggle: (orderId: string) => void;
    onVoucherPress: (voucher: Voucher) => void;
    brandColor: string;
}

const SPACING = 8;

export function OrderCard({ order, isExpanded, onToggle, onVoucherPress, brandColor }: OrderCardProps) {
    const tokens = useDesignTokens();
    const { t } = useI18n();
    const expandAnim = useRef(new Animated.Value(0)).current;

    const isPending = order.status === 'PENDING_FULFILLMENT';
    const accentColor = isPending ? '#F59E0B' : '#22c55e';
    const orderVouchers = order.vouchers || [];
    const voucherCount = orderVouchers.length;

    const statusLabel = isPending
        ? t('codes.pending')
        : order.status === 'REFUNDED'
            ? 'REFUNDED'
            : t('codes.fulfilled');

    useEffect(() => {
        Animated.spring(expandAnim, {
            toValue: isExpanded ? 1 : 0,
            tension: 60,
            friction: 12,
            useNativeDriver: false,
        }).start();
    }, [isExpanded]);

    const vouchersContentHeight = useMemo(() => {
        return voucherCount > 0 ? voucherCount * 156 : 48;
    }, [voucherCount]);

    const bodyMaxHeight = expandAnim.interpolate({
        inputRange: [0, 1],
        outputRange: [0, vouchersContentHeight],
    });

    const bodyOpacity = expandAnim.interpolate({
        inputRange: [0, 0.35, 1],
        outputRange: [0, 0, 1],
    });

    const separatorScaleX = expandAnim.interpolate({
        inputRange: [0, 1],
        outputRange: [0, 1],
    });

    const handleToggle = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        onToggle(order.id);
    };

    return (
        <View
            style={[
                styles.container,
                {
                    backgroundColor: tokens.colors.card,
                    borderColor: isPending
                        ? 'rgba(245,158,11,0.12)'
                        : 'rgba(34,197,94,0.12)',
                },
            ]}
        >
            <Pressable onPress={handleToggle} style={styles.header}>
                <View style={styles.headerLeft}>
                    <View style={styles.providerRow}>
                        <View style={[styles.brandDot, { backgroundColor: brandColor }]} />
                        <Text
                            allowFontScaling={false}
                            style={[styles.providerName, { color: tokens.colors.text.primary }]}
                        >
                            {order.provider}
                        </Text>
                    </View>

                    <View style={styles.specRow}>
                        <Text
                            allowFontScaling={false}
                            style={[styles.fuelSpec, { color: tokens.colors.text.muted }]}
                        >
                            {order.fuelName || order.fuelType}
                        </Text>
                        <Text
                            allowFontScaling={false}
                            style={[styles.amountSpec, { color: brandColor }]}
                        >
                            {order.liters}L × {order.quantity}
                        </Text>
                    </View>

                    <View style={styles.metaRow}>
                        <Text
                            allowFontScaling={false}
                            style={[styles.metaText, { color: tokens.colors.text.dim }]}
                        >
                            ID: {(order.id || '').slice(0, 10).toUpperCase()}
                        </Text>
                        <View style={[styles.metaDot, { backgroundColor: tokens.colors.text.dim }]} />
                        <Text
                            allowFontScaling={false}
                            style={[styles.metaText, { color: tokens.colors.text.dim }]}
                        >
                            {new Date(order.createdAt).toLocaleDateString('uk-UA', {
                                day: 'numeric',
                                month: 'short',
                                year: '2-digit',
                            })}
                        </Text>
                    </View>
                </View>

                <View style={styles.headerRight}>
                    <View
                        style={[
                            styles.statusPill,
                            {
                                backgroundColor: isPending
                                    ? 'rgba(245,158,11,0.1)'
                                    : 'rgba(34,197,94,0.1)',
                                borderColor: isPending
                                    ? 'rgba(245,158,11,0.2)'
                                    : 'rgba(34,197,94,0.2)',
                            },
                        ]}
                    >
                        {isPending
                            ? <Clock size={10} color="#F59E0B" />
                            : <CheckCircle size={10} color="#22c55e" />
                        }
                        <Text
                            allowFontScaling={false}
                            style={[styles.statusText, { color: isPending ? '#F59E0B' : '#22c55e' }]}
                        >
                            {statusLabel}
                        </Text>
                    </View>

                    <View style={[styles.expandBadge, { borderColor: 'rgba(255,255,255,0.06)' }]}>
                        <Text
                            allowFontScaling={false}
                            style={[styles.expandBadgeText, { color: accentColor }]}
                        >
                            {voucherCount}
                        </Text>
                        {isExpanded
                            ? <ChevronDown size={11} color={accentColor} />
                            : <ChevronRight size={11} color={accentColor} />
                        }
                    </View>
                </View>
            </Pressable>

            <Animated.View
                style={[
                    styles.body,
                    { maxHeight: bodyMaxHeight, opacity: bodyOpacity },
                ]}
                pointerEvents={isExpanded ? 'auto' : 'none'}
            >
                <Animated.View
                    style={[
                        styles.separator,
                        {
                            backgroundColor: isPending
                                ? 'rgba(245,158,11,0.08)'
                                : 'rgba(34,197,94,0.08)',
                            transform: [{ scaleX: separatorScaleX }],
                        },
                    ]}
                />

                {voucherCount > 0 ? (
                    <View style={styles.list}>
                        {orderVouchers.map((voucher, idx) => (
                            <VoucherCard
                                key={voucher.id}
                                voucher={voucher}
                                index={idx}
                                isExpanded={isExpanded}
                                onPress={onVoucherPress}
                                brandColor={brandColor}
                            />
                        ))}
                    </View>
                ) : (
                    <Text
                        allowFontScaling={false}
                        style={[styles.emptyText, { color: tokens.colors.text.dim }]}
                    >
                        {t('codes.noVouchersYet')}
                    </Text>
                )}
            </Animated.View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        borderRadius: 20,
        borderWidth: 1,
        overflow: 'hidden',
    },
    header: {
        flexDirection: 'row',
        padding: SPACING * 2.5,
        gap: SPACING,
    },
    headerLeft: {
        flex: 1,
        gap: SPACING,
    },
    providerRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: SPACING,
    },
    brandDot: {
        width: 8,
        height: 8,
        borderRadius: 4,
    },
    providerName: {
        fontSize: 19,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
        fontFamily: 'Rajdhani-Bold',
    },
    specRow: {
        flexDirection: 'row',
        alignItems: 'baseline',
        gap: SPACING * 1.25,
    },
    fuelSpec: {
        fontSize: 10,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
        fontFamily: 'Inter-Bold',
    },
    amountSpec: {
        fontSize: 14,
        letterSpacing: 0.5,
        fontFamily: 'Rajdhani-Bold',
    },
    metaRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: SPACING,
        marginTop: 1,
    },
    metaText: {
        fontSize: 9,
        fontFamily: 'Inter',
        letterSpacing: 1,
    },
    metaDot: {
        width: 2,
        height: 2,
        borderRadius: 1,
        opacity: 0.4,
    },
    headerRight: {
        gap: SPACING,
        alignItems: 'flex-end',
    },
    statusPill: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: SPACING * 1.25,
        paddingVertical: SPACING * 0.625,
        borderRadius: 8,
        borderWidth: 1,
        gap: 5,
        minHeight: 44,
    },
    statusText: {
        fontSize: 8,
        letterSpacing: 1.5,
        fontFamily: 'Inter-Black',
    },
    expandBadge: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 5,
        borderWidth: 1,
        borderRadius: 14,
        paddingHorizontal: SPACING * 1.25,
        paddingVertical: SPACING * 0.625,
    },
    expandBadgeText: {
        fontSize: 13,
        letterSpacing: 0.5,
        fontFamily: 'Rajdhani-Bold',
    },
    body: {
        paddingHorizontal: SPACING * 2.5,
        paddingBottom: SPACING * 2.5,
    },
    separator: {
        height: 1,
        marginBottom: SPACING * 2,
        borderRadius: 1,
    },
    list: {
        gap: SPACING * 1.5,
    },
    emptyText: {
        fontSize: 10,
        textAlign: 'center',
        paddingVertical: SPACING * 2,
        letterSpacing: 1,
        fontFamily: 'Inter',
    },
});
