import React from 'react';
import { View, Pressable, Text, StyleSheet, Platform, Dimensions } from 'react-native';
import { Home, ShoppingCart, QrCode, User, MapPin } from 'lucide-react-native';
import { Link, usePathname } from 'expo-router';
import { useStore } from '../lib/store';
import { BlurView } from 'expo-blur';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { tokens } from '../lib/design-tokens';
import { Haptics } from '../lib/haptics';

const { width } = Dimensions.get('window');

export function BottomTabs() {
    const pathname = usePathname();
    const insets = useSafeAreaInsets();
    const getCartItemCount = useStore((state) => state.getCartItemCount);
    const cartCount = getCartItemCount ? getCartItemCount() : 0;

    const tabs = [
        { name: 'index', icon: Home, path: '/' },
        { name: 'map', icon: MapPin, path: '/map' },
        { name: 'basket', icon: ShoppingCart, path: '/basket', badge: cartCount },
        { name: 'my-codes', icon: QrCode, path: '/my-codes' },
        { name: 'profile', icon: User, path: '/profile' },
    ];

    const isActive = (path: string) => {
        if (path === '/' && pathname === '/') return true;
        if (path !== '/' && pathname.startsWith(path)) return true;
        return false;
    };

    return (
        <View style={[styles.outerContainer, { bottom: 8 }]}>
            <View style={styles.tabContainer}>
                {tabs.map((tab) => {
                    const active = isActive(tab.path);
                    const Icon = tab.icon;

                    return (
                        <Link key={tab.name} href={tab.path as any} asChild>
                            <Pressable
                                onPress={() => {
                                    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                                }}
                                style={styles.tabItem}
                            >
                                <View style={styles.iconWrapper}>
                                    <Icon
                                        size={24} // 24px from web
                                        color={active ? tokens.colors.primary : '#6B7280'} // Active green vs Muted gray
                                        strokeWidth={active ? 2 : 1.5}
                                    />
                                    {typeof tab.badge === 'number' && tab.badge > 0 && (
                                        <View style={styles.badge}>
                                            <Text allowFontScaling={false} style={styles.badgeText}>
                                                {tab.badge}
                                            </Text>
                                        </View>
                                    )}
                                    {active && (
                                        <View style={styles.activeGlow} />
                                    )}
                                </View>
                            </Pressable>
                        </Link>
                    );
                })}
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    outerContainer: {
        position: 'absolute',
        left: 24,
        right: 24,
        height: 64,
        zIndex: 100,
        backgroundColor: 'transparent',
    },
    tabContainer: {
        flex: 1,
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'space-around',
        paddingHorizontal: 12,
    },
    tabItem: {
        width: 56,
        height: '100%',
        alignItems: 'center',
        justifyContent: 'center',
    },
    iconWrapper: {
        position: 'relative',
        alignItems: 'center',
        justifyContent: 'center',
    },
    badge: {
        position: 'absolute',
        top: -6,
        right: -8,
        backgroundColor: '#EF4444',
        minWidth: 16,
        height: 16,
        borderRadius: 8,
        alignItems: 'center',
        justifyContent: 'center',
        paddingHorizontal: 4,
        zIndex: 10,
    },
    badgeText: {
        color: '#FFF',
        fontSize: 10,
        fontFamily: 'Inter-Black',
    },
    activeGlow: {
        position: 'absolute',
        top: -4,
        left: -4,
        right: -4,
        bottom: -4,
        zIndex: -1,
        backgroundColor: 'rgba(0, 255, 106, 0.1)',
        borderRadius: 12,
        shadowColor: tokens.colors.primary,
        shadowRadius: 10,
        shadowOpacity: 0.5,
    }
});
