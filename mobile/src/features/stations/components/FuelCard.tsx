import React, { useRef, useEffect } from 'react';
import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { Fuel } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { formatMoney } from '../../../core/utils/currency';
import { Haptics } from '../../../core/utils/haptics';
import { MeshBackground } from '../../../core/ui';
import { GlowText } from '../../../components/glow-text';
import type { Station, FuelType } from '../../../core/types/api';
import { BRAND_COLORS } from '../../../core/design/tokens';
import { canSell, fuelHeadlinePrice, fuelSaving } from '../lib/pricing';

const ACCENT_WIDTH = 12;

interface FuelCardProps {
  fuel: FuelType;
  station: Station;
  index: number;
  onPress: (station: Station, fuel: FuelType) => void;
}

export function FuelCard({ fuel, station, index, onPress }: FuelCardProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const soft = tokens.surface.soft;
  // Keyed by slug, not by id: the palette holds the networks we carry, and the id is a uuid
  // that would miss every entry.
  const brandColor = BRAND_COLORS[station.slug] || tokens.colors.primary;

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
      Animated.spring(scaleAnim, {
        toValue: 0.99,
        useNativeDriver: true,
        friction: 10,
        tension: 50,
      }),
      Animated.spring(tiltX, { toValue: 1, useNativeDriver: true, friction: 10, tension: 50 }),
      Animated.spring(contentMove, {
        toValue: 3,
        useNativeDriver: true,
        friction: 10,
        tension: 50,
      }),
    ]).start();
  };

  const handlePressOut = () => {
    Animated.parallel([
      Animated.spring(scaleAnim, { toValue: 1, useNativeDriver: true, friction: 3, tension: 100 }),
      Animated.spring(tiltX, { toValue: 0, useNativeDriver: true }),
      Animated.spring(contentMove, {
        toValue: 0,
        useNativeDriver: true,
        friction: 3,
        tension: 100,
      }),
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

  const { amount: savings, hasSaving } = fuelSaving(fuel);
  // Which price this row shows: ours when the shop can sell the fuel, the column price when it
  // cannot - and nothing at all when we have no number, because "0 ₴" is a free fuel.
  const price = fuelHeadlinePrice(fuel);
  // The same question the price and the saving ask. The row answers it in COLOUR as well as in
  // words: an empty shelf keeps the brand accent and the brand price, a fuel we cannot sell drops
  // to grey and says so, so the customer can tell the two apart without opening the fuel.
  const sellable = canSell(fuel);

  return (
    <Animated.View style={{ opacity: entranceAnim, transform: [{ translateY }] }}>
      <Pressable
        onPressIn={handlePressIn}
        onPressOut={handlePressOut}
        onPress={() => {
          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
          onPress(station, fuel);
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
              borderRadius: soft ? tokens.surface.card : undefined,
              // An empty shelf sits back rather than competing with the fuels we can sell. The dim
              // is on the card and not on the entrance animation's opacity, which owns that value.
              opacity: sellable ? 1 : 0.55,
            },
          ]}
        >
          <MeshBackground color={brandColor} intensity={sellable ? 0.05 : 0.02} />
          <View
            style={[
              styles.accent,
              {
                backgroundColor: sellable ? brandColor : tokens.colors.text.disabled,
                width: soft ? tokens.surface.accentWidth : ACCENT_WIDTH,
              },
            ]}
          />

          <View style={styles.content}>
            <View style={styles.leftSection}>
              <View
                style={[
                  styles.iconBox,
                  {
                    backgroundColor: sellable ? brandColor : tokens.colors.surfaceSunken,
                    borderColor: tokens.colors.border,
                    borderWidth: sellable ? 0 : 1,
                    borderRadius: soft ? tokens.surface.icon : undefined,
                  },
                ]}
              >
                <Fuel
                  size={24}
                  color={sellable ? tokens.colors.text.onPrimary : tokens.colors.text.muted}
                />
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
                  {hasSaving && (
                    <Text
                      allowFontScaling={false}
                      style={[styles.basePrice, { color: tokens.colors.text.dim }]}
                    >
                      {formatMoney(fuel.basePrice || 0, { hideSymbol: true })}
                    </Text>
                  )}
                  {price === null ? (
                    // No price to show: the fuel has neither a margin of ours nor a column price.
                    // Saying nothing is honest; "0 ₴" is a free fuel.
                    <Text
                      allowFontScaling={false}
                      style={[styles.discountPrice, { color: tokens.colors.text.dim }]}
                    >
                      —
                    </Text>
                  ) : !sellable ? (
                    // The column price is a number the customer still needs - it is what the market
                    // charges - but it is not OUR price, so it does not wear the brand's colour.
                    <Text
                      allowFontScaling={false}
                      style={[styles.discountPrice, { color: tokens.colors.text.secondary }]}
                    >
                      {formatMoney(price)}
                    </Text>
                  ) : soft ? (
                    <Text
                      allowFontScaling={false}
                      style={[styles.discountPrice, { color: brandColor }]}
                    >
                      {formatMoney(price)}
                    </Text>
                  ) : (
                    <GlowText
                      intensity="high"
                      color={brandColor}
                      glowColor={brandColor}
                      style={styles.discountPrice}
                    >
                      {formatMoney(price)}
                    </GlowText>
                  )}
                </View>
              </View>
            </View>

            {/* One slot, two answers: what the customer saves, or why they cannot buy at all. The
                two never appear together, because a fuel we cannot sell has no saving. */}
            {!sellable ? (
              <View
                style={[
                  styles.outOfStockBadge,
                  {
                    backgroundColor: tokens.colors.surfaceSunken,
                    borderColor: tokens.colors.border,
                    borderRadius: tokens.surface.pill,
                  },
                ]}
              >
                <Text
                  allowFontScaling={false}
                  numberOfLines={1}
                  style={[styles.outOfStockText, { color: tokens.colors.text.muted }]}
                >
                  {t('stations.outOfStock')}
                </Text>
              </View>
            ) : hasSaving ? (
              <View
                style={[
                  styles.savingsBadge,
                  {
                    backgroundColor: `${brandColor}22`,
                    borderColor: `${brandColor}44`,
                  },
                  soft && {
                    minWidth: 100,
                    width: undefined,
                    height: 40,
                    paddingHorizontal: 14,
                    borderRadius: tokens.surface.pill,
                  },
                ]}
              >
                <View style={styles.savingsRow}>
                  <Text
                    allowFontScaling={false}
                    style={[styles.savingsValue, { color: brandColor }, soft && { fontSize: 22 }]}
                  >
                    {formatMoney(savings, { hideSymbol: true })}
                  </Text>
                  <Text
                    allowFontScaling={false}
                    style={[styles.savingsUnit, { color: brandColor }]}
                  >
                    {' '}
                    ₴/L
                  </Text>
                </View>
              </View>
            ) : null}
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
  outOfStockBadge: {
    // Sits in the savings pill's slot: the row is 118 tall and has no second line to spend, so the
    // answer to "can I buy this" goes where the answer to "what do I save" would have gone.
    flexShrink: 0,
    height: 36,
    paddingHorizontal: 12,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  outOfStockText: {
    fontFamily: 'Inter-Black',
    fontSize: 9,
    letterSpacing: 1.2,
    textTransform: 'uppercase',
  },
});
