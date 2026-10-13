import { useCallback, useEffect, useMemo, useState } from 'react';
import { View, StyleSheet, Linking } from 'react-native';
import { MessageSquare, ShieldCheck, MessagesSquare, ArrowRight } from 'lucide-react-native';

import { PageLayout, ScreenHeader, Card, Text, Button, LoadingState, ErrorState } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import {
  getFeedbackDeepLink,
  openTelegramChat,
  type FeedbackDeepLink,
} from '../src/features/feedback/api/feedback';

/**
 * The feedback explainer.
 *
 * Why a screen and not a direct deep link from the tab: a button that silently throws someone
 * into a third-party messenger with no explanation is how "where did my message go" support
 * tickets start. This screen says what the channel is, who can see the message, and what happens
 * to it — and only then hands over.
 *
 * The screen also survives the messenger not being installed: Telegram is absent on some phones,
 * and the fallback is the web form we already have rather than a dead button.
 */
export default function FeedbackScreen() {
  const { t } = useI18n();
  const tokens = useDesignTokens();

  const [link, setLink] = useState<FeedbackDeepLink | null>(null);
  const [available, setAvailable] = useState<boolean | null>(null);
  const [loading, setLoading] = useState(true);
  // A flag, not the message: the copy is rendered from `t` at render time, so switching language
  // re-labels it. It also keeps `t` out of the fetch effect's dependencies - `useI18n` returns a
  // fresh `t` each render, and an effect depending on it would re-fetch forever.
  const [failed, setFailed] = useState(false);
  const [telegramMissing, setTelegramMissing] = useState(false);

  const styles = useMemo(
    () =>
      StyleSheet.create({
        content: { gap: tokens.spacing.lg },
        card: { gap: tokens.spacing.md },
        row: {
          flexDirection: 'row',
          gap: tokens.spacing.md,
          alignItems: 'flex-start',
        },
        icon: { marginTop: tokens.spacing.xxs },
        bullets: { gap: tokens.spacing.sm },
        actions: { gap: tokens.spacing.sm, marginTop: tokens.spacing.sm },
      }),
    [tokens],
  );

  const load = useCallback(async () => {
    setLoading(true);
    setFailed(false);
    try {
      const deepLink = await getFeedbackDeepLink();
      setLink(deepLink);
      setAvailable(deepLink !== null);
    } catch {
      // A network failure is not the same as the feature being off: say so, and keep the tab
      // rather than silently removing the only way to report a problem.
      setFailed(true);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const openChat = async () => {
    if (!link) return;
    const opened = await openTelegramChat(link.url);
    setTelegramMissing(!opened);
  };

  const openWebForm = () => Linking.openURL('https://palne.shop/support');

  if (loading) {
    return (
      <PageLayout hasTabBar>
        <ScreenHeader title={t('nav.feedback')} hideBack />
        <LoadingState />
      </PageLayout>
    );
  }

  // The tab is hidden when the server reports the channel off, so reaching this state means the
  // switch was flipped while the screen was open.
  if (available === false) {
    return (
      <PageLayout hasTabBar>
        <ScreenHeader title={t('nav.feedback')} hideBack />
        <ErrorState
          title={t('feedback.unavailableTitle')}
          description={t('feedback.unavailableBody')}
          secondaryAction={{
            label: t('feedback.openWebForm'),
            onPress: openWebForm,
          }}
        />
      </PageLayout>
    );
  }

  return (
    <PageLayout hasTabBar>
      <ScreenHeader title={t('nav.feedback')} subtitle={t('feedback.subtitle')} hideBack />

      <View style={styles.content}>
        <Card style={styles.card}>
          <Text tone="primary">{t('feedback.whatTitle')}</Text>
          <Text tone="secondary">{t('feedback.whatBody')}</Text>
        </Card>

        <Card style={styles.card}>
          <View style={styles.row}>
            <ShieldCheck size={22} color={tokens.colors.text.secondary} style={styles.icon} />
            <Text tone="secondary" style={{ flex: 1 }}>
              {t('feedback.privateBody')}
            </Text>
          </View>
          <View style={styles.row}>
            <MessagesSquare size={22} color={tokens.colors.text.secondary} style={styles.icon} />
            <Text tone="secondary" style={{ flex: 1 }}>
              {t('feedback.triageBody')}
            </Text>
          </View>
        </Card>

        {failed && <ErrorState description={t('feedback.loadFailed')} onRetry={load} />}

        {telegramMissing && (
          <Card style={styles.card} accent="warning">
            <Text tone="primary">{t('feedback.noTelegramTitle')}</Text>
            <Text tone="secondary">{t('feedback.noTelegramBody')}</Text>
          </Card>
        )}

        <View style={styles.actions}>
          <Button
            label={t('feedback.writeToUs')}
            onPress={openChat}
            fullWidth
            size="lg"
            testID="feedback-write-to-us"
            icon={<ArrowRight size={20} color={tokens.colors.surface} />}
          />
          <Button
            label={t('feedback.openWebForm')}
            variant="ghost"
            onPress={openWebForm}
            fullWidth
            testID="feedback-open-web-form"
          />
        </View>

        <Card style={styles.card}>
          <View style={styles.row}>
            <MessageSquare size={20} color={tokens.colors.text.secondary} style={styles.icon} />
            <Text tone="muted" style={{ flex: 1 }}>
              {t('feedback.tips')}
            </Text>
          </View>
        </Card>
      </View>
    </PageLayout>
  );
}