
import React, { useState } from 'react';
import { View, Text, TouchableOpacity, ScrollView } from 'react-native';
import { Globe, X } from 'lucide-react-native';
import { useI18n, languages, Language } from '@/lib/i18n';
import { cn } from '@/lib/utils';

export function LanguageSelector() {
    const { language, setLanguage } = useI18n();
    const [isOpen, setIsOpen] = useState(false);

    const currentLang = languages.find(l => l.code === language);

    return (
        <View className="relative z-50">
            <TouchableOpacity
                onPress={() => setIsOpen(!isOpen)}
                className="px-4 py-2 bg-black border-2 border-[#00FF80]/50 rounded-none active:scale-[0.98] shadow-[0_0_15px_rgba(0,255,128,0.2)]"
            >
                <Text className="text-xs font-black text-[#00FF80] font-mono tracking-[0.2em] text-glow-intense">{currentLang?.code.toUpperCase()}</Text>
            </TouchableOpacity>

            {isOpen && (
                <View className="absolute top-12 right-0 w-56 bg-[#050505] border-2 border-[#00FF80]/30 rounded-none shadow-[0_0_50px_rgba(0,255,128,0.3)] overflow-hidden">
                    <View className="bg-black px-4 py-3 border-b-2 border-white/5 flex-row justify-between items-center">
                        <Text className="text-[10px] text-gray-400 font-heading uppercase tracking-[0.3em] font-black">SELECT_LOCALE</Text>
                        <TouchableOpacity onPress={() => setIsOpen(false)} className="active:scale-[0.98]">
                            <X size={16} color="#444" />
                        </TouchableOpacity>
                    </View>
                    <ScrollView className="max-h-80">
                        {languages.map((lang) => (
                            <TouchableOpacity
                                key={lang.code}
                                onPress={() => {
                                    setLanguage(lang.code);
                                    setIsOpen(false);
                                }}
                                className={cn(
                                    "flex-row items-center gap-4 px-5 py-5 border-b border-white/5 active:scale-[0.98]",
                                    language === lang.code ? "bg-[#00FF80]/5" : "bg-black"
                                )}
                            >
                                <Text className="text-xl">{lang.flag}</Text>
                                <Text className={cn(
                                    "text-sm font-heading uppercase tracking-[0.2em] font-black",
                                    language === lang.code ? "text-[#00FF80] text-glow-intense" : "text-gray-500"
                                )}>
                                    {lang.name}
                                </Text>
                                {language === lang.code && (
                                    <View className="ml-auto w-1.5 h-1.5 rounded-full bg-[#00FF80] shadow-[0_0_10px_#00FF80]" />
                                )}
                            </TouchableOpacity>
                        ))}
                    </ScrollView>
                </View>
            )}
        </View>
    );
}
