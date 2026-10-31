import { useMemo } from 'react';
import { View, Text, Pressable, StyleSheet } from 'react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';

export type Tab = 'AVAILABLE' | 'SIGNED';

type ContractTabsProps = {
  activeTab: Tab;
  onChange: (tab: Tab) => void;
};

/** The switch between the contracts a brand offers and the ones already signed. */
export function ContractTabs({ activeTab, onChange }: ContractTabsProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  // Built where `tokens` is in scope rather than at module level, so a themed value added
  // here later follows a theme switch instead of freezing at the default theme.
  const styles = useMemo(
    () =>
      StyleSheet.create({
        tabContainer: {
          flexDirection: 'row',
          padding: tokens.spacing.xs,
          borderRadius: tokens.radius.md,
          borderWidth: 1,
          gap: tokens.spacing.xs,
        },
        tab: {
          flex: 1,
          minHeight: 44,
          paddingVertical: tokens.spacing.fine10,
          alignItems: 'center',
          justifyContent: 'center',
        },
        tabText: {
          fontFamily: 'Inter-Black',
          fontSize: tokens.type.caption.fontSize,
          letterSpacing: 1,
        },
      }),
    [tokens],
  );

  return (
    <View
      style={[
        styles.tabContainer,
        { backgroundColor: tokens.colors.card, borderColor: tokens.colors.borderLight },
      ]}
    >
      {(['AVAILABLE', 'SIGNED'] as Tab[]).map((tab) => (
        <Pressable
          key={tab}
          onPress={() => {
            Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
            onChange(tab);
          }}
          style={[
            styles.tab,
            activeTab === tab && {
              backgroundColor: tokens.colors.primary,
              borderRadius: tokens.radius.md,
            },
          ]}
        >
          <Text
            style={[
              styles.tabText,
              { color: activeTab === tab ? tokens.colors.text.onPrimary : tokens.colors.text.dim },
            ]}
          >
            {tab === 'AVAILABLE' ? t('contracts.available') : t('contracts.signed')}
          </Text>
        </Pressable>
      ))}
    </View>
  );
}
