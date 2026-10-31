import { useMemo } from 'react';
import { StyleSheet } from 'react-native';

import { useDesignTokens } from '../../../core/hooks/useTheme';

/**
 * One stylesheet for the contexts screen and the row and field components it renders.
 * It is a hook rather than a module constant because it is built from `tokens`, which
 * only a hook can reach; the values themselves are theme-independent.
 */
export function useContextsStyles() {
  const tokens = useDesignTokens();
  return useMemo(
    () =>
      StyleSheet.create({
        subtitle: {
          fontFamily: 'Inter-Medium',
          fontSize: tokens.type.sectionTitle.fontSize,
          marginBottom: tokens.spacing.fine18,
        },
        sectionLabel: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.caption.fontSize,
          letterSpacing: 3,
          textTransform: 'uppercase',
          marginTop: tokens.spacing.fine10,
          marginBottom: tokens.spacing.md,
        },
        hint: {
          fontFamily: 'Inter-Medium',
          fontSize: tokens.type.caption.fontSize,
          marginBottom: tokens.spacing.md,
        },
        row: {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.fine14,
          padding: tokens.spacing.fine14,
          borderRadius: tokens.radius.fine12,
          borderWidth: 1,
          marginBottom: tokens.spacing.md,
        },
        rowIcon: {
          width: 44,
          height: 44,
          borderRadius: tokens.radius.md,
          borderWidth: 1.5,
          alignItems: 'center',
          justifyContent: 'center',
        },
        activeTag: {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.xs,
        },
        addRow: {
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'center',
          gap: tokens.spacing.sm,
          paddingVertical: tokens.spacing.fine14,
          borderRadius: tokens.radius.fine12,
          borderWidth: 1,
          borderStyle: 'dashed',
          marginTop: tokens.spacing.xs,
        },
        errorBanner: {
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.fine10,
          paddingHorizontal: tokens.spacing.fine14,
          paddingVertical: tokens.spacing.md,
          borderRadius: tokens.radius.md,
          borderWidth: 1,
          marginBottom: tokens.spacing.md,
        },
        errorBannerText: {
          flex: 1,
          fontFamily: 'Inter-Bold',
          fontSize: tokens.type.sectionTitle.fontSize,
        },
        retryBtn: {
          paddingHorizontal: tokens.spacing.md,
          paddingVertical: tokens.spacing.fine6,
          borderRadius: tokens.radius.fine8,
          borderWidth: 1,
        },
        modalOverlay: {
          flex: 1,
          justifyContent: 'flex-end',
        },
        modalSheet: {
          borderTopLeftRadius: 20,
          borderTopRightRadius: 20,
          borderWidth: 1,
          padding: tokens.spacing.xl,
          paddingBottom: tokens.spacing['4xl'],
          gap: tokens.spacing.lg,
        },
        modalHeader: {
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'space-between',
        },
        fieldLabel: {
          fontFamily: 'Rajdhani-SemiBold',
          fontSize: tokens.type.caption.fontSize,
          letterSpacing: 1.5,
          textTransform: 'uppercase',
        },
        // Sentence case on purpose, unlike the label above: this is prose someone has to read, and
        // uppercase tracked-out text is the label treatment.
        fieldError: {
          fontFamily: 'Inter-Medium',
          fontSize: tokens.type.caption.fontSize,
        },
        input: {
          borderRadius: tokens.radius.md,
          paddingHorizontal: tokens.spacing.fine14,
          paddingVertical: tokens.spacing.md,
          fontFamily: 'Inter-Bold',
          fontSize: tokens.type.numericSmall.fontSize,
          borderWidth: 1,
        },
        confirmBtn: {
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'center',
          gap: tokens.spacing.sm,
          paddingVertical: tokens.spacing.lg,
          borderRadius: tokens.radius.fine12,
        },
      }),
    [tokens],
  );
}
