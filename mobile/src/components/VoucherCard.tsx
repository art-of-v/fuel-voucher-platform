import { useRef, useEffect } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { DesignTokens } from '../core/design/tokens';
import type { Voucher } from '../core/types/api';
import { Haptics } from '../core/utils/haptics';

interface VoucherCardProps {
    voucher: Voucher;
    index: number;
    isExpanded: boolean;
    onPress: (voucher: Voucher) => void;
    brandColor: string;
}

type StatusCfg = {
    label: string;
    dotColor: string;
    textColor: string;
};

const SPACING = 8;

function getStatusCfg(status: string, tokens: DesignTokens, brandColor: string): StatusCfg {
    const s = (status || '').toLowerCase();
    if (s === 'active' || s === 'available') {
        return {
            label: 'Active',
            dotColor: brandColor || tokens.colors.primary,
            textColor: brandColor || tokens.colors.primary,
        };
    }
    if (s === 'used') {
        return {
            label: 'Redeemed',
            dotColor: tokens.colors.text.muted,
            textColor: tokens.colors.text.muted,
        };
    }
    if (s === 'pending' || s === 'pending_fulfillment') {
        return {
            label: 'Pending',
            dotColor: '#F59E0B',
            textColor: '#F59E0B',
        };
    }
    return {
        label: 'Expired',
        dotColor: tokens.colors.text.dim,
        textColor: tokens.colors.text.dim,
    };
}

export function VoucherCard({ voucher, index, isExpanded, onPress, brandColor }: VoucherCardProps) {
    const tokens = useDesignTokens();
    const anim = useRef(new Animated.Value(0)).current;

    const isUsed = voucher.status === 'used';
    const isActive = voucher.status === 'active' || voucher.status === 'available';
    const statusCfg = getStatusCfg(voucher.status, tokens, brandColor);
    const voucherId = voucher.externalId || voucher.id || '';

    useEffect(() => {
        if (isExpanded) {
            Animated.spring(anim, {
                toValue: 1,
                delay: index * 80,
                tension: 80,
                friction: 14,
                useNativeDriver: true,
            }).start();
        } else {
            anim.setValue(0);
        }
    }, [isExpanded, index]);

    return (
        <Animated.View
            style={[
                styles.wrapper,
                {
                    opacity: anim,
                    transform: [{
                        translateY: anim.interpolate({
                            inputRange: [0, 1],
                            outputRange: [16, 0],
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
                style={({ pressed }) => [
                    styles.card,
                    {
                        backgroundColor: tokens.colors.card,
                        borderColor: isActive
                            ? `${brandColor || tokens.colors.primary}35`
                            : tokens.colors.borderLight,
                        opacity: isUsed ? 0.55 : 1,
                        transform: pressed ? [{ scale: 0.985 }] : [],
                    },
                ]}
            >
                <View style={styles.inner}>
                    <View style={styles.topRow}>
                        <Text
                            allowFontScaling={false}
                            style={[
                                styles.amount,
                                {
                                    color: isUsed ? tokens.colors.text.muted : tokens.colors.text.primary,
                                    fontFamily: 'Rajdhani-Bold',
                                },
                            ]}
                        >
                            {voucher.amount}
                            <Text
                                allowFontScaling={false}
                                style={[
                                    styles.unit,
                                    { color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted },
                                ]}
                            >
                                {voucher.unit || 'L'}
                            </Text>
                        </Text>

                        <View style={styles.statusBadge}>
                            <View style={[styles.statusDot, { backgroundColor: statusCfg.dotColor }]} />
                            <Text
                                allowFontScaling={false}
                                style={[styles.statusLabel, { color: statusCfg.textColor }]}
                            >
                                {statusCfg.label}
                            </Text>
                        </View>
                    </View>

                    <Text
                        allowFontScaling={false}
                        style={[
                            styles.fuel,
                            {
                                color: isUsed ? tokens.colors.text.dim : tokens.colors.text.secondary,
                            },
                        ]}
                    >
                        {voucher.fuelName || voucher.fuelType}
                    </Text>

                    {voucherId ? (
                        <Text
                            allowFontScaling={false}
                            style={[
                                styles.idText,
                                {
                                    color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted,
                                },
                            ]}
                            numberOfLines={1}
                        >
                            {voucherId}
                        </Text>
                    ) : null}
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
        borderRadius: 16,
        borderWidth: 1,
        overflow: 'hidden',
    },
    inner: {
        padding: SPACING * 3,
        gap: SPACING * 1.5,
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
    statusBadge: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 5,
        minHeight: 44,
        paddingHorizontal: 4,
    },
    statusDot: {
        width: 6,
        height: 6,
        borderRadius: 3,
    },
    statusLabel: {
        fontSize: 11,
        fontFamily: 'Inter-Medium',
        letterSpacing: 0.3,
    },
    fuel: {
        fontSize: 14,
        letterSpacing: 0.3,
        fontFamily: 'Inter',
    },
    idText: {
        fontSize: 13,
        letterSpacing: 1.5,
        fontFamily: 'Inter',
        fontWeight: '500',
    },
});
