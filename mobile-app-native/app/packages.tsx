/// <reference types="nativewind/types" />
import { useState, useEffect, useRef } from "react";
import { View, Text, Pressable, ActivityIndicator, StyleSheet, Animated } from "react-native";
import { useRouter } from "expo-router";
import { ChevronLeft, Minus, Plus, ShoppingCart } from "lucide-react-native";
import { useStore } from "@/lib/store";
import { useI18n } from "@/lib/i18n";
import { getPackages, FuelPackage } from "@/lib/api";
import { normalizeFuelName } from "@/lib/utils";
import { PageLayout } from "@/components/page-layout";
import { GridBackground } from "@/components/grid-background";
import { tokens } from "@/lib/design-tokens";
import { GlowText } from "@/components/glow-text";
import { Haptics } from "@/lib/haptics";

const GLOBAL_PADDING = tokens.spacing.containerPadding;
const ACCENT_WIDTH = 12;

export default function PackagesScreen() {
    const router = useRouter();
    const { selectedStation, selectedFuel, addToCart } = useStore();
    const cartItemCount = useStore(state => state.getCartItemCount());
    const { t } = useI18n();
    const [quantities, setQuantities] = useState<Record<string, number>>({});
    const [addedItems, setAddedItems] = useState<Set<string>>(new Set());
    const [packages, setPackages] = useState<FuelPackage[]>([]);
    const [loading, setLoading] = useState(true);
    const pulseAnim = useRef(new Animated.Value(1)).current;

    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(pulseAnim, { toValue: 0.8, duration: 1500, useNativeDriver: true }),
                Animated.timing(pulseAnim, { toValue: 1, duration: 1500, useNativeDriver: true }),
            ])
        ).start();

        if (selectedStation && selectedFuel) {
            loadData();
        }
    }, [selectedStation, selectedFuel]);

    const loadData = async () => {
        try {
            setLoading(true);
            const allPkgs = await getPackages();
            const filtered = allPkgs.filter(pkg => {
                if (pkg.stationId !== selectedStation!.id) return false;
                if (normalizeFuelName(pkg.fuelName) !== normalizeFuelName(selectedFuel!.name)) return false;
                return true;
            });
            filtered.sort((a, b) => a.liters - b.liters);
            setPackages(filtered);
        } catch (e) {
            console.error("Failed to load packages", e);
        } finally {
            setLoading(false);
        }
    };

    if (!selectedStation || !selectedFuel) return null;

    const brandColor = (tokens.colors.text.brand as any)[selectedStation.id] || tokens.colors.primary;

    const handleAddToCart = (pkg: FuelPackage) => {
        const qty = quantities[pkg.id] || 1;
        addToCart({ package: pkg, station: selectedStation, fuel: selectedFuel, quantity: qty });
        setAddedItems(prev => new Set(prev).add(pkg.id));
        setTimeout(() => setAddedItems(prev => { const n = new Set(prev); n.delete(pkg.id); return n; }), 2000);
    };

    const Header = (
        <View style={styles.header}>
            <View style={styles.headerTop}>
                <Pressable onPress={() => router.back()} style={styles.iconBox}>
                    <ChevronLeft size={24} color="#FFF" />
                </Pressable>

                <View style={styles.headerCenter}>
                    <GlowText intensity="high" color={brandColor} glowColor={brandColor} style={styles.headerTitle}>
                        {selectedFuel.name}
                    </GlowText>
                    <Text allowFontScaling={false} style={[styles.headerSubtitle, { color: brandColor }]}>
                        [ {t('packages.selectCards')} ]
                    </Text>
                </View>

                <Pressable
                    onPress={() => {
                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                        router.push("/basket");
                    }}
                    style={[styles.iconBox, cartItemCount > 0 && styles.iconBoxActive]}
                >
                    <ShoppingCart size={20} color={cartItemCount > 0 ? "#EF4444" : brandColor} />
                    {cartItemCount > 0 && (
                        <View style={[styles.badge, { backgroundColor: "#EF4444" }]}>
                            <Text allowFontScaling={false} style={styles.badgeText}>{cartItemCount}</Text>
                        </View>
                    )}
                </Pressable>
            </View>
        </View>
    );

    return (
        <PageLayout header={Header} background={<GridBackground color={brandColor} />} paddingHorizontal={GLOBAL_PADDING}>
            {loading ? (
                <ActivityIndicator size="small" color={brandColor} style={{ marginTop: 100 }} />
            ) : (
                <View style={styles.container}>
                    {packages.map((pkg, index) => (
                        <PackageButton
                            key={pkg.id}
                            pkg={pkg}
                            brandColor={brandColor}
                            t={t}
                            index={index}
                            onAdd={() => handleAddToCart(pkg)}
                            isAdded={addedItems.has(pkg.id)}
                            quantity={quantities[pkg.id] || 1}
                            setQuantity={(q: number) => setQuantities({ ...quantities, [pkg.id]: q })}
                        />
                    ))}
                </View>
            )}
        </PageLayout>
    );
}

function PackageButton({ pkg, brandColor, t, index, onAdd, isAdded, quantity, setQuantity }: any) {
    const scaleAnim = useRef(new Animated.Value(1)).current;
    const tiltX = useRef(new Animated.Value(0)).current;
    const contentMove = useRef(new Animated.Value(0)).current;
    const entranceAnim = useRef(new Animated.Value(0)).current;

    useEffect(() => {
        Animated.spring(entranceAnim, {
            toValue: 1,
            useNativeDriver: true,
            delay: index * 60,
            friction: 8,
            tension: 50,
        }).start();
    }, []);

    const handlePressIn = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
        Animated.parallel([
            Animated.spring(scaleAnim, { toValue: 0.98, useNativeDriver: true, friction: 5, tension: 60 }),
            Animated.spring(tiltX, { toValue: 1, useNativeDriver: true }),
            Animated.spring(contentMove, { toValue: 4, useNativeDriver: true, friction: 8, tension: 100 })
        ]).start();
    };

    const handlePressOut = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        Animated.parallel([
            Animated.spring(scaleAnim, { toValue: 1, useNativeDriver: true, friction: 3, tension: 100 }),
            Animated.spring(tiltX, { toValue: 0, useNativeDriver: true }),
            Animated.spring(contentMove, { toValue: 0, useNativeDriver: true, friction: 4, tension: 150 })
        ]).start();
    };

    const rotateX = tiltX.interpolate({
        inputRange: [0, 1],
        outputRange: ['0deg', '5deg']
    });

    const translateY = entranceAnim.interpolate({
        inputRange: [0, 1],
        outputRange: [15, 0]
    });

    const savingsPerUnit = pkg.originalPrice - pkg.price;
    const totalSavings = (savingsPerUnit * quantity).toFixed(2);

    return (
        <Animated.View style={{ opacity: entranceAnim, transform: [{ translateY }] }}>
            <Animated.View style={[
                styles.card,
                {
                    transform: [{ perspective: 1000 }, { scale: scaleAnim }, { rotateX }],
                }
            ]}>
                <View style={[styles.packageAccent, { backgroundColor: brandColor }]} />

                <View style={styles.cardTop}>
                    <View style={[styles.literBox, { borderColor: brandColor }]}>
                        <Text allowFontScaling={false} style={styles.literValue}>{pkg.liters}</Text>
                        <Text allowFontScaling={false} style={[styles.literLabel, { color: brandColor }]}>{t('packages.liters')}</Text>
                    </View>

                    <Animated.View style={[styles.priceInfo, { transform: [{ translateX: contentMove }] }]}>
                        <Text allowFontScaling={false} style={styles.currentPrice}>{pkg.price} ₴</Text>
                        <Text allowFontScaling={false} style={styles.basePrice}>{pkg.originalPrice} ₴</Text>
                    </Animated.View>

                    <View style={[styles.savingsBadge, { backgroundColor: brandColor }]}>
                        <Text allowFontScaling={false} style={styles.savingsBadgeText}>
                            -{savingsPerUnit} ₴
                        </Text>
                    </View>
                </View>

                <View style={styles.stepperSection}>
                    <Text allowFontScaling={false} style={styles.sectionLabel}>{t('packages.quantity')}</Text>
                    <View style={styles.stepper}>
                        <Pressable
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                setQuantity(Math.max(1, quantity - 1));
                            }}
                            style={styles.stepBtn}
                        >
                            <Minus size={18} color="#FFF" />
                        </Pressable>
                        <Text allowFontScaling={false} style={[styles.qtyValue, { color: brandColor }]}>{quantity}</Text>
                        <Pressable
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                setQuantity(Math.min(99, quantity + 1));
                            }}
                            style={styles.stepBtn}
                        >
                            <Plus size={18} color="#FFF" />
                        </Pressable>
                    </View>
                </View>

                <View style={styles.summaryArea}>
                    <View>
                        <Text allowFontScaling={false} style={styles.calculationText}>{quantity}x {pkg.liters}{t('packages.unitLiters')}</Text>
                        <Text allowFontScaling={false} style={[styles.savingsSummary, { color: brandColor }]}>
                            {t('packages.totalSavingsLabel')} {totalSavings} ₴
                        </Text>
                    </View>
                    <View style={styles.totalBox}>
                        <Text allowFontScaling={false} style={styles.totalLabel}>{t('packages.payTitle')}</Text>
                        <Text allowFontScaling={false} style={styles.totalValue}>{(pkg.price * quantity).toFixed(2)} ₴</Text>
                    </View>
                </View>

                <Pressable
                    onPressIn={handlePressIn}
                    onPressOut={handlePressOut}
                    onPress={onAdd}
                    disabled={isAdded}
                    style={[
                        styles.mainBtn,
                        {
                            backgroundColor: isAdded ? 'rgba(255,255,255,0.05)' : brandColor,
                            shadowColor: brandColor,
                            opacity: isAdded ? 0.6 : 1
                        }
                    ]}
                >
                    <ShoppingCart size={20} color={isAdded ? brandColor : "#000"} />
                    <Text allowFontScaling={false} style={[styles.mainBtnText, { color: isAdded ? brandColor : "#000" }]}>
                        {isAdded ? t('packages.added') : t('packages.addToCart')}
                    </Text>
                </Pressable>
            </Animated.View>
        </Animated.View>
    );
}

const styles = StyleSheet.create({
    header: {
        paddingTop: 8,
        paddingBottom: 24,
        paddingHorizontal: 0,
    },
    headerTop: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 0,
    },
    headerCenter: {
        flex: 1,
        alignItems: 'center',
    },
    iconBox: {
        width: 44,
        height: 44,
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
        backgroundColor: 'rgba(0,0,0,0.8)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    iconBoxActive: {
        borderColor: '#EF4444',
        shadowColor: '#EF4444',
        shadowOpacity: 0.3,
        shadowRadius: 10,
        elevation: 5,
    },
    headerTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 32,
        lineHeight: 38,
        letterSpacing: -0.5,
        textTransform: 'uppercase',
    },
    headerSubtitle: {
        fontFamily: 'Inter-Black',
        fontSize: 9,
        letterSpacing: 4,
        marginTop: 4,
        opacity: 0.6,
    },
    container: {
        paddingHorizontal: 0,
        paddingBottom: 44,
        gap: 16,
    },
    card: {
        backgroundColor: '#000',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.08)',
        borderRadius: 2,
        padding: 24,
        position: 'relative',
        overflow: 'hidden',
    },
    packageAccent: {
        position: 'absolute',
        left: 0,
        top: 0,
        bottom: 0,
        width: 12,
    },
    cardTop: {
        flexDirection: 'row',
        alignItems: 'center',
        marginBottom: 32,
    },
    literBox: {
        width: 64,
        height: 64,
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'rgba(255,255,255,0.03)',
    },
    literValue: {
        fontFamily: 'Rajdhani-Bold',
        color: '#FFF',
        fontSize: 32,
        lineHeight: 32,
    },
    literLabel: {
        fontFamily: 'Inter-Black',
        fontSize: 9,
        marginTop: -2,
    },
    priceInfo: {
        flex: 1,
        paddingLeft: 20,
    },
    currentPrice: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 24,
        lineHeight: 24,
    },
    basePrice: {
        fontFamily: 'Inter-Medium',
        color: 'rgba(255,255,255,0.2)',
        fontSize: 14,
        textDecorationLine: 'line-through',
        marginTop: 2,
    },
    savingsBadge: {
        paddingHorizontal: 12,
        paddingVertical: 12,
        borderRadius: 2,
    },
    savingsBadgeText: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#000',
        fontSize: 18,
        letterSpacing: 0,
    },
    stepperSection: {
        marginBottom: 24,
    },
    sectionLabel: {
        fontFamily: 'Inter-Black',
        color: 'rgba(255,255,255,0.25)',
        fontSize: 10,
        letterSpacing: 4,
        textTransform: 'uppercase',
        marginBottom: 12,
    },
    stepper: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 16,
    },
    stepBtn: {
        width: 52,
        height: 52,
        backgroundColor: 'rgba(255,255,255,0.04)',
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.06)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    qtyValue: {
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 32,
        color: '#FFF',
        minWidth: 48,
        textAlign: 'center',
    },
    summaryArea: {
        flexDirection: 'row',
        justifyContent: 'space-between',
        alignItems: 'flex-end',
        marginBottom: 24,
        paddingTop: 24,
        borderTopWidth: 1,
        borderTopColor: 'rgba(255,255,255,0.05)',
    },
    calculationText: {
        fontFamily: 'Inter-Bold',
        color: 'rgba(255,255,255,0.2)',
        fontSize: 12,
    },
    savingsSummary: {
        fontFamily: 'Inter-Black',
        fontSize: 10,
        letterSpacing: 1,
        marginTop: 4,
    },
    totalBox: {
        alignItems: 'flex-end',
    },
    totalLabel: {
        fontFamily: 'Inter-Black',
        color: 'rgba(255,255,255,0.3)',
        fontSize: 11,
        letterSpacing: 4,
        textTransform: 'uppercase',
    },
    totalValue: {
        fontFamily: tokens.typography.fonts.heading,
        color: '#FFF',
        fontSize: 36,
        lineHeight: 44,
        letterSpacing: -0.5,
    },
    mainBtn: {
        height: 64,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 16,
        borderRadius: 2,
    },
    mainBtnText: {
        fontFamily: 'Inter-Black',
        fontSize: 16,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    badge: {
        position: 'absolute',
        top: -6,
        right: -6,
        minWidth: 20,
        height: 20,
        borderRadius: 10,
        alignItems: 'center',
        justifyContent: 'center',
        borderWidth: 1,
        borderColor: '#000',
    },
    badgeText: {
        color: '#000',
        fontSize: 11,
        fontFamily: 'Inter-Black',
    }
});
