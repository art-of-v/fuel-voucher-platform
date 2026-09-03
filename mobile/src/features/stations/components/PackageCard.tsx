import React, { useRef, useEffect } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { Minus, Plus, ShoppingCart } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { formatMoney } from '../../../core/utils/currency';
import { Haptics } from '../../../core/utils/haptics';
import { MeshBackground } from '../../../core/ui';
import { useI18n } from '../../../core/i18n';
import type { FuelPackage } from '../../../core/types/api';

const ACCENT_WIDTH = 12;

interface PackageCardProps {
  pkg: FuelPackage;
  brandColor?: string;
  index: number;
  quantity: number;
  isAdded: boolean;
  onAdd: () => void;
  onQuantityChange: (qty: number) => void;
}

export function PackageCard({
  pkg,
  brandColor,
  index,
  quantity,
  isAdded,
  onAdd,
  onQuantityChange,
}: PackageCardProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const soft = tokens.surface.soft;
  const activeBrandColor = brandColor || tokens.colors.primary;
  const scaleAnim = useRef(new Animated.Value(1)).current;
  const tiltX = useRef(new Animated.Value(0)).current;
  const contentMove = useRef(new Animated.Value(0)).current;
  const entranceAnim = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    Animated.spring(entranceAnim, {
      toValue: 1,
      useNativeDriver: true,
      delay: index * 60,
      friction: 8,
      tension: 50,
    }).start();
  }, [index, entranceAnim]);

  const handlePressIn = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    Animated.parallel([
      Animated.spring(scaleAnim, { toValue: 0.99, useNativeDriver: true, friction: 10, tension: 50 }),
      Animated.spring(tiltX, { toValue: 1, useNativeDriver: true, friction: 10, tension: 50 }),
      Animated.spring(contentMove, { toValue: 2, useNativeDriver: true, friction: 10, tension: 50 }),
    ]).start();
  };

  const handlePressOut = () => {
    Animated.parallel([
      Animated.spring(scaleAnim, { toValue: 1, useNativeDriver: true, friction: 3, tension: 100 }),
      Animated.spring(tiltX, { toValue: 0, useNativeDriver: true }),
      Animated.spring(contentMove, { toValue: 0, useNativeDriver: true, friction: 4, tension: 150 }),
    ]).start();
  };

  const rotateX = tiltX.interpolate({
    inputRange: [0, 1],
    outputRange: ['0deg', '5deg'],
  });

  const translateY = entranceAnim.interpolate({
    inputRange: [0, 1],
    outputRange: [15, 0],
  });

  const savingsPerUnit = pkg.originalPrice - pkg.price;

  return (
    <Animated.View style={{ opacity: entranceAnim, transform: [{ translateY }] }}>
      <Animated.View
        style={[
          styles.card,
          {
            backgroundColor: tokens.colors.card,
            borderColor: tokens.colors.borderLight,
            transform: [{ perspective: 1000 }, { scale: scaleAnim }, { rotateX }],
            borderRadius: soft ? tokens.surface.card : undefined,
          },
        ]}
      >
        <MeshBackground color={activeBrandColor} intensity={0.05} variant="hexagon" />
        <View style={[styles.accent, { backgroundColor: activeBrandColor, width: soft ? tokens.surface.accentWidth : ACCENT_WIDTH }]} />

        <View style={styles.cardTop}>
          <View
            style={[
              styles.literBox,
              {
                borderColor: activeBrandColor,
                borderRadius: soft ? tokens.surface.field : undefined,
                // A recessed well. Was a hand-rolled `isDark` pair; `surfaceSunken`
                // is the token for exactly this and covers all eight themes.
                backgroundColor: tokens.colors.surfaceSunken,
              },
            ]}
          >
            <Text
              allowFontScaling={false}
              style={[styles.literValue, { color: tokens.colors.text.primary }]}
            >
              {pkg.liters}
            </Text>
            <Text
              allowFontScaling={false}
              style={[styles.literLabel, { color: activeBrandColor }]}
            >
              {t('packages.liters')}
            </Text>
          </View>

          <Animated.View
            style={[styles.priceInfo, { transform: [{ translateX: contentMove }] }]}
          >
            <Text
              allowFontScaling={false}
              style={[styles.currentPrice, { color: tokens.colors.text.primary }]}
            >
              {formatMoney(pkg.price)}
            </Text>
            <Text
              allowFontScaling={false}
              style={[styles.basePrice, { color: tokens.colors.text.dim }]}
            >
              {formatMoney(pkg.originalPrice)}
            </Text>
          </Animated.View>

          <View
            style={[
              styles.savingsBadge,
              { backgroundColor: activeBrandColor },
              soft && {
                paddingHorizontal: 14,
                paddingVertical: 10,
                borderRadius: tokens.surface.pill,
              },
            ]}
          >
            <Text
              allowFontScaling={false}
              style={[
                styles.savingsBadgeText,
                { color: tokens.colors.text.onPrimary },
              ]}
            >
              {formatMoney(-savingsPerUnit)}
            </Text>
          </View>
        </View>

        <View style={styles.stepperSection}>
          <Text
            allowFontScaling={false}
            style={[styles.sectionLabel, { color: tokens.colors.text.dim }]}
          >
            {t('packages.quantity')}
          </Text>
          <View style={styles.stepper}>
            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onQuantityChange(Math.max(1, quantity - 1));
              }}
              style={[
                styles.stepBtn,
                {
                  backgroundColor: tokens.colors.surfaceSunken,
                  borderColor: tokens.colors.borderLight,
                  borderRadius: soft ? tokens.surface.field : undefined,
                },
              ]}
            >
              <Minus size={18} color={tokens.colors.text.primary} />
            </Pressable>
            <Text
              allowFontScaling={false}
              style={[styles.qtyValue, { color: activeBrandColor }]}
            >
              {quantity}
            </Text>
            <Pressable
              onPress={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
                onQuantityChange(Math.min(99, quantity + 1));
              }}
              style={[
                styles.stepBtn,
                {
                  backgroundColor: tokens.colors.surfaceSunken,
                  borderColor: tokens.colors.borderLight,
                  borderRadius: soft ? tokens.surface.field : undefined,
                },
              ]}
            >
              <Plus size={18} color={tokens.colors.text.primary} />
            </Pressable>
          </View>
        </View>

        <View
          style={[
            styles.summaryArea,
            { borderTopColor: tokens.colors.borderLight },
          ]}
        >
          <View style={styles.totalBox}>
            <Text
              allowFontScaling={false}
              style={[styles.totalLabel, { color: tokens.colors.text.dim }]}
            >
              {t('packages.total')}
            </Text>
            <Text
              allowFontScaling={false}
              style={[styles.totalValue, { color: tokens.colors.text.primary }]}
            >
              {formatMoney(pkg.price * quantity)}
            </Text>
          </View>
        </View>

        <Pressable
          onPressIn={handlePressIn}
          onPressOut={handlePressOut}
          onPress={onAdd}
          disabled={isAdded}
          style={[
            styles.mainBtn,
            {
              backgroundColor: isAdded ? tokens.colors.card : activeBrandColor,
              borderColor: isAdded ? activeBrandColor : 'transparent',
              borderWidth: isAdded ? 1 : 0,
              opacity: isAdded ? 0.7 : 1,
              borderRadius: soft ? tokens.surface.button : undefined,
            },
          ]}
        >
          <ShoppingCart
            size={20}
            color={isAdded ? activeBrandColor : tokens.colors.text.onPrimary}
          />
          <Text
            allowFontScaling={false}
            style={[
              styles.mainBtnText,
              {
                color: isAdded
                  ? activeBrandColor
                  : tokens.colors.text.onPrimary,
              },
            ]}
          >
            {isAdded ? t('packages.added') : t('packages.addToCart')}
          </Text>
        </Pressable>
      </Animated.View>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderWidth: 1,
    borderRadius: 2,
    padding: 24,
    position: 'relative',
    overflow: 'hidden',
  },
  accent: {
    position: 'absolute',
    left: 0,
    top: 0,
    bottom: 0,
  },
  cardTop: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 32,
  },
  literBox: {
    width: 64,
    height: 64,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  literValue: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 32,
    lineHeight: 32,
  },
  literLabel: {
    fontFamily: 'Inter-Black',
    fontSize: 9,
    marginTop: -2,
  },
  priceInfo: {
    flex: 1,
    paddingLeft: 20,
  },
  currentPrice: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 24,
    lineHeight: 24,
  },
  basePrice: {
    fontFamily: 'Inter-Medium',
    fontSize: 14,
    textDecorationLine: 'line-through',
    marginTop: 2,
  },
  savingsBadge: {
    paddingHorizontal: 12,
    paddingVertical: 12,
    borderRadius: 2,
  },
  savingsBadgeText: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 18,
  },
  stepperSection: {
    marginBottom: 24,
  },
  sectionLabel: {
    fontFamily: 'Inter-Black',
    fontSize: 10,
    letterSpacing: 4,
    textTransform: 'uppercase',
    marginBottom: 12,
  },
  stepper: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 16,
  },
  stepBtn: {
    width: 52,
    height: 52,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: 4,
  },
  qtyValue: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 32,
    minWidth: 48,
    textAlign: 'center',
  },
  summaryArea: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    alignItems: 'flex-end',
    marginBottom: 24,
    paddingTop: 24,
    borderTopWidth: 1,
  },
  totalBox: {
    alignItems: 'flex-end',
  },
  totalLabel: {
    fontFamily: 'Inter-Black',
    fontSize: 11,
    letterSpacing: 4,
    textTransform: 'uppercase',
  },
  totalValue: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 36,
    lineHeight: 44,
    letterSpacing: -0.5,
  },
  mainBtn: {
    height: 64,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 16,
    borderRadius: 2,
  },
  mainBtnText: {
    fontFamily: 'Inter-Black',
    fontSize: 16,
    letterSpacing: 2,
    textTransform: 'uppercase',
  },
});
