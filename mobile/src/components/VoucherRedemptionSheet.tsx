import React, { useCallback, useEffect, useState } from 'react';
import {
  View,
  StyleSheet,
  Pressable,
  Alert,
} from 'react-native';
import {
  ChevronRight,
  Copy,
  Info,
  ShieldCheck,
  TriangleAlert,
} from 'lucide-react-native';
import * as Clipboard from 'expo-clipboard';

import { useDesignTokens } from '../core/hooks/useTheme';
import { useI18n } from '../core/i18n';
import { Haptics } from '../core/utils/haptics';
import {
  BottomSheet,
  Button,
  Text,
  toast,
} from '../core/ui';
import { classifyVoucher } from '../core/types/api';
import type { Voucher } from '../core/types/api';
import { formatExpirationDate } from '../core/utils/formatters';
import { VoucherBadge } from './VoucherBadge';
import { QrSvg } from './QrSvg';

/**
 * The wallet QR is the brand-specific payload a pump scanner reads. The
 * server has always supplied this as `qrPayload` / `qrCodeData`; we now
 * render it locally so the screen never depends on a network round-trip
 * for the symbol itself.
 *
 * Falling back to the voucher id is not a fix — it produces a QR symbol the
 * pump does not recognise. The screen makes that explicit to the user.
 */
function pickQrPayload(voucher: Voucher): string | null {
  const candidate =
    (voucher.qrCodeData && voucher.qrCodeData.length > 8 && voucher.qrCodeData) ||
    (voucher as any).qrPayload ||
    null;
  return candidate;
}

export interface VoucherRedemptionSheetProps {
  visible: boolean;
  voucher: Voucher | null;
  user?: { id?: string } | null;
  /** Brand hue resolved by the caller from `BRAND_COLORS`. */
  brandColor: string;
  onClose: () => void;
  /**
   * Ask the caller to mark this voucher used. The caller owns the network
   * call, the toast feedback and the wallet refresh. Returns `true` on
   * success.
   */
  onMarkUsed: (voucher: Voucher) => Promise<boolean>;
  /**
   * Ask the caller to restore a previously-used voucher. Used only from
   * the "USED" state of the sheet so the user can reverse an accidental
   * tap.
   */
  onRestore?: (voucher: Voucher) => Promise<boolean>;
}

/**
 * The redemption experience.
 *
 * The QR is the only thing that needs to be large on this screen. Every
 * other element supports it: provider + amount at the top, status + expiry
 * below the symbol, "Mark as used" pinned in the footer so the user can
 * reach it without taking the phone away from the scanner.
 *
 * The QR is rendered at the largest size that fits the screen with a 24pt
 * margin on each side and the white quiet zone the scanners need. The
 * card behind it uses the themed surface, so the only pure-white surface
 * on screen is the QR's quiet zone — which is correct for the scanners
 * and visually obvious to the eye.
 */
export function VoucherRedemptionSheet({
  visible,
  voucher,
  user,
  brandColor,
  onClose,
  onMarkUsed,
  onRestore,
}: VoucherRedemptionSheetProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const c = tokens.colors;

  const [confirming, setConfirming] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [showRules, setShowRules] = useState(false);

  useEffect(() => {
    if (visible) {
      setConfirming(false);
      setErrorMessage(null);
      setShowRules(false);
    }
  }, [visible, voucher?.id]);

  if (!voucher) {
    return (
      <BottomSheet visible={false} onClose={onClose}>
        <></>
      </BottomSheet>
    );
  }

  const kind = classifyVoucher(voucher, user?.id);
  const isBlocked = kind === 'blocked';
  const isUsed = voucher.status === 'used';
  const isExpired =
    !!voucher.expirationDate &&
    new Date(voucher.expirationDate).getTime() < Date.now();
  const canUse = !isBlocked && kind !== 'gifted_to_worker';
  const expDays = voucher.expirationDate
    ? Math.ceil(
        (new Date(voucher.expirationDate).getTime() - Date.now()) / 86400000,
      )
    : null;
  const isExpiringSoon = expDays !== null && expDays <= 30 && expDays >= 0;
  const payload = pickQrPayload(voucher);
  const workerName = [voucher.workerFirstName, voucher.workerLastName]
    .filter(Boolean)
    .join(' ')
    .trim();

  const statusTone: 'ready' | 'used' | 'expired' | 'blocked' =
    isBlocked ? 'blocked' : isUsed ? 'used' : isExpired ? 'expired' : 'ready';

  const statusLabel = isBlocked
    ? t('voucher.badge.blocked')
    : isUsed
      ? t('codes.used')
      : isExpired
        ? t('voucher.status.expired')
        : t('redemption.ready');

  const statusRole =
    statusTone === 'ready'
      ? 'success'
      : statusTone === 'blocked'
        ? 'danger'
        : 'neutral';

  const statusDotColor =
    statusTone === 'ready'
      ? c.status.success.base
      : statusTone === 'blocked'
        ? c.status.danger.base
        : c.status.neutral.base;

  const handleCopyId = useCallback(async () => {
    await Clipboard.setStringAsync(voucher.externalId || voucher.id);
    toast.success(t('codes.idCopied'));
  }, [voucher.externalId, voucher.id, t]);

  const handleMarkUsed = useCallback(async () => {
    if (confirming || isUsed || !canUse) return;
    setErrorMessage(null);
    setConfirming(true);
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
    try {
      const ok = await onMarkUsed(voucher);
      if (ok) {
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
        toast.success(t('redemption.markedSuccess'));
      } else {
        setErrorMessage(t('redemption.markFailed'));
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      }
    } catch (err: any) {
      setErrorMessage(err?.message || t('redemption.markFailed'));
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    } finally {
      setConfirming(false);
    }
  }, [confirming, isUsed, canUse, onMarkUsed, voucher, t]);

  const handleRestore = useCallback(async () => {
    if (!onRestore || !isUsed) return;
    setErrorMessage(null);
    setConfirming(true);
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    try {
      const ok = await onRestore(voucher);
      if (ok) {
        toast.success(t('redemption.restoredSuccess'));
      } else {
        setErrorMessage(t('redemption.restoreFailed'));
      }
    } catch (err: any) {
      setErrorMessage(err?.message || t('redemption.restoreFailed'));
    } finally {
      setConfirming(false);
    }
  }, [onRestore, isUsed, voucher, t]);

  const confirmMarkUsed = useCallback(() => {
    Alert.alert(
      t('redemption.confirmTitle'),
      t('redemption.confirmMessage'),
      [
        { text: t('common.cancel'), style: 'cancel' },
        { text: t('redemption.confirmAction'), style: 'destructive', onPress: handleMarkUsed },
      ],
    );
  }, [handleMarkUsed, t]);

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  // The QR + quiet zone: only true white on this screen. When the voucher
  // cannot be redeemed (used / expired / blocked), we still show the QR but
  // fade it so it is obviously inert. When the payload is missing we show a
  // recovery state — never an empty card. (A network-offline state would
  // belong here too, but we have no connectivity signal wired in this
  // build.)
  const qrSurface: 'ready' | 'inert' | 'missing' =
    !payload
      ? 'missing'
      : isUsed || isExpired || isBlocked
        ? 'inert'
        : 'ready';

  const qrNode = (() => {
    if (qrSurface === 'missing') {
      return (
        <View style={styles.qrFallback}>
          <TriangleAlert size={36} color={c.text.muted} />
          <Text role="bodyStrong" center style={{ marginTop: tokens.spacing.sm }}>
            {t('redemption.payloadMissing')}
          </Text>
          <Text role="secondary" tone="muted" center style={{ maxWidth: 280, marginTop: tokens.spacing.xs }}>
            {t('redemption.payloadMissingHelp')}
          </Text>
        </View>
      );
    }
    return (
      <View style={[styles.qrHolder, qrSurface === 'inert' && styles.qrInert]}>
        <QrSvg value={payload!} />
      </View>
    );
  })();

  const qrCard = (
    <View style={styles.qrWrap}>
      {qrNode}
    </View>
  );

  const headerRow = (
    <View style={styles.headerRow}>
      <View style={{ flex: 1, gap: 2 }}>
        <Text role="caption" tone="muted" style={{ textTransform: 'uppercase' }}>
          {voucher.provider}
        </Text>
        <Text role="title">
          {voucher.amount}{' '}
          <Text role="numeric" tone="muted">
            {voucher.unit || 'L'}
          </Text>{' '}
          <Text role="body" tone="secondary">
            {voucher.fuelName || voucher.fuelType}
          </Text>
        </Text>
      </View>
      <View
        style={[
          styles.statusPill,
          { backgroundColor: c.status[statusRole].subtle, borderColor: c.status[statusRole].border },
        ]}
      >
        <View style={[styles.statusDot, { backgroundColor: statusDotColor }]} />
        <Text role="label" tone="inherit" style={{ color: statusDotColor }}>
          {statusLabel}
        </Text>
      </View>
    </View>
  );

  const validityStrip = (
    <View style={styles.validityRow}>
      {voucher.expirationDate ? (
        <View style={styles.expRow}>
          {isExpiringSoon && !isUsed && !isExpired && !isBlocked ? (
            <TriangleAlert size={14} color={c.status.warning.base} />
          ) : null}
          <Text
            role="caption"
            tone="muted"
            style={
              isExpiringSoon && !isUsed && !isExpired && !isBlocked
                ? { color: c.status.warning.base }
                : undefined
            }
          >
            {t('codes.expires')}: {formatExpirationDate(voucher.expirationDate)}
          </Text>
        </View>
      ) : (
        <View />
      )}
      {voucher.externalId ? (
        <Pressable
          onPress={handleCopyId}
          accessibilityRole="button"
          accessibilityLabel={`${t('codes.copyId')}: ${voucher.externalId}`}
          style={({ pressed }) => [
            styles.copyIdButton,
            {
              borderColor: c.border,
              backgroundColor: pressed ? c.primarySubtle : 'transparent',
            },
          ]}
        >
          <Copy size={14} color={c.text.secondary} />
          <Text role="caption" tone="secondary" numberOfLines={1}>
            {voucher.externalId}
          </Text>
        </Pressable>
      ) : null}
    </View>
  );

  const footer = (
    <View style={styles.footer}>
      {errorMessage ? (
        <View
          style={[
            styles.errorBanner,
            { borderColor: c.status.danger.border, backgroundColor: c.status.danger.subtle },
          ]}
        >
          <TriangleAlert size={18} color={c.status.danger.base} />
          <Text role="body" tone="danger" style={{ flex: 1 }} numberOfLines={2}>
            {errorMessage}
          </Text>
        </View>
      ) : null}

      {isBlocked ? (
        <View
          style={[
            styles.messageBanner,
            { backgroundColor: c.status.danger.subtle, borderColor: c.status.danger.border },
          ]}
        >
          <ShieldCheck size={18} color={c.status.danger.base} />
          <Text role="body" tone="danger" style={{ flex: 1 }}>
            {t('voucher.error.blocked')}
          </Text>
        </View>
      ) : isUsed ? (
        <Button
          label={t('codes.restoreCode')}
          onPress={handleRestore}
          variant="secondary"
          size="lg"
          fullWidth
          loading={confirming}
          disabled={!onRestore}
        />
      ) : isExpired ? (
        <View
          style={[
            styles.messageBanner,
            { backgroundColor: c.status.neutral.subtle, borderColor: c.status.neutral.border },
          ]}
        >
          <Info size={18} color={c.text.muted} />
          <Text role="body" tone="muted" style={{ flex: 1 }}>
            {t('redemption.expiredMessage')}
          </Text>
        </View>
      ) : kind === 'gifted_to_worker' ? (
        <View
          style={[
            styles.messageBanner,
            { backgroundColor: c.status.info.subtle, borderColor: c.status.info.border },
          ]}
        >
          <Info size={18} color={c.status.info.base} />
          <Text role="body" tone="muted" style={{ flex: 1 }}>
            {t('voucher.error.workerOnly')}
            {workerName ? ` — ${workerName}` : ''}
          </Text>
        </View>
      ) : canUse && payload ? (
        <Button
          label={t('redemption.markAsUsed')}
          onPress={confirmMarkUsed}
          variant="primary"
          size="lg"
          fullWidth
          loading={confirming}
          icon={<ShieldCheck />}
          accessibilityLabel={t('redemption.markAsUsedHint')}
        />
      ) : canUse && !payload ? (
        <Button
          label={t('redemption.markAsUsed')}
          onPress={confirmMarkUsed}
          variant="secondary"
          size="lg"
          fullWidth
          disabled
          accessibilityLabel={t('redemption.payloadMissingHint')}
        />
      ) : null}
    </View>
  );

  return (
    <BottomSheet
      visible={visible}
      onClose={onClose}
      maxHeightRatio={0.94}
      footer={footer}
    >
      {headerRow}

      {qrCard}

      {validityStrip}

      {/* Identity (worker / pool / personal) and progressive-disclosure rules. */}
      {kind !== 'personal' ? (
        <View style={styles.identityRow}>
          <VoucherBadge kind={kind} />
        </View>
      ) : null}

      {voucher.redemptionRules ? (
        <View
          style={[
            styles.rulesBlock,
            { borderColor: c.border, backgroundColor: c.surfaceSunken },
          ]}
        >
          <Pressable
            onPress={() => setShowRules(s => !s)}
            accessibilityRole="button"
            accessibilityLabel={t(showRules ? 'redemption.hideRules' : 'redemption.showRules')}
            style={styles.rulesHeader}
          >
            <Info size={16} color={c.text.secondary} />
            <Text role="bodyStrong" tone="secondary" style={{ flex: 1 }}>
              {t('redemption.redemptionRules')}
            </Text>
            <ChevronRight
              size={16}
              color={c.text.muted}
              style={[
                showRules ? styles.chevronUp : styles.chevronDown,
              ]}
            />
          </Pressable>
          {showRules ? (
            <Text role="body" tone="secondary" style={{ marginTop: tokens.spacing.sm }}>
              {voucher.redemptionRules}
            </Text>
          ) : null}
        </View>
      ) : null}
    </BottomSheet>
  );
}

// Geometry only. Colours, radii and spacing come from tokens.
const styles = StyleSheet.create({
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
  },
  qrWrap: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  qrHolder: {
    backgroundColor: '#FFFFFF', // QR quiet zone must be a true white
    borderRadius: 12,
    padding: 12,
  },
  qrInert: {
    opacity: 0.4,
  },
  qrFallback: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 32,
    paddingHorizontal: 16,
    gap: 4,
    minHeight: 220,
    width: '100%',
    backgroundColor: '#FFFFFF',
    borderRadius: 12,
  },
  validityRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 12,
    flexWrap: 'wrap',
  },
  statusPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 6,
    borderWidth: 1,
  },
  statusDot: {
    width: 7,
    height: 7,
    borderRadius: 4,
  },
  expRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  copyIdButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: 10,
    paddingVertical: 8,
    borderRadius: 10,
    borderWidth: 1,
    minHeight: 36,
    maxWidth: '55%',
  },
  identityRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  rulesBlock: {
    borderRadius: 10,
    borderWidth: 1,
    padding: 12,
  },
  rulesHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  chevronUp: {
    transform: [{ rotate: '-90deg' }],
  },
  chevronDown: {},
  footer: {
    gap: 12,
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderRadius: 10,
    borderWidth: 1,
  },
  messageBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingHorizontal: 12,
    paddingVertical: 12,
    borderRadius: 10,
    borderWidth: 1,
  },
});