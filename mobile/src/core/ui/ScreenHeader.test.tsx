import React from 'react';
import { StyleSheet, View } from 'react-native';
import { render, screen, fireEvent } from '@testing-library/react-native';

import {
  HEADER_BOTTOM_HIT,
  HEADER_CORNER_INWARD,
  HEADER_DROP,
  HEADER_REACH,
  ScreenHeader,
  useHeaderCornerFrames,
} from './ScreenHeader';
import { Haptics } from '../utils/haptics';

/**
 * The corner contract, asserted on the pressable itself.
 *
 * A header corner is pressed without looking — a hand arriving at the top-left
 * lands as often 20-40pt below the glyph as on it, and as often above it, in the
 * safe-area strip, when it comes down from the status bar. So the corner is not a
 * button with a ring of slop: the whole region is the button, and its frame is the
 * region. `hitSlop` cannot express that — React Native clips it to every ancestor's
 * frame and resolves overlapping slops by z-order — so these tests pin the frame:
 * padding that grows the target, negative margins that give the layout back, and `0`
 * on the leading edge, which iOS reserves for its swipe-back gesture.
 */

beforeEach(() => {
  jest.spyOn(Haptics, 'impactAsync').mockResolvedValue(undefined as never);
});

const back = () => screen.getByRole('button');

const backStyle = () => StyleSheet.flatten(back().props.style);

it('is the whole corner, not a slop around a control', () => {
  render(<ScreenHeader title="OKKO" />);

  // A frame and a slop would describe two different targets, one of them clipped.
  expect(back().props.hitSlop).toBeUndefined();
});

it('draws a 52pt control inside a pressable that has no size of its own', () => {
  render(<ScreenHeader title="OKKO" />);

  // `lg` is the drawn control: 52pt box, 24pt glyph — the only rounded box here.
  const drawn = screen
    .UNSAFE_getAllByType(View)
    .map((v) => StyleSheet.flatten(v.props.style))
    .filter((s) => s?.borderRadius !== undefined);

  expect(drawn).toHaveLength(1);
  expect(drawn[0].width).toBe(52);
  expect(drawn[0].height).toBe(52);

  // Yoga sizes `width`/`height` to the border box, so a framed pressable that also
  // set them would squeeze the drawn box into the gap between its own padding and
  // drop it off the row's line — which is exactly what the first build did.
  expect(backStyle().width).toBeUndefined();
  expect(backStyle().height).toBeUndefined();
});

it('reaches inwards and down, and takes the layout back', () => {
  render(<ScreenHeader title="OKKO" />);

  const style = backStyle();

  expect(style.paddingRight).toBe(HEADER_CORNER_INWARD);
  expect(style.marginRight).toBe(-HEADER_CORNER_INWARD);
  expect(style.paddingBottom).toBe(HEADER_BOTTOM_HIT);
  expect(style.marginBottom).toBe(-HEADER_BOTTOM_HIT);
});

it('never spends a pixel on the leading edge', () => {
  render(<ScreenHeader title="OKKO" />);

  // iOS starts its swipe-back gesture in a strip along the leading edge, and
  // react-native-screens cancels the RN touch of anything under it the moment the
  // finger drifts horizontally (`cancelTouchesInParent` in RNSScreenStack.mm).
  const style = backStyle();

  expect(style.paddingLeft).toBe(0);
  expect(style.marginLeft).toBe(0);
});

it('reaches up into the band the row reserves, by the row’s own number', () => {
  render(<ScreenHeader title="OKKO" testID="header" />);

  const style = backStyle();
  const row = StyleSheet.flatten(screen.getByTestId('header').props.style);

  // Both frames have to start where the safe-area band starts, or the part of the
  // region above the row is clipped by the row and never sees the touch. The row is
  // a plain container, so it grows its frame with borders and carries no size.
  expect(style.paddingTop).toBeGreaterThan(0);
  expect(style.paddingTop).toBe(row.borderTopWidth);
  expect(style.marginTop).toBe(-style.paddingTop);
});

it('lets the row own the same bands without eating the content', () => {
  render(<ScreenHeader title="OKKO" testID="header" />);

  const row = screen.getByTestId('header');
  const style = StyleSheet.flatten(row.props.style);

  expect(style.borderBottomWidth).toBe(HEADER_DROP);
  expect(style.marginBottom).toBe(-HEADER_DROP);
  expect(style.borderTopWidth).toBeGreaterThan(0);
  expect(style.marginTop).toBe(-style.borderTopWidth);
  expect(row.props.pointerEvents).toBe('box-none');
});

it('keeps the title from swallowing half of the back target', () => {
  render(<ScreenHeader title="OKKO" />);

  // The frame crosses the 12pt gap and 20pt of the title block; a plain View in
  // front of the control would take those taps and do nothing with them.
  expect(screen.getByTestId('header-title').props.pointerEvents).toBe('none');
});

it('gives the trailing corner the same frame its action answers in', () => {
  render(<ScreenHeader title="OKKO" actions={<View testID="action" />} />);

  // React Native never delivers a touch past an ancestor's frame, so the action's
  // own frame only exists if its slot carries one — and the slot is a plain View,
  // so it has to stay out of the hit test itself.
  const slot = screen.getByTestId('header-actions');
  const style = StyleSheet.flatten(slot.props.style);

  expect(style.borderLeftWidth).toBe(HEADER_CORNER_INWARD);
  expect(style.marginLeft).toBe(-HEADER_CORNER_INWARD);
  expect(style.borderRightWidth).toBe(HEADER_REACH);
  expect(style.marginRight).toBe(-HEADER_REACH);
  expect(style.borderBottomWidth).toBe(HEADER_BOTTOM_HIT);
  expect(style.marginBottom).toBe(-HEADER_BOTTOM_HIT);
  expect(style.borderTopWidth).toBeGreaterThan(0);
  expect(style.marginTop).toBe(-style.borderTopWidth);
  expect(slot.props.pointerEvents).toBe('box-none');
});

it('splits the row between the two corners without a shared pixel', () => {
  // The screen decides what the trailing corner is — the basket on the catalog —
  // so it asks for the same frames the header uses rather than guessing numbers.
  let frames: ReturnType<typeof useHeaderCornerFrames> | undefined;
  const Probe = () => {
    frames = useHeaderCornerFrames();
    return null;
  };
  render(<Probe />);

  const { leading, trailing } = frames!;

  expect(leading.right).toBe(trailing.left);
  expect(leading.left).toBe(0);
  expect(trailing.right).toBe(HEADER_REACH);
  expect(leading.top).toBe(trailing.top);
  expect(leading.bottom).toBe(trailing.bottom);
  expect(leading.bottom).toBe(HEADER_BOTTOM_HIT);
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
