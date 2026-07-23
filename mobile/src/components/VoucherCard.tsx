import { useRef, useEffect, useState } from 'react';
import { View, Text, Pressable, Animated, Image, StyleSheet } from 'react-native';
import { QrCode, ChevronRight } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { DesignTokens } from '../core/design/tokens';
import type { Voucher } from '../core/types/api';
import { Haptics } from '../core/utils/haptics';
import QRCode from 'qrcode';
import Svg, { Rect, Defs, Pattern, Path, LinearGradient, RadialGradient, Stop } from 'react-native-svg';

interface VoucherCardProps {
    voucher: Voucher;
    index: number;
    isExpanded: boolean;
    onVoucherPress: (voucher: Voucher) => void;
    brandColor: string;
}

const VOUCHER_QR_SYNC = ({ value, size = 120 }: { value: string; size?: number }) => {
    const qr = QRCode.create(value, { errorCorrectionLevel: 'L' });
    const cells = qr.modules.data;
    const count = qr.modules.size;
    const tileW = size / count;

    const rows = [];
    for (let i = 0; i < count; i++) {
        const row = [];
        for (let j = 0; j < count; j++) {
            row.push(cells[i * count + j]);
        }
        rows.push(row);
    }

    return (
        <Svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
            {rows.map((row: number[], rowIndex: number) =>
                row.map((cell: number, colIndex: number) =>
                    cell ? (
                        <Rect
                            key={`${rowIndex}-${colIndex}`}
                            x={colIndex * tileW}
                            y={rowIndex * tileW}
                            width={tileW + 0.1}
                            height={tileW + 0.1}
                            fill="#000"
                        />
                    ) : null
                )
            )}
        </Svg>
    );
};

function getStatusConfig(status: string, tokens: DesignTokens, brandColor: string) {
    const s = (status || '').toLowerCase();
    if (s === 'active' || s === 'available') {
        return {
            label: 'READY',
            dot: brandColor || tokens.colors.primary,
            text: brandColor || tokens.colors.primary,
            bg: `${brandColor || tokens.colors.primary}18`,
            border: `${brandColor || tokens.colors.primary}40`,
        };
    }
    if (s === 'used') {
        return {
            label: 'USED',
            dot: tokens.colors.text.dim,
            text: tokens.colors.text.dim,
            bg: 'rgba(255,255,255,0.04)',
            border: 'rgba(255,255,255,0.08)',
        };
    }
    if (s === 'pending' || s === 'pending_fulfillment' || s === 'PENDING_FULFILLMENT') {
        return {
            label: 'PENDING',
            dot: '#F59E0B',
            text: '#F59E0B',
            bg: 'rgba(245,158,11,0.15)',
            border: 'rgba(245,158,11,0.3)',
        };
    }
    return {
        label: 'EXPIRED',
        dot: tokens.colors.error,
        text: tokens.colors.error,
        bg: `${tokens.colors.error}18`,
        border: `${tokens.colors.error}40`,
    };
}

const FlipCardBack = ({ qrData, externalId }: { qrData?: string; externalId?: string }) => {
    const data = qrData || externalId || 'EMPTY';
    return (
        <View style={styles.flipBack}>
            <View style={styles.flipQrBox}>
                <VOUCHER_QR_SYNC value={data} size={100} />
            </View>
            <Text style={styles.flipBackHint}>TAP TO FLIP</Text>
        </View>
    );
};

export function VoucherCard({ voucher, index, isExpanded, onVoucherPress, brandColor }: VoucherCardProps) {
    const tokens = useDesignTokens();
    const staggerAnim = useRef(new Animated.Value(0)).current;
    const scaleAnim = useRef(new Animated.Value(1)).current;
    const glowPulse = useRef(new Animated.Value(0)).current;
    const flipAnim = useRef(new Animated.Value(0)).current;
    const [isFlipped, setIsFlipped] = useState(false);

    const isUsed = voucher.status === 'used';
    const isActive = voucher.status === 'active' || voucher.status === 'available';
    const statusCfg = getStatusConfig(voucher.status, tokens, brandColor);
    const dispColor = isUsed ? tokens.colors.text.dim : (brandColor || tokens.colors.primary);

    useEffect(() => {
        if (isExpanded) {
            Animated.spring(staggerAnim, {
                toValue: 1,
                delay: index * 80,
                tension: 70,
                friction: 10,
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
                    Animated.timing(glowPulse, { toValue: 1, duration: 1800, useNativeDriver: false }),
                    Animated.timing(glowPulse, { toValue: 0, duration: 1800, useNativeDriver: false }),
                ])
            ).start();
        }
    }, [isActive]);

    const handlePress = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
        if (isFlipped) {
            Animated.spring(flipAnim, {
                toValue: 0,
                tension: 60,
                friction: 12,
                useNativeDriver: true,
            }).start(() => setIsFlipped(false));
        } else {
            setIsFlipped(true);
            Animated.spring(flipAnim, {
                toValue: 1,
                tension: 60,
                friction: 12,
                useNativeDriver: true,
            }).start();
        }
    };

    const handleLongPress = () => {
        onVoucherPress(voucher);
    };

    const handlePressIn = () => {
        Animated.spring(scaleAnim, {
            toValue: 0.93,
            tension: 150,
            friction: 10,
            useNativeDriver: true,
        }).start();
    };

    const handlePressOut = () => {
        Animated.spring(scaleAnim, {
            toValue: 1,
            tension: 150,
            friction: 10,
            useNativeDriver: true,
        }).start();
    };

    const frontInterp = flipAnim.interpolate({
        inputRange: [0, 1],
        outputRange: ['0deg', '180deg'],
    });
    const backInterp = flipAnim.interpolate({
        inputRange: [0, 1],
        outputRange: ['180deg', '360deg'],
    });
    const frontOpacity = flipAnim.interpolate({
        inputRange: [0, 0.5, 0.5, 1],
        outputRange: [1, 1, 0, 0],
    });
    const backOpacity = flipAnim.interpolate({
        inputRange: [0, 0.5, 0.5, 1],
        outputRange: [0, 0, 1, 1],
    });

    const borderGlow = glowPulse.interpolate({
        inputRange: [0, 1],
        outputRange: [
            `${brandColor || tokens.colors.primary}40`,
            `${brandColor || tokens.colors.primary}90`,
        ],
    });

    const voucherId = voucher.externalId || voucher.id || '';
    const displayId = voucherId.length > 8
        ? `${voucherId.slice(0, 4)} ${voucherId.slice(4, 8)} ${voucherId.slice(8, 12)}`
        : voucherId;

    return (
        <Animated.View
            style={[
                styles.wrapper,
                {
                    opacity: staggerAnim,
                    transform: [
                        { translateY: staggerAnim.interpolate({ inputRange: [0, 1], outputRange: [24, 0] }) },
                        { scale: scaleAnim },
                    ],
                },
            ]}
        >
            <Pressable
                onPress={handlePress}
                onLongPress={handleLongPress}
                onPressIn={handlePressIn}
                onPressOut={handlePressOut}
                style={({ pressed }) => [
                    styles.card,
                    {
                        backgroundColor: tokens.colors.card,
                        borderColor: isActive
                            ? brandColor || tokens.colors.primary
                            : isUsed
                                ? tokens.colors.borderLight
                                : tokens.colors.borderLight,
                        opacity: isUsed ? 0.55 : 1,
                    },
                ]}
            >
                {isActive && (
                    <Animated.View
                        style={[
                            styles.cardGlow,
                            {
                                borderColor: borderGlow,
                            },
                        ]}
                        pointerEvents="none"
                    />
                )}

                {/* FRONT */}
                <Animated.View
                    style={[
                        styles.face,
                        { opacity: frontOpacity, transform: [{ perspective: 800 }, { rotateY: frontInterp }] },
                    ]}
                >
                    <View style={styles.cardContent}>
                        {/* Status chip */}
                        {!isActive && (
                            <View style={[styles.statusChip, { backgroundColor: statusCfg.bg, borderColor: statusCfg.border }]}>
                                <View style={[styles.dot, { backgroundColor: statusCfg.dot }]} />
                                <Text allowFontScaling={false} style={[styles.statusLabel, { color: statusCfg.text }]}>
                                    {statusCfg.label}
                                </Text>
                            </View>
                        )}

                        {/* Amount */}
                        <View style={{ flex: 1, justifyContent: 'center' }}>
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

                            <Text
                                allowFontScaling={false}
                                style={[
                                    styles.fuel,
                                    {
                                        color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted,
                                        fontFamily: 'Inter-Bold',
                                    },
                                ]}
                                numberOfLines={1}
                            >
                                {voucher.fuelName || voucher.fuelType}
                            </Text>

                            {displayId ? (
                                <Text
                                    allowFontScaling={false}
                                    style={[styles.id, { color: tokens.colors.text.dim, fontFamily: 'Inter' }]}
                                    numberOfLines={1}
                                >
                                    {displayId}
                                </Text>
                            ) : null}
                        </View>

                        {/* Bottom row: QR icon + flip hint */}
                        <View style={styles.bottomRow}>
                            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 2 }}>
                                <QrCode size={10} color={isUsed ? tokens.colors.text.dim : dispColor} />
                                <Text
                                    allowFontScaling={false}
                                    style={[styles.flipHint, { color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted }]}
                                >
                                    FLIP
                                </Text>
                            </View>
                        </View>
                    </View>
                </Animated.View>

                {/* BACK */}
                <Animated.View
                    style={[
                        styles.face,
                        styles.faceBack,
                        { opacity: backOpacity, transform: [{ perspective: 800 }, { rotateY: backInterp }] },
                    ]}
                >
                    <FlipCardBack qrData={voucher.qrCodeData || voucher.qrCodeUrl} externalId={voucher.externalId} />
                </Animated.View>
            </Pressable>
        </Animated.View>
    );
}

const styles = StyleSheet.create({
    wrapper: {
        width: '48%',
    },
    card: {
        borderRadius: 14,
        borderWidth: 1,
        overflow: 'hidden',
        minHeight: 148,
        position: 'relative',
    },
    cardGlow: {
        ...StyleSheet.absoluteFillObject,
        borderRadius: 14,
        borderWidth: 1.5,
        opacity: 0.6,
    },
    face: {
        backfaceVisibility: 'hidden',
    },
    faceBack: {
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
    },
    cardContent: {
        padding: 12,
        gap: 8,
        minHeight: 148,
    },
    statusChip: {
        flexDirection: 'row',
        alignItems: 'center',
        alignSelf: 'flex-start',
        paddingHorizontal: 6,
        paddingVertical: 3,
        borderRadius: 4,
        borderWidth: 1,
        gap: 4,
    },
    dot: {
        width: 5,
        height: 5,
        borderRadius: 2.5,
    },
    statusLabel: {
        fontSize: 7,
        fontFamily: 'Inter-Black',
        letterSpacing: 1,
    },
    amount: {
        fontSize: 26,
        letterSpacing: -1,
        lineHeight: 28,
    },
    unit: {
        fontSize: 14,
        fontFamily: 'Rajdhani-SemiBold',
    },
    fuel: {
        fontSize: 9,
        letterSpacing: 1,
        textTransform: 'uppercase',
        marginTop: 2,
    },
    id: {
        fontSize: 8,
        letterSpacing: 1.5,
        marginTop: 4,
    },
    bottomRow: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'flex-end',
    },
    flipHint: {
        fontSize: 7,
        fontFamily: 'Inter-Black',
        letterSpacing: 1,
    },
    flipBack: {
        padding: 12,
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: 148,
        gap: 8,
    },
    flipQrBox: {
        padding: 8,
        backgroundColor: '#FFFFFF',
        borderRadius: 8,
        overflow: 'hidden',
    },
    flipBackHint: {
        fontSize: 7,
        fontFamily: 'Inter-Black',
        letterSpacing: 1,
        color: 'rgba(255,255,255,0.3)',
    },
});
