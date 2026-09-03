import React from 'react';
import { PageLayout as CorePageLayout } from '../core/ui/PageLayout';
import { GridBackground } from './grid-background';

interface PageLayoutProps {
  children: React.ReactNode;
  header?: React.ReactNode;
  fixedFooter?: React.ReactNode;
  background?: React.ReactNode;
  /** @deprecated Tailwind class strings on the layout are no longer honoured. */
  className?: string;
  /** @deprecated Tailwind class strings on the layout are no longer honoured. */
  scrollClassName?: string;
  disableScroll?: boolean;
}

/**
 * @deprecated Import `PageLayout` from `@/core/ui` instead.
 *
 * This is a compatibility shim over the design system's `PageLayout`, kept so the
 * thirteen screens still importing this path keep working while they migrate one at
 * a time. It differs from the real component in exactly two ways, both deliberate:
 *
 * 1. It defaults `background` to `<GridBackground />`, because that is what these
 *    screens look like today. The real `PageLayout` has no default background.
 * 2. It applies `padding="none"`, because these screens pad their own content.
 *
 * What it fixes for every caller at once: the fixed `paddingBottom: 150` is gone,
 * replaced by the derived clearance in `PageLayout`, and the footer now respects the
 * bottom safe-area inset (this file read `useSafeAreaInsets()` and never used it).
 *
 * The `className` / `scrollClassName` props are accepted and ignored. They were only
 * ever passed as `flex-1`-style utilities that duplicated the layout's own styles;
 * silently dropping them is safer than mapping NativeWind classes onto a component
 * that no longer uses them.
 */
export function PageLayout({
  children,
  header,
  fixedFooter,
  background,
  disableScroll = false,
}: PageLayoutProps) {
  return (
    <CorePageLayout
      header={header}
      footer={fixedFooter}
      scroll={!disableScroll}
      padding="none"
      // The real PageLayout applies vertical rhythm between children. These
      // screens already space their own sections, so the shim opts out to stay
      // visually identical.
      contentContainerStyle={{ gap: 0 }}
      background={background ?? <GridBackground />}
    >
      {children}
    </CorePageLayout>
  );
}
