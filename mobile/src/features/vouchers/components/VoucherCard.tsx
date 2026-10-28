// One voucher card, used by the personal available list and by every company
// stock section (pool and per-worker) so they cannot drift apart
// (multi-company #103, S2).
//
// `resolveBrand` decides *which* brand a brand string is; the theme supplies the
// colour. An unknown brand falls back to the action colour rather than
// guessing a network — a near-miss would paint the wrong brand on the card.
import { View, Text, Pressable, Animated } from 'react-native';
import { AlertTriangle, Ban, Clock, Copy } from 'lucide-react-native';
import type { Voucher } from '../../../core/types/api';
import { classifyVoucher } from '../../../core/types/api';
import { MeshBackground } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { formatExpirationDate } from '../../../core/utils/formatters';
import { VoucherBadge } from './VoucherBadge';
import { VoucherStamp } from './VoucherStamp';
import {
  brandColorFor,
  daysUntilExpiration,
  isExpiringSoon,
  voucherStampTone,
} from '../lib/display';

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
  const stamp = voucherStampTone(voucher);
  const isPending = stamp === 'marked';
  /**
   * A claimed voucher is as spent as a confirmed one — the fuel has left the holder's hands and
   * the card is no longer redeemable — so both get the dead-card treatment and the stamp.
   */
  const isSpent = stamp !== null;
  const kind = classifyVoucher(voucher, userId);
  const isBlocked = kind === 'blocked';
  const workerName = [voucher.workerFirstName, voucher.workerLastName]
    .filter(Boolean)
    .join(' ')
    .trim();
  const bColor = brandColorFor(voucher.brand, tokens);
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
          backgroundColor: isSpent ? tokens.colors.surfaceSunken : tokens.colors.surface,
          borderColor: isSpent
            ? tokens.colors.borderLight
            : pressed
              ? bColor
              : tokens.colors.borderLight,
          transform: pressed ? [{ scale: 0.97 }] : [],
        },
      ]}
    >
      <MeshBackground
        color={isSpent ? tokens.colors.text.dim : bColor}
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
          backgroundColor: isSpent ? tokens.colors.text.dim : bColor,
        }}
      />

      {/* The content dims; the stamp over it stays at full strength, so a dead card still says why. */}
      <View style={{ padding: 22, paddingLeft: 22 + 5 + 16, gap: 14, opacity: isSpent ? 0.55 : 1 }}>
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
                color: isSpent ? tokens.colors.text.dim : tokens.colors.text.primary,
              }}
              numberOfLines={1}
            >
              {voucher.brand}
            </Text>
            <Text
              allowFontScaling={false}
              style={{
                fontSize: 12,
                fontFamily: 'Inter-Bold',
                letterSpacing: 1.5,
                textTransform: 'uppercase',
                color: isSpent ? tokens.colors.text.dim : tokens.colors.text.muted,
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
          ) : isPending ? (
            <View
              style={{
                flexDirection: 'row',
                alignItems: 'center',
                paddingHorizontal: 14,
                paddingVertical: 6,
                borderRadius: 20,
                backgroundColor: tokens.colors.status.warning.subtle,
                gap: 6,
              }}
            >
              <Clock size={12} color={tokens.colors.status.warning.base} />
              <Text
                allowFontScaling={false}
                style={{
                  fontSize: 11,
                  fontFamily: 'Inter-Black',
                  letterSpacing: 0.8,
                  color: tokens.colors.status.warning.base,
                }}
              >
                {t('codes.pendingReconciliation')}
              </Text>
            </View>
          ) : !isSpent ? (
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
              color: isSpent ? tokens.colors.text.dim : tokens.colors.text.primary,
            }}
          >
            {voucher.amount}
            <Text
              allowFontScaling={false}
              style={{
                fontSize: 18,
                fontFamily: 'Rajdhani-SemiBold',
                letterSpacing: 0,
                color: isSpent ? tokens.colors.text.dim : tokens.colors.text.muted,
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
                  color: expiringSoon && !isSpent ? tokens.colors.error : tokens.colors.text.dim,
                }}
              >
                {t('codes.expires')}: {formatExpirationDate(voucher.expirationDate)}
              </Text>
              {expiringSoon && !isSpent && <AlertTriangle size={12} color={tokens.colors.error} />}
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

      {stamp && <VoucherStamp tone={stamp} />}
    </Pressable>
  );
}
