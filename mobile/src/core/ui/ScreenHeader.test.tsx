import React from 'react';
import { StyleSheet } from 'react-native';
import { render, screen, fireEvent } from '@testing-library/react-native';

import { ScreenHeader } from './ScreenHeader';
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
