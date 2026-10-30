import React from 'react';
import { StyleSheet, View } from 'react-native';
import { render, screen, fireEvent } from '@testing-library/react-native';
import { ChevronLeft } from 'lucide-react-native';

import { IconButton } from './IconButton';
import { Haptics } from '../utils/haptics';

/**
 * The size rule, not the skin: `md` is the size every header control uses, and it
 * used to be exactly 44. The back and basket controls were then reported as "very
 * hard to hit" — the eye aims at the 22pt glyph, and a thumb a few points off its
 * edge was outside the target with no slack to absorb it.
 *
 * These assertions pin the numbers so the 44 minimum cannot quietly come back as
 * the working size of an icon-only control.
 */

const renderButton = (props: Partial<React.ComponentProps<typeof IconButton>> = {}) => {
  const onPress = jest.fn();
  render(
    <IconButton icon={<ChevronLeft />} accessibilityLabel="back" onPress={onPress} {...props} />,
  );
  return { onPress, button: screen.getByLabelText('back') };
};

beforeEach(() => {
  jest.spyOn(Haptics, 'impactAsync').mockResolvedValue(undefined as any);
});

describe('size', () => {
  it('builds md at 48, so the visible box is already larger than the minimum', () => {
    const { button } = renderButton();

    const style = StyleSheet.flatten(button.props.style);

    expect(style.width).toBe(48);
    expect(style.height).toBe(48);
  });

  it('does not need slop on md, because the box itself is the slack', () => {
    const { button } = renderButton();

    expect(button.props.hitSlop).toEqual({ top: 0, bottom: 0, left: 0, right: 0 });
  });

  it('pads sm from 36 out to the 44 minimum', () => {
    const { button } = renderButton({ size: 'sm' });

    const style = StyleSheet.flatten(button.props.style);

    expect(style.width).toBe(36);
    expect(button.props.hitSlop).toEqual({ top: 4, bottom: 4, left: 4, right: 4 });
  });

  it('lets the caller reach further where nobody else owns the space', () => {
    const { button } = renderButton({ hitSlop: 4 });

    expect(button.props.hitSlop).toBe(4);
  });
});

describe('frame', () => {
  const frame = { top: 20, bottom: 40, left: 0, right: 12 };

  it('grows the pressable to the region and gives the layout back', () => {
    const { button } = renderButton({ size: 'lg', frame });

    const style = StyleSheet.flatten(button.props.style);

    expect(style.borderTopWidth).toBe(20);
    expect(style.borderBottomWidth).toBe(40);
    expect(style.borderLeftWidth).toBe(0);
    expect(style.borderRightWidth).toBe(12);

    // The margins are the negative of the borders, or the control moves: a zero
    // edge has to stay `0` and not `-0`.
    expect(style.marginTop).toBe(-20);
    expect(style.marginBottom).toBe(-40);
    expect(style.marginLeft).toBe(0);
    expect(style.marginRight).toBe(-12);

    // The drawn box is untouched.
    expect(style.width).toBe(52);
    expect(style.height).toBe(52);
  });

  it('drops the slop, because a frame and a slop are two different targets', () => {
    const { button } = renderButton({ frame });

    expect(button.props.hitSlop).toBeUndefined();
  });

  it('paints the skin on the drawn box, never across the frame', () => {
    const { button } = renderButton({ variant: 'filled', size: 'lg', frame });

    const pressable = StyleSheet.flatten(button.props.style);

    // A background on the pressable would tint the whole corner the moment it is
    // pressed — a 52pt control turning a 100pt-plus region into a coloured block.
    expect(pressable.backgroundColor).toBeUndefined();
    expect(pressable.borderRadius).toBeUndefined();

    const skins = screen
      .UNSAFE_getAllByType(View)
      .filter((v) => StyleSheet.flatten(v.props.style)?.borderRadius !== undefined);

    expect(skins).toHaveLength(1);
    expect(StyleSheet.flatten(skins[0].props.style).width).toBe(52);
  });
});

describe('press', () => {
  it('fires one light haptic for one press', () => {
    const { onPress, button } = renderButton();

    fireEvent.press(button);

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(Haptics.impactAsync).toHaveBeenCalledTimes(1);
    expect(Haptics.impactAsync).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Light);
  });

  it('honours the requested impact style', () => {
    const { button } = renderButton({ hapticStyle: 'heavy' });

    fireEvent.press(button);

    expect(Haptics.impactAsync).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Heavy);
  });

  it('does nothing at all when disabled — no press, no buzz', () => {
    const { onPress, button } = renderButton({ disabled: true });

    fireEvent.press(button);

    expect(onPress).not.toHaveBeenCalled();
    expect(Haptics.impactAsync).not.toHaveBeenCalled();
  });
});
