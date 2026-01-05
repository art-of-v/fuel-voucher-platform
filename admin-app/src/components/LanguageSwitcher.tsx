import { useI18n, languages } from "@/lib/i18n";
import { Globe } from "lucide-react";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@/components/ui/select";

export function LanguageSwitcher() {
    const { language, setLanguage } = useI18n();

    return (
        <Select value={language} onValueChange={(val: any) => setLanguage(val)}>
            <SelectTrigger className="w-[140px] bg-transparent border-none focus:ring-0 text-muted-foreground hover:text-foreground">
                <div className="flex items-center gap-2">
                    <Globe className="h-4 w-4" />
                    <SelectValue placeholder="Language" />
                </div>
            </SelectTrigger>
            <SelectContent>
                {languages.map((lang) => (
                    <SelectItem key={lang.code} value={lang.code}>
                        <span className="mr-2 text-lg">{lang.flag}</span>
                        {lang.name}
                    </SelectItem>
                ))}
            </SelectContent>
        </Select>
    );
}
