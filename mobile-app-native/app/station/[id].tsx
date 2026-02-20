/// <reference types="nativewind/types" />
import { useState, useEffect, useRef } from "react";
import { View, Text, Pressable, StyleSheet, Animated } from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { ChevronLeft, Fuel, ShieldCheck, ShoppingCart } from "lucide-react-native";
import { useStations } from "@/hooks/useStations";
import { PageLayout } from "@/components/page-layout";
import { GridBackground } from "@/components/grid-background";
import { tokens } from "@/lib/design-tokens";
import { GlowText } from "@/components/glow-text";
import { useStore } from "@/lib/store";
import { useI18n } from "@/lib/i18n";
import { Haptics } from "@/lib/haptics";

const GLOBAL_PADDING = tokens.spacing.containerPadding;
const ACCENT_WIDTH = 12;

export default function StationDetailScreen() {
    const { id } = useLocalSearchParams();
    const router = useRouter();
    const { data: stations } = useStations();
    const pulseAnim = useRef(new Animated.Value(1)).current;
    const cartItemCount = useStore(state => state.getCartItemCount());
    const { t } = useI18n();

    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(pulseAnim, { toValue: 0.7, duration: 2000, useNativeDriver: true }),
                Animated.timing(pulseAnim, { toValue: 1, duration: 2000, useNativeDriver: true }),
            ])
        ).start();
    }, []);

    const station = stations?.find(s => s.id === id);
    if (!station) return null;

    const brandColor = (tokens.colors.text.brand as any)[station.id] || tokens.colors.primary;

    const Header = (
        <View style={styles.header}>
            <View style={styles.topNav}>
                <Pressable onPress={() => router.back()} style={styles.backBtn}>
                    <ChevronLeft size={24} color="#FFF" />
                </Pressable>

                <View style={styles.headerCenter}>
                    <GlowText intensity="high" animation="pulse" animatedValue={pulseAnim} color={brandColor} glowColor={brandColor} style={styles.stationTitle}>
                        {station.logoText || station.name}
                    </GlowText>
                    <View style={styles.statusRow}>
                        <Animated.View style={[styles.statusDot, { backgroundColor: brandColor, opacity: pulseAnim }]} />
                        <Text allowFontScaling={false} style={[styles.statusText, { color: brandColor }]}>VERIFIED NODE: {station.id.slice(0, 8)}</Text>
                    </View>
                </View>

                <Pressable
                    onPress={() => {
                        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                        router.push("/basket");
                    }}
                    style={[styles.backBtn, cartItemCount > 0 && styles.iconBoxActive]}
                >
                    <ShoppingCart size={20} color={cartItemCount > 0 ? "#EF4444" : brandColor} />
                    {cartItemCount > 0 && (
                        <View style={[styles.badge, { backgroundColor: "#EF4444" }]}>
                            <Text allowFontScaling={false} style={styles.badgeText}>{cartItemCount}</Text>
                        </View>
                    )}
                </Pressable>
            </View>

            <View style={styles.protocolBanner}>
                <View style={styles.protocolLine} />
                <View style={styles.protocolContent}>
                    <ShieldCheck size={10} color={tokens.colors.primary} />
                    <Text allowFontScaling={false} style={styles.protocolBannerText}>
                        {t('fuel.protocolBanner')}
                    </Text>
                </View>
                <View style={styles.protocolLine} />
            </View>
        </View>
    );

    return (
        <PageLayout
            header={Header}
            background={<GridBackground color={brandColor} />}
            paddingHorizontal={GLOBAL_PADDING}
        >
            <View style={styles.content}>
                <Text allowFontScaling={false} style={styles.sectionLabel}>[ {t('fuel.selectionProtocol')} ]</Text>

                <View style={styles.fuelGrid}>
                    {station.fuels.map((fuel, index) => (
                        <FuelButton
                            key={fuel.id}
                            fuel={fuel}
                            station={station}
                            router={router}
                            t={t}
                            brandColor={brandColor}
                            index={index}
                        />
                    ))}
                </View>

                <View style={styles.footerBranding}>
                    <Text style={styles.footerProtocolText}>[ BULK DISCOUNT RATES ACTIVE ]</Text>
                </View>
            </View>
        </PageLayout>
    );
}

function FuelButton({ fuel, station, router, t, brandColor, index }: any) {
    const { selectStation, selectFuel } = useStore();
    const scaleAnim = useRef(new Animated.Value(1)).current;
    const tiltX = useRef(new Animated.Value(0)).current;
    const contentMove = useRef(new Animated.Value(0)).current;
    const entranceAnim = useRef(new Animated.Value(0)).current;

    useEffect(() => {
        Animated.spring(entranceAnim, {
            toValue: 1,
            useNativeDriver: true,
            delay: index * 80,
            friction: 8,
            tension: 40,
        }).start();
    }, []);

    const handlePressIn = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);

        Animated.parallel([
            Animated.spring(scaleAnim, { toValue: 0.96, useNativeDriver: true, friction: 4, tension: 40 }),
            Animated.spring(tiltX, { toValue: 1, useNativeDriver: true }),
            Animated.spring(contentMove, { toValue: 6, useNativeDriver: true, friction: 5, tension: 50 })
        ]).start();
    };

    const handlePressOut = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);

        Animated.parallel([
            Animated.spring(scaleAnim, { toValue: 1, useNativeDriver: true, friction: 3, tension: 100 }),
            Animated.spring(tiltX, { toValue: 0, useNativeDriver: true }),
            Animated.spring(contentMove, { toValue: 0, useNativeDriver: true, friction: 3, tension: 100 })
        ]).start();
    };

    const rotateX = tiltX.interpolate({
        inputRange: [0, 1],
        outputRange: ['0deg', '8deg']
    });

    const translateY = entranceAnim.interpolate({
        inputRange: [0, 1],
        outputRange: [20, 0]
    });

    return (
        <Animated.View style={{
            opacity: entranceAnim,
            transform: [{ perspective: 1000 }, { translateY }]
        }}>
            <Pressable
                onPressIn={handlePressIn}
                onPressOut={handlePressOut}
                onPress={() => {
                    selectStation(station || null);
                    selectFuel(fuel as any);
                    setTimeout(() => router.push("/packages"), 100);
                }}
                style={{ marginBottom: 16 }}
            >
                <Animated.View style={[
                    styles.fuelCard,
                    {
                        transform: [{ perspective: 1000 }, { scale: scaleAnim }, { rotateX }],
                        shadowColor: brandColor,
                    }
                ]}>
                    <View style={[styles.fuelAccent, { backgroundColor: brandColor, width: ACCENT_WIDTH }]} />
                    <View style={styles.fuelCardContent}>
                        <Animated.View style={[styles.fuelMainInfo, { transform: [{ translateX: contentMove }] }]}>
                            <View style={styles.iconBox}>
                                <Fuel size={20} color={brandColor} />
                            </View>
                            <View style={styles.fuelNameStack}>
                                <Text allowFontScaling={false} style={styles.fuelName}>{fuel.name}</Text>
                                <View style={styles.priceMiniRow}>
                                    <Text allowFontScaling={false} style={styles.basePriceMini}>{(fuel.basePrice || 0).toFixed(2)}</Text>
                                    <GlowText intensity="medium" color={brandColor} style={styles.discountPriceMini}>
                                        {(fuel.discountPrice || 0).toFixed(2)} ₴
                                    </GlowText>
                                </View>
                            </View>
                        </Animated.View>

                        <Animated.View style={[styles.savingsBadge, {
                            backgroundColor: `${brandColor}22`,
                            borderColor: `${brandColor}44`,
                            transform: [{ translateX: Animated.multiply(contentMove, -1.2) }]
                        }]}>
                            <Text allowFontScaling={false} style={[styles.savingsValue, { color: brandColor }]}>
                                -{((fuel.basePrice || 0) - (fuel.discountPrice || 0)).toFixed(2)}
                            </Text>
                            <Text allowFontScaling={false} style={[styles.savingsUnit, { color: brandColor }]}>₴/L</Text>
                        </Animated.View>
                    </View>
                </Animated.View>
            </Pressable>
        </Animated.View>
    );
}

const styles = StyleSheet.create({
    header: {
        paddingTop: 8,
        paddingBottom: 24,
        paddingHorizontal: 0,
    },
    topNav: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 0,
        marginHorizontal: 4,
        marginBottom: 12,
    },
    headerCenter: {
        flex: 1,
        alignItems: 'center',
    },
    backBtn: {
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
    titleArea: {
        alignItems: 'center',
        marginBottom: 32,
    },
    stationTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 32,
        lineHeight: 38,
        letterSpacing: -0.5,
        textAlign: 'center',
        textTransform: 'uppercase',
    },
    statusRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 6,
        marginTop: 4,
    },
    statusDot: {
        width: 5,
        height: 5,
        borderRadius: 2.5,
    },
    statusText: {
        fontFamily: 'Inter-Black',
        color: tokens.colors.primary,
        fontSize: 8,
        letterSpacing: 2,
        textTransform: 'uppercase',
        opacity: 0.6,
    },
    protocolBanner: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
        paddingHorizontal: 4,
    },
    protocolLine: {
        flex: 1,
        height: 1,
        backgroundColor: tokens.colors.primary,
        opacity: 0.15,
    },
    protocolContent: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
        backgroundColor: 'rgba(0, 255, 106, 0.05)',
        paddingHorizontal: 12,
        paddingVertical: 6,
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 106, 0.2)',
        borderRadius: 2,
    },
    protocolBannerText: {
        fontFamily: 'Inter-Black',
        color: tokens.colors.primary,
        fontSize: 8,
        letterSpacing: 2,
        textTransform: 'uppercase',
    },
    content: {
        paddingHorizontal: 0,
        paddingTop: 10,
    },
    sectionLabel: {
        fontFamily: 'Inter-Black',
        color: 'rgba(255, 255, 255, 0.15)',
        fontSize: 9,
        letterSpacing: 3,
        textAlign: 'center',
        marginBottom: 24,
        textTransform: 'uppercase',
    },
    fuelGrid: {
        gap: 16,
    },
    fuelCard: {
        height: 104,
        backgroundColor: '#000',
        borderWidth: 1,
        borderColor: 'rgba(255, 255, 255, 0.08)',
        borderRadius: 2,
        flexDirection: 'row',
        overflow: 'hidden',
    },
    fuelAccent: {
        width: 4,
        height: '100%',
        backgroundColor: tokens.colors.primary,
    },
    fuelCardContent: {
        flex: 1,
        flexDirection: 'row',
        paddingLeft: 16,
        paddingRight: 10,
        alignItems: 'center',
        justifyContent: 'space-between',
    },
    fuelMainInfo: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 16,
    },
    iconBox: {
        width: 48,
        height: 48,
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.05)',
        backgroundColor: 'rgba(255,255,255,0.03)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    fuelNameStack: {
        gap: 2,
    },
    fuelName: {
        fontFamily: 'Rajdhani-Bold',
        color: '#FFF',
        fontSize: 28,
        letterSpacing: -0.5,
        textTransform: 'uppercase',
    },
    priceMiniRow: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 8,
    },
    basePriceMini: {
        fontFamily: 'Inter-Bold',
        color: 'rgba(255, 255, 255, 0.2)',
        fontSize: 12,
        textDecorationLine: 'line-through',
    },
    discountPriceMini: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 22,
    },
    savingsBadge: {
        width: 100,
        height: 48,
        backgroundColor: 'rgba(0, 255, 106, 0.1)',
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 106, 0.3)',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
    },
    savingsValue: {
        fontFamily: 'Rajdhani-Bold',
        color: tokens.colors.primary,
        fontSize: 20,
        lineHeight: 20,
    },
    savingsUnit: {
        fontFamily: 'Inter-Black',
        color: tokens.colors.primary,
        fontSize: 8,
        marginTop: -2,
        opacity: 0.8,
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
        fontSize: 10,
        fontFamily: 'Inter-Black',
    },
    footerBranding: {
        marginTop: 60,
        paddingBottom: 40,
        alignItems: 'center',
        opacity: 0.2,
    },
    footerProtocolText: {
        fontFamily: 'Inter-Black',
        color: tokens.colors.primary,
        fontSize: 9,
        letterSpacing: 4,
    }
});
