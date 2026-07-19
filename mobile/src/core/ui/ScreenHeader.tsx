import React from 'react';
import { View, Text, Pressable, StyleSheet } from 'react-native';
import { ChevronLeft } from 'lucide-react-native';
import { useRouter } from 'expo-router';
import { useDesignTokens } from '../hooks/useTheme';

interface ScreenHeaderProps {
  title: string;
  subtitle?: string;
  showBack?: boolean;
  rightElement?: React.ReactNode;
}

export function ScreenHeader({
  title,
  subtitle,
  showBack = true,
  rightElement,
}: ScreenHeaderProps) {
  const router = useRouter();
  const tokens = useDesignTokens();

  return (
    <View style={[styles.header, { paddingHorizontal: tokens.spacing.containerPadding }]}>
      <View style={styles.topRow}>
        {showBack ? (
          <Pressable
            onPress={() => router.back()}
            style={[styles.iconBox, { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight }]}
          >
            <ChevronLeft size={28} color={tokens.colors.text.primary} />
          </Pressable>
        ) : (
          <View style={{ width: 56 }} />
        )}

        <View style={styles.center}>
          <Text
            allowFontScaling={false}
            style={[styles.title, { color: tokens.colors.primary }]}
          >
            {title}
          </Text>
          {subtitle && (
            <Text
              allowFontScaling={false}
              style={[styles.subtitle, { color: tokens.colors.text.dim }]}
            >
              {subtitle}
            </Text>
          )}
        </View>

        {rightElement || <View style={{ width: 56 }} />}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  header: {
    paddingTop: 8,
    paddingBottom: 24,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  center: {
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
  title: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 32,
    lineHeight: 38,
    letterSpacing: -1,
    textTransform: 'uppercase',
  },
  subtitle: {
    fontFamily: 'Inter-Black',
    fontSize: 8,
    letterSpacing: 4,
    textTransform: 'uppercase',
    marginTop: 2,
    opacity: 0.6,
  },
});
