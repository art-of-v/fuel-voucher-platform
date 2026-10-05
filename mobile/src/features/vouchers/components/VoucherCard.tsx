// One voucher card, used by the personal available list and by every company
// stock section (pool and per-worker) so they cannot drift apart
// (multi-company #103, S2).
//
// `resolveBrand` decides *which* brand a provider string is; the theme supplies the
// colour. An unknown provider falls back to the action colour rather than
// guessing a network — a near-miss would paint the wrong brand on the card.
import { View, Text, Pressable, StyleSheet, Animated } from 'react-native';
import { AlertTriangle, Ban, Copy } from 'lucide-react-native';
import type { Voucher } from '../../../core/types/api';
import { classifyVoucher } from '../../../core/types/api';
import { MeshBackground } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { formatExpirationDate } from '../../../core/utils/formatters';
import { VoucherBadge } from '../../../components/VoucherBadge';
import { brandColorFor, daysUntilExpiration, isExpiringSoon } from '../lib/display';

type VoucherCardProps = {
  voucher: Voucher;
  /** Signed-in user id, needed to tell an owned voucher from a received one. */
  userId?: string;
  /** Shared so every card pulses off one animation instead of starting its own. */
  pulseAnim: Animated.Value;
  onSelect: (voucher: Voucher) => void;
};

export function VoucherCard({ voucher, userId, pulseAnim, onSelect }: VoucherCardProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const isUsed = voucher.status === 'used';
  const kind = classifyVoucher(voucher, userId);
  const isBlocked = kind === 'blocked';
  const workerName = [voucher.workerFirstName, voucher.workerLastName]
    .filter(Boolean)
    .join(' ')
    .trim();
  const bColor = brandColorFor(voucher.provider, tokens);
  const expDays = daysUntilExpiration(voucher.expirationDate);
  const expiringSoon = isExpiringSoon(expDays);
  return (
    <Pressable
      key={voucher.id}
      onPress={() => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
        onSelect(voucher);
      }}
      style={({ pressed }) => [
        {
          width: '100%',
          borderRadius: 18,
          borderWidth: 1,
          overflow: 'hidden',
          position: 'relative',
          backgroundColor: isUsed ? tokens.colors.surfaceSunken : tokens.colors.surface,
          borderColor: isUsed
            ? tokens.colors.borderLight
            : pressed
              ? bColor
              : tokens.colors.borderLight,
          opacity: isUsed ? 0.5 : 1,
          transform: pressed ? [{ scale: 0.97 }] : [],
        },
      ]}
    >
      <MeshBackground
        color={isUsed ? tokens.colors.text.dim : bColor}
        intensity={0.07}
        variant="honeycomb"
      />
      <View
        style={{
          position: 'absolute',
          left: 0,
          top: 0,
          bottom: 0,
          width: 5,
          backgroundColor: isUsed ? tokens.colors.text.dim : bColor,
        }}
      />

      <View style={{ padding: 22, paddingLeft: 22 + 5 + 16, gap: 14 }}>
        <View
          style={{
            flexDirection: 'row',
            justifyContent: 'space-between',
            alignItems: 'flex-start',
          }}
        >
          <View style={{ flex: 1, gap: 4, marginRight: 16 }}>
            <Text
              allowFontScaling={false}
              style={{
                fontSize: 18,
                fontFamily: 'Rajdhani-Bold',
                letterSpacing: 1.5,
                textTransform: 'uppercase',
                color: isUsed ? tokens.colors.text.dim : tokens.colors.text.primary,
              }}
              numberOfLines={1}
            >
              {voucher.provider}
            </Text>
            <Text
              allowFontScaling={false}
              style={{
                fontSize: 12,
                fontFamily: 'Inter-Bold',
                letterSpacing: 1.5,
                textTransform: 'uppercase',
                color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted,
              }}
              numberOfLines={1}
            >
              {voucher.fuelName || voucher.fuelType}
            </Text>
            <VoucherBadge kind={kind} />
            {kind === 'gifted_to_worker' && workerName ? (
              <Text
                allowFontScaling={false}
                style={{
                  fontSize: 11,
                  fontFamily: 'Inter-Medium',
                  color: tokens.colors.text.dim,
                }}
                numberOfLines={1}
              >
                → {workerName}
              </Text>
            ) : null}
          </View>
          {isBlocked ? (
            <View
              style={{
                flexDirection: 'row',
                alignItems: 'center',
                paddingHorizontal: 14,
                paddingVertical: 6,
                borderRadius: 20,
                backgroundColor: `${tokens.colors.error}14`,
                gap: 6,
              }}
            >
              <Ban size={12} color={tokens.colors.error} />
              <Text
                allowFontScaling={false}
                style={{
                  fontSize: 11,
                  fontFamily: 'Inter-Black',
                  letterSpacing: 0.8,
                  color: tokens.colors.error,
                }}
              >
                {t('voucher.badge.blocked')}
              </Text>
            </View>
          ) : !isUsed ? (
            <View
              style={{
                flexDirection: 'row',
                alignItems: 'center',
                paddingHorizontal: 14,
                paddingVertical: 6,
                borderRadius: 20,
                backgroundColor: tokens.colors.primaryDim,
                gap: 6,
              }}
            >
              <Animated.View
                style={{
                  width: 7,
                  height: 7,
                  borderRadius: 3.5,
                  backgroundColor: tokens.colors.primary,
                  opacity: pulseAnim,
                }}
              />
              <Text
                allowFontScaling={false}
                style={{
                  fontSize: 11,
                  fontFamily: 'Inter-Black',
                  letterSpacing: 0.8,
                  color: tokens.colors.primary,
                }}
              >
                READY
              </Text>
            </View>
          ) : (
            <View
              style={{
                flexDirection: 'row',
                alignItems: 'center',
                paddingHorizontal: 14,
                paddingVertical: 6,
                borderRadius: 20,
                backgroundColor: tokens.colors.primaryDim,
                gap: 6,
              }}
            >
              <Text
                allowFontScaling={false}
                style={{
                  fontSize: 11,
                  fontFamily: 'Inter-Black',
                  letterSpacing: 0.8,
                  color: tokens.colors.text.dim,
                }}
              >
                {t('codes.used')}
              </Text>
            </View>
          )}
        </View>

        <View style={{ flexDirection: 'row', alignItems: 'baseline' }}>
          <Text
            allowFontScaling={false}
            style={{
              fontSize: 36,
              fontFamily: 'Rajdhani-Bold',
              letterSpacing: -1,
              lineHeight: 38,
              color: isUsed ? tokens.colors.text.dim : tokens.colors.text.primary,
            }}
          >
            {voucher.amount}
            <Text
              allowFontScaling={false}
              style={{
                fontSize: 18,
                fontFamily: 'Rajdhani-SemiBold',
                letterSpacing: 0,
                color: isUsed ? tokens.colors.text.dim : tokens.colors.text.muted,
              }}
            >
              {' '}
              {voucher.unit || t('common.liter')}
            </Text>
          </Text>
        </View>

        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
          {voucher.expirationDate && (
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
              <Text
                allowFontScaling={false}
                style={{
                  fontSize: 12,
                  fontFamily: 'Inter',
                  letterSpacing: 0.5,
                  color: expiringSoon && !isUsed ? tokens.colors.error : tokens.colors.text.dim,
                }}
              >
                {t('codes.expires')}: {formatExpirationDate(voucher.expirationDate)}
              </Text>
              {expiringSoon && !isUsed && <AlertTriangle size={12} color={tokens.colors.error} />}
            </View>
          )}
          {voucher.externalId && (
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 5, opacity: 0.5 }}>
              <Copy size={10} color={tokens.colors.text.dim} />
              <Text
                allowFontScaling={false}
                style={{
                  fontSize: 10,
                  fontFamily: 'Inter',
                  letterSpacing: 1,
                  textTransform: 'uppercase',
                  color: tokens.colors.text.dim,
                }}
                numberOfLines={1}
              >
                {voucher.externalId}
              </Text>
            </View>
          )}
        </View>
      </View>

      {isUsed && (
        <View style={styles.diagonalStampContainer}>
          {/*
                        The "USED" watermark. Its three colours were raw translucent
                        whites plus a black plaque, so on the light themes it was a
                        dark box with near-invisible text. "Used" is precisely what
                        the neutral status role is for.
                      */}
          <View
            style={[styles.diagonalStamp, { borderColor: tokens.colors.status.neutral.border }]}
          >
            <View
              style={[
                styles.diagonalStampInner,
                {
                  borderColor: tokens.colors.status.neutral.border,
                  backgroundColor: tokens.colors.status.neutral.subtle,
                },
              ]}
            >
              <Text
                style={[styles.diagonalStampText, { color: tokens.colors.status.neutral.base }]}
              >
                {t('codes.used')}
              </Text>
            </View>
          </View>
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  diagonalStampContainer: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    left: 0,
    right: 0,
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
  },
  diagonalStamp: {
    borderWidth: 1,
    padding: 2,
    transform: [{ rotate: '-12deg' }],
  },
  diagonalStampInner: {
    borderWidth: 2,
    paddingHorizontal: 20,
    paddingVertical: 6,
  },
  diagonalStampText: {
    fontSize: 22,
    fontFamily: 'Rajdhani-Bold',
    letterSpacing: 6,
    textTransform: 'uppercase',
  },
});
