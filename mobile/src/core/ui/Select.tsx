import React, { useState } from 'react';
import { ScrollView, StyleProp, View, ViewStyle } from 'react-native';
import { Check, ChevronDown } from 'lucide-react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { BottomSheet } from './BottomSheet';
import { FieldShell } from './TextField';
import { ListItem } from './ListItem';
import { Text } from './Text';

export interface SelectOption<T extends string | number> {
  value: T;
  label: string;
  /** One line qualifying the option — a price, an address, a consequence. */
  description?: string;
  /** Leading element: a flag, a brand dot, an icon. */
  leading?: React.ReactNode;
  disabled?: boolean;
}

export interface SelectProps<T extends string | number> {
  options: SelectOption<T>[];
  value: T | null;
  onChange: (value: T) => void;
  label?: string;
  /** Shown in the field when nothing is selected. */
  placeholder?: string;
  helper?: string;
  error?: string;
  disabled?: boolean;
  /** Sheet title. Defaults to `label`. */
  sheetTitle?: string;
  containerStyle?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * A single choice from a known set: language, theme, legal entity, fuel type,
 * report period, station.
 *
 * Opens a `BottomSheet` rather than a platform picker, because the app already
 * needs a sheet and a native picker cannot show the qualifying line (`price`,
 * `address`) that most of these choices need to be made intelligently.
 *
 * Replaces the pattern of rendering *all* options inline as a vertical stack of
 * `Pressable` rows — which is what made `/profile` a long scroll of eight themes
 * and four languages, and what made `/packages` five competing forms.
 */
export function Select<T extends string | number>({
  options,
  value,
  onChange,
  label,
  placeholder,
  helper,
  error,
  disabled = false,
  sheetTitle,
  containerStyle,
  testID,
}: SelectProps<T>) {
  const tokens = useDesignTokens();
  const [open, setOpen] = useState(false);
  const selected = options.find((o) => o.value === value) ?? null;

  return (
    <>
      <FieldShell
        label={label}
        helper={helper}
        error={error}
        disabled={disabled}
        onPress={() => setOpen(true)}
        containerStyle={containerStyle}
        accessibilityLabel={label ? `${label}: ${selected?.label ?? placeholder ?? ''}` : undefined}
        trailing={
          <ChevronDown
            size={18}
            color={disabled ? tokens.colors.text.disabled : tokens.colors.text.muted}
          />
        }
      >
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.sm }}>
          {selected?.leading}
          <Text
            role="body"
            tone={disabled ? 'disabled' : selected ? 'primary' : 'muted'}
            numberOfLines={1}
            style={{ flex: 1 }}
          >
            {selected?.label ?? placeholder ?? ''}
          </Text>
        </View>
      </FieldShell>

      <BottomSheet
        visible={open}
        onClose={() => setOpen(false)}
        title={sheetTitle ?? label}
        scrollable={false}
        testID={testID ? `${testID}-sheet` : undefined}
        contentStyle={{ paddingHorizontal: 0, gap: 0 }}
      >
        <ScrollView
          style={{ flexGrow: 0 }}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          {options.map((option, i) => (
            <ListItem
              key={String(option.value)}
              title={option.label}
              subtitle={option.description}
              leading={option.leading}
              selected={option.value === value}
              disabled={option.disabled}
              divider={i < options.length - 1}
              showChevron={false}
              onPress={() => {
                onChange(option.value);
                setOpen(false);
              }}
              trailing={
                option.value === value ? (
                  <Check size={20} color={tokens.colors.primary} />
                ) : undefined
              }
            />
          ))}
        </ScrollView>
      </BottomSheet>
    </>
  );
}
