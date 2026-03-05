/// <reference types="nativewind/types" />
import { useState } from "react";
import { View, Text, Pressable, ActivityIndicator, StyleSheet, Modal } from "react-native";
import * as LocalAuthentication from 'expo-local-authentication';
import { useRouter } from "expo-router";
import { ChevronLeft, CreditCard, ShieldCheck, AlertTriangle, Apple, Smartphone, Cat } from "lucide-react-native";
import { useStore } from "../src/lib/store";
import { useI18n } from "../src/lib/i18n";
import { createMonobankInvoice } from "../src/lib/api";
import { PageLayout } from "../src/components/page-layout";
import { useDesignTokens } from "../src/lib/design-tokens";
import { Haptics } from "../src/lib/haptics";
import * as Linking from 'expo-linking';

export default function CheckoutScreen() {
    const router = useRouter();
    const tokens = useDesignTokens();
    const { t } = useI18n();
    const {
        cart,
        getDiscountedTotal,
        isAuthenticated,
        clearCart
    } = useStore();
    const [isProcessing, setIsProcessing] = useState(false);
    const [paymentMethod, setPaymentMethod] = useState("monobank");
    const [showEmulation, setShowEmulation] = useState(false);
    const [emulationStatus, setEmulationStatus] = useState<'idle' | 'authenticating' | 'success'>('idle');

    const GLOBAL_PADDING = tokens.spacing.containerPadding;
    const discountedTotal = getDiscountedTotal();

    const handlePaymentEnd = async () => {
        try {
            setIsProcessing(true);

            if (cart.length === 0) return;

            // For now, we only support paying for the first item in the cart if there are multiple,
            // or we could iterate, but Monobank works best with one invoice at a time.
            const item = cart[0];

            const response = await createMonobankInvoice({
                packageId: item.package.id,
                stationId: item.station.id,
                stationName: item.station.name,
                fuelType: item.fuel.name,
                fuelName: item.fuel.name,
                liters: item.package.liters,
                quantity: item.quantity,
                price: item.package.price * item.quantity
            });

            if (response.pageUrl) {
                // Open Monobank payment page
                await Linking.openURL(response.pageUrl);

                // Clear cart and move to my-codes (status will update via webhook)
                clearCart();
                router.push("/my-codes");
            } else {
                throw new Error("No payment URL received");
            }
        } catch (e) {
            console.error("Payment error details:", e);
            alert(e instanceof Error ? e.message : "Payment initialization failed");
            setIsProcessing(false);
            setEmulationStatus('idle');
            setShowEmulation(false);
        }
    };

    const handleEmulationAuth = async () => {
        setEmulationStatus('authenticating');
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);

        // DEV MOCK: Always pass verification quickly
        setTimeout(() => {
            setEmulationStatus('success');
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
            setTimeout(() => {
                setShowEmulation(false);
                handlePaymentEnd();
            }, 600); // Quick delay to see the success state before navigating
        }, 500); // Quick fake authentication delay
    };

    const Header = (
        <View style={styles.headerContainer}>
            <View style={styles.headerTopRow}>
                <Pressable
                    onPress={() => router.back()}
                    style={[styles.backButton, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}
                >
                    <ChevronLeft size={20} color={tokens.colors.text.primary} />
                </Pressable>

                <View style={styles.headerCenter}>
                    <Text allowFontScaling={false} style={[styles.headerTitle, { color: tokens.colors.text.primary }]}>{t('basket.checkoutTitle')}</Text>
                    <Text allowFontScaling={false} style={[styles.headerSubtitle, { color: tokens.colors.primary }]}>{t('checkout.verifyOrder')}</Text>
                </View>

                <View style={{ width: 44 }} />
            </View>
        </View>
    );

    const fixedFooter = cart.length > 0 ? (
        <View style={styles.footerRegion}>
            <Pressable
                onPress={() => {
                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
                    handlePaymentEnd();
                }}
                disabled={isProcessing}
                style={[
                    styles.primaryBtn,
                    { backgroundColor: tokens.colors.primary },
                    isProcessing && { opacity: 0.5 }
                ]}
            >
                {isProcessing ? (
                    <ActivityIndicator color={tokens.colors.isDark ? '#000' : '#FFF'} />
                ) : (
                    <Text allowFontScaling={false} style={[styles.primaryBtnText, { color: tokens.colors.isDark ? '#000' : '#FFF' }]}>
                        {t('packages.payTitle')} {discountedTotal.toFixed(2)} ₴
                    </Text>
                )}
            </Pressable>
        </View>
    ) : null;

    if (!isAuthenticated) {
        return (
            <PageLayout>
                <View style={[styles.emptyContainer, { backgroundColor: tokens.colors.background }]}>
                    <View style={[styles.deniedIconBox, { borderColor: `${tokens.colors.error}44` }]}>
                        <View style={[styles.deniedIconInner, { backgroundColor: `${tokens.colors.error}11` }]}>
                            <AlertTriangle size={40} color={tokens.colors.error} />
                        </View>
                    </View>
                    <Text allowFontScaling={false} style={[styles.deniedTitle, { color: tokens.colors.text.primary }]}>{t('checkout.accessDenied')}</Text>
                    <Text allowFontScaling={false} style={[styles.deniedSubtext, { color: tokens.colors.text.dim }]}>{t('checkout.loginRequired')}</Text>
                    <Pressable
                        onPress={() => router.push("/profile")}
                        style={[styles.loginBtn, { backgroundColor: tokens.colors.primary }]}
                    >
                        <Text allowFontScaling={false} style={[styles.loginBtnText, { color: tokens.colors.isDark ? "#000" : "#FFF" }]}>{t('checkout.gotoLogin')}</Text>
                    </Pressable>
                </View>
            </PageLayout>
        );
    }

    return (
        <PageLayout
            header={Header}
            fixedFooter={fixedFooter}
        >
            <View style={{ gap: 24, paddingHorizontal: GLOBAL_PADDING }}>
                {/* Order Summary */}
                <View>
                    <Text allowFontScaling={false} style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>{t('checkout.orderSummary')}</Text>
                    <View style={[styles.summaryCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                        {cart.map((item) => (
                            <View key={item.id} style={styles.summaryRow}>
                                <View>
                                    <Text allowFontScaling={false} style={[styles.summaryItemTitle, { color: tokens.colors.text.primary }]}>{item.station.logoText || item.station.name}</Text>
                                    <Text allowFontScaling={false} style={[styles.summaryItemSubtitle, { color: tokens.colors.primary }]}>{item.fuel.name} x {item.quantity}</Text>
                                </View>
                                <Text allowFontScaling={false} style={[styles.summaryItemPrice, { color: tokens.colors.text.primary }]}>{item.package.price * item.quantity} ₴</Text>
                            </View>
                        ))}
                    </View>
                </View>

            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    headerContainer: {
        paddingTop: 8,
        paddingBottom: 24,
    },
    headerTopRow: {
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
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    headerTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 32,
        lineHeight: 38,
        letterSpacing: -1,
        textTransform: 'uppercase',
    },
    headerSubtitle: {
        fontFamily: 'Inter-Black',
        fontSize: 8,
        letterSpacing: 4,
        textTransform: 'uppercase',
        marginTop: 2,
        opacity: 0.6,
    },
    footerRegion: {
        paddingBottom: 72,
    },
    primaryBtn: {
        width: '100%',
        height: 64,
        borderRadius: 4,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 12,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 0.8,
        shadowRadius: 15,
        elevation: 10,
    },
    primaryBtnText: {
        fontFamily: 'Inter-Black',
        fontSize: 16,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    emptyContainer: {
        marginTop: 80,
        alignItems: 'center',
        paddingHorizontal: 40,
    },
    deniedIconBox: {
        width: 80,
        height: 80,
        borderWidth: StyleSheet.hairlineWidth,
        padding: 4,
        borderRadius: 2,
        marginBottom: 24,
    },
    deniedIconInner: {
        flex: 1,
        alignItems: 'center',
        justifyContent: 'center',
    },
    deniedTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 24,
        marginBottom: 8,
        textTransform: 'uppercase',
    },
    deniedSubtext: {
        fontFamily: 'Inter-Bold',
        fontSize: 10,
        letterSpacing: 2,
        textTransform: 'uppercase',
        textAlign: 'center',
        marginBottom: 40,
    },
    loginBtn: {
        paddingHorizontal: 40,
        paddingVertical: 16,
        borderRadius: 12,
    },
    loginBtnText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    sectionLabel: {
        fontFamily: 'Inter-Bold',
        fontSize: 9,
        letterSpacing: 2,
        textTransform: 'uppercase',
        marginBottom: 12,
        paddingHorizontal: 4,
    },
    summaryCard: {
        borderWidth: 1,
        borderRadius: 2,
        padding: 20,
        gap: 16,
    },
    summaryRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    summaryItemTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 16,
        textTransform: 'uppercase',
    },
    summaryItemSubtitle: {
        fontFamily: 'Inter-Bold',
        fontSize: 9,
        textTransform: 'uppercase',
    },
    summaryItemPrice: {
        fontFamily: 'Inter-Black',
        fontSize: 18,
    },
    methodItem: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: 20,
        borderWidth: 1,
        borderRadius: 4,
    },
    methodItemActive: {
    },
    methodItemInactive: {
    },
    methodLeft: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
    },
    methodText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        textTransform: 'uppercase',
    },
    selectionDot: {
        width: 8,
        height: 8,
        borderRadius: 4,
        // Manual Glow
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 1,
        shadowRadius: 6,
    },
    secureTag: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        paddingVertical: 16,
        borderWidth: StyleSheet.hairlineWidth,
        borderStyle: 'dashed',
        borderRadius: 2,
        gap: 8,
    },
    secureTagText: {
        fontFamily: 'Inter-Black',
        fontSize: 9,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    applePayBackdrop: {
        flex: 1,
        backgroundColor: 'rgba(0,0,0,0.6)',
        justifyContent: 'flex-end',
    },
    applePaySheet: {
        borderTopLeftRadius: 24,
        borderTopRightRadius: 24,
        paddingBottom: 40,
        paddingHorizontal: 20,
        zIndex: 10000,
    },
    applePayHeader: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'center',
        paddingVertical: 16,
        borderBottomWidth: 1,
    },
    applePayHeaderText: {
        fontFamily: 'Inter-Black',
        fontSize: 12,
        letterSpacing: 2,
    },
    applePayCancelText: {
        fontFamily: 'Inter-Black',
        fontSize: 12,
    },
    applePayContent: {
        alignItems: 'center',
        paddingVertical: 32,
    },
    applePayAmount: {
        fontFamily: 'Inter-Black',
        fontSize: 48,
        marginTop: 16,
    },
    applePayMerchant: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        marginTop: 4,
        textTransform: 'uppercase',
        letterSpacing: 1,
    },
    applePayCardInfo: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 16,
        paddingVertical: 8,
        borderRadius: 8,
        marginTop: 24,
        gap: 8,
    },
    applePayCardText: {
        fontFamily: 'Inter-Black',
        fontSize: 12,
    },
    applePayAuthBtn: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        width: '100%',
        height: 56,
        borderRadius: 12,
        marginTop: 32,
    },
    applePayAuthBtnText: {
        fontFamily: 'Inter-Black',
        fontSize: 16,
        textTransform: 'uppercase',
    },
    monoBtnShadow: {
        shadowColor: '#000',
        shadowOffset: { width: 0, height: 10 },
        shadowOpacity: 0.3,
        shadowRadius: 15,
        elevation: 10,
    },
});