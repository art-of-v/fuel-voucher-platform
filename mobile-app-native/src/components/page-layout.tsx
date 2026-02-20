import { View, ScrollView, StyleSheet, Platform } from "react-native";
import { SafeAreaView, useSafeAreaInsets } from "react-native-safe-area-context";
import { cn } from "../lib/utils";
import { GridBackground } from "./grid-background";

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
    const insets = useSafeAreaInsets();
    const ContentWrapper = disableScroll ? View : ScrollView;

    return (
        <SafeAreaView
            className={cn("flex-1 bg-black relative", className)}
            edges={['top', 'left', 'right']}
            style={{ flex: 1, backgroundColor: '#000' }}
        >
            {/* Fixed Background Region */}
            <View className="absolute inset-0 z-0" style={{ position: 'absolute', top: 0, left: 0, right: 0, bottom: 0 }}>
                {background || <GridBackground />}
            </View>

            {/* Fixed Header Region */}
            {header && (
                <View className="z-40 relative" style={{ zIndex: 40 }}>
                    {header}
                </View>
            )}

            {/* Scrollable Content Region */}
            <ContentWrapper
                className={cn("flex-1 relative", scrollClassName)}
                style={{ flex: 1 }}
                contentContainerStyle={!disableScroll ? { paddingBottom: 150 } : undefined}
                showsVerticalScrollIndicator={false}
            >
                {children}
            </ContentWrapper>

            {/* Fixed Footer Region */}
            {fixedFooter && (
                <View style={styles.footerContainer}>
                    {fixedFooter}
                </View>
            )}
        </SafeAreaView>
    );
}

const styles = StyleSheet.create({
    footerContainer: {
        zIndex: 50,
        backgroundColor: '#000',
        borderTopWidth: 1,
        borderTopColor: 'rgba(255, 255, 255, 0.1)',
    }
});
