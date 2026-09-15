import { useRef, useEffect, useMemo } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { Check, AlertTriangle, Copy } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { DesignTokens } from '../core/design/tokens';
import type { Voucher } from '../core/types/api';
import { classifyVoucher } from '../core/types/api';
import { Haptics } from '../core/utils/haptics';
import { MeshBackground } from '../core/ui';
import { useI18n } from '../core/i18n';
import { formatExpirationDate } from '../core/utils/formatters';
import { VoucherBadge } from './VoucherBadge';

const LEGACY_ACCENT_WIDTH = 5;
const SOFT_ACCENT_WIDTH = 3;

interface VoucherCardProps {
    voucher: Voucher;
    index: number;
    isExpanded: boolean;
    onPress: (voucher: Voucher) => void;
    onLongPress?: (voucher: Voucher) => void;
    brandColor: string;
}

type StatusConfig = {
    labelKey: string;
    icon: 'dot' | 'check';
    dotColor: string;
    textColor: string;
    bg: string;
};

/**
 * Voucher lifecycle state → colour, resolved from the theme's status palette.
 *
 * Two things changed here in Phase 2. The raw literals (`#F59E0B`,
 * `rgba(245,158,11,0.12)`) are gone, and `used` no longer borrows the brand
 * colour: an active voucher and a spent one used to be painted the same hue at
 * slightly different container opacity, which is not a legible difference. Spent
 * is the `neutral` role — done, not wrong.
 *
 * `active` deliberately keeps the brand colour. It is the one state the user can
 * act on, so it is the one state allowed to look like the product.
 */
function getStatusConfig(status: string, tokens: DesignTokens, brandColor: string): StatusConfig {
    const s = (status || '').toLowerCase();
    if (s === 'active' || s === 'available' || s === 'assigned') {
        const accent = brandColor || tokens.colors.primary;
        return {
            labelKey: 'codes.active',
            icon: 'dot',
            dotColor: accent,
            textColor: accent,
            bg: `${accent}18`,
        };
    }
    if (s === 'used') {
        const role = tokens.colors.status.neutral;
        return {
            labelKey: 'codes.redeemed',
            icon: 'check',
            dotColor: role.base,
            textColor: role.base,
            bg: role.subtle,
        };
    }
    if (s === 'pending' || s === 'pending_fulfillment') {
        const role = tokens.colors.status.warning;
        return {
            labelKey: 'codes.pending',
            icon: 'dot',
            dotColor: role.base,
            textColor: role.base,
            bg: role.subtle,
        };
    }
    if (s === 'blocked') {
        const role = tokens.colors.status.danger;
        return {
            labelKey: 'voucher.badge.blocked',
            icon: 'dot',
            dotColor: role.base,
            textColor: role.base,
            bg: role.subtle,
        };
    }
    const role = tokens.colors.status.danger;
    return {
        labelKey: 'voucher.status.expired',
        icon: 'dot',
        dotColor: role.base,
        textColor: role.base,
        bg: role.subtle,
    };
}

export function VoucherCard({ voucher, index, isExpanded, onPress, onLongPress, brandColor }: VoucherCardProps) {
    const tokens = useDesignTokens();
    const { t } = useI18n();
    const accentWidth = tokens.surface.soft ? SOFT_ACCENT_WIDTH : LEGACY_ACCENT_WIDTH;
    const staggerAnim = useRef(new Animated.Value(0)).current;

    const isUsed = voucher.status === 'used';
    const isActive = voucher.status === 'active' || voucher.status === 'available' || voucher.status === 'assigned';
    const statusCfg = getStatusConfig(voucher.status, tokens, brandColor);
    const kind = classifyVoucher(voucher);

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
                            ? tokens.colors.surfaceSunken
                            : tokens.colors.surface,
                        borderColor: isActive
                            ? `${brandColor || tokens.colors.primary}35`
                            : tokens.colors.borderLight,
                        opacity: isUsed ? 0.5 : 1,
                        transform: pressed ? [{ scale: 0.97 }] : [],
                    },
                ]}
            >
                <MeshBackground color={brandColor} intensity={0.07} variant="honeycomb" />
                <View style={[styles.accent, { width: accentWidth, backgroundColor: isUsed ? tokens.colors.text.dim : (brandColor || tokens.colors.primary) }]} />

                <View style={[styles.content, { paddingLeft: 22 + accentWidth + 16 }]}>
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
                            <VoucherBadge kind={kind} />
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
                                {t(statusCfg.labelKey)}
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
                                {' '}{voucher.unit || t('common.liter')}
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
                                                ? tokens.colors.status.warning.base
                                                : tokens.colors.text.dim,
                                        },
                                    ]}
                                >
                                    {t('codes.expires')}: {formatExpirationDate(voucher.expirationDate)}
                                </Text>
                                {isExpiringSoon && !isUsed && (
                                    <AlertTriangle size={12} color={tokens.colors.status.warning.base} />
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
    },
    content: {
        padding: 22,
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
