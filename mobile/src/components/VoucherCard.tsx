import { useRef, useEffect, useMemo } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { Check, AlertTriangle, Copy } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { DesignTokens } from '../core/design/tokens';
import type { Voucher } from '../core/types/api';
import { Haptics } from '../core/utils/haptics';
import { MeshBackground } from '../core/ui';

const ACCENT_WIDTH = 5;

interface VoucherCardProps {
    voucher: Voucher;
    index: number;
    isExpanded: boolean;
    onPress: (voucher: Voucher) => void;
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

export function VoucherCard({ voucher, index, isExpanded, onPress, onLongPress, brandColor }: VoucherCardProps) {
    const tokens = useDesignTokens();
    const staggerAnim = useRef(new Animated.Value(0)).current;

    const isUsed = voucher.status === 'used';
    const isActive = voucher.status === 'active' || voucher.status === 'available';
    const statusCfg = getStatusConfig(voucher.status, tokens, brandColor);

    useEffect(() => {
        if (isExpanded) {
            Animated.spring(staggerAnim, {
                toValue: 1,
                delay: index * 60,
                tension: 50,
                friction: 8,
                useNativeDriver: true,
            }).start();
        } else {
            staggerAnim.setValue(0);
        }
    }, [isExpanded, index]);

    const expDays = useMemo(() => {
        if (!voucher.expirationDate) return null;
        const now = new Date();
        const exp = new Date(voucher.expirationDate);
        const diff = Math.ceil((exp.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));
        return diff;
    }, [voucher.expirationDate]);

    const isExpiringSoon = expDays !== null && expDays <= 30;

    const formatExpDate = (dateStr: string) => {
        const d = new Date(dateStr);
        return d.toLocaleDateString('uk-UA', { day: 'numeric', month: 'short', year: '2-digit' });
    };

    return (
        <Animated.View
            style={[
                styles.wrapper,
                {
                    opacity: staggerAnim,
                    transform: [{
                        translateY: staggerAnim.interpolate({
                            inputRange: [0, 1],
                            outputRange: [20, 0],
                        }),
                    }],
                },
            ]}
        >
            <Pressable
                onPress={() => {
                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                    onPress(voucher);
                }}
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
                            ? `${brandColor || tokens.colors.primary}35`
                            : tokens.colors.borderLight,
                        opacity: isUsed ? 0.5 : 1,
                        transform: pressed ? [{ scale: 0.97 }] : [],
                    },
                ]}
            >
                <MeshBackground color={brandColor} intensity={0.07} variant="honeycomb" />
                <View style={[styles.accent, { backgroundColor: isUsed ? tokens.colors.text.dim : (brandColor || tokens.colors.primary) }]} />

                <View style={styles.content}>
                    <View style={styles.topSection}>
                        <View style={styles.topLeft}>
                            <Text
                                allowFontScaling={false}
                                style={[styles.provider, { color: isUsed ? tokens.colors.text.dim : tokens.colors.text.primary }]}
                                numberOfLines={1}
                            >
                                {voucher.provider}
                            </Text>
                            <Text
                                allowFontScaling={false}
                                style={[styles.fuel, { color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted }]}
                                numberOfLines={1}
                            >
                                {voucher.fuelName || voucher.fuelType}
                            </Text>
                        </View>

                        <View style={[styles.statusPill, { backgroundColor: statusCfg.bg }]}>
                            {statusCfg.icon === 'dot' ? (
                                <View style={[styles.dot, { backgroundColor: statusCfg.dotColor }]} />
                            ) : (
                                <Check size={11} color={statusCfg.dotColor} strokeWidth={3} />
                            )}
                            <Text
                                allowFontScaling={false}
                                style={[styles.statusLabel, { color: statusCfg.textColor }]}
                            >
                                {statusCfg.label}
                            </Text>
                        </View>
                    </View>

                    <View style={styles.amountRow}>
                        <Text
                            allowFontScaling={false}
                            style={[
                                styles.amount,
                                { color: isUsed ? tokens.colors.text.dim : tokens.colors.text.primary },
                            ]}
                        >
                            {voucher.amount}
                            <Text
                                allowFontScaling={false}
                                style={[styles.unit, { color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted }]}
                            >
                                {' '}{voucher.unit || 'L'}
                            </Text>
                        </Text>
                    </View>

                    <View style={styles.metaRow}>
                        {voucher.expirationDate && (
                            <View style={styles.expRow}>
                                <Text
                                    allowFontScaling={false}
                                    style={[
                                        styles.expDate,
                                        {
                                            color: isExpiringSoon && !isUsed
                                                ? '#F59E0B'
                                                : tokens.colors.text.dim,
                                        },
                                    ]}
                                >
                                    Exp: {formatExpDate(voucher.expirationDate)}
                                </Text>
                                {isExpiringSoon && !isUsed && (
                                    <AlertTriangle size={12} color="#F59E0B" />
                                )}
                            </View>
                        )}
                        {voucher.externalId && (
                            <View style={styles.idRow}>
                                <Copy size={10} color={tokens.colors.text.dim} />
                                <Text
                                    allowFontScaling={false}
                                    style={[styles.idText, { color: tokens.colors.text.dim }]}
                                    numberOfLines={1}
                                >
                                    {voucher.externalId}
                                </Text>
                            </View>
                        )}
                    </View>
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
    accent: {
        position: 'absolute',
        left: 0,
        top: 0,
        bottom: 0,
        width: ACCENT_WIDTH,
    },
    content: {
        padding: 22,
        paddingLeft: 22 + ACCENT_WIDTH + 16,
        gap: 14,
    },
    topSection: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
    },
    topLeft: {
        flex: 1,
        gap: 4,
        marginRight: 16,
    },
    provider: {
        fontSize: 18,
        fontFamily: 'Rajdhani-Bold',
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    fuel: {
        fontSize: 12,
        fontFamily: 'Inter-Bold',
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    amountRow: {
        flexDirection: 'row',
        alignItems: 'baseline',
    },
    amount: {
        fontSize: 36,
        fontFamily: 'Rajdhani-Bold',
        letterSpacing: -1,
        lineHeight: 38,
    },
    unit: {
        fontSize: 18,
        fontFamily: 'Rajdhani-SemiBold',
        letterSpacing: 0,
    },
    statusPill: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 14,
        paddingVertical: 6,
        borderRadius: 20,
        gap: 6,
    },
    dot: {
        width: 7,
        height: 7,
        borderRadius: 3.5,
    },
    statusLabel: {
        fontSize: 11,
        fontFamily: 'Inter-Black',
        letterSpacing: 0.8,
    },
    metaRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 16,
        flexWrap: 'wrap',
    },
    expRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 6,
    },
    expDate: {
        fontSize: 12,
        fontFamily: 'Inter',
        letterSpacing: 0.5,
    },
    idRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 5,
        opacity: 0.5,
    },
    idText: {
        fontSize: 10,
        fontFamily: 'Inter',
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
});
