import { View, Text, Pressable } from 'react-native';
import { AlertTriangle } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { useMyCodesStyles } from './myCodesStyles';

type CodesErrorStateProps = {
  /** Reloads the wallet; the screen decides what that means. */
  onRetry: () => void;
};

/**
 * The wallet's "could not load" state, extracted from `app/my-codes.tsx` unchanged (planning
 * #807). Shown only when the load failed and there is nothing cached to fall back on - the screen
 * owns that condition.
 */
export function CodesErrorState({ onRetry }: CodesErrorStateProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const styles = useMyCodesStyles(tokens);
  return (
    <View style={styles.errorContainer}>
      <AlertTriangle size={40} color={tokens.colors.error} />
      <Text
        allowFontScaling={false}
        style={[styles.errorTitle, { color: tokens.colors.text.primary }]}
      >
        {t('codes.failedToLoad')}
      </Text>
      <Pressable
        onPress={() => {
          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
          onRetry();
        }}
        accessibilityRole="button"
        accessibilityLabel={t('common.retry')}
        style={({ pressed }) => [
          {
            marginTop: tokens.spacing['2xl'],
            paddingHorizontal: tokens.spacing['2xl'],
            paddingVertical: tokens.spacing.md,
            borderWidth: 1,
            borderColor: tokens.colors.primary,
            backgroundColor: pressed ? tokens.colors.primaryDim : 'transparent',
          },
        ]}
      >
        <Text
          style={{
            color: tokens.colors.primary,
            fontSize: tokens.type.caption.fontSize,
            letterSpacing: 1.5,
          }}
        >
          {t('codes.retry')}
        </Text>
      </Pressable>
    </View>
  );
}
