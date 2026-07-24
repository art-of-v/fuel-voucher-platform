import { useRef, useEffect } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { Check } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { DesignTokens } from '../core/design/tokens';
import type { Voucher } from '../core/types/api';
import { Haptics } from '../core/utils/haptics';
import { MeshBackground } from '../core/ui';

const ACCENT_WIDTH = 12;

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

function formatVoucherId(id: string): string {
    const clean = id.replace(/\s/g, '');
    if (clean.length <= 4) return clean;
    const groups: string[] = [];
    for (let i = 0; i < clean.length; i += 4) {
        groups.push(clean.slice(i, i + 4));
    }
    return groups.join(' ');
}

export function VoucherCard({ voucher, index, isExpanded, onPress, onLongPress, brandColor }: VoucherCardProps) {
    const tokens = useDesignTokens();
    const staggerAnim = useRef(new Animated.Value(0)).current;

    const isUsed = voucher.status === 'used';
    const isActive = voucher.status === 'active' || voucher.status === 'available';
    const statusCfg = getStatusConfig(voucher.status, tokens, brandColor);
    const dispColor = isUsed ? tokens.colors.text.dim : (brandColor || tokens.colors.primary);

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

    const voucherId = voucher.externalId || voucher.id || '';

    return (
        <Animated.View
            style={[
                styles.wrapper,
                {
                    opacity: staggerAnim,
                    transform: [{
                        translateY: staggerAnim.interpolate({
                            inputRange: [0, 1],
                            outputRange: [15, 0],
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
                            ? `${brandColor || tokens.colors.primary}40`
                            : tokens.colors.borderLight,
                        opacity: isUsed ? 0.5 : 1,
                        transform: pressed ? [{ scale: 0.99 }] : [],
                    },
                ]}
            >
                <MeshBackground color={brandColor} intensity={0.05} />
                <View style={[styles.accent, { backgroundColor: isUsed ? tokens.colors.text.dim : dispColor }]} />

                <View style={styles.content}>
                    <View style={styles.topRow}>
                        <View style={styles.amountRow}>
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
                        </View>

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

                    <View style={styles.metaRow2}>
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
        borderRadius: 2,
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
        padding: 16,
        paddingLeft: 16 + ACCENT_WIDTH + 12,
        gap: 10,
    },
    topRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    amountRow: {
        flexDirection: 'row',
        alignItems: 'baseline',
    },
    amount: {
        fontSize: 28,
        letterSpacing: -1,
        lineHeight: 30,
    },
    unit: {
        fontSize: 14,
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
    metaRow2: {
        flexDirection: 'row',
        alignItems: 'baseline',
        gap: 10,
    },
    fuel: {
        fontSize: 14,
        letterSpacing: 0.3,
    },
    idText: {
        fontSize: 12,
        letterSpacing: 2,
        fontWeight: '500',
    },
    separator: {
        height: 1,
        borderRadius: 1,
    },
    showQrBtn: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
        paddingVertical: 6,
        borderRadius: 2,
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
