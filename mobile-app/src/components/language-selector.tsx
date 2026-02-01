import { useI18n, languages, type Language } from "@/lib/i18n";
import { Globe, ChevronDown } from "lucide-react";
import { useState, useRef, useEffect } from "react";

export function LanguageSelector() {
  const { language, setLanguage } = useI18n();
  const currentLang = languages.find(l => l.code === language);
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const handleLanguageChange = (langCode: Language) => {
    console.log('[LanguageSelector] Changing language from', language, 'to', langCode);
    setLanguage(langCode);
    console.log('[LanguageSelector] Language changed to', langCode);
    setIsOpen(false);
  };

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      document.addEventListener('touchstart', handleClickOutside as any);
    }

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('touchstart', handleClickOutside as any);
    };
  }, [isOpen]);

  return (
    <div className="relative" ref={dropdownRef}>
      <button
        data-testid="button-language-selector"
        onClick={() => setIsOpen(!isOpen)}
        className="flex items-center gap-2 px-3 py-2 bg-black/60 border border-white/20 hover:border-primary/50 transition-colors text-sm active:scale-95"
      >
        <Globe className="w-4 h-4 text-primary" />
        <span className="text-lg">{currentLang?.flag}</span>
        <ChevronDown className={`w-3 h-3 text-primary transition-transform ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {isOpen && (
        <div className="absolute right-0 top-full mt-2 min-w-[180px] bg-black/95 border border-primary/30 backdrop-blur-xl z-[100] shadow-[0_0_20px_rgba(0,255,128,0.3)]">
          {languages.map((lang) => (
            <button
              key={lang.code}
              data-testid={`button-language-${lang.code}`}
              onClick={() => handleLanguageChange(lang.code)}
              className={`w-full flex items-center gap-3 px-4 py-3 hover:bg-primary/10 transition-colors active:scale-95 ${language === lang.code ? 'text-primary bg-primary/5' : 'text-gray-300'
                }`}
            >
              <span className="text-xl">{lang.flag}</span>
              <span className="font-heading uppercase text-sm">{lang.name}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
