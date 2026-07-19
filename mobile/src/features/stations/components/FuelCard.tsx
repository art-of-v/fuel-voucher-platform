import React, { useRef, useEffect } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { Fuel } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { Haptics } from '../../../core/utils/haptics';
import { MeshBackground, PressableScale } from '../../../core/ui';
import { GlowText } from '../../../components/glow-text';
import type { Station, FuelType } from '../../../core/types/api';
import { BRAND_COLORS } from '../../../core/design/tokens';

const ACCENT_WIDTH = 12;

interface FuelCardProps {
  fuel: FuelType;
  station: Station;
  index: number;
  onPress: (station: Station, fuel: FuelType) => void;
}

export function FuelCard({ fuel, station, index, onPress }: FuelCardProps) {
  const tokens = useDesignTokens();
  const brandColor = BRAND_COLORS[station.id] || tokens.colors.primary;

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
  }, [index, entranceAnim]);

  const handlePressIn = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    Animated.parallel([
      Animated.spring(scaleAnim, { toValue: 0.99, useNativeDriver: true, friction: 10, tension: 50 }),
      Animated.spring(tiltX, { toValue: 1, useNativeDriver: true, friction: 10, tension: 50 }),
      Animated.spring(contentMove, { toValue: 3, useNativeDriver: true, friction: 10, tension: 50 }),
    ]).start();
  };

  const handlePressOut = () => {
    Animated.parallel([
      Animated.spring(scaleAnim, { toValue: 1, useNativeDriver: true, friction: 3, tension: 100 }),
      Animated.spring(tiltX, { toValue: 0, useNativeDriver: true }),
      Animated.spring(contentMove, { toValue: 0, useNativeDriver: true, friction: 3, tension: 100 }),
    ]).start();
  };

  const rotateX = tiltX.interpolate({
    inputRange: [0, 1],
    outputRange: ['0deg', '8deg'],
  });

  const translateY = entranceAnim.interpolate({
    inputRange: [0, 1],
    outputRange: [20, 0],
  });

  const savings = (fuel.basePrice || 0) - (fuel.discountPrice || 0);

  return (
    <Animated.View style={{ opacity: entranceAnim, transform: [{ translateY }] }}>
      <Pressable
        onPressIn={handlePressIn}
        onPressOut={handlePressOut}
        onPress={() => {
          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
          setTimeout(() => onPress(station, fuel), 100);
        }}
        style={{ marginBottom: 16 }}
      >
        <Animated.View
          style={[
            styles.card,
            {
              transform: [{ perspective: 1000 }, { scale: scaleAnim }, { rotateX }],
              shadowColor: brandColor,
              backgroundColor: tokens.colors.card,
              borderColor: tokens.colors.borderLight,
            },
          ]}
        >
          <MeshBackground color={brandColor} intensity={0.05} />
          <View style={[styles.accent, { backgroundColor: brandColor, width: ACCENT_WIDTH }]} />

          <View style={styles.content}>
            <View style={styles.leftSection}>
              <View style={[styles.iconBox, { backgroundColor: brandColor }]}>
                <Fuel size={24} color={tokens.colors.isDark ? '#000' : '#FFF'} />
              </View>
              <View style={styles.textStack}>
                <Text
                  allowFontScaling={false}
                  numberOfLines={1}
                  style={[styles.fuelName, { color: tokens.colors.text.dim }]}
                >
                  {fuel.name}
                </Text>
                <View style={styles.priceRow}>
                  <Text
                    allowFontScaling={false}
                    style={[styles.basePrice, { color: tokens.colors.text.dim }]}
                  >
                    {(fuel.basePrice || 0).toFixed(2)}
                  </Text>
                  <GlowText
                    intensity="high"
                    color={brandColor}
                    glowColor={brandColor}
                    style={styles.discountPrice}
                  >
                    {(fuel.discountPrice || 0).toFixed(2)} ₴
                  </GlowText>
                </View>
              </View>
            </View>

            <View
              style={[
                styles.savingsBadge,
                {
                  backgroundColor: `${brandColor}22`,
                  borderColor: `${brandColor}44`,
                },
              ]}
            >
              <View style={styles.savingsRow}>
                <Text
                  allowFontScaling={false}
                  style={[styles.savingsValue, { color: brandColor }]}
                >
                  -{savings.toFixed(2)}
                </Text>
                <Text
                  allowFontScaling={false}
                  style={[styles.savingsUnit, { color: brandColor }]}
                >
                  {' '}₴/L
                </Text>
              </View>
            </View>
          </View>
        </Animated.View>
      </Pressable>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  card: {
    height: 118,
    width: '100%',
    borderWidth: 1,
    borderRadius: 4,
    flexDirection: 'row',
    overflow: 'hidden',
  },
  accent: {
    height: '100%',
  },
  content: {
    flex: 1,
    flexDirection: 'row',
    paddingHorizontal: 12,
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  leftSection: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
  },
  iconBox: {
    width: 44,
    height: 44,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: 2,
  },
  textStack: {
    flex: 1,
    flexShrink: 1,
    gap: 2,
  },
  priceRow: {
    flexDirection: 'row',
    alignItems: 'baseline',
    gap: 10,
  },
  fuelName: {
    fontFamily: 'Inter-Black',
    fontSize: 16,
    letterSpacing: -0.2,
    textTransform: 'uppercase',
  },
  basePrice: {
    fontFamily: 'Inter-Bold',
    fontSize: 16,
    textDecorationLine: 'line-through',
  },
  discountPrice: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 28,
  },
  savingsBadge: {
    width: 100,
    height: 48,
    borderWidth: 1,
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
});
