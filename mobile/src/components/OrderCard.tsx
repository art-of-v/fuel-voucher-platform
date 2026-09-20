import { useRef, useEffect, useMemo } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import ReanimatedSwipeable, { type SwipeableMethods } from 'react-native-gesture-handler/ReanimatedSwipeable';
import { ChevronDown, ChevronRight, Clock, CheckCircle, CreditCard, Trash2 } from 'lucide-react-native';
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
    const swipeableRef = useRef<SwipeableMethods>(null);
    /**
     * Read in `handleToggle` to make the reveal modal. Kept in a ref rather
     * than state because it must not trigger a re-render of the whole card
     * mid-gesture.
     */
    const isSwipeOpenRef = useRef(false);

    const needsPayment = order.status === 'PENDING_PAYMENT';
    const isPending = order.status === 'PENDING_FULFILLMENT' || needsPayment;
    const isPartiallyRefunded = order.status === 'PARTIALLY_REFUNDED';
    /**
     * Only an unpaid order has anything to swipe to. Fulfilled orders keep a
     * plain card so the list has no dead horizontal drag zones, and no
     * Swipeable is mounted behind them to steal the scroll gesture.
     */
    const canSwipe = needsPayment && (!!onPay || !!onDelete);

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
        // The reveal is modal: while actions are showing, the first tap on the
        // card dismisses them instead of also expanding the order.
        if (isSwipeOpenRef.current) {
            swipeableRef.current?.close();
            return;
        }
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        onToggle(order.id);
    };

    // Both actions collapse the reveal first, so the alert that follows (delete
    // confirmation) is not left floating above an open card.
    const handleSwipePay = () => {
        swipeableRef.current?.close();
        onPay?.(order);
    };

    const handleSwipeDelete = () => {
        swipeableRef.current?.close();
        onDelete?.(order);
    };

    /**
     * iOS Clock-style reveal. `renderRightActions` output is laid out inside an
     * `absoluteFill` row-reversed container, so this row stretches to the full
     * card height for free and the last child sits against the screen edge —
     * hence delete last, since iOS reserves the outermost slot for the
     * destructive action and puts anything secondary inboard of it.
     */
    const renderRightActions = () => (
        <View style={styles.swipeActions}>
            {onPay && (
                // The coloured box is a plain View so its fill always paints:
                // a Pressable whose background lives only in a function-form
                // style does not render that background at rest on Fabric when
                // nested in a Reanimated view (the swipe container). The inner
                // Pressable is transparent and only carries touch + feedback.
                <View style={[styles.swipeAction, { backgroundColor: tokens.colors.primary }]}>
                    <Pressable
                        onPress={handleSwipePay}
                        accessibilityLabel={t('codes.payNow') || 'PAY'}
                        style={styles.swipeActionPressable}
                    >
                        {({ pressed }) => (
                            <View style={[styles.swipeActionInner, { opacity: pressed ? 0.7 : 1 }]}>
                                <CreditCard size={22} color={tokens.colors.text.onPrimary} />
                                <Text
                                    allowFontScaling={false}
                                    style={[styles.swipeActionText, { color: tokens.colors.text.onPrimary, fontFamily: 'Inter-Black' }]}
                                >
                                    {t('codes.payNow') || 'PAY'}
                                </Text>
                            </View>
                        )}
                    </Pressable>
                </View>
            )}
            {onDelete && (
                <View style={[styles.swipeAction, { backgroundColor: statusRole.base }]}>
                    <Pressable
                        onPress={handleSwipeDelete}
                        accessibilityLabel={t('codes.deleteAction') || 'DELETE'}
                        style={styles.swipeActionPressable}
                    >
                        {({ pressed }) => (
                            <View style={[styles.swipeActionInner, { opacity: pressed ? 0.7 : 1 }]}>
                                <Trash2 size={22} color={statusRole.onBase} />
                                <Text
                                    allowFontScaling={false}
                                    style={[styles.swipeActionText, { color: statusRole.onBase, fontFamily: 'Inter-Black' }]}
                                >
                                    {t('codes.deleteAction') || 'DELETE'}
                                </Text>
                            </View>
                        )}
                    </Pressable>
                </View>
            )}
        </View>
    );

    return (
        <ReanimatedSwipeable
            ref={swipeableRef}
            enabled={canSwipe}
            friction={2}
            rightThreshold={40}
            overshootRight={false}
            renderRightActions={canSwipe ? renderRightActions : undefined}
            // The Clock "tick": you feel the reveal land. Fires before the
            // settle animation, which is what makes it read as a detent.
            onSwipeableWillOpen={() => {
                isSwipeOpenRef.current = true;
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            }}
            onSwipeableClose={() => {
                isSwipeOpenRef.current = false;
            }}
        >
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

                <Pressable
                    onPress={handleToggle}
                    style={styles.header}
                    // Swipe actions are invisible to VoiceOver, so the same two
                    // actions are exposed on the card itself via the rotor.
                    accessibilityActions={
                        canSwipe
                            ? [
                                ...(onPay ? [{ name: 'pay', label: t('codes.payNow') || 'PAY' }] : []),
                                ...(onDelete ? [{ name: 'delete', label: t('codes.deleteAction') || 'DELETE' }] : []),
                            ]
                            : undefined
                    }
                    onAccessibilityAction={(event) => {
                        const action = event.nativeEvent.actionName;
                        if (action === 'pay') onPay?.(order);
                        else if (action === 'delete') onDelete?.(order);
                    }}
                >
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
                        {/*
                         * Actions live in the swipe reveal, so an unpaid card now
                         * reads its state from the same pill as every other order
                         * instead of swapping the pill out for a button row.
                         */}
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
        </ReanimatedSwipeable>
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
    /**
     * The swipe reveal. RNGH mounts this inside an `absoluteFill`, row-reversed
     * row that stretches to the card's full height, so `alignItems: 'center'`
     * floats the two buttons vertically centred in that strip rather than
     * stretching them edge to edge. `paddingHorizontal` insets them from the
     * card's trailing edge (left) and the screen edge (right); `gap` separates
     * them — the intrinsic width of this row is what the component measures to
     * decide how far the card opens.
     *
     * They are discrete buttons, not one coloured drawer: each carries its own
     * `radius.md` (10) fill, the app's own button shape. This is the fix for the
     * reveal reading as bare text — the legacy `Swipeable` never gave this
     * container height on the New Architecture, so the fills collapsed away.
     */
    swipeActions: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 12,
        gap: 10,
    },
    swipeAction: {
        width: 88,
        height: 72,
        borderRadius: 10,
        overflow: 'hidden',
    },
    swipeActionPressable: {
        flex: 1,
    },
    swipeActionInner: {
        flex: 1,
        alignItems: 'center',
        justifyContent: 'center',
        gap: 7,
    },
    swipeActionText: {
        fontSize: 12,
        letterSpacing: 0.5,
    },
});
