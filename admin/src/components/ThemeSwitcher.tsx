import { Palette } from "lucide-react";
import { useTheme } from "@/lib/theme-store";
import { themeOptions } from "@/lib/themes";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export function ThemeSwitcher() {
  const { theme, setTheme } = useTheme();

  return (
    <Select
      value={theme}
      onValueChange={(val: string) => {
        // The select hands back a string; the store wants a theme id. An unknown id is
        // ignored rather than cast in.
        const match = themeOptions.find((opt) => opt.id === val);
        if (match) setTheme(match.id);
      }}
    >
      <SelectTrigger className="w-[150px] bg-transparent border-none focus:ring-0 text-muted-foreground hover:text-foreground">
        <div className="flex items-center gap-2">
          <Palette className="h-4 w-4" />
          <SelectValue placeholder="Theme" />
        </div>
      </SelectTrigger>
      <SelectContent>
        {themeOptions.map((opt) => (
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
  );
}
