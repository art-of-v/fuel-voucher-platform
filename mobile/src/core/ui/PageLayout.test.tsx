import React from 'react';
import { StyleSheet, Text } from 'react-native';
import { render, screen } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { chrome, spacing } from '../design/layout';
import { PageLayout } from './PageLayout';

// A notch-sized inset, so the test can tell where the safe area was applied: the
// library's own mock reports zeros, which would pass either way.
const mockInsetTop = 59;
const mockMetrics = {
  frame: { x: 0, y: 0, width: 390, height: 844 },
  insets: { top: mockInsetTop, bottom: 34, left: 0, right: 0 },
};

/**
 * The header's corner targets reach past the row, and a touch is never delivered
 * past an ancestor's frame — so the slot that holds the header has to span the same
 * bands the row does. When it did not, the reach existed in the styles and never in
 * the hit test: taps under the back control landed on the content.
 */
it('gives the header slot the safe area plus the corner band, and takes it back', () => {
  render(
    <SafeAreaProvider initialMetrics={mockMetrics}>
      <PageLayout header={<Text>Title</Text>} testID="page">
        <Text>Body</Text>
      </PageLayout>
    </SafeAreaProvider>,
  );

  const slot = screen.getByTestId('page-header-slot');
  const style = StyleSheet.flatten(slot.props.style);

  expect(style.paddingTop).toBe(mockInsetTop + spacing.sm);
  expect(style.paddingBottom).toBe(chrome.headerCornerDrop);
  expect(style.marginBottom).toBe(-chrome.headerCornerDrop);
  // The slot is a band, not a surface: only the header's own controls answer in it.
  expect(slot.props.pointerEvents).toBe('box-none');
});
