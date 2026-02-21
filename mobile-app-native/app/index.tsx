/// <reference types="nativewind/types" />
import { View, Text, StyleSheet, Image, Pressable, Dimensions, Animated, ActivityIndicator } from "react-native";
import { useRouter, Redirect } from "expo-router";
import { ArrowRight, MapPin, AlertTriangle, Zap } from "lucide-react-native";
import { useStations } from "@/hooks/useStations";
import { PageLayout } from "@/components/page-layout";
import { tokens } from "@/lib/design-tokens";
import { GlowText } from "@/components/glow-text";

import { useStore } from "@/lib/store";
import { useI18n } from "@/lib/i18n";
import { useEffect, useRef, useMemo } from "react";
import { Haptics } from "@/lib/haptics";
import { useAuth } from "../src/hooks/useAuth";

const SCREEN_WIDTH = Dimensions.get('window').width;
const GLOBAL_PADDING = tokens.spacing.containerPadding;
const ACCENT_WIDTH = 12;
const CONTENT_WIDTH = SCREEN_WIDTH - (GLOBAL_PADDING * 2) - ACCENT_WIDTH;

export default function HomeScreen() {
    const router = useRouter();
    const { data: stations, isLoading: stationsLoading } = useStations();
    const { isAuthenticated, isLoading: authLoading } = useAuth();
    const pulseAnim = useRef(new Animated.Value(1)).current;
    const { t } = useI18n();

    // Sort stations by priority: OKKO, WOG, UPG, KLO
    const sortedStations = useMemo(() => {
        if (!stations) return [];
        const PRIORITY_ORDER = ['okko', 'wog', 'upg', 'klo'];

        return [...stations].sort((a, b) => {
            const indexA = PRIORITY_ORDER.indexOf(a.id.toLowerCase());
            const indexB = PRIORITY_ORDER.indexOf(b.id.toLowerCase());

            // If both found, sort by index
            if (indexA !== -1 && indexB !== -1) return indexA - indexB;
            // If only A found, it comes first
            if (indexA !== -1) return -1;
            // If only B found, it comes first
            if (indexB !== -1) return 1;
            // Otherwise keep original order
            return 0;
        });
    }, [stations]);

    useEffect(() => {
        Animated.loop(
            Animated.sequence([
                Animated.timing(pulseAnim, { toValue: 0.6, duration: 2000, useNativeDriver: true }),
                Animated.timing(pulseAnim, { toValue: 1, duration: 2000, useNativeDriver: true }),
            ])
        ).start();
    }, []);

    if (authLoading) {
        return (
            <View style={[styles.container, { flex: 1, backgroundColor: '#000', justifyContent: 'center', alignItems: 'center' }]}>
                <ActivityIndicator color={tokens.colors.primary} />
            </View>
        );
    }

    if (!isAuthenticated) {
        return <Redirect href="/landing" />;
    }

    const headerComponent = (
        <View style={styles.header}>
            <View style={styles.brandMain}>

                {/* ROW 1: LOGO + TITLES */}
                <View style={styles.topRow}>
                    <View style={styles.logoContainer}>
                        <Animated.View style={[styles.logoSlot, { opacity: pulseAnim }]}>
                            {/* THIN BASE BORDER */}
                            <View style={styles.reticleBase} />

                            {/* TECHNICAL CORNER ACCENTS */}
                            <View style={[styles.corner, styles.topLeft, { borderColor: tokens.colors.primary }]} />
                            <View style={[styles.corner, styles.topRight, { borderColor: tokens.colors.primary }]} />
                            <View style={[styles.corner, styles.bottomLeft, { borderColor: tokens.colors.primary }]} />
                            <View style={[styles.corner, styles.bottomRight, { borderColor: tokens.colors.primary }]} />

                            <View style={styles.logoInner}>
                                <Image
                                    source={require('../assets/adaptive-icon.png')}
                                    style={styles.logoImg}
                                    resizeMode="contain"
                                />
                            </View>
                        </Animated.View>
                    </View>

                    <View style={styles.brandTitle}>
                        <GlowText intensity="high" align="left" animation="pulse" animatedValue={pulseAnim} style={styles.lembergText}>LEMBERG</GlowText>
                        <Text allowFontScaling={false} style={styles.subtitleText}>FUEL CORP.</Text>

                        {/* UNIFIED HORIZONTAL DIVIDER - FADE TO TEXT */}
                        <View style={styles.brandDivider}>
                            {/* LEFT SIDE: Solid -> Fade Out */}
                            <View style={styles.dividerFlex}>
                                <View style={[styles.lineSolid, { shadowColor: tokens.colors.primary }]} />
                                {/* Stepped Gradient Simulation for Robustness */}
                                <View style={[styles.fadeStep, { opacity: 0.8 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.6 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.4 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.2 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.1 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.05 }]} />
                            </View>

                            <Text allowFontScaling={false} style={styles.taglineText}>DOMINATE</Text>

                            {/* RIGHT SIDE: Fade In -> Solid */}
                            <View style={styles.dividerFlex}>
                                <View style={[styles.fadeStep, { opacity: 0.05 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.1 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.2 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.4 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.6 }]} />
                                <View style={[styles.fadeStep, { opacity: 0.8 }]} />
                                <View style={[styles.lineSolid, { shadowColor: tokens.colors.primary }]} />
                            </View>
                        </View>
                    </View>
                </View>
                {/* ROW 2: PROTOCOL BANNER */}
                <View style={{ width: '100%', alignItems: 'flex-start', marginBottom: 16, paddingLeft: 0 }}>
                    <GlowText intensity="none" align="left" animation="pulse" animatedValue={pulseAnim} color="#FFF" style={styles.bannerLabel}>{t('stations.title')}</GlowText>
                    <GlowText intensity="high" align="left" animation="pulse" animatedValue={pulseAnim} style={styles.bannerLabel}>{t('stations.title2')}</GlowText>
                </View>

            </View>
        </View>
    );

    return (
        <PageLayout header={headerComponent}>
            <View style={[styles.container, { paddingHorizontal: GLOBAL_PADDING }]}>

                {/* 4. STATION PROTOCOLS */}
                {(!stations || stations.length === 0) && (
                    <View style={{ height: 100, alignItems: 'center', justifyContent: 'center' }}>
                        <Text style={{ color: 'rgba(255,255,255,0.5)' }}>{t('stations.initializing')}</Text>
                    </View>
                )}

                <View style={styles.stationGrid}>
                    {sortedStations.map((station, index) => (
                        <StationButton
                            key={station.id}
                            index={index}
                            station={station}
                            router={router}
                            t={t}
                            globalPulse={pulseAnim}
                        />
                    ))}
                </View>
            </View>
        </PageLayout>
    );
}

// Unified "The Parallax Depth" Interactive Button Component with Haptics
function StationButton({ station, router, t, globalPulse, index }: any) {
    const brandColor = (tokens.colors.text.brand as any)[station.id] || tokens.colors.primary;

    // Animation Values
    const scaleAnim = useRef(new Animated.Value(1)).current;
    const tiltX = useRef(new Animated.Value(0)).current;
    const contentMove = useRef(new Animated.Value(0)).current;
    const entranceAnim = useRef(new Animated.Value(0)).current;

    useEffect(() => {
        Animated.spring(entranceAnim, {
            toValue: 1,
            useNativeDriver: true,
            delay: index * 100,
            friction: 8,
            tension: 40,
        }).start();
    }, []);

    const translateY = entranceAnim.interpolate({
        inputRange: [0, 1],
        outputRange: [20, 0]
    });

    const handlePressIn = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);

        Animated.parallel([
            Animated.spring(scaleAnim, {
                toValue: 0.985,
                useNativeDriver: true,
                friction: 10,
                tension: 100,
            }),
            Animated.spring(tiltX, {
                toValue: 1,
                useNativeDriver: true,
                friction: 10,
                tension: 100,
            }),
            Animated.spring(contentMove, {
                toValue: 4, // Subtle Parallax
                useNativeDriver: true,
                friction: 10,
                tension: 100,
            })
        ]).start();
    };

    const handlePressOut = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);

        Animated.parallel([
            Animated.spring(scaleAnim, {
                toValue: 1,
                useNativeDriver: true,
                friction: 3,
                tension: 100,
            }),
            Animated.spring(tiltX, {
                toValue: 0,
                useNativeDriver: true,
            }),
            Animated.spring(contentMove, {
                toValue: 0,
                useNativeDriver: true,
                friction: 3,
                tension: 100,
            })
        ]).start();
    };

    const rotateX = tiltX.interpolate({
        inputRange: [0, 1],
        outputRange: ['0deg', '3deg']
    });

    return (
        <Animated.View style={{
            opacity: entranceAnim,
            transform: [
                { perspective: 1000 },
                { translateY: translateY }
            ]
        }}>
            <Pressable
                onPressIn={handlePressIn}
                onPressOut={handlePressOut}
                onPress={() => {
                    setTimeout(() => {
                        router.push(`/station/${station.id}`);
                    }, 130);
                }}
                style={{ marginBottom: 16 }}
            >
                {({ pressed }) => (
                    <View style={{ transform: [{ perspective: 1000 }] }}>
                        <Animated.View style={[
                            styles.stationCard,
                            {
                                marginBottom: 0,
                                transform: [
                                    { scale: scaleAnim },
                                    { rotateX: rotateX }
                                ],
                                backgroundColor: pressed ? `${brandColor}44` : '#000',
                                borderColor: pressed ? brandColor : 'rgba(255,255,255,0.08)',
                                borderWidth: pressed ? 2 : 1,
                                shadowColor: brandColor,
                                shadowOpacity: pressed ? 0.6 : 0,
                                shadowRadius: 15,
                                elevation: pressed ? 12 : 0,
                            }
                        ]}>
                            <View style={{ width: ACCENT_WIDTH, height: '100%', backgroundColor: brandColor }} />

                            <View style={{
                                flex: 1,
                                flexDirection: 'row',
                                alignItems: 'center',
                                paddingLeft: 12,
                                paddingRight: 24,
                                justifyContent: 'space-between',
                                height: '100%'
                            }}>
                                <Animated.View style={{
                                    justifyContent: 'center',
                                    transform: [{ translateX: contentMove }]
                                }}>
                                    <View style={{ flexDirection: 'row', alignItems: 'center', marginBottom: 4 }}>
                                        <Text allowFontScaling={false} style={[styles.cardName, { color: brandColor }]}>{station.logoText || station.name || 'UNKNOWN'}</Text>
                                        <Zap size={14} color={brandColor} style={{ marginTop: 2, marginLeft: 8 }} />
                                    </View>
                                    <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                                        <Animated.View style={[styles.statusPip, { backgroundColor: brandColor, opacity: globalPulse }]} />
                                        <Text allowFontScaling={false} style={styles.statusLabel}>{t('stations.onlineReady')}</Text>
                                    </View>
                                </Animated.View>

                                <Animated.View style={[
                                    styles.cardArrow,
                                    { transform: [{ translateX: Animated.multiply(contentMove, -1) }] }
                                ]}>
                                    <ArrowRight size={20} color={brandColor} />
                                </Animated.View>
                            </View>
                        </Animated.View>
                    </View>
                )}
            </Pressable>
        </Animated.View>
    );
}

const styles = StyleSheet.create({
    container: {
        paddingTop: 0, // Adjusted because header is now static
        width: '100%',
        paddingHorizontal: 0,
    },
    header: {
        width: '100%',
        marginBottom: 20,
        alignItems: 'flex-start',
    },
    brandMain: {
        width: '100%',
        flexDirection: 'column',
        alignItems: 'flex-start',
        marginBottom: 10,
    },
    topRow: {
        flexDirection: 'row',
        alignItems: 'flex-start',
        justifyContent: 'flex-start',
        width: '100%',
        marginBottom: 20,
    },
    logoContainer: {
        width: 110,
        height: 110,
        marginRight: 20,
    },
    logoSlot: {
        width: '100%',
        height: '100%',
        padding: 6,
        justifyContent: 'center',
        alignItems: 'center',
        position: 'relative',
    },
    reticleBase: {
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        borderWidth: 1,
        borderColor: 'rgba(0, 255, 102, 0.3)',
    },
    logoInner: {
        width: '100%',
        height: '100%',
        backgroundColor: '#000',
        padding: 4,
    },
    logoImg: {
        width: '100%',
        height: '100%',
        // Removed tintColor to see original image
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 1,
        shadowRadius: 20,
    },
    corner: {
        position: 'absolute',
        width: 20,
        height: 20,
        borderWidth: 5,
        zIndex: 10,
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 0.9,
        shadowRadius: 8,
        elevation: 8,
    },
    topLeft: {
        top: 0,
        left: 0,
        borderRightWidth: 0,
        borderBottomWidth: 0,
    },
    topRight: {
        top: 0,
        right: 0,
        borderLeftWidth: 0,
        borderBottomWidth: 0,
    },
    bottomLeft: {
        bottom: 0,
        left: 0,
        borderRightWidth: 0,
        borderTopWidth: 0,
    },
    bottomRight: {
        bottom: 0,
        right: 0,
        borderLeftWidth: 0,
        borderTopWidth: 0,
    },
    brandTitle: {
        justifyContent: 'flex-start', // Top-align with logo
        alignItems: 'flex-start',
        flex: 1,
        paddingTop: 8, // Optical alignment with logo crown
    },
    lembergText: {
        color: tokens.colors.primary,
        fontFamily: 'Rajdhani-Bold',
        fontSize: 36,
        letterSpacing: 4,
        lineHeight: 44,
        marginBottom: -2,
    },
    subtitleText: {
        color: '#FFF',
        fontFamily: 'Rajdhani',
        fontSize: 12,
        letterSpacing: 15, // Optimal wide track for mobile width
        opacity: 0.9,
        textAlign: 'left',
        marginBottom: 4, // Tight cluster
    },
    brandDivider: {
        width: '100%',
        flexDirection: 'row',
        alignItems: 'center',
        marginTop: 4,
    },
    dividerFlex: {
        flex: 1, // Forces equal width on both sides
        flexDirection: 'row',
        alignItems: 'center',
    },
    lineSolid: {
        flex: 1,
        height: 2,
        backgroundColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 1,
        shadowRadius: 4,
        opacity: 0.9,
    },
    fadeStep: {
        width: 4, // Small steps for smooth gradient
        height: 2,
        backgroundColor: tokens.colors.primary,
    },
    taglineText: {
        color: tokens.colors.primary,
        fontFamily: 'Inter-Black',
        fontSize: 8,
        letterSpacing: 8,
        textTransform: 'uppercase',
        marginHorizontal: 4, // Minimal gap, lines "touch" via fade
        opacity: 0.8,
        textAlign: 'center',
        includeFontPadding: false,
        textAlignVertical: 'center',
    },
    bannerLabel: {
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 28, // Scaled for hierarchy
        letterSpacing: 0,
        textAlign: 'left',
        lineHeight: 30,
        color: tokens.colors.primary,
        marginBottom: -2,
    },
    stationGrid: {
        width: '100%',
        marginBottom: 20,
    },
    stationCard: {
        width: '100%',
        height: 104,
        backgroundColor: '#000', // Absolute black integration
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.08)',
        borderRadius: 2,
        flexDirection: 'row',
        marginBottom: 16, // Rhythm matching web
        overflow: 'hidden',
    },
    cardName: {
        color: '#FFF',
        fontFamily: 'Rajdhani-Bold',
        fontSize: 32, // More optical thickness
        textTransform: 'uppercase',
        letterSpacing: -0.5, // Tight web rhythm
        lineHeight: 32,
    },
    statusPip: {
        width: 6,
        height: 6,
        borderRadius: 3,
        marginRight: 6,
    },
    statusLabel: {
        color: 'rgba(255,255,255,0.4)',
        fontFamily: 'Inter-Bold',
        fontSize: 10,
        letterSpacing: 2,
        lineHeight: 14,
    },
    cardArrow: {
        width: 48,
        height: 48,
        borderWidth: 1,
        borderColor: tokens.colors.primary,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 2,
        backgroundColor: 'rgba(0, 255, 106, 0.05)',
    },
    footer: {
        marginTop: 40,
        marginBottom: 40,
        alignItems: 'center',
    },
    vText: {
        color: 'rgba(255,255,255,0.1)',
        fontFamily: 'Inter-Bold',
        fontSize: 9,
        letterSpacing: 2,
    }
});
