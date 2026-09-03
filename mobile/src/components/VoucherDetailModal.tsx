import { useEffect, useState } from 'react';
import { View, Text, Pressable, Modal, StyleSheet, Animated, Easing, Image } from 'react-native';
import { X, Copy, ShieldCheck, Ban, AlertTriangle } from 'lucide-react-native';
import { useDesignTokens } from '../core/hooks/useTheme';
import { useI18n } from '../core/i18n';
import { Haptics } from '../core/utils/haptics';
import { BlurView } from 'expo-blur';
import { MeshBackground } from '../core/ui';
import { VoucherBadge } from './VoucherBadge';
import { classifyVoucher } from '../core/types/api';
import type { Voucher } from '../core/types/api';
import { formatExpirationDate } from '../core/utils/formatters';
import * as Clipboard from 'expo-clipboard';

/**
 * Decorative sweep over the QR. It is a HUD flourish, not a status — it was
 * painted `#DC2626`, which in this system's vocabulary means "error", on the one
 * surface a cashier has to read. Phase 2 only takes the colour off the raw hex
 * and onto the theme accent; whether the sweep should exist at all belongs to the
 * Phase 3 pass over the redemption flow.
 */
const QrScannerOverlay = () => {
    const tokens = useDesignTokens();
    const [scanAnim] = useState(new Animated.Value(0));

    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(scanAnim, {
                    toValue: 200,
                    duration: 2000,
                    easing: Easing.linear,
                    useNativeDriver: true,
                }),
                Animated.timing(scanAnim, {
                    toValue: 0,
                    duration: 2000,
                    easing: Easing.linear,
                    useNativeDriver: true,
                }),
            ])
        ).start();
    }, []);

    return (
        <Animated.View
            style={[
                {
                    position: 'absolute',
                    top: 12,
                    left: 12,
                    right: 12,
                    height: 2,
                    backgroundColor: tokens.colors.primary,
                    zIndex: 10,
                    opacity: 0.8,
                },
                { transform: [{ translateY: scanAnim }] }
            ]}
        />
    );
};

interface VoucherDetailModalProps {
    visible: boolean;
    voucher: Voucher | null;
    user?: { id?: string } | null;
    onClose: () => void;
    onToggleUsed: (voucher: Voucher) => void;
    brandColor: string;
}

export function VoucherDetailModal({ visible, voucher, user, onClose, onToggleUsed, brandColor }: VoucherDetailModalProps) {
    const tokens = useDesignTokens();
    const { t } = useI18n();

    const handleCopy = async (text: string) => {
        await Clipboard.setStringAsync(text);
    };

    return (
        <Modal
            visible={visible}
            animationType="fade"
            transparent={true}
            onRequestClose={onClose}
        >
            <View style={[styles.modalBackdrop, { backgroundColor: tokens.colors.overlay }]}>
                {voucher && (() => {
                    const bColor = brandColor;
                    const isUsed = voucher.status === 'used';
                    const imageUrl = voucher.imageUrl || (voucher as any).image_url;
                    const kind = classifyVoucher(voucher, user?.id);
                    const isBlocked = kind === 'blocked';
                    const canUse = kind !== 'gifted_to_worker' && kind !== 'blocked';
                    const workerName = [voucher.workerFirstName, voucher.workerLastName].filter(Boolean).join(' ').trim();
                    return (
                        <View style={[styles.modalContent, { backgroundColor: tokens.colors.background, borderColor: tokens.colors.borderLight }]}>
                            <MeshBackground color={bColor} intensity={0.04} />
                            <View style={[styles.modalAccent, { backgroundColor: isUsed ? tokens.colors.text.dim : bColor }]} />

                            <View style={styles.modalBody}>
                                <View style={styles.modalTopRow}>
                                    <View style={styles.modalInfo}>
                                        <Text allowFontScaling={false} style={[styles.modalProvider, { color: tokens.colors.text.secondary }]}>
                                            {voucher.provider}
                                        </Text>
                                        <Text allowFontScaling={false} style={[styles.modalFuel, { color: tokens.colors.text.primary }]}>
                                            {voucher.fuelName || voucher.fuelType}
                                        </Text>
                                        <Text allowFontScaling={false} style={[styles.modalAmount, { color: isUsed ? tokens.colors.text.dim : bColor }]}>
                                             {voucher.amount}
                                             <Text style={[styles.modalUnit, { color: tokens.colors.text.muted }]}> {voucher.unit || 'L'}</Text>
                                         </Text>
                                         {voucher.expirationDate && (
                                             <View style={{ flexDirection: 'row', alignItems: 'center', gap: 5, marginTop: 4 }}>
                                                 <Text allowFontScaling={false} style={{ fontSize: 10, color: tokens.colors.text.dim, fontFamily: 'Inter', letterSpacing: 0.5 }}>
                                                      {t('codes.expires')}: {formatExpirationDate(voucher.expirationDate)}
                                                 </Text>
                                                 {(() => {
                                                     const days = Math.ceil((new Date(voucher.expirationDate).getTime() - Date.now()) / (86400000));
                                                     return days <= 30 && days >= 0 && !isUsed ? (
                                                         <AlertTriangle size={11} color={tokens.colors.error} />
                                                     ) : null;
                                                 })()}
                                             </View>
                                         )}
                                         <View style={{ marginTop: 8, flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
                                             <VoucherBadge kind={kind} />
                                             {kind === 'gifted_to_worker' && workerName ? (
                                                 <Text allowFontScaling={false} style={{ fontSize: 11, fontFamily: 'Inter-Medium', color: tokens.colors.text.dim }}>→ {workerName}</Text>
                                             ) : null}
                                         </View>
                                    </View>
                                    {/*
                                      "Redeemed" is a *neutral* status, not an error
                                      and not a brand state, so it uses the neutral
                                      status role rather than a raw translucent
                                      white that only existed on the dark themes.
                                    */}
                                    <View style={[styles.modalStatusPill, { backgroundColor: isBlocked ? `${tokens.colors.error}14` : isUsed ? tokens.colors.status.neutral.subtle : `${bColor}18` }]}>
                                        <View style={[styles.modalStatusDot, { backgroundColor: isBlocked ? tokens.colors.error : isUsed ? tokens.colors.text.dim : bColor }]} />
                                        <Text allowFontScaling={false} style={[styles.modalStatusText, { color: isBlocked ? tokens.colors.error : isUsed ? tokens.colors.text.dim : bColor }]}>
                                            {isBlocked ? t('voucher.badge.blocked') : isUsed ? 'REDEEMED' : 'READY'}
                                        </Text>
                                    </View>
                                </View>

                                {isBlocked ? (
                                    <View style={[styles.modalQrWrap, { borderColor: tokens.colors.borderLight, paddingVertical: 40, alignItems: 'center', gap: 12 }]}>
                                        <Ban size={40} color={tokens.colors.error} />
                                        <Text allowFontScaling={false} style={{ color: tokens.colors.text.muted, fontSize: 11, fontFamily: 'Inter-Bold', letterSpacing: 1, textTransform: 'uppercase', textAlign: 'center' }}>
                                            {t('voucher.error.blocked')}
                                        </Text>
                                    </View>
                                ) : (
                                <View style={[styles.modalQrWrap, { borderColor: tokens.colors.borderLight }]}>
                                    <View style={styles.modalQrBox}>
                                        {imageUrl ? (
                                            <Image
                                                source={{ uri: imageUrl }}
                                                style={{ width: 220, height: 220 }}
                                                resizeMode="contain"
                                            />
                                        ) : (
                                            <View style={{ width: 220, height: 220, backgroundColor: tokens.colors.card, alignItems: 'center', justifyContent: 'center' }}>
                                                <Text allowFontScaling={false} style={{ color: tokens.colors.text.muted, fontSize: 10, fontFamily: 'Inter-Bold', letterSpacing: 1, textTransform: 'uppercase' }}>{t('codes.qrUnavailable')}</Text>
                                            </View>
                                        )}
                                        <QrScannerOverlay />
                                    </View>
                                {isUsed && (
                                    <BlurView intensity={40} tint={tokens.colors.isDark ? "dark" : "light"} style={styles.modalQrOverlay} />
                                )}
                            </View>
                                )}

                            <View style={[styles.modalSep, { backgroundColor: tokens.colors.borderLight }]} />

                            {canUse ? (
                            <Pressable
                                onPress={() => {
                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
                                    onToggleUsed(voucher);
                                }}
                                style={[
                                    styles.modalActionBtn,
                                    {
                                        backgroundColor: isUsed ? tokens.colors.status.neutral.subtle : bColor,
                                        borderColor: isUsed ? tokens.colors.status.neutral.border : 'transparent',
                                        borderWidth: isUsed ? 1 : 0,
                                    },
                                ]}
                            >
                                {!isUsed && <ShieldCheck size={20} color={tokens.colors.text.onPrimary} />}
                                <Text
                                    allowFontScaling={false}
                                    style={[
                                        styles.modalActionText,
                                        { color: isUsed ? tokens.colors.text.primary : (tokens.colors.text.onPrimary) },
                                    ]}
                                >
                                    {isUsed ? t('codes.restoreCode') : t('codes.markAsUsed')}
                                </Text>
                            </Pressable>
                            ) : (
                            <View
                                style={[
                                    styles.modalActionBtn,
                                    { backgroundColor: tokens.colors.status.neutral.subtle, borderColor: tokens.colors.status.neutral.border, borderWidth: 1, paddingHorizontal: 16 },
                                ]}
                            >
                                <Text
                                    allowFontScaling={false}
                                    style={[
                                        styles.modalActionText,
                                        { color: tokens.colors.text.muted, fontSize: 11, letterSpacing: 0.5, textTransform: 'none', textAlign: 'center' },
                                    ]}
                                >
                                    {isBlocked ? t('voucher.error.blocked') : t('voucher.error.workerOnly')}
                                </Text>
                            </View>
                            )}

                            <Pressable
                                onPress={() => {
                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                    handleCopy(voucher.externalId || voucher.id);
                                }}
                                style={styles.modalIdRow}
                            >
                                <Copy size={12} color={tokens.colors.text.dim} />
                                <Text allowFontScaling={false} style={[styles.modalIdText, { color: tokens.colors.text.dim }]}>
                                    ID: {voucher.externalId}
                                </Text>
                            </Pressable>
                        </View>

                        <Pressable
                            onPress={onClose}
                            style={[styles.modalCloseBtn, { borderColor: tokens.colors.borderLight }]}
                        >
                            <X size={18} color={tokens.colors.text.primary} />
                        </Pressable>
                    </View>
                )
            })()}
            </View>
        </Modal>
    );
}

const styles = StyleSheet.create({
    modalBackdrop: {
        flex: 1,
        // Scrim colour is `tokens.colors.overlay`, applied at the call site. This
        // was the app's fifth and strongest black alpha (0.92); the QR's own white
        // quiet zone provides the scanner contrast, not the backdrop.
        alignItems: 'center',
        justifyContent: 'center',
        padding: 32,
    },
    modalContent: {
        width: '100%',
        maxWidth: 380,
        borderWidth: 1,
        borderRadius: 2,
        overflow: 'hidden',
        position: 'relative',
    },
    modalAccent: {
        position: 'absolute',
        left: 0,
        top: 0,
        bottom: 0,
        width: 8,
        zIndex: 2,
    },
    modalBody: {
        padding: 24,
        paddingLeft: 24 + 8 + 12,
        paddingRight: 12 + 32,
        gap: 20,
    },
    modalTopRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
    },
    modalInfo: {
        gap: 4,
    },
    modalProvider: {
        fontFamily: 'Inter-Black',
        fontSize: 11,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    modalFuel: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 26,
        letterSpacing: -0.5,
        textTransform: 'uppercase',
    },
    modalAmount: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 20,
        letterSpacing: -0.5,
    },
    modalUnit: {
        fontFamily: 'Rajdhani-SemiBold',
        fontSize: 14,
    },
    modalStatusPill: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 6,
        paddingHorizontal: 12,
        paddingVertical: 6,
        borderRadius: 2,
    },
    modalStatusDot: {
        width: 6,
        height: 6,
        borderRadius: 3,
    },
    modalStatusText: {
        fontFamily: 'Inter-Black',
        fontSize: 9,
        letterSpacing: 1,
    },
    modalQrWrap: {
        alignItems: 'center',
        justifyContent: 'center',
        padding: 4,
        borderWidth: 1,
        borderRadius: 2,
        position: 'relative',
    },
    modalQrBox: {
        padding: 8,
        // Deliberately a raw `#FFFFFF` and not a token: this is the QR quiet
        // zone. Scanners need a true-white margin around the symbol, so it must
        // not follow the theme. The only hardcoded colour in this file that is
        // intentional.
        backgroundColor: '#FFFFFF',
        borderRadius: 2,
        overflow: 'hidden',
        position: 'relative',
    },
    modalQrOverlay: {
        ...StyleSheet.absoluteFillObject,
        zIndex: 5,
    },
    modalSep: {
        height: 1,
        borderRadius: 1,
    },
    modalActionBtn: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 10,
        height: 56,
        borderRadius: 2,
    },
    modalActionText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        letterSpacing: 1.5,
        textTransform: 'uppercase',
    },
    modalIdRow: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 6,
        opacity: 0.5,
    },
    modalIdText: {
        fontFamily: 'Inter',
        fontSize: 11,
        fontWeight: '700',
        letterSpacing: 2,
        textTransform: 'uppercase',
        textAlign: 'center',
    },
    modalCloseBtn: {
        position: 'absolute',
        top: 12,
        right: 12,
        width: 32,
        height: 32,
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 2,
        zIndex: 10,
    },
});

