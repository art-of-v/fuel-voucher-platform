import { useRef, useEffect, useMemo } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { ChevronDown, ChevronRight, Clock, CheckCircle, ExternalLink, Trash2 } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { Order, Voucher } from '../core/types/api';
import { VoucherCard } from './VoucherCard';
import { useI18n } from '../core/i18n';
import { Haptics } from '../core/utils/haptics';
import { formatExpirationDate } from '../core/utils/formatters';
import Svg, { Rect, Defs, Pattern, Path, RadialGradient, Stop } from 'react-native-svg';

interface OrderCardProps {
    order: Order;
    isExpanded: boolean;
    onToggle: (orderId: string) => void;
    onVoucherPress: (voucher: Voucher) => void;
    onVoucherLongPress: (voucher: Voucher) => void;
    onPay?: (order: Order) => void;
    onDelete?: (order: Order) => void;
    brandColor: string;
}

const OrderMesh = ({ color, intensity = 0.04 }: { color: string; intensity?: number }) => (
    <View style={StyleSheet.absoluteFill}>
        <Svg height="100%" width="100%">
            <Defs>
                <Pattern
                    id="order-mesh"
                    patternUnits="userSpaceOnUse"
                    width="20"
                    height="20"
                    viewBox="0 0 20 20"
                >
                    <Path
                        d="M 20 0 L 0 0 L 0 20"
                        fill="none"
                        stroke={color}
                        strokeWidth="0.5"
                        opacity={intensity}
                    />
                </Pattern>
                <RadialGradient id="order-glow" cx="50%" cy="0%" r="80%">
                    <Stop offset="0" stopColor={color} stopOpacity={0.04} />
                    <Stop offset="1" stopColor="transparent" stopOpacity="0" />
                </RadialGradient>
            </Defs>
            <Rect width="100%" height="100%" fill="url(#order-mesh)" />
            <Rect width="100%" height="100%" fill="url(#order-glow)" />
        </Svg>
    </View>
);

export function OrderCard({ order, isExpanded, onToggle, onVoucherPress, onVoucherLongPress, onPay, onDelete, brandColor }: OrderCardProps) {
    const tokens = useDesignTokens();
    const { t } = useI18n();
    const expandAnim = useRef(new Animated.Value(0)).current;

    const needsPayment = order.status === 'PENDING_PAYMENT';
    const isPending = order.status === 'PENDING_FULFILLMENT' || needsPayment;
    const isPartiallyRefunded = order.status === 'PARTIALLY_REFUNDED';

    /**
     * One decision, made once: the order's lifecycle state maps to a semantic
     * status role, and every colour below is read off that role. Before Phase 2
     * this file hardcoded #EF4444 / #F59E0B / #a855f7 / #22c55e plus nine
     * separate `rgba(...)` literals, so order state was painted the same way on
     * a white canvas as on a black one.
     *
     * A fully refunded order is `neutral`, not `success` — it used to share the
     * green "fulfilled" treatment, which claimed the fuel had been delivered.
     */
    const statusRole = needsPayment
        ? tokens.colors.status.danger
        : isPending
            ? tokens.colors.status.warning
            : isPartiallyRefunded
                ? tokens.colors.status.info
                : order.status === 'REFUNDED'
                    ? tokens.colors.status.neutral
                    : tokens.colors.status.success;
    const accentColor = statusRole.base;
    const orderVouchers = order.vouchers || [];
    const voucherCount = orderVouchers.length;

    const statusLabel = needsPayment
        ? t('codes.unpaid') || 'UNPAID'
        : isPending
            ? t('codes.pending')
            : order.status === 'REFUNDED'
                ? 'REFUNDED'
                : isPartiallyRefunded
                    ? t('codes.partiallyRefunded')
                    : t('codes.fulfilled');

    useEffect(() => {
        Animated.spring(expandAnim, {
            toValue: isExpanded ? 1 : 0,
            tension: 55,
            friction: 11,
            useNativeDriver: false,
        }).start();
    }, [isExpanded]);

    const vouchersContentHeight = useMemo(() => {
        return voucherCount > 0 ? voucherCount * 260 : 56;
    }, [voucherCount]);

    const bodyMaxHeight = expandAnim.interpolate({
        inputRange: [0, 1],
        outputRange: [0, vouchersContentHeight],
    });

    const bodyOpacity = expandAnim.interpolate({
        inputRange: [0, 0.4, 1],
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
                    borderColor: statusRole.border,
                },
            ]}
        >
            <OrderMesh color={accentColor} intensity={0.04} />

            <Pressable onPress={handleToggle} style={styles.header}>
                <View style={styles.headerLeft}>
                    <View style={styles.providerRow}>
                        <View style={[styles.brandDot, { backgroundColor: brandColor }]} />
                        <Text
                            allowFontScaling={false}
                            style={[styles.providerName, { color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold' }]}
                        >
                            {order.provider}
                        </Text>
                    </View>

                    <View style={styles.specRow}>
                        <Text
                            allowFontScaling={false}
                            style={[styles.fuelSpec, { color: tokens.colors.text.muted, fontFamily: 'Inter-Bold' }]}
                        >
                            {order.fuelName || order.fuelType}
                        </Text>
                        <Text
                            allowFontScaling={false}
                            style={[styles.amountSpec, { color: brandColor, fontFamily: 'Rajdhani-Bold' }]}
                        >
                            {order.lineItems.map(li => `${li.liters}${t('common.liter')}×${li.quantity}`).join(', ')}
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
                            {formatExpirationDate(order.createdAt)}
                        </Text>
                    </View>
                </View>

                 <View style={styles.headerRight}>
                    {needsPayment ? (
                        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
                            {onDelete && (
                                <Pressable
                                    onPress={() => onDelete(order)}
                                    hitSlop={8}
                                    accessibilityLabel={t('codes.deleteOrder')}
                                    style={({ pressed }) => [
                                        styles.deleteButton,
                                        {
                                            opacity: pressed ? 0.6 : 1,
                                        },
                                    ]}
                                >
                                    <Trash2 size={16} color={statusRole.base} />
                                </Pressable>
                            )}
                            <Pressable
                                onPress={() => onPay?.(order)}
                                accessibilityLabel={t('codes.payNow') || 'PAY'}
                                style={({ pressed }) => [
                                    styles.payButton,
                                    {
                                        backgroundColor: statusRole.base,
                                        opacity: pressed ? 0.85 : 1,
                                        transform: pressed ? [{ scale: 0.97 }] : [],
                                    },
                                ]}
                            >
                                <ExternalLink size={13} color={statusRole.onBase} />
                                <Text
                                    allowFontScaling={false}
                                    style={[styles.payButtonText, { color: statusRole.onBase, fontFamily: 'Inter-Black' }]}
                                >
                                    {t('codes.payNow') || 'PAY'}
                                </Text>
                            </Pressable>
                        </View>
                    ) : (
                        <View
                            style={[
                                styles.statusPill,
                                {
                                    backgroundColor: statusRole.subtle,
                                    borderColor: statusRole.border,
                                },
                            ]}
                        >
                            {isPending
                                ? <Clock size={10} color={statusRole.base} />
                                : <CheckCircle size={10} color={statusRole.base} />
                            }
                            <Text
                                allowFontScaling={false}
                                style={[styles.statusText, { color: statusRole.base, fontFamily: 'Inter-Black' }]}
                            >
                                {statusLabel}
                            </Text>
                        </View>
                    )}

                    <View style={[styles.expandBadge, { borderColor: tokens.colors.borderSubtle }]}>
                        <Text
                            allowFontScaling={false}
                            style={[styles.expandBadgeText, { color: accentColor, fontFamily: 'Rajdhani-Bold' }]}
                        >
                            {voucherCount}
                        </Text>
                        {isExpanded
                            ? <ChevronDown size={12} color={accentColor} />
                            : <ChevronRight size={12} color={accentColor} />
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
                            backgroundColor: statusRole.border,
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
                                        onLongPress={onVoucherLongPress}
                                        brandColor={brandColor}
                                    />
                                ))}
                            </View>
                        ) : (
                            <Text
                                allowFontScaling={false}
                                style={[styles.emptyText, { color: tokens.colors.text.dim, fontFamily: 'Inter' }]}
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
        position: 'relative',
    },
    header: {
        flexDirection: 'row',
        padding: 20,
        gap: 8,
    },
    headerLeft: {
        flex: 1,
        gap: 8,
    },
    providerRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
    },
    brandDot: {
        width: 8,
        height: 8,
        borderRadius: 4,
    },
    providerName: {
        fontSize: 20,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    specRow: {
        flexDirection: 'row',
        alignItems: 'baseline',
        gap: 10,
    },
    fuelSpec: {
        fontSize: 11,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    amountSpec: {
        fontSize: 15,
        letterSpacing: 0.5,
    },
    metaRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
        marginTop: 2,
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
        opacity: 0.5,
    },
    headerRight: {
        gap: 8,
        alignItems: 'flex-end',
    },
    statusPill: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 10,
        paddingVertical: 5,
        borderRadius: 8,
        borderWidth: 1,
        gap: 5,
    },
    statusText: {
        fontSize: 8,
        letterSpacing: 1.5,
    },
    expandBadge: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 5,
        borderWidth: 1,
        borderRadius: 14,
        paddingHorizontal: 10,
        paddingVertical: 5,
    },
    expandBadgeText: {
        fontSize: 14,
        letterSpacing: 0.5,
    },
    body: {
        paddingHorizontal: 20,
        paddingBottom: 20,
    },
    separator: {
        height: 1,
        marginBottom: 16,
        borderRadius: 1,
    },
    list: {
        gap: 14,
    },
    emptyText: {
        fontSize: 11,
        textAlign: 'center',
        paddingVertical: 16,
        letterSpacing: 1,
    },
    payButton: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        paddingHorizontal: 16,
        paddingVertical: 10,
        borderRadius: 10,
        gap: 6,
    },
    payButtonText: {
        fontSize: 12,
        letterSpacing: 1.5,
    },
    deleteButton: {
        alignItems: 'center',
        justifyContent: 'center',
        width: 40,
        height: 40,
        borderRadius: 10,
    },
});
