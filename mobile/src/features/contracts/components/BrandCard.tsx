import { useMemo } from 'react';
import { View, Text, Pressable, StyleSheet } from 'react-native';
import { CheckCircle2, Landmark, PenTool } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import type { Station } from '../../../core/types/api';

type BrandCardProps = {
  station: Station;
  /** Whether this brand already has a signed contract, which hides the sign action. */
  signed: boolean;
  onSign: (station: Station) => void;
};

/** One brand in the list of contracts on offer, with its sign action. */
export function BrandCard({ station, signed, onSign }: BrandCardProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  // Built where `tokens` is in scope rather than at module level, so a themed value added
  // here later follows a theme switch instead of freezing at the default theme.
  const styles = useMemo(
    () =>
      StyleSheet.create({
        providerCard: {
          padding: tokens.spacing.lg,
          borderRadius: tokens.radius.lg,
          borderWidth: 1,
        },
        providerInfo: {
          flexDirection: 'row',
          alignItems: 'center',
        },
        providerIcon: {
          width: 44,
          height: 44,
          borderRadius: tokens.radius.full,
          borderWidth: 1.5,
          alignItems: 'center',
          justifyContent: 'center',
          marginRight: tokens.spacing.lg,
        },
        actionBtn: {
          width: 44,
          height: 44,
          borderRadius: tokens.radius.xl,
          alignItems: 'center',
          justifyContent: 'center',
        },
      }),
    [tokens],
  );

  return (
    <View
      style={[
        styles.providerCard,
        { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
      ]}
    >
      <View style={styles.providerInfo}>
        <View
          style={[
            styles.providerIcon,
            { backgroundColor: `${station.color}22`, borderColor: station.color },
          ]}
        >
          <Landmark size={20} color={station.color} />
        </View>
        <View style={{ flex: 1 }}>
          <Text
            style={{
              color: tokens.colors.text.primary,
              fontFamily: 'Rajdhani-Bold',
              fontSize: tokens.type.heading.fontSize,
            }}
          >
            {station.name}
          </Text>
          <Text
            style={{
              color: tokens.colors.text.dim,
              fontSize: tokens.type.caption.fontSize,
            }}
          >
            {signed ? t('contracts.signedStatus') : t('contracts.needsSignature')}
          </Text>
        </View>
        {signed ? (
          <CheckCircle2 size={24} color={tokens.colors.primary} />
        ) : (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={`${t('contracts.signAction')} — ${station.name}`}
            hitSlop={4}
            onPress={() => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
              onSign(station);
            }}
            style={[styles.actionBtn, { backgroundColor: tokens.colors.primary }]}
          >
            <PenTool size={16} color={tokens.colors.text.onPrimary} />
          </Pressable>
        )}
      </View>
    </View>
  );
}
