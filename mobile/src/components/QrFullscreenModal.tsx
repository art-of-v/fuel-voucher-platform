import { useEffect, useRef, useState } from 'react';
import { View, Text, Pressable, Animated, Dimensions, StyleSheet, Image } from 'react-native';
import { X } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import type { Voucher } from '../core/types/api';
import QRCode from 'qrcode';
import Svg, { Rect } from 'react-native-svg';

const { width: SCREEN_WIDTH } = Dimensions.get('window');
const QR_SIZE = SCREEN_WIDTH * 0.78;

interface QrFullscreenModalProps {
    voucher: Voucher | null;
    visible: boolean;
    onClose: () => void;
}

const QrImage = ({ value, size, color = '#000' }: { value: string; size: number; color?: string }) => {
    const qr = QRCode.create(value, { errorCorrectionLevel: 'L' });
    const cells = qr.modules.data;
    const count = qr.modules.size;
    const tileW = size / count;

    const rows: number[][] = [];
    for (let i = 0; i < count; i++) {
        const row: number[] = [];
        for (let j = 0; j < count; j++) {
            row.push(cells[i * count + j]);
        }
        rows.push(row);
    }

    return (
        <Svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
            {rows.map((row, rowIndex) =>
                row.map((cell, colIndex) =>
                    cell ? (
                        <Rect
                            key={`${rowIndex}-${colIndex}`}
                            x={colIndex * tileW}
                            y={rowIndex * tileW}
                            width={tileW + 0.1}
                            height={tileW + 0.1}
                            fill={color}
                        />
                    ) : null
                )
            )}
        </Svg>
    );
};

export function QrFullscreenModal({ voucher, visible, onClose }: QrFullscreenModalProps) {
    const tokens = useDesignTokens();
    const bgOpacity = useRef(new Animated.Value(0)).current;
    const qrScale = useRef(new Animated.Value(0.85)).current;
    const contentSlide = useRef(new Animated.Value(20)).current;

    useEffect(() => {
        if (visible) {
            Animated.parallel([
                Animated.timing(bgOpacity, {
                    toValue: 1,
                    duration: 250,
                    useNativeDriver: true,
                }),
                Animated.spring(qrScale, {
                    toValue: 1,
                    tension: 80,
                    friction: 12,
                    useNativeDriver: true,
                }),
                Animated.timing(contentSlide, {
                    toValue: 0,
                    duration: 300,
                    delay: 80,
                    useNativeDriver: true,
                }),
            ]).start();
        } else {
            bgOpacity.setValue(0);
            qrScale.setValue(0.85);
            contentSlide.setValue(20);
        }
    }, [visible]);

    if (!voucher) return null;

    const qrData = voucher.qrCodeData || (voucher as any).qr_code_data || voucher.externalId || 'EMPTY';
    const isWog = voucher.provider?.toLowerCase().includes('wog');
    const isUsed = voucher.status === 'used';
    const imageUrl = voucher.imageUrl || (voucher as any).image_url;

    return (
        <Pressable style={styles.backdrop} onPress={onClose}>
            <Animated.View
                style={[StyleSheet.absoluteFill, { backgroundColor: '#000', opacity: bgOpacity }]}
            />

            <Animated.View
                style={[
                    styles.content,
                    { transform: [{ translateY: contentSlide }] },
                ]}
            >
                <Pressable style={styles.closeBtn} onPress={onClose}>
                    <View style={[styles.closeCircle, { borderColor: 'rgba(255,255,255,0.15)' }]}>
                        <X size={18} color="rgba(255,255,255,0.6)" />
                    </View>
                </Pressable>

                {/* Voucher info */}
                <View style={styles.infoSection}>
                    <Text
                        allowFontScaling={false}
                        style={[styles.amountText, { color: tokens.colors.text.primary, fontFamily: 'Rajdhani-Bold' }]}
                    >
                        {voucher.amount}
                        <Text
                            allowFontScaling={false}
                            style={[styles.unitText, { color: tokens.colors.text.muted }]}
                        >
                            {voucher.unit || 'L'}
                        </Text>
                    </Text>
                    <Text
                        allowFontScaling={false}
                        style={[styles.fuelText, { color: tokens.colors.text.muted, fontFamily: 'Inter' }]}
                    >
                        {voucher.fuelName || voucher.fuelType} Voucher
                    </Text>
                </View>

                {/* QR Code */}
                <Animated.View style={[styles.qrSection, { transform: [{ scale: qrScale }] }]}>
                    <View style={styles.qrBox}>
                        {imageUrl ? (
                            <Image
                                source={{ uri: imageUrl }}
                                style={{ width: QR_SIZE, height: QR_SIZE }}
                                resizeMode="contain"
                            />
                        ) : (
                            <QrImage
                                value={isWog ? qrData.trim() : qrData}
                                size={QR_SIZE}
                                color={isUsed ? '#555' : '#000'}
                            />
                        )}
                    </View>
                    {isUsed && (
                        <View style={[styles.usedOverlay, { backgroundColor: 'rgba(0,0,0,0.7)' }]} />
                    )}
                </Animated.View>

                {/* Voucher number */}
                <Text
                    allowFontScaling={false}
                    style={[styles.idText, { color: tokens.colors.text.dim, fontFamily: 'Inter' }]}
                    selectable
                >
                    {voucher.externalId || voucher.id}
                </Text>

                {isUsed && (
                    <View style={[styles.usedChip, { borderColor: 'rgba(255,255,255,0.1)' }]}>
                        <Text
                            allowFontScaling={false}
                            style={[styles.usedChipText, { color: tokens.colors.text.dim, fontFamily: 'Inter-Bold' }]}
                        >
                            REDEEMED
                        </Text>
                    </View>
                )}

                <Text
                    allowFontScaling={false}
                    style={[styles.tapHint, { color: tokens.colors.text.dim }]}
                >
                    Tap anywhere to close
                </Text>
            </Animated.View>
        </Pressable>
    );
}

const styles = StyleSheet.create({
    backdrop: {
        ...StyleSheet.absoluteFillObject,
        justifyContent: 'center',
        alignItems: 'center',
        zIndex: 100,
    },
    content: {
        width: '100%',
        height: '100%',
        alignItems: 'center',
        justifyContent: 'center',
        paddingHorizontal: 24,
        paddingVertical: 60,
    },
    closeBtn: {
        position: 'absolute',
        top: 60,
        right: 24,
        zIndex: 10,
    },
    closeCircle: {
        width: 36,
        height: 36,
        borderRadius: 18,
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
    },
    infoSection: {
        alignItems: 'center',
        gap: 4,
        marginBottom: 32,
    },
    amountText: {
        fontSize: 42,
        letterSpacing: -2,
        lineHeight: 44,
    },
    unitText: {
        fontSize: 20,
        fontFamily: 'Rajdhani-SemiBold',
        letterSpacing: 0,
    },
    fuelText: {
        fontSize: 15,
        letterSpacing: 0.5,
        opacity: 0.8,
    },
    qrSection: {
        alignItems: 'center',
        justifyContent: 'center',
        position: 'relative',
    },
    qrBox: {
        padding: 4,
        backgroundColor: '#FFFFFF',
        borderRadius: 16,
        overflow: 'hidden',
    },
    usedOverlay: {
        ...StyleSheet.absoluteFillObject,
        borderRadius: 16,
    },
    idText: {
        fontSize: 14,
        letterSpacing: 2,
        fontWeight: '500',
        marginTop: 24,
        textAlign: 'center',
    },
    usedChip: {
        marginTop: 16,
        borderWidth: 1,
        borderRadius: 20,
        paddingHorizontal: 14,
        paddingVertical: 6,
    },
    usedChipText: {
        fontSize: 9,
        letterSpacing: 2,
    },
    tapHint: {
        position: 'absolute',
        bottom: 40,
        fontSize: 11,
        letterSpacing: 1,
        opacity: 0.3,
    },
});
