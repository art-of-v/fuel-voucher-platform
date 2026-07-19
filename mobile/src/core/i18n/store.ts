import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { translations, Language } from './translations';

interface I18nStore {
  language: Language;
  setLanguage: (lang: Language) => void;
  t: (key: string) => string;
}

export const useI18n = create<I18nStore>()(
  persist(
    (set, get) => ({
      language: 'uk',
      setLanguage: (lang) => set({ language: lang }),
      t: (key: string) => {
        const lang = get().language;
        return translations[lang]?.[key] || translations['en']?.[key] || key;
      },
    }),
    {
      name: 'lemberg-language',
      storage: createJSONStorage(() => AsyncStorage),
    },
  ),
);

export const languages: { code: Language; name: string; flag: string }[] = [
  { code: 'en', name: 'English', flag: '🇬🇧' },
  { code: 'uk', name: 'Українська', flag: '🇺🇦' },
  { code: 'de', name: 'Deutsch', flag: '🇩🇪' },
  { code: 'es', name: 'Español', flag: '🇪🇸' },
];
