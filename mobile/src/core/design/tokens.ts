import { ThemeType, themes, getThemeStatus } from './themes';
import { BRAND_COLORS } from './palette';
import { fonts, typeScale, MIN_FONT_SIZE } from './typography';
import {
  spacing,
  radius,
  elevation,
  control,
  chrome,
  zIndex,
  motion,
  hitSlopFor,
  TOUCH_TARGET_MIN,
  TOUCH_TARGET_GAP,
} from './layout';

export { BRAND_COLORS };

/**
 * The design token entry point.
 *
 * Consume via `useDesignTokens()` — never import `themes` or a raw hex directly
 * in a component.
 *
 * Structure:
 *   colors      semantic colour roles + resolved status palette
 *   type        the typography scale (semantic roles, not sizes)
 *   fonts       font family names
 *   spacing     4px-based scale
 *   radius      one shape scale, identical on every theme
 *   elevation   border-or-shadow presets
 *   control     control heights
 *   chrome      fixed chrome dimensions the layout system derives padding from
 *   zIndex      layering scale
 *   motion      durations and press physics
 *
 * Deprecated groups (`surface`, `glows`, `effects`, and the aliases inside
 * `colors`) exist only so that screens not yet migrated keep compiling. They are
 * marked, and `docs/design/DESIGN_SYSTEM.md` § Deprecated patterns lists them.
 */
export const baseTokens = {
  spacing,
  radius,
  elevation,
  control,
  chrome,
  zIndex,
  motion,
  fonts,
  type: typeScale,
  touchTarget: {
    min: TOUCH_TARGET_MIN,
    gap: TOUCH_TARGET_GAP,
    slopFor: hitSlopFor,
  },
  minFontSize: MIN_FONT_SIZE,

  /**
   * @deprecated Blur is retained only for the `glass` theme's frosted surfaces.
   * Do not add new blur.
   */
  effects: {
    blurIntensity: 40,
  },
} as const;

/**
 * @deprecated Shape is no longer theme-dependent. These keys are aliases over
 * the single `radius` scale, kept because nine files still branch on
 * `tokens.surface.soft`. `soft` is permanently `true`, so those ternaries now
 * resolve to one language; delete the branch when touching the file.
 */
const surfaceShape = {
  soft: true as const,
  card: radius.lg,
  button: radius.md,
  field: radius.md,
  icon: radius.md,
  pill: radius.full,
  accentWidth: 3,
};

export function getTokens(themeType: ThemeType = 'lemberg') {
  const themeColors = themes[themeType] || themes.lemberg;
  const status = getThemeStatus(themeType);

  return {
    ...baseTokens,
    surface: surfaceShape,
    colors: {
      ...themeColors,

      /** Scrim behind sheets, dialogs and full-screen overlays. */
      overlay: themeColors.isDark ? 'rgba(0, 0, 0, 0.72)' : 'rgba(17, 17, 20, 0.44)',

      text: {
        ...themeColors.text,
        /** Text/icon colour on a `primary` fill. */
        onPrimary: themeColors.onPrimary,
        /**
         * Third-party fuel-brand colours. Identity, not state — see
         * `palette.ts`.
         */
        brand: { ...BRAND_COLORS },
      },

      /**
       * Status roles. Each has `base` / `onBase` / `subtle` / `border`, resolved
       * for this theme's canvas. Never hardcode a status colour in a component.
       */
      status,

      /** Shorthands for the most common status usage (an icon or a label). */
      success: status.success.base,
      warning: status.warning.base,
      danger: status.danger.base,
      info: status.info.base,

      /**
       * @deprecated Use `status.danger`. Aliased to the semantic role so that
       * unmigrated screens become consistent without being edited.
       */
      error: status.danger.base,
    },

    /**
     * @deprecated Glows are not part of the design language. One consumer
     * remains (`components/glow-text.tsx`), which is itself deprecated.
     */
    glows: {
      primary: {
        low: {
          shadowColor: themeColors.primary,
          shadowOpacity: 0.4,
          shadowRadius: 5,
          shadowOffset: { width: 0, height: 0 },
        },
        medium: {
          shadowColor: themeColors.primary,
          shadowOpacity: 0.6,
          shadowRadius: 15,
          shadowOffset: { width: 0, height: 0 },
        },
        high: {
          shadowColor: themeColors.primary,
          shadowOpacity: 0.8,
          shadowRadius: 30,
          shadowOffset: { width: 0, height: 0 },
        },
      },
      text: { high: [5, 15, 30] },
    },
  };
}

export type DesignTokens = ReturnType<typeof getTokens>;
export const defaultTokens = getTokens('lemberg');
