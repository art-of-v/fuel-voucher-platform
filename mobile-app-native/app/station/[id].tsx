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
import Svg, { Rect, Defs, Pattern, Path, LinearGradient, Stop } from 'react-native-svg';

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
                <Pressable onPress={() => router.back()} style={styles.iconBox}>
                    <ChevronLeft size={28} color="#FFF" />
                </Pressable>

                <View style={styles.headerCenter}>
                    <GlowText intensity="high" animation="pulse" animatedValue={pulseAnim} color={brandColor} glowColor={brandColor} style={styles.stationTitle}>
                        {station.logoText || station.name}
                    </GlowText>
                </View>

                <View style={{ width: 56 }} />
            </View>
        </View>
    );

    return (
        <PageLayout header={Header}>
            <View style={{ paddingHorizontal: GLOBAL_PADDING }}>
                <View style={styles.content}>
                    <View style={styles.fuelGrid}>
                        {station.fuels
                            .slice()
                            .sort((a, b) => {
                                const getPriority = (name: string) => {
                                    const n = name.toUpperCase();
                                    if (n.includes('ДП')) return 1;
                                    if (n.includes('ГАЗ') || n.includes('LPG')) return 3;
                                    if (n.includes('ADBLUE')) return 4;
                                    return 2; // Default for Gasoline (A-95, etc)
                                };
                                return getPriority(a.name) - getPriority(b.name);
                            })
                            .map((fuel, index) => (
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

                    <View style={styles.footerBranding} />
                </View>
            </View>
        </PageLayout>
    );
}

const MeshBackground = ({ id = "honeycomb" }) => (
    <View style={StyleSheet.absoluteFill}>
        <Svg height="100%" width="100%">
            <Defs>
                <Pattern id={id} width="20" height="34" patternUnits="userSpaceOnUse">
                    <Path
                        d="M10 0 L20 5.8 L20 17.4 L10 23.2 L0 17.4 L0 5.8 Z M10 34 L20 28.2 L20 11.6 L10 17.4 L0 11.6 L0 28.2 Z"
                        fill="#000"
                        stroke="#1a1a1a"
                        strokeWidth="1"
                    />
                </Pattern>
                {/* Glossy Lacquer Sheen */}
                <LinearGradient id={`${id}-lacquer`} x1="0%" y1="0%" x2="100%" y2="100%">
                    <Stop offset="0%" stopColor="#fff" stopOpacity={0.12} />
                    <Stop offset="45%" stopColor="#fff" stopOpacity={0.02} />
                    <Stop offset="48%" stopColor="#fff" stopOpacity={0.15} />
                    <Stop offset="50%" stopColor="#fff" stopOpacity={0.02} />
                    <Stop offset="100%" stopColor="#000" stopOpacity={0.5} />
                </LinearGradient>
                {/* Specular Rim Light */}
                <LinearGradient id={`${id}-rim`} x1="0%" y1="0%" x2="0%" y2="100%">
                    <Stop offset="0%" stopColor="#fff" stopOpacity={0.2} />
                    <Stop offset="5%" stopColor="#fff" stopOpacity={0} />
                    <Stop offset="95%" stopColor="#000" stopOpacity={0} />
                    <Stop offset="100%" stopColor="#fff" stopOpacity={0.1} />
                </LinearGradient>
            </Defs>
            <Rect width="100%" height="100%" fill="#050505" />
            <Rect width="100%" height="100%" fill={`url(#${id})`} />
            <Rect width="100%" height="100%" fill={`url(#${id}-lacquer)`} />
            <Rect width="100%" height="100%" fill={`url(#${id}-rim)`} />
        </Svg>
        <View style={{ position: 'absolute', top: 0, left: 0, right: 0, height: 1.5, backgroundColor: 'rgba(255,255,255,0.15)' }} />
        <View style={{ position: 'absolute', bottom: 0, left: 0, right: 0, height: 1.5, backgroundColor: 'rgba(0,0,0,0.8)' }} />
    </View>
);

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
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);

        Animated.parallel([
            Animated.spring(scaleAnim, { toValue: 0.99, useNativeDriver: true, friction: 10, tension: 50 }),
            Animated.spring(tiltX, { toValue: 1, useNativeDriver: true, friction: 10, tension: 50 }),
            Animated.spring(contentMove, { toValue: 3, useNativeDriver: true, friction: 10, tension: 50 })
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
        <Animated.View style={{ opacity: entranceAnim, transform: [{ translateY }] }}>
            <Pressable
                onPressIn={handlePressIn}
                onPressOut={handlePressOut}
                onPress={() => {
                    selectStation(station);
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
                    <MeshBackground id={`honeycomb-${index}`} />
                    <View style={[styles.fuelAccent, { backgroundColor: brandColor, width: ACCENT_WIDTH }]} />
                    <View style={styles.fuelCardContent}>
                        <View style={styles.fuelMainInfo}>
                            <View style={[styles.fuelIconBox, { backgroundColor: brandColor }]}>
                                <Fuel size={24} color="#000" />
                            </View>
                            <View style={styles.fuelTextStack}>
                                <Text allowFontScaling={false} numberOfLines={1} style={styles.fuelName}>{fuel.name}</Text>
                                <View style={styles.priceColumn}>
                                    <Text allowFontScaling={false} style={styles.basePriceMini}>{(fuel.basePrice || 0).toFixed(2)}</Text>
                                    <GlowText intensity="high" color={brandColor} glowColor={brandColor} style={styles.discountPriceMini}>
                                        {(fuel.discountPrice || 0).toFixed(2)} ₴
                                    </GlowText>
                                </View>
                            </View>
                        </View>

                        <Animated.View style={[styles.savingsBadge, {
                            backgroundColor: `${brandColor}22`,
                            borderColor: `${brandColor}44`,
                        }]}>
                            <View style={styles.savingsRow}>
                                <Text allowFontScaling={false} style={[styles.savingsValue, { color: brandColor }]}>
                                    -{((fuel.basePrice || 0) - (fuel.discountPrice || 0)).toFixed(2)}
                                </Text>
                                <Text allowFontScaling={false} style={[styles.savingsUnit, { color: brandColor }]}> ₴/L</Text>
                            </View>
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
        paddingHorizontal: GLOBAL_PADDING,
    },
    topNav: {
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 0,
        marginBottom: 12,
    },
    headerCenter: {
        flex: 1,
        alignItems: 'center',
    },
    iconBox: {
        width: 56, // Larger container
        height: 56, // Larger container
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
    stationTitle: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 42,
        lineHeight: 48,
        letterSpacing: -1,
        textAlign: 'center',
        textTransform: 'uppercase',
    },
    content: {
        paddingHorizontal: 0,
        paddingTop: 10,
    },
    fuelGrid: {
        gap: 16,
    },
    fuelCard: {
        height: 118,
        width: '100%',
        backgroundColor: '#0c0c0c',
        borderWidth: 1,
        borderColor: 'rgba(0, 0, 0, 0.9)',
        borderRadius: 4,
        flexDirection: 'row',
        overflow: 'hidden',
    },
    fuelAccent: {
        width: 4,
        height: '100%',
    },
    fuelCardContent: {
        flex: 1,
        flexDirection: 'row',
        paddingHorizontal: 12,
        alignItems: 'center',
        justifyContent: 'space-between',
    },
    fuelMainInfo: {
        flex: 1,
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
    },
    fuelIconBox: {
        width: 44,
        height: 44,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 2,
    },
    fuelTextStack: {
        flex: 1,
        flexShrink: 1,
        gap: 2,
    },
    priceColumn: {
        flexDirection: 'row',
        alignItems: 'baseline',
        gap: 10,
    },
    fuelName: {
        fontFamily: 'Inter-Black',
        color: '#94A3B8',
        fontSize: 16,
        letterSpacing: -0.2,
        textTransform: 'uppercase',
    },

    basePriceMini: {
        fontFamily: 'Inter-Bold',
        color: 'rgba(255, 255, 255, 0.55)',
        fontSize: 16,
        textDecorationLine: 'line-through',
    },
    discountPriceMini: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 28,
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
        flexDirection: 'row',
    },
    savingsRow: {
        flexDirection: 'row',
        alignItems: 'baseline',
    },
    savingsValue: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 26,
    },
    savingsUnit: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        opacity: 0.9,
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
    badgeActive: {
        backgroundColor: "#EF4444",
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
