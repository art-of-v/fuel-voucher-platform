import React from 'react';
import { View } from 'react-native';
import { LogOut, Trash2 } from 'lucide-react-native';

import { Button } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';

/**
 * Sign out and delete account.
 *
 * `onDelete` only *asks* — it opens the confirmation dialog. The screen owns that
 * dialog because a destructive confirmation is a navigation-flavoured concern and
 * belongs beside the other overlays it is rendered with.
 */
export interface AccountActionsProps {
  onSignOut: () => void;
  onDelete: () => void;
}

export function AccountActions({ onSignOut, onDelete }: AccountActionsProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  return (
    <View style={{ gap: tokens.spacing.md, paddingTop: tokens.spacing.sm }}>
      <Button
        label={t('profile.signOut')}
        variant="secondary"
        size="md"
        icon={<LogOut size={18} />}
        onPress={onSignOut}
        fullWidth
      />

      {/*
        `hapticStyle` rather than a manual `Haptics.impactAsync` in `onPress`.
        Button already fires a haptic on every press, so the screen used to buzz
        twice on this row — a Medium from the primitive immediately followed by a
        Heavy from the handler, which reads as a stutter rather than as emphasis.
        Weighting the primitive's own haptic says the same thing once.
      */}
      <Button
        label={t('profile.deleteAccount')}
        variant="destructive"
        size="md"
        icon={<Trash2 size={18} />}
        hapticStyle="heavy"
        onPress={onDelete}
        fullWidth
      />
    </View>
  );
}
