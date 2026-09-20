import React from 'react';
import { PageLayout } from './PageLayout';
import { GridBackground } from './GridBackground';

interface GridPageLayoutProps {
  children: React.ReactNode;
  header?: React.ReactNode;
  fixedFooter?: React.ReactNode;
  background?: React.ReactNode;
  disableScroll?: boolean;
}

/**
 * A `PageLayout` preset: the grid-backed, self-padded variant that the pre-redesign
 * screens use. It differs from the base `PageLayout` in exactly three deliberate
 * ways, all so the migrating screens stay visually identical:
 *
 * 1. `background` defaults to `<GridBackground />` (the base layout has no default).
 * 2. `padding="none"` — these screens pad their own content.
 * 3. `contentContainerStyle={{ gap: 0 }}` — they space their own sections.
 *
 * This was previously the `@deprecated` `components/page-layout` shim. It is promoted
 * here — a real, named member of the design system rather than a shim under a
 * duplicate `PageLayout` name — so a screen picks the base `PageLayout` or this grid
 * preset explicitly. As screens adopt the redesign (opt-in background, standard
 * padding), they move to the base `PageLayout` and this preset's call sites shrink.
 *
 * The old shim's `className` / `scrollClassName` props are gone: they were accepted
 * and ignored (no call site passed them), so they are dropped rather than carried.
 */
export function GridPageLayout({
  children,
  header,
  fixedFooter,
  background,
  disableScroll = false,
}: GridPageLayoutProps) {
  return (
    <PageLayout
      header={header}
      footer={fixedFooter}
      scroll={!disableScroll}
      padding="none"
      contentContainerStyle={{ gap: 0 }}
      background={background ?? <GridBackground />}
    >
      {children}
    </PageLayout>
  );
}
