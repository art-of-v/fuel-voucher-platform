import type { TextStyle } from 'react-native';

/**
 * Typography — one deliberate hierarchy.
 *
 * What this replaces: `baseTokens.typography`, which had zero consumers (every
 * screen hand-wrote `fontSize`/`fontFamily`), and a house style in which the
 * dominant typographic device was an 8–10px uppercase label with 4–8px letter
 * spacing. That produced screens where the smallest, hardest-to-read element
 * carried the label and the largest element carried decoration.
 *
 * Rules encoded here:
 *  1. **Nothing smaller than 12px.** 8px and 9px text existed in 14 files.
 *  2. **Letter spacing never exceeds 0.4.** The old scale went to 8.
 *  3. **Uppercase is a role, not a default.** Exactly two roles are uppercase
 *     (`sectionTitle`, `label`); everything else is sentence case.
 *  4. **Rajdhani is for numbers and titles; Inter is for prose.** Rajdhani is a
 *     condensed display face — it is wrong for body copy and right for prices,
 *     litre counts and screen titles.
 *  5. **Font scaling stays on**, bounded per role by `maxFontSizeMultiplier`.
 *     Controls cap at 1.3 (they live in fixed-height touch targets); prose caps
 *     at 1.6. The previous global `allowFontScaling: false` is removed.
 */

/** Font families, matching the faces registered in `app/_layout.tsx`. */
export const fonts = {
  /** Rajdhani 700 — display, titles, numeric values. */
  display: 'Rajdhani-Bold',
  /** Rajdhani 600 — secondary display. */
  displayMedium: 'Rajdhani-SemiBold',
  /** Rajdhani 400. */
  displayRegular: 'Rajdhani',
  /** Inter 400 — body copy. */
  body: 'Inter',
  /** Inter 500 — labels, quiet emphasis. */
  bodyMedium: 'Inter-Medium',
  /** Inter 700 — emphasis, buttons. */
  bodyBold: 'Inter-Bold',
  /**
   * Inter 900. Retained because unmigrated screens reference it, but it is not
   * used by any role below — 900 weight at 9px was the old label style.
   */
  bodyBlack: 'Inter-Black',
} as const;

export type TextRole =
  | 'display'
  | 'title'
  | 'heading'
  | 'sectionTitle'
  | 'body'
  | 'bodyStrong'
  | 'secondary'
  | 'caption'
  | 'label'
  | 'numericLarge'
  | 'numeric'
  | 'numericSmall'
  | 'button'
  | 'buttonSmall';

export interface TypeStyle {
  fontFamily: string;
  fontSize: number;
  lineHeight: number;
  letterSpacing: number;
  textTransform?: TextStyle['textTransform'];
  /** Accessibility scaling cap for this role. */
  maxFontSizeMultiplier: number;
}

export const typeScale: Record<TextRole, TypeStyle> = {
  /** One per screen at most: a hero value or a landing statement. */
  display: {
    fontFamily: fonts.display,
    fontSize: 34,
    lineHeight: 38,
    letterSpacing: -0.6,
    maxFontSizeMultiplier: 1.25,
  },
  /** Screen title. The "where am I" line in a header. */
  title: {
    fontFamily: fonts.display,
    fontSize: 26,
    lineHeight: 30,
    letterSpacing: -0.4,
    maxFontSizeMultiplier: 1.3,
  },
  /** Card title, dialog title, major in-content heading. */
  heading: {
    fontFamily: fonts.display,
    fontSize: 20,
    lineHeight: 24,
    letterSpacing: -0.2,
    maxFontSizeMultiplier: 1.35,
  },
  /** Groups content within a screen. Uppercase by role. */
  sectionTitle: {
    fontFamily: fonts.bodyBold,
    fontSize: 13,
    lineHeight: 16,
    letterSpacing: 0.3,
    textTransform: 'uppercase',
    maxFontSizeMultiplier: 1.3,
  },
  /** Default reading size. */
  body: {
    fontFamily: fonts.body,
    fontSize: 15,
    lineHeight: 22,
    letterSpacing: 0,
    maxFontSizeMultiplier: 1.6,
  },
  /** Body with emphasis — a name, a selected option, an answer. */
  bodyStrong: {
    fontFamily: fonts.bodyBold,
    fontSize: 15,
    lineHeight: 22,
    letterSpacing: 0,
    maxFontSizeMultiplier: 1.6,
  },
  /** Supporting copy: descriptions, addresses, helper text. */
  secondary: {
    fontFamily: fonts.body,
    fontSize: 13,
    lineHeight: 18,
    letterSpacing: 0,
    maxFontSizeMultiplier: 1.5,
  },
  /** Metadata: dates, counts, footnotes. The smallest role. */
  caption: {
    fontFamily: fonts.body,
    fontSize: 12,
    lineHeight: 16,
    letterSpacing: 0,
    maxFontSizeMultiplier: 1.4,
  },
  /** Field labels and badge text. The only other uppercase role. */
  label: {
    fontFamily: fonts.bodyMedium,
    fontSize: 12,
    lineHeight: 16,
    letterSpacing: 0.4,
    textTransform: 'uppercase',
    maxFontSizeMultiplier: 1.3,
  },
  /** A total, a balance — the number the screen is about. */
  numericLarge: {
    fontFamily: fonts.display,
    fontSize: 32,
    lineHeight: 36,
    letterSpacing: -0.6,
    maxFontSizeMultiplier: 1.25,
  },
  /** A price or quantity in a card or row. */
  numeric: {
    fontFamily: fonts.display,
    fontSize: 22,
    lineHeight: 26,
    letterSpacing: -0.2,
    maxFontSizeMultiplier: 1.3,
  },
  /** An inline amount inside a sentence or a compact row. */
  numericSmall: {
    fontFamily: fonts.bodyBold,
    fontSize: 14,
    lineHeight: 18,
    letterSpacing: 0,
    maxFontSizeMultiplier: 1.4,
  },
  /** Default control label. */
  button: {
    fontFamily: fonts.bodyBold,
    fontSize: 15,
    lineHeight: 20,
    letterSpacing: 0.2,
    maxFontSizeMultiplier: 1.3,
  },
  /** Control label in a compact control. */
  buttonSmall: {
    fontFamily: fonts.bodyBold,
    fontSize: 13,
    lineHeight: 16,
    letterSpacing: 0.2,
    maxFontSizeMultiplier: 1.3,
  },
};

/**
 * Smallest permitted font size anywhere in the product. Enforced by review, not
 * by the type system — but documented so the number is not a matter of opinion.
 */
export const MIN_FONT_SIZE = 12;
