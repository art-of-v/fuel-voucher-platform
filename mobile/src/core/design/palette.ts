/**
 * Status palette — semantic status colours that must read correctly on every
 * supported theme.
 *
 * Why this file exists: before Phase 2, status colour was hardcoded per
 * component (`#22c55e`, `#F59E0B`, `#EF4444`, `#a855f7`, `#3b82f6`), which meant
 * a "fulfilled" green sat next to the `lemberg` brand green `#16FF00` and read
 * as a rendering bug. Status is now resolved once, here, and exposed through
 * semantic tokens.
 *
 * Status colour depends on the *canvas*, not on the brand — a green that is
 * legible on `#000000` is not the same green that is legible on `#FAF9F6`. So we
 * keep two sets and pick by `theme.isDark`. A theme may override individual
 * roles via `ThemeColors.status` when its canvas is unusual.
 *
 * Each role has four parts so that a component never has to invent a tint:
 *   base   — the colour of the icon/label/indicator itself
 *   onBase — text/icon colour when `base` is used as a fill
 *   subtle — low-emphasis container fill (badges, inline feedback, rows)
 *   border — outline for `subtle` containers
 */

export interface StatusRole {
  base: string;
  onBase: string;
  subtle: string;
  border: string;
}

export interface StatusPalette {
  success: StatusRole;
  warning: StatusRole;
  danger: StatusRole;
  info: StatusRole;
  /**
   * Neutral status: "used", "expired", "archived", "closed". A spent voucher is
   * not an error, and before Phase 2 it shared the brand colour with an active
   * one — so redemption state was not encoded in colour at all.
   */
  neutral: StatusRole;
}

/** Roles legible on dark canvases (`#000000`–`#1E293B`). */
const dark: StatusPalette = {
  success: {
    base: '#34D399',
    onBase: '#04231A',
    subtle: 'rgba(52, 211, 153, 0.14)',
    border: 'rgba(52, 211, 153, 0.32)',
  },
  warning: {
    base: '#FBBF24',
    onBase: '#241A02',
    subtle: 'rgba(251, 191, 36, 0.14)',
    border: 'rgba(251, 191, 36, 0.32)',
  },
  danger: {
    base: '#FF5D5D',
    onBase: '#2A0606',
    subtle: 'rgba(255, 93, 93, 0.14)',
    border: 'rgba(255, 93, 93, 0.34)',
  },
  info: {
    base: '#7DD3FC',
    onBase: '#04202E',
    subtle: 'rgba(125, 211, 252, 0.14)',
    border: 'rgba(125, 211, 252, 0.30)',
  },
  neutral: {
    base: 'rgba(255, 255, 255, 0.55)',
    onBase: '#0A0A0A',
    subtle: 'rgba(255, 255, 255, 0.08)',
    border: 'rgba(255, 255, 255, 0.16)',
  },
};

/** Roles legible on light canvases (`#F7F7F5`–`#FFFFFF`). */
const light: StatusPalette = {
  success: {
    base: '#047857',
    onBase: '#FFFFFF',
    subtle: 'rgba(4, 120, 87, 0.10)',
    border: 'rgba(4, 120, 87, 0.24)',
  },
  warning: {
    base: '#A16207',
    onBase: '#FFFFFF',
    subtle: 'rgba(161, 98, 7, 0.10)',
    border: 'rgba(161, 98, 7, 0.24)',
  },
  danger: {
    base: '#B91C1C',
    onBase: '#FFFFFF',
    subtle: 'rgba(185, 28, 28, 0.09)',
    border: 'rgba(185, 28, 28, 0.22)',
  },
  info: {
    base: '#0E7490',
    onBase: '#FFFFFF',
    subtle: 'rgba(14, 116, 144, 0.10)',
    border: 'rgba(14, 116, 144, 0.24)',
  },
  neutral: {
    base: 'rgba(0, 0, 0, 0.50)',
    onBase: '#FFFFFF',
    subtle: 'rgba(0, 0, 0, 0.05)',
    border: 'rgba(0, 0, 0, 0.12)',
  },
};

export const statusPalettes = { dark, light };

export function getStatusPalette(
  isDark: boolean,
  overrides?: Partial<StatusPalette>,
): StatusPalette {
  const base = isDark ? dark : light;
  if (!overrides) return base;
  return {
    success: { ...base.success, ...overrides.success },
    warning: { ...base.warning, ...overrides.warning },
    danger: { ...base.danger, ...overrides.danger },
    info: { ...base.info, ...overrides.info },
    neutral: { ...base.neutral, ...overrides.neutral },
  };
}

/**
 * Fuel-brand accents. These identify a third-party brand, so they are *data*,
 * not theme colours, and are deliberately exempt from the status palette. They
 * must never be used to carry state — that is what the status roles are for.
 */
export const BRAND_COLORS: Record<string, string> = {
  okko: '#16FF00',
  wog: '#008B45',
  upg: '#00C853',
  klo: '#FFCE00',
  // `shell` and `socar` used to live as raw hex literals inside `app/my-codes.tsx`,
  // which meant the wallet knew two brand colours the rest of the app did not.
  shell: '#FF0000',
  socar: '#C0C0C0',
};
