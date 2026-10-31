import { View, Text, TextInput } from 'react-native';
import { AlertCircle } from 'lucide-react-native';

import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useContextsStyles } from './contexts.styles';

/** A labelled text input for the create-company sheet, with its error line underneath. */
export function Field({
  label,
  value,
  onChangeText,
  onBlur,
  error,
  keyboardType,
  tokens,
}: {
  label: string;
  value: string;
  onChangeText: (v: string) => void;
  onBlur?: () => void;
  /** Already-localized message. Its presence IS the error state, as in core/ui TextField. */
  error?: string;
  keyboardType?: 'default' | 'number-pad';
  tokens: ReturnType<typeof useDesignTokens>;
}) {
  const styles = useContextsStyles();
  return (
    <View style={{ gap: tokens.spacing.fine6 }}>
      <Text style={[styles.fieldLabel, { color: tokens.colors.text.dim }]}>{label}</Text>
      <TextInput
        testID={`field-${label}`}
        value={value}
        onChangeText={onChangeText}
        onBlur={onBlur}
        keyboardType={keyboardType ?? 'default'}
        autoCapitalize="none"
        // Nothing about an invalid field is invisible to a screen reader: the message below is
        // announced with the field, and the flag says the value will be rejected rather than that
        // the input is disabled.
        aria-invalid={!!error}
        placeholderTextColor={tokens.colors.text.dim}
        style={[
          styles.input,
          {
            backgroundColor: tokens.colors.background,
            color: tokens.colors.text.primary,
            borderColor: error ? tokens.colors.status.danger.base : tokens.colors.borderLight,
          },
        ]}
      />
      {error ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.xs }}>
          <AlertCircle size={13} color={tokens.colors.status.danger.base} />
          <Text style={[styles.fieldError, { color: tokens.colors.status.danger.base }]}>
            {error}
          </Text>
        </View>
      ) : null}
    </View>
  );
}
