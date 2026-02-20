/// <reference types="nativewind/types" />
import { useState, useEffect } from "react";
import { View, Text, Pressable, ActivityIndicator, StyleSheet } from "react-native";
import { useRouter } from "expo-router";
import { ChevronLeft, Lock, Zap, ShieldCheck } from "lucide-react-native";
import { useStore } from "../src/lib/store";
import { useI18n } from "../src/lib/i18n";
import { createPurchase, completePurchase } from "../src/lib/api";
import { PageLayout } from "../src/components/page-layout";
import { GridBackground } from "../src/components/grid-background";
import { tokens } from "../src/lib/design-tokens";
import { Haptics } from "../src/lib/haptics";

const GLOBAL_PADDING = tokens.spacing.containerPadding;

export default function PaymentPage() {
    const router = useRouter();
    const { cart, getDiscountedTotal, clearCart } = useStore();
    const { t } = useI18n();
    const [isProcessing, setIsProcessing] = useState(false);
    const [status, setStatus] = useState(t('payment.ready'));

    const total = getDiscountedTotal();

    useEffect(() => {
        if (cart.length === 0 && !isProcessing) {
            router.push("/");
        }
    }, [cart]);

    const handleMockPayment = async () => {
        setIsProcessing(true);
        setStatus(t('payment.establishTunnel'));
        try {
            const firstItem = cart[0];
            const purchaseRes = await createPurchase({
                packageId: firstItem.package.id,
                stationId: firstItem.station.id,
                stationName: firstItem.station.name,
                fuelType: firstItem.fuel.name,
                fuelName: firstItem.fuel.name,
                liters: firstItem.package.liters * firstItem.quantity,
                price: firstItem.package.price * firstItem.quantity
            });

            await new Promise(resolve => setTimeout(resolve, 1500));
            setStatus(t('payment.verifyHandshake'));

            await new Promise(resolve => setTimeout(resolve, 1500));
            setStatus(t('payment.finalize'));

            await completePurchase(purchaseRes.id);

            await new Promise(resolve => setTimeout(resolve, 1000));
            clearCart();
            router.push("/my-codes");
        } catch (e) {
            console.error("Payment error", e);
            setStatus(t('payment.encryptionFailed'));
            setIsProcessing(false);
        }
    };

    const Header = (
        <View style={styles.header}>
            <View style={styles.headerRow}>
                <Pressable
                    onPress={() => router.back()}
                    style={styles.backButton}
                >
                    <ChevronLeft size={20} color="#FFF" />
                </Pressable>

                <View style={styles.headerCenter}>
                    <Text allowFontScaling={false} style={styles.headerTitle}>{t('payment.title')}</Text>
                    <Text allowFontScaling={false} style={styles.headerSubtitle}>{t('payment.secureGateway')} [ TUNNELING ]</Text>
                </View>

                <View style={{ width: 44 }} />
            </View>
        </View>
    );

    return (
        <PageLayout header={Header} background={<GridBackground />} paddingHorizontal={GLOBAL_PADDING}>
            <View style={styles.card}>
                <Text allowFontScaling={false} style={styles.cardLabel}>{t('payment.payload')}</Text>
                <View style={styles.itemsList}>
                    {cart.map((item) => (
                        <View key={item.id} style={styles.itemRow}>
                            <Text allowFontScaling={false} style={styles.itemName}>
                                {item.station.logoText || item.station.name} x {item.quantity}
                            </Text>
                            <Text allowFontScaling={false} style={styles.itemPrice}>
                                {item.package.price * item.quantity} ₴
                            </Text>
                        </View>
                    ))}
                </View>
                <View style={styles.totalRow}>
                    <Text allowFontScaling={false} style={styles.totalLabel}>{t('payment.totalAmount')}</Text>
                    <Text allowFontScaling={false} style={styles.totalValue}>{total.toFixed(2)} ₴</Text>
                </View>
            </View>

            <View style={styles.actions}>
                {isProcessing ? (
                    <View style={styles.processingWrapper}>
                        <View style={styles.loaderBox}>
                            <ActivityIndicator size="large" color={tokens.colors.primary} />
                        </View>
                        <Text allowFontScaling={false} style={styles.statusText}>{status}</Text>
                    </View>
                ) : (
                    <View style={styles.readyWrapper}>
                        <View style={styles.portInfo}>
                            <Lock size={14} color={tokens.colors.primary} />
                            <Text allowFontScaling={false} style={styles.portLabel}>{t('payment.securePort')}</Text>
                        </View>

                        <Pressable
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
                                handleMockPayment();
                            }}
                            style={({ pressed }) => [
                                styles.payBtn,
                                pressed && { transform: [{ scale: 0.98 }], opacity: 0.9 }
                            ]}
                        >
                            <Zap size={24} color="#000" />
                            <Text allowFontScaling={false} style={styles.payBtnText}>
                                {t('payment.complete')}
                            </Text>
                        </Pressable>

                        <View style={styles.agreementBox}>
                            <ShieldCheck size={16} color={tokens.colors.primary} />
                            <Text allowFontScaling={false} style={styles.agreementText}>
                                {t('payment.agreement')}
                            </Text>
                        </View>
                    </View>
                )}
            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    header: {
        paddingTop: 8,
        paddingBottom: 24,
    },
    headerRow: {
        flexDirection: 'row',
        alignItems: 'center',
    },
    headerCenter: {
        flex: 1,
        alignItems: 'center',
    },
    backButton: {
        width: 44,
        height: 44,
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
        backgroundColor: 'rgba(0,0,0,0.8)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    headerTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 32,
        letterSpacing: -1,
        textTransform: 'uppercase',
    },
    headerSubtitle: {
        fontFamily: 'Inter-Black',
        color: tokens.colors.primary,
        fontSize: 8,
        letterSpacing: 4,
        textTransform: 'uppercase',
        marginTop: 2,
        opacity: 0.6,
    },
    card: {
        backgroundColor: '#000',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.08)',
        padding: 24,
        borderRadius: 2,
        marginBottom: 32,
    },
    cardLabel: {
        color: 'rgba(255,255,255,0.3)',
        fontSize: 9,
        fontFamily: 'Inter-Black',
        letterSpacing: 2,
        marginBottom: 16,
    },
    itemsList: {
        gap: 8,
        marginBottom: 24,
    },
    itemRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
    },
    itemName: {
        color: 'rgba(255,255,255,0.6)',
        fontSize: 12,
        fontFamily: 'Inter-Bold',
        textTransform: 'uppercase',
    },
    itemPrice: {
        color: '#FFF',
        fontSize: 14,
        fontFamily: 'Inter-Black',
    },
    totalRow: {
        borderTopWidth: 1,
        borderTopColor: 'rgba(255,255,255,0.1)',
        paddingTop: 16,
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-end',
    },
    totalLabel: {
        color: '#FFF',
        fontSize: 20,
        fontFamily: tokens.typography.fonts.heading,
        textTransform: 'uppercase',
    },
    totalValue: {
        color: tokens.colors.primary,
        fontSize: 36,
        fontFamily: tokens.typography.fonts.heading,
    },
    actions: {
        alignItems: 'center',
        paddingVertical: 20,
    },
    processingWrapper: {
        alignItems: 'center',
    },
    loaderBox: {
        width: 100,
        height: 100,
        alignItems: 'center',
        justifyContent: 'center',
        marginBottom: 20,
    },
    statusText: {
        color: tokens.colors.primary,
        fontFamily: 'Inter-Black',
        fontSize: 10,
        letterSpacing: 2,
        textAlign: 'center',
    },
    readyWrapper: {
        width: '100%',
        gap: 24,
    },
    portInfo: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
        justifyContent: 'center',
    },
    portLabel: {
        fontSize: 9,
        color: 'rgba(255,255,255,0.4)',
        fontFamily: 'Inter',
        letterSpacing: 3,
        textTransform: 'uppercase',
    },
    payBtn: {
        width: '100%',
        backgroundColor: tokens.colors.primary,
        height: 64,
        borderRadius: 4,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 0.8,
        shadowRadius: 15,
        elevation: 10,
    },
    payBtnText: {
        color: '#000',
        fontSize: 14,
        fontFamily: 'Inter-Black',
        textTransform: 'uppercase',
        letterSpacing: 2,
    },
    agreementBox: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
        padding: 16,
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.05)',
        backgroundColor: 'rgba(255,255,255,0.03)',
        borderRadius: 2,
    },
    agreementText: {
        flex: 1,
        fontSize: 8,
        color: 'rgba(255,255,255,0.2)',
        fontFamily: 'Inter',
        letterSpacing: 1,
        lineHeight: 14,
        textTransform: 'uppercase',
    }
});
