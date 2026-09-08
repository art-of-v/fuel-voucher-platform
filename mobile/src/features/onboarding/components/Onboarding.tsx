import { useState } from 'react';
import { View, StyleSheet } from 'react-native';
import { Fuel, Lock, QrCode, ShieldCheck } from 'lucide-react-native';
import { Text, Button } from '@/core/ui';
import { useDesignTokens } from '@/core/hooks/useTheme';
import { useI18n } from '@/core/i18n';

/**
 * First-run onboarding (Apple Guideline 5.1.1 — data collection must be
 * explained before sign-up). Three value slides plus a dedicated phone-number
 * disclosure slide, shown once before the phone-registration form. Slide
 * advance is tap-driven (no swipe gestures needed — clarity before character),
 * the dots and the primary label reflect the current step, and Skip ends the
 * flow immediately at any point.
 */

const SLIDES = [
  { icon: Fuel, titleKey: 'onboarding.slide1.title', bodyKey: 'onboarding.slide1.body' },
  { icon: Lock, titleKey: 'onboarding.slide2.title', bodyKey: 'onboarding.slide2.body' },
  { icon: QrCode, titleKey: 'onboarding.slide3.title', bodyKey: 'onboarding.slide3.body' },
  { icon: ShieldCheck, titleKey: 'onboarding.slide4.title', bodyKey: 'onboarding.slide4.body' },
] as const;

const LAST = SLIDES.length - 1;

export function Onboarding({ onFinish }: { onFinish: () => void }) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const [index, setIndex] = useState(0);

  const slide = SLIDES[index];
  const Icon = slide.icon;
  const isLast = index === LAST;

  const s = StyleSheet.create({
    root: { flex: 1 },
    topRow: { alignItems: 'flex-end', paddingBottom: tokens.spacing.sm },
    skip: { minHeight: tokens.touchTarget.min, paddingHorizontal: tokens.spacing.sm, justifyContent: 'center' },
    body: { flex: 1, justifyContent: 'center', gap: tokens.spacing['2xl'] },
    iconSlot: {
      alignSelf: 'center',
      width: 96,
      height: 96,
      borderRadius: tokens.radius.xl,
      backgroundColor: tokens.colors.surfaceSunken,
      borderWidth: 1,
      borderColor: tokens.colors.border,
      alignItems: 'center',
      justifyContent: 'center',
    },
    copy: { gap: tokens.spacing.md, alignItems: 'center' },
    title: { textAlign: 'center' },
    bodyText: { textAlign: 'center', color: tokens.colors.text.secondary },
    badge: { alignSelf: 'center' },
    dots: { flexDirection: 'row', justifyContent: 'center', gap: tokens.spacing.sm, paddingVertical: tokens.spacing.lg },
    dot: { width: 8, height: 8, borderRadius: tokens.radius.full },
    dotActive: { width: 24 },
  });

  return (
    <View style={s.root}>
      {/* Skip — always available, never forced through the flow */}
      <View style={s.topRow}>
        <Button
          label={t('onboarding.skip')}
          variant="ghost"
          size="sm"
          style={s.skip}
          onPress={onFinish}
        />
      </View>

      <View style={s.body}>
        <View style={s.iconSlot}>
          <Icon size={40} color={tokens.colors.primary} />
        </View>

        <View style={s.copy}>
          <Text role="title" style={s.title}>{t(slide.titleKey)}</Text>
          <Text role="body" style={s.bodyText}>{t(slide.bodyKey)}</Text>
          {index === LAST && (
            <Text role="label" tone="muted" style={s.badge}>
              {t('onboarding.slide4.badge')}
            </Text>
          )}
        </View>
      </View>

      <View style={s.dots}>
        {SLIDES.map((_, i) => (
          <View
            key={i}
            style={[
              s.dot,
              i === index
                ? [s.dotActive, { backgroundColor: tokens.colors.primary }]
                : { backgroundColor: tokens.colors.borderStrong },
            ]}
          />
        ))}
      </View>

      <Button
        label={isLast ? t('onboarding.start') : t('onboarding.next')}
        variant="primary"
        size="lg"
        fullWidth
        onPress={() => (isLast ? onFinish() : setIndex(i => i + 1))}
      />
    </View>
  );
}