import { Text, Pressable } from 'react-native';
import { ChevronRight } from 'lucide-react-native';
import { router } from 'expo-router';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';

type CompanyHubLinkProps = {
  /** Whether the wallet above it is empty; the link sits closer to an empty state. */
  isEmpty: boolean;
};

/**
 * The company owner's way into the hub, extracted from `app/my-codes.tsx` unchanged (planning
 * #807).
 *
 * The pool and every worker's holdings live in the company hub: the wallet keeps receipts only,
 * so the same fuel is never listed in two places that can disagree. The screen renders this
 * outside the empty-state branch on purpose - a company whose fuel is all used up has no receipts
 * left, and that is exactly when it most needs the way into the hub.
 */
export function CompanyHubLink({ isEmpty }: CompanyHubLinkProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  return (
    <Pressable
      accessibilityRole="button"
      onPress={() => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        router.push('/company');
      }}
      style={{
        marginTop: isEmpty ? tokens.spacing.md : tokens.spacing['2xl'],
        flexDirection: 'row',
        alignItems: 'center',
        justifyContent: 'space-between',
        paddingVertical: tokens.spacing.fine14,
        paddingHorizontal: tokens.spacing.lg,
        borderRadius: tokens.radius.lg,
        borderWidth: 1,
        borderColor: `${tokens.colors.primary}33`,
        backgroundColor: `${tokens.colors.primary}0F`,
      }}
    >
      <Text
        allowFontScaling={false}
        style={{
          fontSize: tokens.type.sectionTitle.fontSize,
          fontFamily: 'Inter',
          color: tokens.colors.text.muted,
        }}
      >
        {t('company.managementTitle')}
      </Text>
      <ChevronRight size={18} color={tokens.colors.primary} />
    </Pressable>
  );
}
