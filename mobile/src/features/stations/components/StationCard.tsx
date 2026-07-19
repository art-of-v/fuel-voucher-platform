import React, { useRef, useEffect } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { ArrowRight, Zap } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { Haptics } from '../../../core/utils/haptics';
import type { Station } from '../../../core/types/api';
import { BRAND_COLORS } from '../../../core/design/tokens';

const ACCENT_WIDTH = 12;

interface StationCardProps {
  station: Station;
  index: number;
  onPress: (station: Station) => void;
}

export function StationCard({ station, index, onPress }: StationCardProps) {
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
      delay: index * 100,
      friction: 8,
      tension: 40,
    }).start();
  }, [index, entranceAnim]);

  const handlePressIn = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    Animated.parallel([
      Animated.spring(scaleAnim, { toValue: 0.985, useNativeDriver: true, friction: 10, tension: 100 }),
      Animated.spring(tiltX, { toValue: 1, useNativeDriver: true, friction: 10, tension: 100 }),
      Animated.spring(contentMove, { toValue: 4, useNativeDriver: true, friction: 10, tension: 100 }),
    ]).start();
  };

  const handlePressOut = () => {
    Animated.parallel([
      Animated.spring(scaleAnim, { toValue: 1, useNativeDriver: true, friction: 3, tension: 100 }),
      Animated.spring(tiltX, { toValue: 0, useNativeDriver: true, friction: 3, tension: 100 }),
      Animated.spring(contentMove, { toValue: 0, useNativeDriver: true, friction: 3, tension: 100 }),
    ]).start();
  };

  const rotateX = tiltX.interpolate({
    inputRange: [0, 1],
    outputRange: ['0deg', '3deg'],
  });

  const translateY = entranceAnim.interpolate({
    inputRange: [0, 1],
    outputRange: [20, 0],
  });

  return (
    <Animated.View
      style={{ opacity: entranceAnim, transform: [{ translateY }] }}
    >
      <Pressable
        onPressIn={handlePressIn}
        onPressOut={handlePressOut}
        onPress={() => {
          setTimeout(() => onPress(station), 130);
        }}
        style={{ marginBottom: 16 }}
      >
        {({ pressed }) => (
          <View style={{ transform: [{ perspective: 1000 }] }}>
            <Animated.View
              style={[
                styles.card,
                {
                  transform: [{ scale: scaleAnim }, { rotateX }],
                  backgroundColor: pressed ? `${brandColor}44` : tokens.colors.card,
                  borderColor: pressed ? brandColor : tokens.colors.borderLight,
                  borderWidth: pressed ? 2 : 1,
                  shadowColor: brandColor,
                  shadowOpacity: pressed ? 0.6 : 0,
                  shadowRadius: 15,
                  elevation: pressed ? 12 : 0,
                },
              ]}
            >
              <View
                style={{ width: ACCENT_WIDTH, height: '100%', backgroundColor: brandColor }}
              />

              <View style={styles.cardContent}>
                <Animated.View
                  style={{
                    justifyContent: 'center',
                    transform: [{ translateX: contentMove }],
                  }}
                >
                  <View style={{ flexDirection: 'row', alignItems: 'center', marginBottom: 4 }}>
                    <Text
                      allowFontScaling={false}
                      style={[styles.cardName, { color: tokens.colors.text.primary }]}
                    >
                      {station.logoText || station.name || 'UNKNOWN'}
                    </Text>
                    <Zap size={14} color={brandColor} style={{ marginTop: 2, marginLeft: 8 }} />
                  </View>
                  <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                    <View
                      style={[styles.statusPip, { backgroundColor: brandColor }]}
                    />
                    <Text
                      allowFontScaling={false}
                      style={[styles.statusLabel, { color: tokens.colors.text.muted }]}
                    >
                      ONLINE \u2022 READY
                    </Text>
                  </View>
                </Animated.View>

                <Animated.View
                  style={[
                    styles.cardArrow,
                    {
                      transform: [{ translateX: Animated.multiply(contentMove, -1) }],
                      borderColor: tokens.colors.primary,
                      backgroundColor: tokens.colors.primaryDim,
                    },
                  ]}
                >
                  <ArrowRight size={20} color={tokens.colors.primary} />
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
  card: {
    width: '100%',
    borderRadius: 2,
    flexDirection: 'row',
    overflow: 'hidden',
    height: 104,
  },
  cardContent: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    paddingLeft: 12,
    paddingRight: 24,
    justifyContent: 'space-between',
  },
  cardName: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 32,
    textTransform: 'uppercase',
    letterSpacing: -0.5,
    lineHeight: 32,
  },
  statusPip: {
    width: 6,
    height: 6,
    borderRadius: 3,
    marginRight: 6,
  },
  statusLabel: {
    fontFamily: 'Inter-Bold',
    fontSize: 10,
    letterSpacing: 2,
    lineHeight: 14,
  },
  cardArrow: {
    width: 48,
    height: 48,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: 2,
  },
});
