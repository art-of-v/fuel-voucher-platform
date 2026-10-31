import { create } from "zustand";
import { persist } from "zustand/middleware";
import { brands } from "./locales/brands";
import { brandRefusals } from "./locales/brandRefusals";
import { common } from "./locales/common";
import { contracts } from "./locales/contracts";
import { imports } from "./locales/imports";
import { operations } from "./locales/operations";
import { orders } from "./locales/orders";
import { reconciliation } from "./locales/reconciliation";
import { redemptionRefusals } from "./locales/redemptionRefusals";
import { report } from "./locales/report";
import { settings } from "./locales/settings";
import { settingsAutomation } from "./locales/settingsAutomation";
import { ui } from "./locales/ui";
import { users } from "./locales/users";
import { voucherExchange } from "./locales/voucherExchange";
import { vouchers } from "./locales/vouchers";

export type Language = "en" | "uk" | "de" | "es";

export const languages: { code: Language; name: string; flag: string }[] = [
  { code: "en", name: "English", flag: "🇬🇧" },
  { code: "uk", name: "Українська", flag: "🇺🇦" },
  { code: "de", name: "Deutsch", flag: "🇩🇪" },
  { code: "es", name: "Español", flag: "🇪🇸" },
];

const localeModules = [
  brands,
  brandRefusals,
  common,
  contracts,
  imports,
  operations,
  orders,
  reconciliation,
  redemptionRefusals,
  report,
  settings,
  settingsAutomation,
  ui,
  users,
  voucherExchange,
  vouchers,
];

const merge = (lang: Language): Record<string, string> =>
  Object.assign({}, ...localeModules.map((module) => module[lang]));

// Exported so a test can compare the blocks against each other: they are edited by hand and
// nothing else in the build notices when they drift. See i18n-parity.test.ts.
export const translations: Record<Language, Record<string, string>> = {
  en: merge("en"),
  uk: merge("uk"),
  de: merge("de"),
  es: merge("es"),
};

interface I18nStore {
  language: Language;
  setLanguage: (lang: Language) => void;
  /**
   * The parameters are the values a translation interpolates (`{0}`, `{1}`, ...).
   *
   * They are numbers as often as they are strings - a count of payments, a quantity of litres -
   * and the replacement below has always coerced them. The type says so, because the alternative
   * was a caller casting every number to a string by hand to satisfy a signature that lied.
   */
  t: (key: string, ...params: (string | number)[]) => string;
}

export const useI18n = create<I18nStore>()(
  persist(
    (set, get) => ({
      language: "uk", // Default to Ukrainian
      setLanguage: (lang) => set({ language: lang }),
      t: (key: string, ...params: (string | number)[]) => {
        const lang = get().language;
        let translation =
          translations[lang]?.[key] || translations["en"]?.[key] || key;
        params.forEach((param, i) => {
          translation = translation.replace(`{${i}}`, String(param));
        });
        return translation;
      },
    }),
    {
      name: "admin-lemberg-language", // Unique storage key for admin
    },
  ),
);
