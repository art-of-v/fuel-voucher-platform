import React from 'react';
import { Text as RNText, TextProps as RNTextProps, TextStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import type { TextRole } from '../design/typography';

export type TextTone =
  | 'primary'
  | 'secondary'
  | 'muted'
  | 'disabled'
  | 'accent'
  | 'onAccent'
  | 'success'
  | 'warning'
  | 'danger'
  | 'info'
  | 'inherit';

/**
 * `role` shadows React Native's ARIA `role` prop, which is deliberate: the type
 * role is the decision a caller makes on every single text node, and ARIA roles on
 * text are rare enough to be expressed with `accessibilityRole` instead.
 */
export interface TextProps extends Omit<RNTextProps, 'style' | 'role'> {
  /**
   * The semantic role of this text. Chooses family, size, line height, letter
   * spacing, casing and the accessibility scaling cap in one decision.
   */
  role?: TextRole;
  /** Semantic colour. Defaults to `primary`. */
  tone?: TextTone;
  /** Centre the text. */
  center?: boolean;
  style?: TextStyle | TextStyle[] | (TextStyle | false | undefined)[];
  children?: React.ReactNode;
}

/**
 * The typography primitive.
 *
 * Every piece of text in the product should go through this component. It is the
 * only way to make the type scale enforceable: with bare `<Text>`, a screen can
 * and did invent `fontSize: 8, letterSpacing: 6`.
 *
 * Font scaling is **on** by default, capped per role (`maxFontSizeMultiplier`).
 * The previous convention was `allowFontScaling={false}` on nearly every text
 * node, which made the app unusable at large system font sizes. Pass
 * `allowFontScaling={false}` explicitly only where a fixed-height container
 * genuinely cannot accommodate growth, and note why.
 */
export function Text({
  role = 'body',
  tone = 'primary',
  center,
  style,
  children,
  ...rest
}: TextProps) {
  const tokens = useDesignTokens();
  const spec = tokens.type[role];

  const color = React.useMemo(() => {
    const c = tokens.colors;
    switch (tone) {
      case 'primary':
        return c.text.primary;
      case 'secondary':
        return c.text.secondary;
      case 'muted':
        return c.text.muted;
      case 'disabled':
        return c.text.disabled;
      case 'accent':
        return c.primary;
      case 'onAccent':
        return c.text.onPrimary;
      case 'success':
        return c.status.success.base;
      case 'warning':
        return c.status.warning.base;
      case 'danger':
        return c.status.danger.base;
      case 'info':
        return c.status.info.base;
      case 'inherit':
        return undefined;
    }
  }, [tokens, tone]);

  const { maxFontSizeMultiplier, ...typeStyle } = spec;

  return (
    <RNText
      maxFontSizeMultiplier={maxFontSizeMultiplier}
      style={[typeStyle, color ? { color } : null, center ? { textAlign: 'center' } : null, style]}
      {...rest}
    >
      {children}
    </RNText>
  );
}
