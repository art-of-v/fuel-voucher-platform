import React from 'react';
import { Insets, Pressable, StyleProp, View, ViewStyle } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { Haptics } from '../utils/haptics';

export type IconButtonVariant = 'plain' | 'outlined' | 'filled' | 'danger';
export type IconButtonSize = 'sm' | 'md' | 'lg';

export interface IconButtonProps {
  icon: React.ReactElement<{ size?: number; color?: string }>;
  onPress: () => void;
  /**
   * Required. An icon-only control with no accessible name is invisible to a
   * screen reader — the audit found the contract-signing action was a bare
   * 40×40 circle with a `PenTool` glyph and nothing else.
   */
  accessibilityLabel: string;
  variant?: IconButtonVariant;
  size?: IconButtonSize;
  disabled?: boolean;
  /** Circular instead of the standard `radius.md` square. */
  round?: boolean;
  hapticStyle?: 'light' | 'medium' | 'heavy';
  /**
   * Extra reach beyond the box, for space that belongs to nobody else. Clipped by
   * every ancestor's frame, and two overlapping slops resolve by z-order — when the
   * reach has to be a real region, use `frame` instead.
   */
  hitSlop?: Insets | number;
  /**
   * The region the pressable itself answers in, past the box it draws — a corner
   * that is one button rather than a ring around one. The frame is padding on the
   * pressable and the negative margins take the same space back, so nothing moves
   * and the background stays on the drawn box. The caller must own every pixel the
   * frame covers: the pressable takes those taps.
   *
   * Unlike `hitSlop` this is the element's frame, so it survives ancestor clipping
   * exactly as the layout does — the ancestors still have to contain it.
   */
  frame?: Insets;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * `md` is 48, not the 44 minimum: at exactly 44 the header's back and basket
 * controls were missed on the first tap often enough to be reported as broken,
 * because the eye aims at the 22pt glyph and a few points off its edge is
 * outside the target. `sm` is 36 visually and borrows 4pt of slop on every edge
 * to reach 44.
 */
const BOX: Record<IconButtonSize, number> = { sm: 36, md: 48, lg: 52 };
const GLYPH: Record<IconButtonSize, number> = { sm: 18, md: 22, lg: 24 };

/**
 * An `Insets` frame as a style for a **container**: the borders pad the view, the
 * margins take the same space back, so the frame grows, the children stay where they
 * were and the layout does not move.
 *
 * Do not put this on an element that also sets `width`/`height`. Yoga sizes those to
 * the border box, so the borders would eat the box instead of growing the frame — a
 * 52pt control framed by 40pt of drop shrinks to 4pt of content and sits off its
 * line. `IconButton` frames its pressable with padding (`paddingForFrame`) for
 * exactly that reason.
 *
 * `0 - v`, not `-v`: a zero edge must produce `0`, and `-0` is a different value to
 * anything that compares styles.
 */
export function frameToStyle(frame: Insets): ViewStyle {
  const top = frame.top ?? 0;
  const right = frame.right ?? 0;
  const bottom = frame.bottom ?? 0;
  const left = frame.left ?? 0;
  return {
    borderTopWidth: top,
    borderRightWidth: right,
    borderBottomWidth: bottom,
    borderLeftWidth: left,
    borderColor: 'transparent',
    marginTop: 0 - top,
    marginRight: 0 - right,
    marginBottom: 0 - bottom,
    marginLeft: 0 - left,
  };
}

/**
 * The same frame on the pressable itself, as padding.
 *
 * The pressable sets no `width`/`height` while framed: the drawn box is a child, and
 * it is what sizes the pressable. Padding puts the box on its line, the negative
 * margins give the layout back, and the touch area is the padding box.
 */
function paddingForFrame(frame: Insets): ViewStyle {
  const top = frame.top ?? 0;
  const right = frame.right ?? 0;
  const bottom = frame.bottom ?? 0;
  const left = frame.left ?? 0;
  return {
    paddingTop: top,
    paddingRight: right,
    paddingBottom: bottom,
    paddingLeft: left,
    marginTop: 0 - top,
    marginRight: 0 - right,
    marginBottom: 0 - bottom,
    marginLeft: 0 - left,
  };
}

/**
 * A single-icon action: back, close, share, expand, overflow.
 *
 * No icon action in the product is smaller than the 44pt minimum: `sm` reaches
 * it through `hitSlop`, the larger variants are 48 and 52 on their own.
 */
export function IconButton({
  icon,
  onPress,
  accessibilityLabel,
  variant = 'plain',
  size = 'md',
  disabled = false,
  round = false,
  hapticStyle = 'light',
  hitSlop,
  frame,
  style,
  testID,
}: IconButtonProps) {
  const tokens = useDesignTokens();
  const c = tokens.colors;
  const box = BOX[size];

  const skin = (pressed: boolean) => {
    if (disabled) {
      return { background: 'transparent', border: 'transparent', content: c.text.disabled };
    }
    switch (variant) {
      case 'plain':
        return {
          background: pressed ? c.primarySubtle : 'transparent',
          border: 'transparent',
          content: c.text.primary,
        };
      case 'outlined':
        return {
          background: pressed ? c.primarySubtle : 'transparent',
          border: c.border,
          content: c.text.primary,
        };
      case 'filled':
        return {
          background: pressed ? c.primaryPressed : c.primary,
          border: 'transparent',
          content: c.text.onPrimary,
        };
      case 'danger':
        return {
          background: pressed ? c.status.danger.subtle : 'transparent',
          border: c.status.danger.border,
          content: c.status.danger.base,
        };
    }
  };

  // With a frame the drawn box sizes the pressable: `width`/`height` would be the
  // border box, and 52pt of them around 92pt of frame leaves a 4pt control.
  const sizing = frame ? paddingForFrame(frame) : { width: box, height: box };

  return (
    <Pressable
      onPress={() => {
        if (disabled) return;
        const map = {
          light: Haptics.ImpactFeedbackStyle.Light,
          medium: Haptics.ImpactFeedbackStyle.Medium,
          heavy: Haptics.ImpactFeedbackStyle.Heavy,
        };
        Haptics.impactAsync(map[hapticStyle]);
        onPress();
      }}
      disabled={disabled}
      hitSlop={frame ? undefined : (hitSlop ?? tokens.touchTarget.slopFor(box))}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ disabled }}
      testID={testID}
      style={[sizing, style]}
    >
      {({ pressed }) => {
        const s = skin(pressed && !disabled);
        // The skin lives on the drawn box, never on the pressable: a background on
        // a framed pressable would paint the whole corner.
        return (
          <View
            style={{
              width: box,
              height: box,
              borderRadius: round ? tokens.radius.full : tokens.radius.md,
              alignItems: 'center',
              justifyContent: 'center',
              backgroundColor: s.background,
              borderWidth: s.border === 'transparent' ? 0 : 1,
              borderColor: s.border,
            }}
          >
            {React.cloneElement(icon, { size: GLYPH[size], color: s.content })}
          </View>
        );
      }}
    </Pressable>
  );
}
