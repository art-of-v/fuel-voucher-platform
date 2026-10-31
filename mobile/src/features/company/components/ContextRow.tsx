import { View, Text, Pressable } from 'react-native';
import { Check } from 'lucide-react-native';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { Haptics } from '../../../core/utils/haptics';
import { useContextsStyles } from './contexts.styles';

/** One selectable account context: personal, an owned company, or one the user works for. */
export function ContextRow({
  icon,
  title,
  subtitle,
  active,
  activeLabel,
  onPress,
  tokens,
}: {
  icon: React.ReactNode;
  title: string;
  subtitle?: string;
  active: boolean;
  activeLabel: string;
  onPress: () => void;
  tokens: ReturnType<typeof useDesignTokens>;
}) {
  const styles = useContextsStyles();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected: active }}
      onPress={() => {
        Haptics.selectionAsync();
        onPress();
      }}
      style={[
        styles.row,
        {
          backgroundColor: active ? `${tokens.colors.primary}14` : tokens.colors.card,
          borderColor: active ? tokens.colors.primary : tokens.colors.borderLight,
        },
      ]}
    >
      <View
        style={[
          styles.rowIcon,
          { borderColor: tokens.colors.borderAccent, backgroundColor: tokens.colors.surfaceSunken },
        ]}
      >
        {icon}
      </View>
      <View style={{ flex: 1 }}>
        <Text
          style={{
            color: tokens.colors.text.primary,
            fontFamily: 'Rajdhani-Bold',
            fontSize: tokens.type.numericSmall.fontSize,
          }}
          numberOfLines={1}
        >
          {title}
        </Text>
        {subtitle ? (
          <Text
            style={{ color: tokens.colors.text.dim, fontSize: tokens.type.caption.fontSize }}
            numberOfLines={1}
          >
            {subtitle}
          </Text>
        ) : null}
      </View>
      {active ? (
        <View style={styles.activeTag}>
          <Check size={16} color={tokens.colors.primary} />
          <Text
            style={{
              color: tokens.colors.primary,
              fontFamily: 'Inter-Black',
              fontSize: tokens.type.caption.fontSize,
              letterSpacing: 0.8,
            }}
          >
            {activeLabel.toUpperCase()}
          </Text>
        </View>
      ) : null}
    </Pressable>
  );
}
