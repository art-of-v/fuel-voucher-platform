/**
 * Layout primitives: spacing, radius, elevation, touch targets, z-index and the
 * fixed chrome dimensions the layout system owns.
 *
 * Everything in this file exists so that no screen has to guess. The audit found
 * `paddingBottom: 150` in the shared layout *plus* `paddingBottom: 100` in the
 * screens that use it, `84` in one footer, and a hardcoded `bottom: 8` on the tab
 * bar with `useSafeAreaInsets()` imported and never applied. Those numbers are
 * now derived, in one place, from the chrome that actually exists.
 */

import { StyleSheet } from 'react-native';

// ─────────────────────────────────────────────────────────────────────────────
// Spacing — a 4px base unit.
// ─────────────────────────────────────────────────────────────────────────────

export const spacing = {
  /** 2 — hairline gaps, icon-to-glyph nudges. */
  xxs: 2,
  /** 4 */
  xs: 4,
  /** 8 — inside a chip, between an icon and its label. */
  sm: 8,
  /** 12 — inside a compact row. */
  md: 12,
  /** 16 — default gap between siblings; default card padding. */
  lg: 16,
  /** 20 — generous card padding. */
  xl: 20,
  /** 24 — screen horizontal padding. */
  '2xl': 24,
  /** 32 — between content groups. */
  '3xl': 32,
  /** 40 — between major sections. */
  '4xl': 40,
  /** 56 — above a terminal element (empty state, success). */
  '5xl': 56,

  // Named aliases retained from the previous token set (widely referenced).
  /** Screen horizontal padding. */
  containerPadding: 24,
  /** Gap between cards in a list. */
  cardGap: 16,
  /** Gap between titled sections. */
  sectionGap: 32,
  /** Platform hairline. */
  hairline: StyleSheet.hairlineWidth,
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Radius — one scale, all themes.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Shape philosophy: **moderate, consistent, and hierarchical.**
 *
 * Rejected: `radius: 2` (the sharp HUD language — it reads as a technical
 * console, which is wrong for a product about money and legal documents) and
 * full pills for containers (playful, and it wastes the horizontal space that
 * numeric alignment needs).
 *
 * Radius encodes **how far a surface is from the page**, so it must decrease as
 * you nest inward:
 *
 *     sheet (20) > card (14) > control (10) > badge (6)
 *
 * A child's radius is always smaller than its parent's. `full` is reserved for
 * things that are genuinely circular — status dots, avatars, icon-only round
 * affordances — never for rectangles.
 */
export const radius = {
  none: 0,
  /** 6 — badges, chips, tags, inline code. */
  sm: 6,
  /** 10 — buttons, text fields, icon buttons, list rows, segmented controls. */
  md: 10,
  /** 14 — cards, tiles, panels. */
  lg: 14,
  /** 20 — bottom sheets, dialogs, full-bleed modals. */
  xl: 20,
  /** 999 — circles only. */
  full: 999,
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Elevation.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Surface differentiation rule: **a surface uses a border OR a shadow, never
 * both.**
 *
 * - Content surfaces (cards, rows, tiles) are differentiated by *colour +
 *   hairline border*. No shadow. This keeps dense lists calm and works on the
 *   translucent `glass` theme, where a shadow has nothing to fall on.
 * - Floating surfaces (sheets, dialogs, toasts, the tab bar) are differentiated
 *   by *shadow*, because they must read as detached from the content beneath.
 *
 * The audit found borders, shadows and glows applied simultaneously, plus
 * `shadowRadius: 10` glow blobs used as an active-state indicator. Glow is not
 * part of this system.
 */
export const elevation = {
  /** Flush with the canvas. */
  none: {},
  /** Content surface: card, row, tile. Border does the work; see `Card`. */
  flat: {},
  /** Menus, popovers, the tab bar. */
  low: {
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.12,
    shadowRadius: 8,
    elevation: 3,
  },
  /** Bottom sheets, toasts. */
  medium: {
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 6 },
    shadowOpacity: 0.18,
    shadowRadius: 16,
    elevation: 8,
  },
  /** Dialogs — the only thing above a sheet. */
  high: {
    shadowColor: '#000000',
    shadowOffset: { width: 0, height: 12 },
    shadowOpacity: 0.24,
    shadowRadius: 28,
    elevation: 16,
  },
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Touch targets & control sizing.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Minimum touch target, both platforms: **44×44**. Anything visually smaller
 * must extend its target with `hitSlop` — see `hitSlopFor`.
 *
 * The audit found a 40×40 icon-only contract-signing button, a 32×32 modal
 * close, and a `paddingVertical: 7` "PAY" button around 9px text.
 */
export const TOUCH_TARGET_MIN = 44;

export const control = {
  /** 36 — compact chips and inline controls. Requires `hitSlop`. */
  sm: 36,
  /** 44 — icon buttons, secondary actions. Meets the minimum exactly. */
  md: 44,
  /** 52 — text fields, selects, list rows, default buttons. */
  lg: 52,
  /** 56 — the primary action on a screen. */
  xl: 56,
} as const;

/** `hitSlop` needed to bring a control of `size` up to the 44pt minimum. */
export function hitSlopFor(size: number) {
  const pad = Math.max(0, Math.ceil((TOUCH_TARGET_MIN - size) / 2));
  return { top: pad, bottom: pad, left: pad, right: pad };
}

/** Minimum gap between two adjacent independent targets. */
export const TOUCH_TARGET_GAP = spacing.sm;

// ─────────────────────────────────────────────────────────────────────────────
// Chrome dimensions — the numbers the layout system derives padding from.
// ─────────────────────────────────────────────────────────────────────────────

export const chrome = {
  /** Height of the bottom tab bar's touch row, excluding the safe-area inset. */
  tabBarHeight: 56,
  /** Height of a screen header row (the back/title/action line). */
  headerHeight: 56,
  /** Breathing room between the last content item and the chrome below it. */
  contentBottomGap: spacing.lg,
  /** Height of a sticky footer action bar, excluding the safe-area inset. */
  footerHeight: 72,
  /** Max width a bottom sheet's grabber occupies. */
  sheetGrabberWidth: 36,
  /** Fraction of screen height a sheet may occupy before it must scroll. */
  sheetMaxHeightRatio: 0.9,
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Z-index — a scale, so nothing has to invent `zIndex: 100`.
// ─────────────────────────────────────────────────────────────────────────────

export const zIndex = {
  background: 0,
  content: 1,
  sticky: 10,
  header: 20,
  footer: 30,
  tabBar: 40,
  sheet: 50,
  dialog: 60,
  toast: 70,
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Motion.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Animation is used for two things only: **acknowledging a press** and
 * **explaining where a surface came from**. It is never decorative — there are
 * no looping animations in this system (the audit found a 2px red laser line
 * looping permanently across a scannable QR code).
 */
export const motion = {
  /** Press feedback, state cross-fades. */
  fast: 120,
  /** Sheet/dialog entry and exit. */
  normal: 220,
  /** Full-screen transitions. */
  slow: 320,
  /** Scale applied on press to a card or row. */
  pressScale: 0.985,
  /** Opacity applied on press to a control that must not resize. */
  pressOpacity: 0.7,
  /** Spring config used by `PressableScale`. */
  spring: { friction: 8, tension: 220 },
} as const;

export type Spacing = keyof typeof spacing;
export type Radius = keyof typeof radius;
export type Elevation = keyof typeof elevation;
