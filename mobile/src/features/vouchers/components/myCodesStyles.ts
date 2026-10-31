import { useMemo } from 'react';
import { StyleSheet } from 'react-native';
import type { DesignTokens } from '../../../core/design/tokens';

/**
 * The my-codes wallet styles, extracted from `app/my-codes.tsx` unchanged
 * (planning #807).
 *
 * The styles take `tokens` as an argument rather than reading the theme themselves: the screen
 * already holds the same token object, and two reads of the store would be two chances to
 * disagree.
 */
export function useMyCodesStyles(tokens: DesignTokens) {
  // Built where `tokens` is in scope rather than at module level. The values below
  // are all theme-independent, but a themed colour added here later would silently
  // freeze at the default theme instead of following a switch.
  return useMemo(
    () =>
      StyleSheet.create({
        sectionHeader: {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.sm,
        },
        sectionLabel: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.caption.fontSize,
          letterSpacing: 6,
          textTransform: 'uppercase',
          marginBottom: tokens.spacing.sm,
        },
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
          padding: tokens.spacing.xxs,
          transform: [{ rotate: '-12deg' }],
        },
        diagonalStampInner: {
          borderWidth: 2,
          paddingHorizontal: tokens.spacing.xl,
          paddingVertical: tokens.spacing.fine6,
        },
        diagonalStampText: {
          fontSize: tokens.type.numeric.fontSize,
          fontFamily: 'Rajdhani-Bold',
          letterSpacing: 6,
          textTransform: 'uppercase',
        },
        emptyContainer: {
          marginTop: tokens.spacing.emptyStateClearance,
          alignItems: 'center',
          justifyContent: 'center',
          paddingVertical: tokens.spacing.emptyStateClearance,
          borderRadius: tokens.radius.lg,
        },
        emptyIconBox: {
          width: 80,
          height: 80,
          borderRadius: tokens.radius.full,
          alignItems: 'center',
          justifyContent: 'center',
          marginBottom: tokens.spacing['2xl'],
        },
        emptyTitle: {
          fontFamily: 'Rajdhani-Bold',
          fontSize: tokens.type.numeric.fontSize,
          letterSpacing: 4,
          textTransform: 'uppercase',
        },
        emptySubtitle: {
          fontSize: tokens.type.caption.fontSize,
          fontFamily: 'Inter-Bold',
          marginTop: tokens.spacing.sm,
          letterSpacing: 1,
        },
        errorContainer: {
          marginTop: tokens.spacing.emptyStateClearance,
          alignItems: 'center',
          justifyContent: 'center',
          paddingVertical: tokens.spacing.emptyStateClearance,
          borderRadius: tokens.radius.lg,
          gap: tokens.spacing.lg,
        },
        errorTitle: {
          fontFamily: 'Rajdhani-Bold',
          fontSize: tokens.type.heading.fontSize,
          letterSpacing: 3,
          textTransform: 'uppercase',
          textAlign: 'center',
        },
      }),
    [tokens],
  );
}
