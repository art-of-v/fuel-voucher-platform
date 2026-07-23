import { useRef, useEffect, useMemo } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { ChevronDown, ChevronRight, Clock, CheckCircle } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { Order, Voucher } from '../core/types/api';
import { VoucherCard } from './VoucherCard';
import { useI18n } from '../core/i18n';
import { Haptics } from '../core/utils/haptics';
import Svg, { Rect, Defs, Pattern, Path, LinearGradient, RadialGradient, Stop } from 'react-native-svg';

interface OrderCardProps {
    order: Order;
    isExpanded: boolean;
    onToggle: (orderId: string) => void;
    onVoucherPress: (voucher: Voucher) => void;
    brandColor: string;
}

const HoneycombMesh = ({ color, intensity = 0.06 }: { color: string; intensity?: number }) => (
    <View style={StyleSheet.absoluteFill}>
        <Svg height="100%" width="100%">
            <Defs>
                <Pattern
                    id="mesh-grid"
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
            <Rect width="100%" height="100%" fill="url(#mesh-grid)" />
            <Rect width="100%" height="100%" fill="url(#order-glow)" />
        </Svg>
    </View>
);

export function OrderCard({ order, isExpanded, onToggle, onVoucherPress, brandColor }: OrderCardProps) {
    const tokens = useDesignTokens();
    const { t } = useI18n();
    const expandAnim = useRef(new Animated.Value(0)).current;
    const contentHeight = useRef(new Animated.Value(0)).current;

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
            tension: 50,
            friction: 10,
            useNativeDriver: false,
        }).start();
    }, [isExpanded]);

    const vouchersContentHeight = useMemo(() => {
        const rowCount = Math.ceil(voucherCount / 2);
        const rowHeight = 160 + 10;
        return voucherCount > 0 ? rowCount * rowHeight : 60;
    }, [voucherCount]);

    const bodyMaxHeight = expandAnim.interpolate({
        inputRange: [0, 1],
        outputRange: [0, vouchersContentHeight],
    });

    const bodyOpacity = expandAnim.interpolate({
        inputRange: [0, 0.5, 1],
        outputRange: [0, 0, 1],
    });

    const separatorScale = expandAnim.interpolate({
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
                    borderColor: isPending ? 'rgba(245,158,11,0.2)' : 'rgba(34,197,94,0.2)',
                },
            ]}
        >
            <HoneycombMesh color={accentColor} intensity={0.05} />

            {/* Header Area */}
            <Pressable
                onPress={handleToggle}
                style={styles.header}
            >
                <View style={styles.headerLeft}>
                    {/* Provider + Fuel row */}
                    <View style={styles.providerRow}>
                        <View style={[styles.brandDot, { backgroundColor: brandColor }]} />
                        <Text
                            allowFontScaling={false}
                            style={[
                                styles.providerName,
                                { color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold' },
                            ]}
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
                                    ? 'rgba(245,158,11,0.12)'
                                    : 'rgba(34,197,94,0.12)',
                                borderColor: isPending
                                    ? 'rgba(245,158,11,0.3)'
                                    : 'rgba(34,197,94,0.3)',
                            },
                        ]}
                    >
                        {isPending
                            ? <Clock size={10} color="#F59E0B" />
                            : <CheckCircle size={10} color="#22c55e" />
                        }
                        <Text
                            allowFontScaling={false}
                            style={[
                                styles.statusText,
                                { color: isPending ? '#F59E0B' : '#22c55e', fontFamily: 'Inter-Black' },
                            ]}
                        >
                            {statusLabel}
                        </Text>
                    </View>

                    <View style={[styles.expandBadge, { borderColor: tokens.colors.borderLight }]}>
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

            {/* Expandable body */}
            <Animated.View
                style={[
                    styles.body,
                    {
                        maxHeight: bodyMaxHeight,
                        opacity: bodyOpacity,
                    },
                ]}
                pointerEvents={isExpanded ? 'auto' : 'none'}
            >
                <Animated.View
                    style={[
                        styles.separator,
                        {
                            backgroundColor: isPending
                                ? 'rgba(245,158,11,0.12)'
                                : 'rgba(34,197,94,0.12)',
                            transform: [{ scaleX: separatorScale }],
                        },
                    ]}
                />

                {voucherCount > 0 ? (
                    <View style={styles.grid}>
                        {orderVouchers.map((voucher, idx) => (
                            <VoucherCard
                                key={voucher.id}
                                voucher={voucher}
                                index={idx}
                                isExpanded={isExpanded}
                                onVoucherPress={onVoucherPress}
                                brandColor={brandColor}
                            />
                        ))}
                    </View>
                ) : (
                    <Text
                        allowFontScaling={false}
                        style={[styles.emptyVouchers, { color: tokens.colors.text.dim, fontFamily: 'Inter' }]}
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
        padding: 16,
        gap: 8,
    },
    headerLeft: {
        flex: 1,
        gap: 6,
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
        gap: 8,
    },
    fuelSpec: {
        fontSize: 10,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    amountSpec: {
        fontSize: 14,
        letterSpacing: 0.5,
    },
    metaRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
        marginTop: 2,
    },
    metaText: {
        fontSize: 9,
        fontFamily: 'Inter',
        letterSpacing: 1,
    },
    headerRight: {
        alignItems: 'flex-end',
        justifyContent: 'space-between',
        gap: 8,
    },
    statusPill: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 8,
        paddingVertical: 4,
        borderRadius: 6,
        borderWidth: 1,
        gap: 4,
    },
    statusText: {
        fontSize: 8,
        letterSpacing: 1.5,
    },
    expandBadge: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 4,
        borderWidth: 1,
        borderRadius: 12,
        paddingHorizontal: 8,
        paddingVertical: 4,
    },
    expandBadgeText: {
        fontSize: 13,
        letterSpacing: 0.5,
    },
    body: {
        paddingHorizontal: 16,
        paddingBottom: 16,
    },
    separator: {
        height: 1,
        marginBottom: 14,
        borderRadius: 1,
    },
    grid: {
        flexDirection: 'row',
        flexWrap: 'wrap',
        gap: 10,
    },
    emptyVouchers: {
        fontSize: 10,
        textAlign: 'center',
        paddingVertical: 16,
        letterSpacing: 1,
    },
});
