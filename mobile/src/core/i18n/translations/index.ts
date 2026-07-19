import { en } from './en';
import { uk } from './uk';
import { de } from './de';
import { es } from './es';

export type Language = 'en' | 'uk' | 'de' | 'es';

export const translations: Record<Language, Record<string, string>> = {
  en,
  uk,
  de,
  es,
};
