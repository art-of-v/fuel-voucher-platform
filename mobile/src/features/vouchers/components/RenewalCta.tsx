import { View, Text, Pressable } from 'react-native';
import { RefreshCw } from 'lucide-react-native';
import { router } from 'expo-router';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';

type RenewalCtaProps = {
  /** How many of the user's vouchers are near-expiry/expired and renewable. */
  renewableCount: number;
};

/**
 * The near-expiry renewal call to action, extracted from `app/my-codes.tsx` unchanged (planning
 * #807) — the discoverable entry point into the dedicated multi-select screen.
 *
 * The screen decides whether it shows at all: the feature has to be on, the user has to actually
 * have renewable vouchers, and the context has to be personal. Renewal is a self-service
 * checkout, and buying into a company context is S4 (multi-company epic #103).
 */
export function RenewalCta({ renewableCount }: RenewalCtaProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  return (
    <Pressable
      accessibilityRole="button"
      onPress={() => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
        router.push('/renew-select');
      }}
      style={({ pressed }) => [
        {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.fine14,
          padding: tokens.spacing.lg,
          borderRadius: tokens.radius.lg,
          borderWidth: 1,
          borderColor: pressed ? tokens.colors.warning : tokens.colors.status.warning.border,
          backgroundColor: tokens.colors.status.warning.subtle,
          transform: pressed ? [{ scale: 0.98 }] : [],
        },
      ]}
    >
      <View
        style={{
          width: 42,
          height: 42,
          borderRadius: tokens.radius.full,
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: `${tokens.colors.warning}22`,
        }}
      >
        <RefreshCw size={20} color={tokens.colors.warning} />
      </View>
      <View style={{ flex: 1, gap: tokens.spacing.fine3 }}>
        <Text
          allowFontScaling={false}
          style={{
            fontSize: tokens.type.numericSmall.fontSize,
            fontFamily: 'Rajdhani-Bold',
            letterSpacing: 0.5,
            color: tokens.colors.text.primary,
          }}
        >
          {t('codes.renewCta.title')}
        </Text>
        <Text
          allowFontScaling={false}
          style={{
            fontSize: tokens.type.caption.fontSize,
            fontFamily: 'Inter',
            color: tokens.colors.text.muted,
          }}
        >
          {t('codes.renewCta.subtitle', String(renewableCount))}
        </Text>
      </View>
      <View
        style={{
          paddingHorizontal: tokens.spacing.fine14,
          // A tap target in its own right, and never squeezed: without `flexShrink: 0` a
          // long localized label (VERLÄNGERN, ПРОДОВЖИТИ) let the pill compress into the
          // text block, which is what read as the CTA touching the subtitle (#179).
          minHeight: tokens.control.sm,
          flexShrink: 0,
          justifyContent: 'center',
          borderRadius: tokens.radius.md,
          backgroundColor: tokens.colors.warning,
        }}
      >
        <Text
          allowFontScaling={false}
          numberOfLines={1}
          style={{
            fontSize: tokens.type.caption.fontSize,
            fontFamily: 'Inter-Black',
            letterSpacing: 1,
            textTransform: 'uppercase',
            color: tokens.colors.status.warning.onBase,
          }}
        >
          {t('codes.renewCta.action')}
        </Text>
      </View>
    </Pressable>
  );
}
