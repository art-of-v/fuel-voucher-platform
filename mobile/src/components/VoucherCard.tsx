import { useRef, useEffect } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { QrCode, Check } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { DesignTokens } from '../core/design/tokens';
import type { Voucher } from '../core/types/api';
import { Haptics } from '../core/utils/haptics';

interface VoucherCardProps {
    voucher: Voucher;
    index: number;
    isExpanded: boolean;
    onShowQr: (voucher: Voucher) => void;
    onLongPress?: (voucher: Voucher) => void;
    brandColor: string;
}

type StatusConfig = {
    label: string;
    icon: 'dot' | 'check';
    dotColor: string;
    textColor: string;
    bg: string;
};

function getStatusConfig(status: string, tokens: DesignTokens, brandColor: string): StatusConfig {
    const s = (status || '').toLowerCase();
    if (s === 'active' || s === 'available') {
        return {
            label: 'Ready',
            icon: 'dot',
            dotColor: brandColor || tokens.colors.primary,
            textColor: brandColor || tokens.colors.primary,
            bg: `${brandColor || tokens.colors.primary}18`,
        };
    }
    if (s === 'used') {
        return {
            label: 'Redeemed',
            icon: 'check',
            dotColor: brandColor || tokens.colors.primary,
            textColor: brandColor || tokens.colors.primary,
            bg: `${brandColor || tokens.colors.primary}12`,
        };
    }
    if (s === 'pending' || s === 'pending_fulfillment') {
        return {
            label: 'Pending',
            icon: 'dot',
            dotColor: '#F59E0B',
            textColor: '#F59E0B',
            bg: 'rgba(245,158,11,0.12)',
        };
    }
    return {
        label: 'Expired',
        icon: 'dot',
        dotColor: tokens.colors.error,
        textColor: tokens.colors.error,
        bg: `${tokens.colors.error}14`,
    };
}

function formatVoucherId(id: string): string {
    const clean = id.replace(/\s/g, '');
    if (clean.length <= 4) return clean;
    const groups: string[] = [];
    for (let i = 0; i < clean.length; i += 4) {
        groups.push(clean.slice(i, i + 4));
    }
    const masked = groups.map((g, idx) =>
        idx < groups.length - 2 ? g : '••••'
    );
    return masked.join('  •  ');
}

export function VoucherCard({ voucher, index, isExpanded, onShowQr, onLongPress, brandColor }: VoucherCardProps) {
    const tokens = useDesignTokens();
    const staggerAnim = useRef(new Animated.Value(0)).current;
    const glowPulse = useRef(new Animated.Value(0)).current;

    const isUsed = voucher.status === 'used';
    const isActive = voucher.status === 'active' || voucher.status === 'available';
    const statusCfg = getStatusConfig(voucher.status, tokens, brandColor);
    const dispColor = isUsed ? tokens.colors.text.dim : (brandColor || tokens.colors.primary);

    useEffect(() => {
        if (isExpanded) {
            Animated.spring(staggerAnim, {
                toValue: 1,
                delay: index * 100,
                tension: 70,
                friction: 12,
                useNativeDriver: true,
            }).start();
        } else {
            staggerAnim.setValue(0);
        }
    }, [isExpanded, index]);

    useEffect(() => {
        if (isActive) {
            Animated.loop(
                Animated.sequence([
                    Animated.timing(glowPulse, { toValue: 1, duration: 2200, useNativeDriver: false }),
                    Animated.timing(glowPulse, { toValue: 0, duration: 2200, useNativeDriver: false }),
                ])
            ).start();
        }
    }, [isActive]);

    const borderGlow = glowPulse.interpolate({
        inputRange: [0, 1],
        outputRange: [
            `${brandColor || tokens.colors.primary}25`,
            `${brandColor || tokens.colors.primary}70`,
        ],
    });

    const voucherId = voucher.externalId || voucher.id || '';

    return (
        <Animated.View
            style={[
                styles.wrapper,
                {
                    opacity: staggerAnim,
                    transform: [
                        {
                            translateY: staggerAnim.interpolate({
                                inputRange: [0, 1],
                                outputRange: [24, 0],
                            }),
                        },
                    ],
                },
            ]}
        >
            <Pressable
                onLongPress={() => {
                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                    onLongPress?.(voucher);
                }}
                style={({ pressed }) => [
                    styles.card,
                    {
                        backgroundColor: isUsed
                            ? 'rgba(255,255,255,0.02)'
                            : tokens.colors.card,
                        borderColor: isActive
                            ? `${brandColor || tokens.colors.primary}50`
                            : isUsed
                                ? 'rgba(255,255,255,0.04)'
                                : tokens.colors.borderLight,
                        opacity: isUsed ? 0.5 : 1,
                        transform: pressed ? [{ scale: 0.99 }] : [],
                    },
                ]}
            >
                {isActive && (
                    <Animated.View
                        style={[
                            styles.activeGlow,
                            {
                                borderColor: borderGlow,
                                shadowColor: brandColor || tokens.colors.primary,
                            },
                        ]}
                        pointerEvents="none"
                    />
                )}

                <View style={styles.content}>
                    {/* Top row: amount + status */}
                    <View style={styles.topRow}>
                        <Text
                            allowFontScaling={false}
                            style={[
                                styles.amount,
                                {
                                    color: isUsed ? tokens.colors.text.dim : tokens.colors.text.primary,
                                    fontFamily: 'Rajdhani-Bold',
                                },
                            ]}
                        >
                            {voucher.amount}
                            <Text
                                allowFontScaling={false}
                                style={[styles.unit, { color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted }]}
                            >
                                {voucher.unit || 'L'}
                            </Text>
                        </Text>

                        <View style={[styles.statusPill, { backgroundColor: statusCfg.bg }]}>
                            {statusCfg.icon === 'dot' ? (
                                <View style={[styles.dot, { backgroundColor: statusCfg.dotColor }]} />
                            ) : (
                                <Check size={10} color={statusCfg.dotColor} strokeWidth={3} />
                            )}
                            <Text
                                allowFontScaling={false}
                                style={[styles.statusLabel, { color: statusCfg.textColor, fontFamily: 'Inter-Bold' }]}
                            >
                                {statusCfg.label}
                            </Text>
                        </View>
                    </View>

                    {/* Fuel type */}
                    <Text
                        allowFontScaling={false}
                        style={[
                            styles.fuel,
                            {
                                color: isUsed ? tokens.colors.text.dim : tokens.colors.text.secondary,
                                fontFamily: 'Inter',
                            },
                        ]}
                    >
                        {voucher.fuelName || voucher.fuelType}
                    </Text>

                    {/* Voucher ID */}
                    {voucherId ? (
                        <Text
                            allowFontScaling={false}
                            style={[
                                styles.idText,
                                {
                                    color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted,
                                    fontFamily: 'Inter',
                                },
                            ]}
                        >
                            {formatVoucherId(voucherId)}
                        </Text>
                    ) : null}

                    {/* Separator */}
                    <View style={[styles.separator, { backgroundColor: isUsed ? 'rgba(255,255,255,0.04)' : 'rgba(255,255,255,0.06)' }]} />

                    {/* Show QR button */}
                    <Pressable
                        onPress={() => {
                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                            onShowQr(voucher);
                        }}
                        style={({ pressed }) => [
                            styles.showQrBtn,
                            {
                                backgroundColor: pressed ? 'rgba(255,255,255,0.06)' : 'transparent',
                            },
                        ]}
                    >
                        <QrCode size={14} color={isUsed ? tokens.colors.text.dim : dispColor} />
                        <Text
                            allowFontScaling={false}
                            style={[
                                styles.showQrText,
                                {
                                    color: isUsed ? tokens.colors.text.dim : dispColor,
                                    fontFamily: 'Inter-Medium',
                                },
                            ]}
                        >
                            {isUsed ? 'View QR' : 'Show QR'}
                        </Text>
                        <Text
                            allowFontScaling={false}
                            style={[styles.chevron, { color: isUsed ? tokens.colors.text.dim : dispColor }]}
                        >
                            →
                        </Text>
                    </Pressable>
                </View>
            </Pressable>
        </Animated.View>
    );
}

const styles = StyleSheet.create({
    wrapper: {
        width: '100%',
    },
    card: {
        borderRadius: 18,
        borderWidth: 1,
        overflow: 'hidden',
        position: 'relative',
    },
    activeGlow: {
        ...StyleSheet.absoluteFillObject,
        borderRadius: 18,
        borderWidth: 1.5,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 0.6,
        shadowRadius: 10,
        elevation: 6,
        opacity: 0.7,
    },
    content: {
        padding: 22,
        gap: 10,
    },
    topRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    amount: {
        fontSize: 32,
        letterSpacing: -1.5,
        lineHeight: 34,
    },
    unit: {
        fontSize: 16,
        fontFamily: 'Rajdhani-SemiBold',
        letterSpacing: 0,
    },
    statusPill: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 10,
        paddingVertical: 5,
        borderRadius: 20,
        gap: 5,
    },
    dot: {
        width: 6,
        height: 6,
        borderRadius: 3,
    },
    statusLabel: {
        fontSize: 10,
        letterSpacing: 0.5,
    },
    fuel: {
        fontSize: 16,
        letterSpacing: 0.3,
    },
    idText: {
        fontSize: 14,
        letterSpacing: 2,
        fontWeight: '500',
    },
    separator: {
        height: 1,
        borderRadius: 1,
        marginVertical: 2,
    },
    showQrBtn: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
        paddingVertical: 6,
        borderRadius: 8,
        alignSelf: 'flex-start',
    },
    showQrText: {
        fontSize: 13,
        letterSpacing: 0.5,
    },
    chevron: {
        fontSize: 14,
        fontFamily: 'Inter',
    },
});
