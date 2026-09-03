import React, { useState } from 'react';
import {
  Pressable,
  StyleProp,
  TextInput,
  TextInputProps,
  View,
  ViewStyle,
} from 'react-native';
import { AlertCircle } from 'lucide-react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Text } from './Text';

export interface TextFieldProps
  extends Omit<TextInputProps, 'style' | 'placeholderTextColor' | 'editable'> {
  /** Always provide one. A placeholder is not a label — it disappears on input. */
  label?: string;
  /** Persistent guidance shown below the field. Replaced by `error` when set. */
  helper?: string;
  /**
   * Error message. Presence of this prop *is* the error state — a field is never
   * "in error" without saying why.
   */
  error?: string;
  disabled?: boolean;
  /** Marks the field required and announces it to assistive tech. */
  required?: boolean;
  leading?: React.ReactElement<{ size?: number; color?: string }>;
  /** Trailing element — a unit, a visibility toggle, a clear button. */
  trailing?: React.ReactNode;
  /** Centre the text and widen tracking — for codes and PINs. */
  codeStyle?: boolean;
  containerStyle?: StyleProp<ViewStyle>;
}

/**
 * The text input primitive.
 *
 * States, all owned here rather than by the call site:
 *   default   — `surfaceSunken` well, hairline border
 *   focused   — border steps to `colors.focus`, 1.5pt
 *   filled    — same as default; the value itself is the signal
 *   error     — danger border, danger message with an icon, `aria-invalid`
 *   disabled  — `colors.disabled` fill, disabled text, not editable
 *
 * The audit found the phone and OTP fields hand-rolled with `borderWidth: 1`
 * against a conditional radius, error text as a bare red `Text` above the field,
 * and — on the OTP field — no `textContentType`, so the OS could not autofill the
 * SMS code the app had just triggered. That prop is now passed through; use
 * `textContentType="oneTimeCode"` and `autoComplete="sms-otp"` for OTP entry.
 */
export function TextField({
  label,
  helper,
  error,
  disabled = false,
  required = false,
  leading,
  trailing,
  codeStyle = false,
  containerStyle,
  onFocus,
  onBlur,
  ...inputProps
}: TextFieldProps) {
  const tokens = useDesignTokens();
  const c = tokens.colors;
  const [focused, setFocused] = useState(false);

  const borderColor = error
    ? c.status.danger.base
    : focused
      ? c.focus
      : c.border;

  const borderWidth = focused || error ? 1.5 : 1;

  return (
    <View style={[{ gap: tokens.spacing.sm }, containerStyle]}>
      {label ? (
        <Text role="label" tone={disabled ? 'disabled' : 'muted'}>
          {required ? `${label} *` : label}
        </Text>
      ) : null}

      <View
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.md,
          minHeight: tokens.control.lg,
          paddingHorizontal: tokens.spacing.lg,
          borderRadius: tokens.radius.md,
          borderWidth,
          borderColor,
          backgroundColor: disabled ? c.disabled : c.surfaceSunken,
        }}
      >
        {leading
          ? React.cloneElement(leading, {
              size: 18,
              color: disabled ? c.text.disabled : focused ? c.focus : c.text.muted,
            })
          : null}

        <TextInput
          editable={!disabled}
          placeholderTextColor={c.text.disabled}
          onFocus={(e) => {
            setFocused(true);
            onFocus?.(e);
          }}
          onBlur={(e) => {
            setFocused(false);
            onBlur?.(e);
          }}
          accessibilityLabel={label}
          accessibilityHint={error ?? helper}
          accessibilityState={{ disabled }}
          /*
           * The error state has to be announced, not just drawn: a screen-reader
           * user gets the danger border and the red message for free from
           * `accessibilityHint`, but nothing tells them the field is *rejected*.
           * `accessibilityState` has no `invalid` member, so this is the flag.
           */
          aria-invalid={!!error}
          maxFontSizeMultiplier={tokens.type.body.maxFontSizeMultiplier}
          style={{
            flex: 1,
            paddingVertical: tokens.spacing.md,
            fontFamily: codeStyle ? tokens.fonts.display : tokens.fonts.body,
            fontSize: codeStyle ? 24 : tokens.type.body.fontSize,
            letterSpacing: codeStyle ? 6 : 0,
            textAlign: codeStyle ? 'center' : 'left',
            color: disabled ? c.text.disabled : c.text.primary,
          }}
          {...inputProps}
        />

        {trailing}
      </View>

      {error ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.xs }}>
          <AlertCircle size={14} color={c.status.danger.base} />
          <Text role="secondary" tone="danger" style={{ flex: 1 }}>
            {error}
          </Text>
        </View>
      ) : helper ? (
        <Text role="secondary" tone="muted">
          {helper}
        </Text>
      ) : null}
    </View>
  );
}

/**
 * A field-shaped control that opens something instead of accepting typing.
 * Used by `Select`; exported so a date picker or an address lookup can match.
 */
export function FieldShell({
  label,
  helper,
  error,
  disabled = false,
  onPress,
  children,
  trailing,
  containerStyle,
  accessibilityLabel,
}: {
  label?: string;
  helper?: string;
  error?: string;
  disabled?: boolean;
  onPress: () => void;
  children: React.ReactNode;
  trailing?: React.ReactNode;
  containerStyle?: StyleProp<ViewStyle>;
  accessibilityLabel?: string;
}) {
  const tokens = useDesignTokens();
  const c = tokens.colors;

  return (
    <View style={[{ gap: tokens.spacing.sm }, containerStyle]}>
      {label ? (
        <Text role="label" tone={disabled ? 'disabled' : 'muted'}>
          {label}
        </Text>
      ) : null}

      <Pressable
        onPress={disabled ? undefined : onPress}
        disabled={disabled}
        accessibilityRole="button"
        accessibilityLabel={accessibilityLabel ?? label}
        accessibilityState={{ disabled }}
        style={({ pressed }) => ({
          flexDirection: 'row',
          alignItems: 'center',
          gap: tokens.spacing.md,
          minHeight: tokens.control.lg,
          paddingHorizontal: tokens.spacing.lg,
          borderRadius: tokens.radius.md,
          borderWidth: error ? 1.5 : 1,
          borderColor: error ? c.status.danger.base : pressed ? c.focus : c.border,
          backgroundColor: disabled ? c.disabled : c.surfaceSunken,
        })}
      >
        <View style={{ flex: 1 }}>{children}</View>
        {trailing}
      </Pressable>

      {error ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.xs }}>
          <AlertCircle size={14} color={c.status.danger.base} />
          <Text role="secondary" tone="danger" style={{ flex: 1 }}>
            {error}
          </Text>
        </View>
      ) : helper ? (
        <Text role="secondary" tone="muted">
          {helper}
        </Text>
      ) : null}
    </View>
  );
}
