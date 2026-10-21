import React, { useState } from 'react';
import { Share, View } from 'react-native';
import { Redirect } from 'expo-router';
import * as Clipboard from 'expo-clipboard';
import { Copy, Gift, Share2 } from 'lucide-react-native';

import { Button, Card, PageLayout, ScreenHeader, Text, TextField, toast } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useI18n } from '../src/core/i18n';
import { useStore } from '../src/core/state/appStore';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { useReferral } from '../src/features/referral/hooks/useReferral';
import { REFERRAL_CODE_MAX_LENGTH } from '../src/features/referral/lib/referralCode';
import { referralErrorText } from '../src/features/referral/lib/referralErrors';
import { Haptics } from '../src/core/utils/haptics';

/**
 * The referral screen — the surface the referral backend was built for.
 *
 * The backend holds exactly two things (planning #288): a code the customer
 * hands out, and the code of whoever referred them. There is no list of
 * referrals, no count of friends, and no bonus — the backend pays none — so
 * this screen shows two codes and says nothing else. A screen that promised a
 * reward would be promising money the product does not pay.
 *
 * The shape of it:
 *
 *   - The customer's own code. If they have one, it is shown with Copy and
 *     Share, and it cannot be changed: the server sets it once and refuses the
 *     second attempt. If they do not have one, they choose it — the server has
 *     no "generate" endpoint, and the customer owns the code they hand out.
 *   - A friend's code. Redeemable once, and never for one's own code.
 *
 * Both writes share one rate-limit budget — five an hour, together — so the
 * screen fires them only on a deliberate press and never on a refresh.
 */
export default function ReferralScreen() {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
  const storeAuth = useStore((s) => s.isAuthenticated);
  const isAuthenticated = storeAuth || hookAuth;

  const {
    myCode,
    referredBy,
    createCode,
    redeemCode,
    isCreating,
    isRedeeming,
    createError,
    redeemError,
    retryAfterSeconds,
  } = useReferral();

  const [draftCode, setDraftCode] = useState('');
  const [friendCode, setFriendCode] = useState('');

  const Header = <ScreenHeader title={t('referral.title')} subtitle={t('referral.subtitle')} />;

  // The auth guard mirrors the other secondary screens: redirect once we know
  // the customer is signed out, but not while the check is still in flight.
  if (!isAuthenticated && !authLoading) {
    return <Redirect href="/landing" />;
  }

  const createErrorText = createError
    ? referralErrorText(t, createError, retryAfterSeconds)
    : undefined;
  const redeemErrorText = redeemError
    ? referralErrorText(t, redeemError, retryAfterSeconds)
    : undefined;

  const onCopy = async () => {
    if (!myCode) return;
    await Clipboard.setStringAsync(myCode);
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    toast.success(t('referral.copied'));
  };

  const onShare = async () => {
    if (!myCode) return;
    try {
      await Share.share({ message: t('referral.shareMessage', myCode) });
    } catch {
      // The share sheet was dismissed. Nothing failed, so nothing is reported.
    }
  };

  return (
    <PageLayout header={Header}>
      <View style={{ gap: tokens.spacing.lg }}>
        {/* ---------------------------------------------------------------
            The customer's own code.
            --------------------------------------------------------------- */}
        <Card style={{ gap: tokens.spacing.md }}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: tokens.spacing.sm }}>
            <Gift size={18} color={tokens.colors.primary} />
            <Text role="heading">{t('referral.myCodeTitle')}</Text>
          </View>

          {myCode ? (
            <>
              <TextField
                label={t('referral.myCodeLabel')}
                value={myCode}
                readOnly
                codeStyle
                testID="referral-my-code"
              />
              <Text role="secondary" tone="muted">
                {t('referral.myCodeImmutable')}
              </Text>
              <View style={{ flexDirection: 'row', gap: tokens.spacing.sm }}>
                <Button
                  variant="secondary"
                  size="md"
                  label={t('referral.copy')}
                  icon={<Copy />}
                  onPress={onCopy}
                />
                <Button
                  variant="primary"
                  size="md"
                  label={t('referral.share')}
                  icon={<Share2 />}
                  onPress={onShare}
                />
              </View>
            </>
          ) : (
            <>
              <TextField
                label={t('referral.chooseCodeLabel')}
                helper={t('referral.chooseCodeHelper')}
                error={createErrorText}
                value={draftCode}
                onChangeText={setDraftCode}
                autoCapitalize="characters"
                autoCorrect={false}
                maxLength={REFERRAL_CODE_MAX_LENGTH}
                codeStyle
                returnKeyType="done"
                onSubmitEditing={() => createCode(draftCode)}
                testID="referral-create-code"
              />
              <Button
                label={t('referral.createCode')}
                loading={isCreating}
                disabled={draftCode.trim().length === 0}
                onPress={() => createCode(draftCode)}
              />
            </>
          )}
        </Card>

        {/* ---------------------------------------------------------------
            A friend's code.
            --------------------------------------------------------------- */}
        <Card style={{ gap: tokens.spacing.md }}>
          <Text role="heading">{t('referral.friendCodeTitle')}</Text>

          {referredBy ? (
            <>
              <TextField
                label={t('referral.friendCodeLabel')}
                value={referredBy}
                readOnly
                codeStyle
                testID="referral-redeemed-code"
              />
              <Text role="secondary" tone="muted">
                {t('referral.friendCodeImmutable')}
              </Text>
            </>
          ) : (
            <>
              <TextField
                label={t('referral.friendCodeInputLabel')}
                helper={t('referral.friendCodeHelper')}
                error={redeemErrorText}
                value={friendCode}
                onChangeText={setFriendCode}
                autoCapitalize="characters"
                autoCorrect={false}
                maxLength={REFERRAL_CODE_MAX_LENGTH}
                codeStyle
                returnKeyType="done"
                onSubmitEditing={() => redeemCode(friendCode)}
                testID="referral-friend-code"
              />
              <Button
                variant="secondary"
                label={t('referral.applyCode')}
                loading={isRedeeming}
                disabled={friendCode.trim().length === 0}
                onPress={() => redeemCode(friendCode)}
              />
            </>
          )}
        </Card>
      </View>
    </PageLayout>
  );
}
