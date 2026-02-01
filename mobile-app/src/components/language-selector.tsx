import { useI18n, languages, type Language } from "@/lib/i18n";
import { Globe } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

export function LanguageSelector() {
  const { language, setLanguage } = useI18n();
  const currentLang = languages.find(l => l.code === language);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          data-testid="button-language-selector"
          className="flex items-center gap-2 px-3 py-2 bg-black/60 border border-white/20 hover:border-primary/50 transition-colors text-sm"
        >
          <Globe className="w-4 h-4 text-primary" />
          <span className="text-lg">{currentLang?.flag}</span>
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        className="bg-black/95 border-primary/30 backdrop-blur-xl"
      >
        {languages.map((lang) => (
          <DropdownMenuItem
            key={lang.code}
            data-testid={`button-language-${lang.code}`}
            onClick={() => setLanguage(lang.code)}
            className={`flex items-center gap-3 hover:bg-primary/10 ${language === lang.code ? 'text-primary' : 'text-gray-300'
              }`}
          >
            <span className="text-xl">{lang.flag}</span>
            <span className="font-heading uppercase">{lang.name}</span>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
