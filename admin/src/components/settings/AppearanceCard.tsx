import { Palette, Smartphone } from "lucide-react";
import { useI18n } from "@/lib/i18n";
import { ThemeSwitcher } from "@/components/ThemeSwitcher";
import { themeOptions } from "@/lib/themes";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

/**
 * The two appearances, in one file because they are the same question asked twice and must never
 * be confused for one another.
 *
 * <p>
 * The top card is the operator's own: it persists to localStorage (`theme-store.ts`), it is
 * per-browser, and it paints the panel in front of you. The bottom card is the installation's: it
 * is a server setting, one value for everybody, and it decides what the employee-facing app opens
 * in. The wording of each hint says which is which, because an operator who reads "Color theme"
 * next to "Applies to this browser only" would reasonably conclude neither one reaches a phone.
 * </p>
 */
export function AppearanceCard() {
  const { t } = useI18n();

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Palette className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t("appearance.title")}</h2>
      </div>

      <div className="bg-card border border-border rounded-xl p-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">{t("appearance.theme")}</p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("appearance.themeHint")}
            </p>
          </div>
          <ThemeSwitcher />
        </div>
      </div>
    </div>
  );
}

/**
 * The theme every employee's app opens in, saved on the server for the whole installation.
 *
 * <p>
 * It is a default and not a directive, and the hint says so: a worker who has chosen a theme in
 * their profile keeps it. The list comes from the server (`appearance.available`) rather than from
 * the panel's own catalogue, because the two are not the same list — the panel has the four forge
 * treatments and the phone has no palette for them, so offering one here would be an error the
 * operator could only discover by triggering it.
 * </p>
 */
export function AppDefaultThemeCard({
  theme,
  available,
  onChange,
}: {
  theme: string;
  available: string[];
  onChange: (theme: string) => void;
}) {
  const { t } = useI18n();

  // Labels come from the panel's own catalogue so a theme is named the same way in both pickers.
  // An id the panel does not know keeps its raw name rather than vanishing from the list, so a
  // newly added phone theme is still selectable before the panel catches up.
  const options = available.map((id) => ({
    id,
    label: themeOptions.find((opt) => opt.id === id)?.label ?? id,
    swatch: themeOptions.find((opt) => opt.id === id)?.swatch ?? "transparent",
  }));

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Smartphone className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t("appearance.appTheme")}</h2>
      </div>

      <div className="bg-card border border-border rounded-xl p-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">
              {t("appearance.defaultTheme")}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("appearance.appThemeHint")}
            </p>
          </div>
          <Select value={theme} onValueChange={onChange}>
            <SelectTrigger className="w-[170px]">
              <SelectValue placeholder={t("appearance.defaultTheme")} />
            </SelectTrigger>
            <SelectContent>
              {options.map((opt) => (
                <SelectItem key={opt.id} value={opt.id}>
                  <span className="flex items-center gap-2">
                    <span
                      className="h-3.5 w-3.5 rounded-full border border-border shrink-0"
                      style={{ backgroundColor: opt.swatch }}
                    />
                    {opt.label}
                  </span>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>
    </div>
  );
}
