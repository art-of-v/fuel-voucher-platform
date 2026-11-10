import React from 'react';
import { StyleSheet } from 'react-native';
import { render, screen } from '@testing-library/react-native';

import type { ThemeType } from '../design/themes';
import { Button } from './Button';

/**
 * The primary fill is the loudest colour on a screen, so it is the one place a frozen
 * palette is most visible. It was pinned to a single hex — the then-default theme's
 * green — in September 2026, which painted a green CTA on every theme whose primary is
 * not green. The fill must follow the active theme, never the default's.
 */

let mockTheme: ThemeType = 'obsidian';

jest.mock('../hooks/useTheme', () => {
  const { getTokens } = jest.requireActual<typeof import('../design/tokens')>('../design/tokens');
  return { useDesignTokens: () => getTokens(mockTheme) };
});

const { getTokens } = jest.requireActual<typeof import('../design/tokens')>('../design/tokens');

type Node = { props?: { style?: unknown }; parent?: Node | null } | null;

function fillOf(label: string): string {
  // The fill is painted by the view around the label, not by the label itself, so
  // walk out from the text to the first node that carries a background.
  let node = screen.getByText(label) as unknown as Node;
  while (node) {
    const flat = (node.props?.style ? StyleSheet.flatten(node.props.style as never) : null) as {
      backgroundColor?: string;
    } | null;
    if (flat?.backgroundColor) return flat.backgroundColor;
    node = node.parent ?? null;
  }
  return '';
}

describe('Button — the primary fill', () => {
  it('is the active theme’s primary, not the default theme’s', () => {
    mockTheme = 'lemberg';
    render(<Button label="Build route" onPress={() => {}} />);

    expect(fillOf('Build route')).toBe(getTokens('lemberg').colors.primary);
    // The regression this pins: a constant frozen to the default (obsidian) primary
    // would paint purple on this green theme, so a test that only checked the default
    // would have passed throughout the bug.
    expect(fillOf('Build route')).not.toBe(getTokens('obsidian').colors.primary);
  });

  it('still matches the primary on the default theme', () => {
    mockTheme = 'obsidian';
    render(<Button label="Build route" onPress={() => {}} />);

    expect(fillOf('Build route')).toBe(getTokens('obsidian').colors.primary);
  });
});
