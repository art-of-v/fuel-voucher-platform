import React from 'react';
import { View } from 'react-native';

import { Card, Select, SectionHeader, Text } from '../../../core/ui';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useStore } from '../../../core/state/appStore';
import { useI18n, languages, Language } from '../../../core/i18n';
import { themeOptions, ThemeType } from '../../../core/design/themes';
import { Haptics } from '../../../core/utils/haptics';

/**
 * Language and theme pickers.
 *
 * Takes no props on purpose: the language and the theme are both global, persisted
 * preferences owned by `core/i18n` and `core/state/appStore`, and passing them
 * down would mean the screen re-deriving the option lists just to hand them over.
 */
export function PreferencesSection() {
  const tokens = useDesignTokens();
  const { t, language, setLanguage } = useI18n();
  const { theme, setTheme } = useStore();

  const languageOptions = languages.map((l) => ({
    value: l.code,
    label: l.name,
    leading: <Text role="heading">{l.flag}</Text>,
  }));

  const themeSelectOptions = themeOptions.map((opt) => ({
    value: opt.id,
    label: t(opt.label),
    leading: (
      <View
        style={{
          width: 14,
          height: 14,
          borderRadius: 7,
          backgroundColor: opt.color,
          borderWidth: 1,
          borderColor: tokens.colors.borderStrong,
        }}
      />
    ),
  }));

  return (
    <View style={{ gap: tokens.spacing.xs }}>
      <SectionHeader title={t('profile.settingsSection')} />

      <Card padding="md" style={{ backgroundColor: tokens.colors.surface, gap: tokens.spacing.md }}>
        <Select<Language>
          label={t('profile.language')}
          options={languageOptions}
          value={language}
          onChange={(val) => {
            setLanguage(val);
            Haptics.selectionAsync();
          }}
        />

        <Select<ThemeType>
          label={t('profile.theme')}
          options={themeSelectOptions}
          value={theme}
          onChange={(val) => {
            setTheme(val);
            Haptics.selectionAsync();
          }}
        />
      </Card>
    </View>
  );
}
