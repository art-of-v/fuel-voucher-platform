import React from 'react';
import { StyleSheet, View } from 'react-native';
import { render, screen, fireEvent } from '@testing-library/react-native';

import { HEADER_ACTION_FRAME, ScreenHeader } from './ScreenHeader';
import { Haptics } from '../utils/haptics';

/**
 * Why the leading edge is asserted at all: iOS starts its swipe-back gesture in a
 * strip along the leading edge, and react-native-screens cancels the RN touch of
 * anything under that strip the moment the finger drifts horizontally
 * (`cancelTouchesInParent` in `RNSScreenStack.mm`). The header's back control used
 * to be pulled to the edge with a negative margin, so part of its target sat in that
 * strip and a tap that moved a few points was cancelled. The control must clear the
 * strip and spend any extra reach inwards.
 */

beforeEach(() => {
  jest.spyOn(Haptics, 'impactAsync').mockResolvedValue(undefined as never);
});

const back = () => screen.getByRole('button');

it('keeps the back control clear of the leading edge', () => {
  render(<ScreenHeader title="OKKO" />);

  const style = StyleSheet.flatten(back().props.style);

  expect(style.marginLeft ?? 0).toBe(0);
  expect(style.width).toBe(48);
});

it('spends the extra reach inwards, never towards the edge', () => {
  render(<ScreenHeader title="OKKO" />);

  expect(back().props.hitSlop).toMatchObject({ left: 0 });
  expect(back().props.hitSlop.right).toBeGreaterThan(0);
});

it('makes the back target the whole header corner', () => {
  render(<ScreenHeader title="OKKO" />);

  const style = StyleSheet.flatten(back().props.style);
  const slop = back().props.hitSlop;

  // 48pt of box plus the reach on both axes: 72 across and the full row height,
  // which is what a thumb that lands beside the glyph needs to still count.
  expect(style.width + slop.left + slop.right).toBeGreaterThanOrEqual(72);
  expect(style.height + slop.top + slop.bottom).toBeGreaterThanOrEqual(64);
});

it('keeps the title from swallowing half of the back target', () => {
  render(<ScreenHeader title="OKKO" />);

  // The reach crosses the 16pt gap and 8pt of the title block; a plain View in
  // front of the control would take those taps and do nothing with them.
  expect(screen.getByTestId('header-title').props.pointerEvents).toBe('none');
});

it('gives a trailing action a frame its hitSlop can reach into', () => {
  render(<ScreenHeader title="OKKO" actions={<View testID="action" />} />);

  // React Native never delivers a touch past an ancestor's frame, so a trailing
  // action's reach only exists if its slot carries one.
  const slot = StyleSheet.flatten(screen.getByTestId('header-actions').props.style);

  expect(slot.borderWidth).toBe(HEADER_ACTION_FRAME.borderWidth);
  expect(slot.margin).toBe(-HEADER_ACTION_FRAME.borderWidth);
});

it('renders no back control when the screen is a root', () => {
  render(<ScreenHeader title="OKKO" hideBack />);

  expect(screen.queryByRole('button')).toBeNull();
});

it('runs the handler the screen passed instead of the default pop', () => {
  const onBack = jest.fn();
  render(<ScreenHeader title="OKKO" onBack={onBack} />);

  fireEvent.press(back());

  expect(onBack).toHaveBeenCalledTimes(1);
});
