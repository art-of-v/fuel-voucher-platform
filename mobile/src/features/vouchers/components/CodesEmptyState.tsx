import type { ReactNode } from 'react';
import { View, Text, Pressable, Animated } from 'react-native';
import { QrCode as QrIcon } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { GlowText } from '../../../components/glow-text';
// The summary bar comes through the barrel on purpose: it is the same module the wallet screen
// re-exports for its tests, and importing the file directly would hand them a second component.
import { WalletSummaryBar } from '.';
import { useMyCodesStyles } from './myCodesStyles';

type CodesEmptyStateProps = {
  /** The only receipts left are fully spent ones: the wallet is filtered, not empty. */
  onlySpentLeft: boolean;
  isWorkerContext: boolean;
  isCompanyContext: boolean;
  /** Shared with the wallet cards so the empty state pulses with the same animation. */
  pulseAnim: Animated.Value;
  ordersCount: number;
  fulfilledCount: number;
  pendingCount: number;
  /** The spent-fuel chip, rendered by the screen so both states share one instance. */
  spentFilter: ReactNode;
  onRefresh: () => void;
};

/**
 * The wallet's empty state, extracted from `app/my-codes.tsx` unchanged (planning #807).
 *
 * "Nothing to use" is not "nothing bought": the counts and the spent filter stay on screen when
 * the only receipts left are spent ones, because they are the proof that the wallet is filtered
 * rather than empty. The screen decides when this state applies and whether it is the spent one.
 */
export function CodesEmptyState({
  onlySpentLeft,
  isWorkerContext,
  isCompanyContext,
  pulseAnim,
  ordersCount,
  fulfilledCount,
  pendingCount,
  spentFilter,
  onRefresh,
}: CodesEmptyStateProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const styles = useMyCodesStyles(tokens);
  return (
    <>
      {/* The counts stay on screen in this state on purpose: they are the proof that the
          wallet is filtered, not empty, and they are the numbers the customer remembers. */}
      {onlySpentLeft ? (
        <WalletSummaryBar
          ordersCount={ordersCount}
          fulfilledCount={fulfilledCount}
          pendingCount={pendingCount}
        />
      ) : null}
      {spentFilter}
      <View style={styles.emptyContainer}>
        <View style={styles.emptyIconBox}>
          <QrIcon size={40} color={tokens.colors.primary} />
        </View>
        <GlowText
          intensity="low"
          align="center"
          animation="pulse"
          animatedValue={pulseAnim}
          style={[styles.emptyTitle, { color: tokens.colors.text.primary }]}
        >
          {onlySpentLeft
            ? t('codes.emptySpentTitle')
            : isWorkerContext
              ? t('codes.stock.workerEmpty')
              : isCompanyContext
                ? t('codes.stock.companyEmpty')
                : t('codes.noAssets')}
        </GlowText>
        <Text
          allowFontScaling={false}
          style={[styles.emptySubtitle, { color: tokens.colors.text.muted }]}
        >
          {onlySpentLeft
            ? t('codes.emptySpentSubtitle')
            : isWorkerContext
              ? t('codes.stock.workerEmptySub')
              : isCompanyContext
                ? t('codes.stock.companyEmptySub')
                : t('codes.purchaseFuel')}
        </Text>
        <Pressable
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            onRefresh();
          }}
          accessibilityRole="button"
          accessibilityLabel={t('common.retry')}
          style={{
            marginTop: tokens.spacing['2xl'],
            padding: tokens.spacing.md,
            borderWidth: 1,
            borderColor: tokens.colors.primary,
          }}
        >
          <Text style={{ color: tokens.colors.primary, fontSize: tokens.type.caption.fontSize }}>
            ⟳ REFRESH
          </Text>
        </Pressable>
      </View>
    </>
  );
}
