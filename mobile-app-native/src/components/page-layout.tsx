import React from "react";
import { View, ScrollView, StyleSheet, Platform } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { cn } from "../lib/utils";

interface PageLayoutProps {
    children: React.ReactNode;
    header?: React.ReactNode;
    fixedFooter?: React.ReactNode;
    background?: React.ReactNode;
    className?: string;
    scrollClassName?: string;
    disableScroll?: boolean;
}

export function PageLayout({
    children,
    header,
    fixedFooter,
    background,
    className,
    scrollClassName,
    disableScroll = false
}: PageLayoutProps) {
    const ContentWrapper = disableScroll ? View : ScrollView;

    return (
        <SafeAreaView className={cn("flex-1 bg-black relative", className)} edges={['top', 'left', 'right']}>
            {/* Fixed Background Region */}
            {background && (
                <View className="absolute inset-0 z-0">
                    {background}
                </View>
            )}

            {/* Fixed Header Region */}
            {header && (
                <View className="z-40 relative">
                    {header}
                </View>
            )}

            {/* Scrollable Content Region */}
            <ContentWrapper
                className={cn("flex-1 relative", scrollClassName)}
                contentContainerStyle={!disableScroll ? { paddingBottom: 100 } : undefined}
                showsVerticalScrollIndicator={false}
            >
                {children}
            </ContentWrapper>

            {/* Fixed Footer Region */}
            {fixedFooter && (
                <SafeAreaView edges={['bottom']} className="z-50 relative bg-black border-t border-white/10 shadow-lg">
                    {fixedFooter}
                </SafeAreaView>
            )}
        </SafeAreaView>
    );
}
