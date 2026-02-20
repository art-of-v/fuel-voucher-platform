/// <reference types="nativewind/types" />
import { View, Text, StyleSheet, Image, Pressable, Dimensions, Animated } from "react-native";
import React, { useRef } from "react";
import { useRouter } from "expo-router";
import { PageLayout } from "@/components/page-layout";
import { GlowText } from "@/components/glow-text";
import { tokens } from "@/lib/design-tokens";
import { useStore } from "@/lib/store";
import { useI18n } from "@/lib/i18n";
import { Haptics } from "@/lib/haptics";

const { width } = Dimensions.get('window');

export default function LandingScreen() {
    const router = useRouter();
    const login = useStore(state => state.login);
    const { t } = useI18n();

    const scaleAnim = useRef(new Animated.Value(1)).current;

    const handlePressIn = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        Animated.spring(scaleAnim, {
            toValue: 0.99,
            useNativeDriver: true,
            friction: 12,
            tension: 40
        }).start();
    };

    const handlePressOut = () => {
        Animated.spring(scaleAnim, {
            toValue: 1,
            useNativeDriver: true,
            friction: 12,
            tension: 100
        }).start();
    };

    const handleEnter = () => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
        login();
        router.replace('/');
    };

    return (
        <PageLayout>
            <View style={styles.content}>
                {/* 1. HERO LOGO */}
                <View style={styles.logoContainer}>
                    <View style={styles.logoFrame}>
                        <View style={styles.logoGlow} />
                        <Image
                            source={require('../assets/original_lion_watermark.png')}
                            style={styles.logoImage}
                            resizeMode="cover"
                        />
                    </View>
                </View>

                {/* 2. TITLE BLOCK */}
                <View style={styles.titleBlock}>
                    <Text allowFontScaling={false} style={styles.brandTitle}>LEMBERG</Text>
                    <Text allowFontScaling={false} style={styles.brandSubtitle}>FUEL CORP.</Text>
                    <View style={styles.demiurgBadge}>
                        <Text style={styles.demiurgText}>ДЕМІУРГ // SYSTEM</Text>
                    </View>
                </View>

                {/* 3. GLOWING TEXT */}
                <View style={styles.glowSection}>
                    <GlowText intensity="high" style={styles.glowTextMain}>{t('landing.secure')}</GlowText>
                    <GlowText intensity="high" style={styles.glowTextMain}>{t('landing.access')}</GlowText>
                </View>

                {/* 4. ACTION */}
                <Animated.View style={{ transform: [{ scale: scaleAnim }] }}>
                    <Pressable
                        onPressIn={handlePressIn}
                        onPressOut={handlePressOut}
                        onPress={handleEnter}
                        style={styles.enterButton}
                    >
                        <Text allowFontScaling={false} style={styles.btnText}>{t('landing.initialize')}</Text>
                    </Pressable>
                </Animated.View>

                <View style={styles.footer}>
                    <Text style={styles.footerText}>{t('landing.encrypted')}</Text>
                </View>
            </View>
        </PageLayout>
    );
}

const styles = StyleSheet.create({
    content: {
        flex: 1,
        alignItems: 'center',
        justifyContent: 'center',
        paddingHorizontal: 20,
        zIndex: 10,
    },
    logoContainer: {
        marginBottom: 40,
        shadowColor: tokens.colors.primary,
        shadowOpacity: 0.5,
        shadowRadius: 20,
        elevation: 10,
    },
    logoFrame: {
        width: 140,
        height: 140,
        borderWidth: 2,
        borderColor: tokens.colors.primary,
        padding: 4,
        backgroundColor: 'rgba(0,10,5,0.8)',
    },
    logoGlow: {
        ...StyleSheet.absoluteFillObject,
        backgroundColor: tokens.colors.primary,
        opacity: 0.1,
    },
    logoImage: {
        width: '100%',
        height: '100%',
        tintColor: tokens.colors.primary,
    },
    titleBlock: {
        alignItems: 'center',
        marginBottom: 60,
    },
    brandTitle: {
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 64,
        color: '#FFF',
        letterSpacing: -2,
        textAlign: 'center',
        lineHeight: 74,
    },
    brandSubtitle: {
        fontFamily: 'Inter-Black',
        fontSize: 10,
        color: tokens.colors.primary,
        letterSpacing: 12,
        marginTop: 4,
        opacity: 0.8,
        textAlign: 'center',
    },
    demiurgBadge: {
        marginTop: 32,
        borderWidth: 1,
        borderColor: 'rgba(255,255,255,0.1)',
        paddingHorizontal: 16,
        paddingVertical: 6,
        backgroundColor: '#000',
    },
    demiurgText: {
        fontFamily: 'Inter-Black',
        fontSize: 9,
        color: 'rgba(255,255,255,0.4)',
        letterSpacing: 4,
    },
    glowSection: {
        marginBottom: 60,
        alignItems: 'center',
    },
    glowTextMain: {
        fontFamily: tokens.typography.fonts.heading,
        fontSize: 32,
        color: tokens.colors.primary,
        letterSpacing: 4,
        lineHeight: 40,
    },
    enterButton: {
        width: '100%',
        maxWidth: 320,
        height: 64,
        backgroundColor: tokens.colors.primary,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 4,
        // Manual Glow
        shadowColor: tokens.colors.primary,
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 1,
        shadowRadius: 20,
        elevation: 10,
    },
    btnText: {
        fontFamily: 'Inter-Black',
        fontSize: 14,
        color: '#000',
        letterSpacing: 4,
        textTransform: 'uppercase',
    },
    footer: {
        position: 'absolute',
        bottom: 40,
    },
    footerText: {
        fontFamily: 'Inter-Bold',
        fontSize: 10,
        color: 'rgba(255,255,255,0.2)',
        letterSpacing: 1,
    }
});
