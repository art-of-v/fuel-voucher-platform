/// <reference types="nativewind/types" />
import { useState } from "react";
import { View, Text, Pressable, ActivityIndicator, StyleSheet } from "react-native";
import { useRouter } from "expo-router";
import { ChevronLeft, CreditCard, ShieldCheck, AlertTriangle, Apple, Smartphone } from "lucide-react-native";
import { useAuth } from "../src/hooks/useAuth";
import { useStore } from "../src/lib/store";
import { useI18n } from "../src/lib/i18n";
import { PageLayout } from "../src/components/page-layout";
import { tokens } from "../src/lib/design-tokens";
import { Haptics } from "../src/lib/haptics";

const GLOBAL_PADDING = tokens.spacing.containerPadding;

export default function CheckoutScreen() {
    const router = useRouter();
    const { isLoading, isAuthenticated } = useAuth();
    const { t } = useI18n();
    const {
        cart,
        getDiscountedTotal
    } = useStore();
    const [isProcessing, setIsProcessing] = useState(false);
    const [paymentMethod, setPaymentMethod] = useState("apple_pay");

    const discountedTotal = getDiscountedTotal();

    const Header = (
        <View style={styles.headerContainer}>
            <View style={styles.headerTopRow}>
                <Pressable
                    onPress={() => router.back()}
                    style={styles.backButton}
                >
                    <ChevronLeft size={20} color="#FFF" />
                </Pressable>

                <View style={styles.headerCenter}>
                    <Text allowFontScaling={false} style={styles.headerTitle}>{t('basket.checkoutTitle')}</Text>
                    <Text allowFontScaling={false} style={styles.headerSubtitle}>{t('checkout.verifyOrder')} [ UNLOCKED ]</Text>
                </View>

                <View style={{ width: 44 }} />
            </View>
        </View>
    );

    const fixedFooter = cart.length > 0 ? (
        <View style={styles.footerRegion}>
            <Pressable
                onPress={async () => {
                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
                    setIsProcessing(true);
                    router.push("/payment");
                }}
                disabled={isProcessing}
                style={({ pressed }) => [
                    styles.primaryBtn,
                    isProcessing && { opacity: 0.5 },
                    pressed && !isProcessing && { transform: [{ scale: 0.98 }] }
                ]}
            >
                {isProcessing ? (
                    <ActivityIndicator color="#000" />
                ) : (
                    <>
                        <CreditCard size={20} color="#000" />
                        <Text allowFontScaling={false} style={styles.primaryBtnText}>
                            {t('packages.payTitle')} {discountedTotal.toFixed(2)} ₴
                        </Text>
                    </>
                )}
            </Pressable>
        </View>
    ) : null;

    if (isLoading) {
        return (
            <View style={styles.centerContainer}>
                <ActivityIndicator size="large" color={tokens.colors.primary} />
            </View>
        );
    }

    if (!isAuthenticated) {
        return (
            <PageLayout>
                <View style={styles.emptyContainer}>
                    <View style={styles.deniedIconBox}>
                        <View style={styles.deniedIconInner}>
                            <AlertTriangle size={40} color="#EF4444" />
                        </View>
                    </View>
                    <Text allowFontScaling={false} style={styles.deniedTitle}>{t('checkout.accessDenied')}</Text>
                    <Text allowFontScaling={false} style={styles.deniedSubtext}>{t('checkout.loginRequired')}</Text>
                    <Pressable
                        onPress={() => router.push("/profile")}
                        style={styles.loginBtn}
                    >
                        <Text allowFontScaling={false} style={styles.loginBtnText}>{t('checkout.gotoLogin')}</Text>
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
                    <Text allowFontScaling={false} style={styles.sectionLabel}>ORDER DATA</Text>
                    <View style={styles.summaryCard}>
                        {cart.map((item) => (
                            <View key={item.id} style={styles.summaryRow}>
                                <View>
                                    <Text allowFontScaling={false} style={styles.summaryItemTitle}>{item.station.logoText || item.station.name}</Text>
                                    <Text allowFontScaling={false} style={styles.summaryItemSubtitle}>{item.fuel.name} x {item.quantity}</Text>
                                </View>
                                <Text allowFontScaling={false} style={styles.summaryItemPrice}>{item.package.price * item.quantity} ₴</Text>
                            </View>
                        ))}
                    </View>
                </View>

                {/* Payment Methods */}
                <View>
                    <Text allowFontScaling={false} style={styles.sectionLabel}>PAYMENT METHOD</Text>
                    <View style={{ gap: 12 }}>
                        {[
                            { id: 'apple_pay', name: 'Apple Pay', icon: Apple },
                            { id: 'google_pay', name: 'Google Pay', icon: Smartphone },
                            { id: 'card', name: 'Credit/Debit Card', icon: CreditCard },
                        ].map((method) => {
                            const active = paymentMethod === method.id;
                            const Icon = method.icon;
                            return (
                                <Pressable
                                    key={method.id}
                                    onPress={() => {
                                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                        setPaymentMethod(method.id);
                                    }}
                                    style={[
                                        styles.methodItem,
                                        active ? styles.methodItemActive : styles.methodItemInactive
                                    ]}
                                >
                                    <View style={styles.methodLeft}>
                                        <Icon size={20} color={active ? tokens.colors.primary : tokens.colors.text.dim} />
                                        <Text allowFontScaling={false} style={[styles.methodText, active ? styles.textActive : styles.textInactive]}>{method.name}</Text>
                                    </View>
                                    {active && <View style={styles.selectionDot} />}
                                </Pressable>
                            );
                        })}
                    </View>
                </View>

                {/* Secure Tag */}
                <View style={styles.secureTag}>
                    <ShieldCheck size={14} color={tokens.colors.primary} />
                    <Text allowFontScaling={false} style={styles.secureTagText}>[ ENCRYPTED TRANSACTION PROTOCOL ]</Text>
                </View>
            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    centerContainer: {
        flex: 1,
        backgroundColor: tokens.colors.background,
        alignItems: 'center',
        justifyContent: 'center',
    },
    headerContainer: {
        paddingHorizontal: 0,
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
        backgroundColor: 'rgba(0,0,0,0.8)',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    headerTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 32,
        lineHeight: 38,
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
    footerRegion: {
        padding: 16,
    },
    primaryBtn: {
        backgroundColor: tokens.colors.primary,
        width: '100%',
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
    primaryBtnText: {
        fontFamily: 'Inter-Black',
        color: '#000',
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
        borderWidth: tokens.spacing.hairline,
        borderColor: 'rgba(239, 68, 68, 0.3)',
        padding: 4,
        borderRadius: tokens.effects.radius.xs,
        marginBottom: 24,
    },
    deniedIconInner: {
        flex: 1,
        backgroundColor: 'rgba(239, 68, 68, 0.1)',
        alignItems: 'center',
        justifyContent: 'center',
    },
    deniedTitle: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 24,
        marginBottom: 8,
        textTransform: 'uppercase',
    },
    deniedSubtext: {
        fontFamily: tokens.typography.fonts.bodyBold,
        color: tokens.colors.text.dim,
        fontSize: 10,
        letterSpacing: 2,
        textTransform: 'uppercase',
        textAlign: 'center',
        marginBottom: 40,
    },
    loginBtn: {
        paddingHorizontal: 40,
        paddingVertical: 16,
        backgroundColor: tokens.colors.primary,
        borderRadius: tokens.effects.radius.lg,
    },
    loginBtnText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: '#000',
        fontSize: 14,
        letterSpacing: 1,
        textTransform: 'uppercase',
    },
    sectionLabel: {
        fontFamily: tokens.typography.fonts.bodyBold,
        color: tokens.colors.text.dim,
        fontSize: 9,
        letterSpacing: 2,
        textTransform: 'uppercase',
        marginBottom: 12,
        paddingHorizontal: 4,
    },
    summaryCard: {
        backgroundColor: '#000',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.08)',
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
        fontFamily: tokens.typography.fonts.heading,
        color: tokens.colors.text.primary,
        fontSize: 16,
        textTransform: 'uppercase',
    },
    summaryItemSubtitle: {
        fontFamily: tokens.typography.fonts.bodyBold,
        color: tokens.colors.primary,
        fontSize: 9,
        textTransform: 'uppercase',
    },
    summaryItemPrice: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: tokens.colors.text.primary,
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
        backgroundColor: 'rgba(0, 255, 128, 0.08)',
        borderColor: tokens.colors.primary,
    },
    methodItemInactive: {
        backgroundColor: 'rgba(0,0,0,0.4)',
        borderColor: tokens.colors.borderLight,
    },
    methodLeft: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
    },
    methodText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        fontSize: 14,
        textTransform: 'uppercase',
    },
    textActive: {
        color: '#FFF',
    },
    textInactive: {
        color: tokens.colors.text.dim,
    },
    selectionDot: {
        width: 8,
        height: 8,
        borderRadius: 4,
        backgroundColor: tokens.colors.primary,
        // Manual Glow
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 1,
        shadowRadius: 6,
    },
    secureTag: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        paddingVertical: 16,
        backgroundColor: 'rgba(0, 255, 128, 0.03)',
        borderWidth: tokens.spacing.hairline,
        borderColor: 'rgba(0, 255, 128, 0.1)',
        borderStyle: 'dashed',
        borderRadius: tokens.effects.radius.xs,
        gap: 8,
    },
    secureTagText: {
        fontFamily: tokens.typography.fonts.bodyBlack,
        color: tokens.colors.primary,
        fontSize: 9,
        letterSpacing: 2,
        textTransform: 'uppercase',
    }
});
