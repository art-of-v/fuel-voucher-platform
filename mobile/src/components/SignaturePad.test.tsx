import React from 'react';
import { render, screen, act } from '@testing-library/react-native';

import { SignaturePad } from './SignaturePad';

/**
 * The pad captures a stroke per pan gesture and hands the whole set upward.
 *
 * The bug this exists for: the stroke used to be read out of a ref inside a
 * `setPaths` updater, and the ref was cleared on the next line. React calls that
 * updater during the re-render, by which time the ref was already empty - so each
 * stroke was committed as an empty string, `filter(Boolean)` dropped it, and the
 * drawing vanished the instant the finger came off the screen. Worse, `onCapture`
 * then reported a non-empty JSON array, so the screen believed it had a signature
 * and sent `"[]"` to the API, which is where the save error came from.
 */

// The gesture module is driven by hand so a pan can be replayed deterministically.
const handlers: Record<string, (e: { x: number; y: number }) => void> = {};

jest.mock('react-native-gesture-handler', () => {
  const R = require('react');
  const { View } = require('react-native');
  const chainable = (name: string) => ({
    [name]: (fn: (e: { x: number; y: number }) => void) => {
      handlers[name] = fn;
      return api;
    },
    runOnJS: () => api,
  });
  const api: any = { ...chainable('onStart'), ...chainable('onUpdate'), ...chainable('onEnd') };
  const chain = () => api;
  (api as any).onStart = (fn: any) => {
    handlers.onStart = fn;
    return api;
  };
  (api as any).onUpdate = (fn: any) => {
    handlers.onUpdate = fn;
    return api;
  };
  (api as any).onEnd = (fn: any) => {
    handlers.onEnd = fn;
    return api;
  };
  (api as any).runOnJS = () => api;
  return {
    __esModule: true,
    Gesture: { Pan: chain },
    GestureDetector: ({ children }: any) => R.createElement(View, null, children),
    GestureHandlerRootView: ({ children }: any) => R.createElement(View, null, children),
  };
});

const point = (x: number, y: number) => ({ x, y });

function drawStroke(...pts: Array<{ x: number; y: number }>) {
  act(() => {
    handlers.onStart(point(10, 10));
    for (const p of pts.slice(1)) handlers.onUpdate(p);
    handlers.onEnd({} as never);
  });
}

describe('SignaturePad', () => {
  it('keeps a finished stroke on the pad after the finger lifts', () => {
    render(<SignaturePad onCapture={jest.fn()} />);

    drawStroke(point(10, 10), point(20, 20), point(30, 15));

    // The stroke must still be in the tree once the gesture ended.
    expect(screen.UNSAFE_getAllByProps({ strokeWidth: 3 }).length).toBeGreaterThan(0);
  });

  it('hands the stroke upward once the gesture ends', () => {
    const onCapture = jest.fn();
    render(<SignaturePad onCapture={onCapture} />);

    drawStroke(point(10, 10), point(20, 20));

    const captured = onCapture.mock.calls[onCapture.mock.calls.length - 1]?.[0];
    expect(captured).toBeDefined();
    // A committed stroke is path data, not an empty string.
    expect(JSON.parse(captured as string)).toEqual([expect.stringContaining('M10,10')]);
  });

  it('never reports an empty string as a stroke', () => {
    const onCapture = jest.fn();
    render(<SignaturePad onCapture={onCapture} />);

    drawStroke(point(5, 5), point(9, 9));

    // `"[]"` is what the API rejected: the screen believed it had a signature.
    for (const call of onCapture.mock.calls) {
      for (const stroke of JSON.parse(call[0] as string)) {
        expect(stroke).not.toBe('');
      }
    }
  });

  it('keeps earlier strokes when another one is drawn', () => {
    const onCapture = jest.fn();
    render(<SignaturePad onCapture={onCapture} />);

    drawStroke(point(10, 10), point(20, 20));
    drawStroke(point(40, 40), point(50, 50));

    expect(
      JSON.parse(onCapture.mock.calls[onCapture.mock.calls.length - 1][0] as string),
    ).toHaveLength(2);
  });
});
