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
import { useDesignTokens } from "@/lib/design-tokens";
import { GlowText } from "@/components/glow-text";
import { Haptics } from "@/lib/haptics";
import Svg, { Rect, Defs, Pattern, Path, LinearGradient, Stop } from 'react-native-svg';

const ACCENT_WIDTH = 12;

export default function PackagesScreen() {
    const router = useRouter();
    const tokens = useDesignTokens();
    const { selectedStation, selectedFuel, addToCart } = useStore();
    const cartItemCount = useStore(state => state.getCartItemCount());
    const { t } = useI18n();
    const [quantities, setQuantities] = useState<Record<string, number>>({});
    const [addedItems, setAddedItems] = useState<Set<string>>(new Set());
    const [packages, setPackages] = useState<FuelPackage[]>([]);
    const [loading, setLoading] = useState(true);
    const pulseAnim = useRef(new Animated.Value(1)).current;

    const GLOBAL_PADDING = tokens.spacing.containerPadding;

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
        <View style={[styles.header, { paddingHorizontal: GLOBAL_PADDING }]}>
            <View style={styles.headerTop}>
                <Pressable onPress={() => router.back()} style={[styles.iconBox, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}>
                    <ChevronLeft size={28} color={tokens.colors.text.primary} />
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
                    style={[styles.iconBox, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }, cartItemCount > 0 && styles.iconBoxActive]}
                >
                    <ShoppingCart size={28} color={brandColor} />
                    {cartItemCount > 0 && (
                        <View style={[styles.badge, { backgroundColor: tokens.colors.primary, borderColor: tokens.colors.background }]}>
                            <Text allowFontScaling={false} style={[styles.badgeText, { color: tokens.colors.isDark ? "#000" : "#FFF" }]}>{cartItemCount}</Text>
                        </View>
                    )}
                </Pressable>
            </View>
        </View>
    );

    return (
        <PageLayout header={Header}>
            <View style={{ paddingHorizontal: GLOBAL_PADDING }}>
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
            </View>
        </PageLayout>
    );
}

const MeshBackground = ({ id = "honeycomb", tokens }: any) => (
    <View style={StyleSheet.absoluteFill}>
        <Svg height="100%" width="100%">
            <Defs>
                <Pattern id={id} width="20" height="34" patternUnits="userSpaceOnUse">
                    <Path
                        d="M10 0 L20 5.8 L20 17.4 L10 23.2 L0 17.4 L0 5.8 Z M10 34 L20 28.2 L20 11.6 L10 17.4 L0 11.6 L0 28.2 Z"
                        fill="transparent"
                        stroke={tokens.colors.isDark ? "rgba(255,255,255,0.05)" : "rgba(0,0,0,0.05)"}
                        strokeWidth="1"
                    />
                </Pattern>
                {/* Glossy Lacquer Sheen */}
                <LinearGradient id={`${id}-lacquer`} x1="0%" y1="0%" x2="100%" y2="100%">
                    <Stop offset="0%" stopColor="#fff" stopOpacity={tokens.colors.isDark ? 0.08 : 0.04} />
                    <Stop offset="45%" stopColor="#fff" stopOpacity={0.01} />
                    <Stop offset="48%" stopColor="#fff" stopOpacity={tokens.colors.isDark ? 0.1 : 0.05} />
                    <Stop offset="50%" stopColor="#fff" stopOpacity={0.01} />
                    <Stop offset="100%" stopColor="#000" stopOpacity={tokens.colors.isDark ? 0.3 : 0.1} />
                </LinearGradient>
            </Defs>
            <Rect width="100%" height="100%" fill={tokens.colors.card} />
            <Rect width="100%" height="100%" fill={`url(#${id})`} />
            <Rect width="100%" height="100%" fill={`url(#${id}-lacquer)`} />
        </Svg>
        <View style={{ position: 'absolute', top: 0, left: 0, right: 0, height: 1, backgroundColor: tokens.colors.borderLight }} />
    </View>
);

function PackageButton({ pkg, brandColor, t, index, onAdd, isAdded, quantity, setQuantity }: any) {
    const tokens = useDesignTokens();
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
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        Animated.parallel([
            Animated.spring(scaleAnim, { toValue: 0.99, useNativeDriver: true, friction: 10, tension: 50 }),
            Animated.spring(tiltX, { toValue: 1, useNativeDriver: true, friction: 10, tension: 50 }),
            Animated.spring(contentMove, { toValue: 2, useNativeDriver: true, friction: 10, tension: 50 })
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

    return (
        <Animated.View style={{ opacity: entranceAnim, transform: [{ translateY }] }}>
            <Animated.View style={[
                styles.card,
                {
                    backgroundColor: tokens.colors.card,
                    borderColor: tokens.colors.borderLight,
                    transform: [{ perspective: 1000 }, { scale: scaleAnim }, { rotateX }],
                }
            ]}>
                <MeshBackground id={`pkg-mesh-${index}`} tokens={tokens} />
                <View style={[styles.packageAccent, { backgroundColor: brandColor }]} />

                <View style={styles.cardTop}>
                    <View style={[styles.literBox, { borderColor: brandColor, backgroundColor: tokens.colors.isDark ? 'rgba(255,255,255,0.03)' : 'rgba(0,0,0,0.03)' }]}>
                        <Text allowFontScaling={false} style={[styles.literValue, { color: tokens.colors.text.primary }]}>{pkg.liters}</Text>
                        <Text allowFontScaling={false} style={[styles.literLabel, { color: brandColor }]}>{t('packages.liters')}</Text>
                    </View>

                    <Animated.View style={[styles.priceInfo, { transform: [{ translateX: contentMove }] }]}>
                        <Text allowFontScaling={false} style={[styles.currentPrice, { color: tokens.colors.text.primary }]}>{pkg.price} ₴</Text>
                        <Text allowFontScaling={false} style={[styles.basePrice, { color: tokens.colors.text.dim }]}>{pkg.originalPrice} ₴</Text>
                    </Animated.View>

                    <View style={[styles.savingsBadge, { backgroundColor: brandColor }]}>
                        <Text allowFontScaling={false} style={[styles.savingsBadgeText, { color: tokens.colors.isDark ? "#000" : "#FFF" }]}>
                            -{savingsPerUnit} ₴
                        </Text>
                    </View>
                </View>

                <View style={styles.stepperSection}>
                    <Text allowFontScaling={false} style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}>{t('packages.quantity')}</Text>
                    <View style={styles.stepper}>
                        <Pressable
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                setQuantity(Math.max(1, quantity - 1));
                            }}
                            style={[styles.stepBtn, { backgroundColor: tokens.colors.isDark ? 'rgba(255,255,255,0.04)' : 'rgba(0,0,0,0.04)', borderColor: tokens.colors.borderLight }]}
                        >
                            <Minus size={18} color={tokens.colors.text.primary} />
                        </Pressable>
                        <Text allowFontScaling={false} style={[styles.qtyValue, { color: brandColor }]}>{quantity}</Text>
                        <Pressable
                            onPress={() => {
                                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                setQuantity(Math.min(99, quantity + 1));
                            }}
                            style={[styles.stepBtn, { backgroundColor: tokens.colors.isDark ? 'rgba(255,255,255,0.04)' : 'rgba(0,0,0,0.04)', borderColor: tokens.colors.borderLight }]}
                        >
                            <Plus size={18} color={tokens.colors.text.primary} />
                        </Pressable>
                    </View>
                </View>

                <View style={[styles.summaryArea, { borderTopColor: tokens.colors.borderLight }]}>
                    <View style={styles.totalBox}>
                        <Text allowFontScaling={false} style={[styles.totalLabel, { color: tokens.colors.text.dim }]}>{t('packages.payTitle')}</Text>
                        <Text allowFontScaling={false} style={[styles.totalValue, { color: tokens.colors.text.primary }]}>{(pkg.price * quantity).toFixed(2)} ₴</Text>
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
                            backgroundColor: isAdded ? tokens.colors.card : brandColor,
                            borderColor: isAdded ? brandColor : 'transparent',
                            borderWidth: isAdded ? 1 : 0,
                            opacity: isAdded ? 0.7 : 1
                        }
                    ]}
                >
                    <ShoppingCart size={20} color={isAdded ? brandColor : (tokens.colors.isDark ? "#000" : "#FFF")} />
                    <Text allowFontScaling={false} style={[styles.mainBtnText, { color: isAdded ? brandColor : (tokens.colors.isDark ? "#000" : "#FFF") }]}>
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
    },
    headerTop: {
        flexDirection: 'row',
        alignItems: 'center',
        marginHorizontal: 12,
    },
    headerCenter: {
        flex: 1,
        alignItems: 'center',
    },
    iconBox: {
        width: 56,
        height: 56,
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    iconBoxActive: {
        borderColor: '#EF4444',
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
        paddingBottom: 44,
        gap: 16,
    },
    card: {
        borderWidth: 1,
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
        width: ACCENT_WIDTH,
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
    },
    literValue: {
        fontFamily: 'Rajdhani-Bold',
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
        fontFamily: 'Rajdhani-Bold',
        fontSize: 24,
        lineHeight: 24,
    },
    basePrice: {
        fontFamily: 'Inter-Medium',
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
        fontFamily: 'Rajdhani-Bold',
        fontSize: 18,
        letterSpacing: 0,
    },
    stepperSection: {
        marginBottom: 24,
    },
    sectionLabel: {
        fontFamily: 'Inter-Black',
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
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    qtyValue: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 32,
        minWidth: 48,
        textAlign: 'center',
    },
    summaryArea: {
        flexDirection: 'row',
        justifyContent: 'flex-end',
        alignItems: 'flex-end',
        marginBottom: 24,
        paddingTop: 24,
        borderTopWidth: 1,
    },
    totalBox: {
        alignItems: 'flex-end',
    },
    totalLabel: {
        fontFamily: 'Inter-Black',
        fontSize: 11,
        letterSpacing: 4,
        textTransform: 'uppercase',
    },
    totalValue: {
        fontFamily: 'Rajdhani-Bold',
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
    },
    badgeText: {
        fontSize: 11,
        fontFamily: 'Inter-Black',
    }
});
