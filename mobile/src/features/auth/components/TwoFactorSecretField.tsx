import React from 'react';
import { View, StyleSheet } from 'react-native';
import * as Clipboard from 'expo-clipboard';
import { Copy } from 'lucide-react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { toast } from '../../../core/feedback/toastStore';
import { Haptics } from '../../../core/utils/haptics';
import { Button, Text } from '../../../core/ui';

interface TwoFactorSecretFieldProps {
  secret: string;
}

export function TwoFactorSecretField({ secret }: TwoFactorSecretFieldProps) {
  const tokens = useDesignTokens();
  const t = useI18n((s) => s.t);

  const handleCopy = async () => {
    await Clipboard.setStringAsync(secret);
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    toast.success(t('phoneAuth.twoFactorSecretCopied'));
  };

  return (
    <View
      style={[
        styles.container,
        {
          borderColor: tokens.colors.border ?? tokens.colors.text.muted,
          borderRadius: tokens.radius.md,
          padding: tokens.spacing.md,
          gap: tokens.spacing.xs,
        },
      ]}
    >
      <Text role="caption" tone="secondary">
        {t('phoneAuth.twoFactorEnrollHint')}
      </Text>
      <Text role="body" selectable style={{ fontFamily: undefined }}>
        {secret}
      </Text>
      <Button
        label={t('phoneAuth.twoFactorSecretCopy')}
        variant="ghost"
        size="sm"
        icon={<Copy />}
        onPress={handleCopy}
        testID="two-factor-secret-copy"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    borderWidth: 1,
  },
});
