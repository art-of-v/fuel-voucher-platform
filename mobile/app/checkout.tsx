/// <reference types="nativewind/types" />
import { useState } from "react";
import { View, Text, Pressable, StyleSheet } from "react-native";
import { useRouter } from "expo-router";
import { User, Building2 } from "lucide-react-native";
import { useQuery } from "@tanstack/react-query";
import { useStore } from "../src/core/state/appStore";
import { useCartStore } from "../src/features/cart/store/cartStore";
import { useI18n } from "../src/core/i18n";
import { createBulkMonobankInvoice } from "../src/features/vouchers/api/purchases";
import { getLegalProfile } from "../src/features/profile/api/updateLegalProfile";
import { Haptics } from "../src/core/utils/haptics";
import { PageLayout } from "../src/components/page-layout";
import { GridBackground } from "../src/components/grid-background";
import { PhoneAuthForm } from "../src/features/auth/components/PhoneAuthForm";
import { useAuth } from "../src/features/auth/hooks/useAuth";
import { useDesignTokens } from "../src/core/hooks/useTheme";
import { Button, ScreenHeader } from "../src/core/ui";
import { formatMoney } from "../src/core/utils/currency";
import * as Linking from 'expo-linking';

export default function CheckoutScreen() {
    const router = useRouter();
    const tokens = useDesignTokens();
    const soft = tokens.surface.soft;
    const { t } = useI18n();
    const { isAuthenticated: storeAuth, login } = useStore();
    const { cart, getDiscountedTotal, clearCart } = useCartStore();
    const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
    const isAuthenticated = storeAuth || hookAuth;
    const [isProcessing, setIsProcessing] = useState(false);

    // Company purchase support: the selector only appears if the user owns a
    // legal entity. Defaults to a personal purchase (§3 of the spec).
    const { data: legalProfile } = useQuery({
        queryKey: ['legal-profile'],
        queryFn: getLegalProfile,
        enabled: isAuthenticated,
    });
    const [purchaseMode, setPurchaseMode] = useState<'personal' | 'company'>('personal');

    const GLOBAL_PADDING = tokens.spacing.containerPadding;
    const discountedTotal = getDiscountedTotal();

    const handlePaymentEnd = async () => {
        try {
            setIsProcessing(true);

            if (cart.length === 0) return;

            // Checked: createMonobankInvoice below will trigger SecurityService.signPayload
            // inside apiFetch, which handles the single, cryptographically secure Face ID prompt.
            // We no longer need this manual LocalAuthentication block which caused a double prompt.


            // Aggregate all cart items into one Monobank invoice
            const items = cart.map((item) => ({
                packageId: item.package.id,
                stationId: item.station.id,
                stationName: item.station.name,
                fuelType: item.fuel.id,
                fuelName: item.fuel.name,
                liters: item.package.liters,
                quantity: item.quantity,
                price: item.package.price * item.quantity,
            }));

            // Send legalEntityId only when a company purchase is selected.
            const legalEntityId =
                purchaseMode === 'company' && legalProfile ? legalProfile.id : undefined;

            const response = await createBulkMonobankInvoice(items, legalEntityId);

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
        }
    };

    const Header = (
        <ScreenHeader
            title={t('basket.checkoutTitle')}
            subtitle={t('checkout.verifyOrder')}
        />
    );

    const fixedFooter = cart.length > 0 ? (
        // Padding, the hairline and the bottom safe-area inset are owned by
        // PageLayout's footer slot — the screen no longer sets paddingBottom: 72.
        <Button
            label={`${t('packages.payTitle')} ${formatMoney(discountedTotal)}`}
            onPress={handlePaymentEnd}
            loading={isProcessing}
            hapticStyle="heavy"
        />
    ) : null;

    if (!isAuthenticated && !authLoading) {
        return (
            <PageLayout background={<GridBackground />}>
                <View style={{ flex: 1, justifyContent: 'center', paddingBottom: 40 }}>
                    <PhoneAuthForm
                        onSuccess={() => {
                            login();
                        }}
                    />
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
                    <View style={[styles.summaryCard, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight, borderRadius: soft ? tokens.surface.card : undefined }]}>
                        {cart.map((item) => (
                            <View key={item.id} style={styles.summaryRow}>
                                <View>
                                    <Text allowFontScaling={false} style={[styles.summaryItemTitle, { color: tokens.colors.text.primary }]}>{item.station.logoText || item.station.name}</Text>
                                    <Text allowFontScaling={false} style={[styles.summaryItemSubtitle, { color: tokens.colors.primary }]}>{item.fuel.name} x {item.quantity}</Text>
                                </View>
                                <Text allowFontScaling={false} style={[styles.summaryItemPrice, { color: tokens.colors.text.primary }]}>{formatMoney(item.package.price * item.quantity)}</Text>
                            </View>
                        ))}
                    </View>
                </View>

                {/* Purchase mode: personal vs company (owners only) */}
                {legalProfile && (
                    <View>
                        <Text allowFontScaling={false} style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>{t('checkout.purchaseAs')}</Text>
                        <View style={{ gap: 12 }}>
                            {(['personal', 'company'] as const).map((mode) => {
                                const active = purchaseMode === mode;
                                return (
                                    <Pressable
                                        key={mode}
                                        onPress={() => {
                                            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                            setPurchaseMode(mode);
                                        }}
                                        style={[
                                            styles.methodItem,
                                            {
                                                backgroundColor: tokens.colors.card,
                                                borderColor: active ? tokens.colors.primary : tokens.colors.borderLight,
                                                borderRadius: soft ? 12 : 4,
                                            },
                                            active && { borderWidth: 1.5 },
                                        ]}
                                    >
                                        <View style={styles.methodLeft}>
                                            {mode === 'personal' ? (
                                                <User size={18} color={active ? tokens.colors.primary : tokens.colors.text.muted} />
                                            ) : (
                                                <Building2 size={18} color={active ? tokens.colors.primary : tokens.colors.text.muted} />
                                            )}
                                            <View style={{ flex: 1 }}>
                                                <Text allowFontScaling={false} style={[styles.methodText, { color: active ? tokens.colors.primary : tokens.colors.text.primary }]}>
                                                    {mode === 'personal' ? t('checkout.personal') : t('checkout.company')}
                                                </Text>
                                                {mode === 'company' && (
                                                    <Text allowFontScaling={false} numberOfLines={1} style={{ color: tokens.colors.text.dim, fontFamily: 'Inter-Medium', fontSize: 11, marginTop: tokens.spacing.xs / 2 }}>
                                                        {legalProfile.name}
                                                    </Text>
                                                )}
                                            </View>
                                        </View>
                                        <View style={{ width: 18, height: 18, borderRadius: 9, borderWidth: 2, borderColor: active ? tokens.colors.primary : tokens.colors.borderLight, alignItems: 'center', justifyContent: 'center' }}>
                                            {active && <View style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: tokens.colors.primary }} />}
                                        </View>
                                    </Pressable>
                                );
                            })}
                        </View>
                        {purchaseMode === 'company' && (
                            <Text allowFontScaling={false} style={{ color: tokens.colors.text.dim, fontFamily: 'Inter-Medium', fontSize: 11, marginTop: tokens.spacing.sm, paddingHorizontal: tokens.spacing.xs }}>
                                {t('checkout.companyNote')}
                            </Text>
                        )}
                    </View>
                )}

            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
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
});