import React, { useState } from "react";
import { View, Text, Pressable, TextInput, StyleSheet, ScrollView } from "react-native";
import { useRouter } from "expo-router";
import { ChevronLeft, Minus, Plus, Trash2, Tag, Zap, ShoppingCart, X, Check } from "lucide-react-native";
import { useStore } from "../src/lib/store";
import { useI18n } from "../src/lib/i18n";
import { PageLayout } from "../src/components/page-layout";
import { GlowText } from "../src/components/glow-text";
import { tokens } from "../src/lib/design-tokens";
import { useSafeAreaInsets } from "react-native-safe-area-context";

export default function BasketScreen() {
    const router = useRouter();
    const insets = useSafeAreaInsets();
    const { t } = useI18n();
    const {
        cart,
        updateQuantity,
        removeFromCart,
        clearCart,
        promocode,
        discount,
        applyPromocode,
        clearPromocode,
        getCartTotal,
        getDiscountedTotal
    } = useStore();

    const [promoInput, setPromoInput] = useState("");
    const [promoError, setPromoError] = useState(false);

    const total = getCartTotal();
    const discountedTotal = getDiscountedTotal();
    const discountAmount = total - discountedTotal;

    const handleApplyPromo = () => {
        setPromoError(false);
        if (applyPromocode(promoInput)) {
            setPromoInput("");
        } else {
            setPromoError(true);
        }
    };

    const Header = (
        <View style={styles.header}>
            <Pressable
                onPress={() => router.push("/")}
                style={styles.backButton}
            >
                <ChevronLeft size={24} color="#FFF" />
            </Pressable>
            <View style={{ flex: 1 }}>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
                    <ShoppingCart size={20} color="#00FF80" />
                    <Text allowFontScaling={false} style={styles.headerTitle}>{t('basket.title')}</Text>
                </View>
                <Text allowFontScaling={false} style={styles.headerSubtitle}>{cart.length} {t('basket.cards')}</Text>
            </View>
            <Pressable onPress={() => clearCart()}>
                <Text allowFontScaling={false} style={styles.removeText}>{t('basket.remove')}</Text>
            </Pressable>
        </View>
    );

    const fixedFooter = cart.length > 0 ? (
        <View style={[styles.footer, { paddingBottom: 72 }]}>
            <View style={styles.promoIndicator}>
                <Tag size={16} color="#00FF80" />
                <Text allowFontScaling={false} style={styles.promoIndicatorText}>{t('basket.promocode')}</Text>
            </View>

            {promocode ? (
                <View style={styles.activePromo}>
                    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
                        <Check size={20} color="#00FF80" />
                        <Text style={styles.activePromoCode}>{promocode}</Text>
                        <Text style={styles.activePromoDiscount}>(-{discount}%)</Text>
                    </View>
                    <Pressable onPress={clearPromocode}>
                        <X size={20} color="#EF4444" />
                    </Pressable>
                </View>
            ) : (
                <View style={styles.promoInputRow}>
                    <TextInput
                        value={promoInput}
                        onChangeText={(text) => {
                            setPromoInput(text);
                            setPromoError(false);
                        }}
                        placeholder={t('basket.enterCode')}
                        placeholderTextColor="#666"
                        style={[styles.promoInput, promoError && { borderColor: '#EF4444' }]}
                    />
                    <Pressable
                        onPress={handleApplyPromo}
                        disabled={!promoInput}
                        style={styles.applyButton}
                    >
                        <Text style={styles.applyButtonText}>{t('basket.apply')}</Text>
                    </Pressable>
                </View>
            )}

            <View style={styles.summary}>
                <View style={styles.summaryRow}>
                    <Text style={styles.summaryLabel}>{t('basket.subtotal')}</Text>
                    <Text style={styles.summaryValue}>{total} ₴</Text>
                </View>

                {discount > 0 && (
                    <View style={[styles.summaryRow, { marginBottom: 2 }]}>
                        <Text style={{ color: '#00FF80', fontWeight: '700', fontSize: 10 }}>{t('basket.discount')} ({discount}%)</Text>
                        <Text style={{ color: '#00FF80', fontWeight: '700', fontSize: 10 }}>-{discountAmount} ₴</Text>
                    </View>
                )}

                <View style={styles.totalRow}>
                    <Text style={styles.totalLabel}>{t('basket.totalToPay')}</Text>
                    <GlowText
                        style={{ fontSize: 24, fontFamily: 'Rajdhani-Bold' }}
                        color="#FFF"
                        glowColor="#FFF"
                        intensity="high"
                    >
                        {discountedTotal} ₴
                    </GlowText>
                </View>
            </View>

            <Pressable
                onPress={() => router.push("/checkout")}
                style={styles.checkoutButton}
            >
                <Zap size={20} color="#000" />
                <Text style={styles.checkoutButtonText}>{t('basket.checkout')}</Text>
            </Pressable>
        </View>
    ) : null;

    if (cart.length === 0) {
        return (
            <PageLayout header={Header}>
                <View style={styles.emptyState}>
                    <ShoppingCart size={80} color="#333" />
                    <Text style={styles.emptyStateTitle}>{t('basket.empty')}</Text>
                    <Text style={styles.emptyStateSub}>{t('basket.browseStations')}</Text>
                    <Pressable
                        onPress={() => router.push("/")}
                        style={styles.browseButton}
                    >
                        <Text style={styles.browseButtonText}>{t('basket.browseStations')}</Text>
                    </Pressable>
                </View>
            </PageLayout>
        );
    }

    return (
        <PageLayout
            header={Header}
            fixedFooter={fixedFooter}
            disableScroll={false}
        >
            <View style={{ padding: 16, paddingBottom: 100 }}>
                {cart.map((item) => (
                    <View key={item.id} style={styles.card}>
                        <View style={styles.cardHeader}>
                            <View style={{ flex: 1 }}>
                                <Text style={styles.cardTitle}>
                                    {item.station.name} - {item.fuel.name}
                                </Text>
                                <Text style={styles.cardBadge}>{item.package.liters} {t('packages.liters')}</Text>
                            </View>
                            <Pressable onPress={() => removeFromCart(item.id)} style={{ padding: 4 }}>
                                <Trash2 size={20} color="#EF4444" />
                            </Pressable>
                        </View>

                        <View style={styles.cardFooter}>
                            <View style={styles.stepper}>
                                <Pressable
                                    onPress={() => updateQuantity(item.id, item.quantity - 1)}
                                    style={styles.stepperBtn}
                                >
                                    <Minus size={20} color="#FFF" />
                                </Pressable>
                                <Text style={styles.stepperValue}>{item.quantity}</Text>
                                <Pressable
                                    onPress={() => updateQuantity(item.id, item.quantity + 1)}
                                    style={styles.stepperBtn}
                                >
                                    <Plus size={20} color="#FFF" />
                                </Pressable>
                            </View>
                            <View style={{ alignItems: 'flex-end' }}>
                                <Text style={styles.itemMeta}>{item.quantity} x {item.package.price} ₴</Text>
                                <Text style={styles.itemTotal}>{item.package.price * item.quantity} ₴</Text>
                            </View>
                        </View>
                    </View>
                ))}
            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    header: {
        backgroundColor: '#000',
        borderBottomWidth: 1,
        borderBottomColor: 'rgba(0, 255, 128, 0.15)',
        paddingHorizontal: 16,
        paddingVertical: 12,
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
    },
    backButton: {
        padding: 6,
        borderWidth: 1,
        borderColor: 'rgba(255, 255, 255, 0.15)',
        backgroundColor: 'rgba(0, 0, 0, 0.5)',
        borderRadius: 4,
    },
    headerTitle: {
        fontWeight: 'bold',
        fontSize: 18,
        color: '#FFF',
        textTransform: 'uppercase',
        letterSpacing: 0.5,
    },
    headerSubtitle: {
        fontSize: 10,
        color: '#9CA3AF',
        fontWeight: '700',
        textTransform: 'uppercase',
        letterSpacing: 1,
    },
    removeText: {
        color: '#EF4444',
        fontSize: 10,
        fontWeight: '700',
        textTransform: 'uppercase',
        letterSpacing: 1,
    },
    footer: {
        backgroundColor: '#000',
        paddingHorizontal: 12,
        paddingBottom: 72,
        paddingTop: 4,
        borderTopWidth: 1,
        borderTopColor: 'rgba(255, 255, 255, 0.1)',
    },
    promoIndicator: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 6,
        marginBottom: 2,
    },
    promoIndicatorText: {
        fontSize: 9,
        color: '#9CA3AF',
        fontWeight: '700',
        textTransform: 'uppercase',
        letterSpacing: 1,
    },
    promoInputRow: {
        flexDirection: 'row',
        gap: 6,
        marginBottom: 4,
    },
    promoInput: {
        flex: 1,
        backgroundColor: 'rgba(0, 0, 0, 0.5)',
        borderWidth: 1,
        borderColor: 'rgba(255, 255, 255, 0.15)',
        paddingHorizontal: 12,
        height: 36,
        color: '#FFF',
        fontWeight: '700',
        fontSize: 12,
        textTransform: 'uppercase',
        borderRadius: 4,
    },
    applyButton: {
        backgroundColor: 'rgba(0, 255, 128, 0.1)',
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 128, 0.25)',
        paddingHorizontal: 12,
        height: 36,
        justifyContent: 'center',
        borderRadius: 4,
    },
    applyButtonText: {
        color: '#00FF80',
        fontWeight: '700',
        fontSize: 11,
        textTransform: 'uppercase',
    },
    activePromo: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'space-between',
        backgroundColor: 'rgba(0, 255, 128, 0.04)',
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 128, 0.15)',
        padding: 6,
        borderRadius: 4,
        marginBottom: 4,
    },
    activePromoCode: {
        fontWeight: '700',
        color: '#00FF80',
        fontSize: 13,
    },
    activePromoDiscount: {
        color: '#9CA3AF',
        fontSize: 11,
    },
    summary: {
        borderTopWidth: 1,
        borderTopColor: 'rgba(255, 255, 255, 0.05)',
        paddingTop: 4,
        marginBottom: 6,
    },
    summaryRow: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        marginBottom: 2,
    },
    summaryLabel: {
        color: '#6B7280',
        fontWeight: '700',
        fontSize: 10,
    },
    summaryValue: {
        color: '#6B7280',
        fontWeight: '700',
        fontSize: 10,
    },
    totalRow: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginTop: 0,
    },
    totalLabel: {
        fontWeight: '700',
        color: '#FFF',
        fontSize: 15,
        textTransform: 'uppercase',
    },
    checkoutButton: {
        width: '100%',
        backgroundColor: '#00FF80',
        height: 48,
        borderRadius: 4,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 10,
    },
    checkoutButtonText: {
        color: '#000',
        fontWeight: '900',
        fontSize: 16,
        textTransform: 'uppercase',
        letterSpacing: 1,
    },
    card: {
        backgroundColor: '#18181bcc',
        borderWidth: 1,
        borderColor: 'rgba(255, 255, 255, 0.1)',
        padding: 16,
        borderRadius: 8,
        marginBottom: 12,
    },
    cardHeader: {
        flexDirection: 'row',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        marginBottom: 16,
    },
    cardTitle: {
        fontWeight: '700',
        color: '#FFF',
        fontSize: 18,
        textTransform: 'uppercase',
    },
    cardBadge: {
        color: '#00FF80',
        fontSize: 12,
        fontWeight: '700',
        textTransform: 'uppercase',
    },
    cardFooter: {
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'space-between',
    },
    stepper: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 16,
    },
    stepperBtn: {
        width: 40,
        height: 40,
        backgroundColor: 'rgba(255, 255, 255, 0.05)',
        borderWidth: 1,
        borderColor: 'rgba(255, 255, 255, 0.1)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    stepperValue: {
        fontSize: 24,
        fontWeight: '900',
        color: '#00FF80',
        minWidth: 40,
        textAlign: 'center',
        fontFamily: 'Rajdhani-Bold',
    },
    itemMeta: {
        fontSize: 10,
        color: '#6B7280',
        fontWeight: '700',
        textTransform: 'uppercase',
    },
    itemTotal: {
        color: '#FFF',
        fontWeight: '700',
        fontSize: 20,
    },
    emptyState: {
        flex: 1,
        alignItems: 'center',
        justifyContent: 'center',
        padding: 32,
        paddingVertical: 80,
    },
    emptyStateTitle: {
        fontSize: 24,
        fontWeight: '900',
        color: '#FFF',
        textTransform: 'uppercase',
        marginTop: 16,
        marginBottom: 8,
    },
    emptyStateSub: {
        color: '#6B7280',
        textAlign: 'center',
        marginBottom: 32,
    },
    browseButton: {
        backgroundColor: '#00FF80',
        paddingHorizontal: 32,
        paddingVertical: 16,
        borderRadius: 8,
    },
    browseButtonText: {
        color: '#000',
        fontWeight: '900',
        fontSize: 18,
        textTransform: 'uppercase',
        letterSpacing: 1,
    }
});
