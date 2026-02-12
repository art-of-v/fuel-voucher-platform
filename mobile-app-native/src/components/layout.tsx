import React from 'react';
import { View, Image, TouchableOpacity, Text } from 'react-native';
import { Link, usePathname } from 'expo-router';
import { User, ShoppingCart, Home, QrCode } from 'lucide-react-native';
import { useStore } from '@/lib/store';
import lionLogo from '@assets/generated_images/profile_cyberpunk_lion_logo.png';
import { cn } from '@/lib/utils';

export default function Layout({ children }: { children: React.ReactNode }) {
    const pathname = usePathname();
    const getCartItemCount = useStore((state) => state.getCartItemCount);
    const cartCount = getCartItemCount();

    const isActive = (path: string) => {
        if (path === '/') return pathname === '/' || pathname === '/index';
        return pathname.startsWith(path);
    };

    return (
        <View className="flex-1 bg-[#050505] relative">
            {/* Background lion logo watermark */}
            <View className="absolute inset-0 z-0 items-center justify-center opacity-[0.05] pointer-events-none">
                <Image
                    source={lionLogo}
                    className="w-[500px] h-[500px] saturate-0 contrast-200 mix-blend-screen"
                    style={{ width: 500, height: 500, objectFit: 'contain' } as any}
                />
            </View>
            <View className="absolute bottom-0 right-0 w-[400px] h-[400px] opacity-[0.12] z-0 pointer-events-none" style={{ width: 400, height: 400 }}>
                <Image
                    source={lionLogo}
                    className="w-full h-full"
                    style={{ width: '100%', height: '100%', objectFit: 'contain', filter: 'grayscale(100%)' } as any}
                />
            </View>

            {/* Main Content */}
            <View className="flex-1 z-10 pb-20">
                {children}
            </View>

            {/* Floating Navigation */}
            <View className="absolute bottom-6 left-6 right-6 z-50">
                <View className="flex-row justify-between items-center px-6 py-4">
                    <Link href="/" asChild>
                        <TouchableOpacity className="items-center justify-center">
                            <Home
                                size={24}
                                color={isActive('/') ? '#00FF80' : '#6b7280'}
                            />
                        </TouchableOpacity>
                    </Link>

                    <Link href="/basket" asChild>
                        <TouchableOpacity className="items-center justify-center relative">
                            <ShoppingCart
                                size={24}
                                color={isActive('/basket') ? '#00FF80' : '#6b7280'}
                            />
                            {cartCount > 0 && (
                                <View className="absolute -top-2 -right-2 w-5 h-5 bg-red-500 rounded-full items-center justify-center">
                                    <Text className="text-white text-[10px] font-bold">
                                        {cartCount > 9 ? '9+' : cartCount}
                                    </Text>
                                </View>
                            )}
                        </TouchableOpacity>
                    </Link>

                    <Link href="/my-codes" asChild>
                        <TouchableOpacity className="items-center justify-center">
                            <QrCode
                                size={24}
                                color={isActive('/my-codes') ? '#00FF80' : '#6b7280'}
                            />
                        </TouchableOpacity>
                    </Link>

                    <Link href="/profile" asChild>
                        <TouchableOpacity className="items-center justify-center">
                            <User
                                size={24}
                                color={isActive('/profile') ? '#00FF80' : '#6b7280'}
                            />
                        </TouchableOpacity>
                    </Link>
                </View>
            </View>
        </View>
    );
}
