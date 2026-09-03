import React, { useCallback, useEffect } from 'react';
import { Animated, BackHandler, Easing, Modal, Pressable, View } from 'react-native';
import { CircleAlert, Info, TriangleAlert } from 'lucide-react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useDesignTokens } from '../hooks/useTheme';
import { useI18n } from '../i18n';
import { Button } from './Button';
import { Text } from './Text';

export type ConfirmTone = 'neutral' | 'warning' | 'destructive';

export interface ConfirmDialogProps {
  visible: boolean;
  /** What the user is about to do. A question or an imperative, not a label. */
  title: string;
  /**
   * The consequence — specifically, what becomes irreversible. "This cannot be
   * undone" is the minimum for a destructive action.
   */
  message?: string;
  /** Label for the confirming action. Name the action, never "OK"/"Yes". */
  confirmLabel: string;
  /** Label for the escape. Defaults to `common.cancel`. */
  cancelLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
  /**
   * `destructive` fills the confirm button in the danger role **and puts cancel
   * first**, so the safe action is where the thumb already is.
   */
  tone?: ConfirmTone;
  /** Confirm is in flight — spinner on the confirm button, dialog held open. */
  loading?: boolean;
  testID?: string;
}

/**
 * A blocking decision.
 *
 * Use this **only** when the app cannot proceed without an answer and the
 * consequence is real: deleting a cart line, clearing a cart, declining an
 * invitation, recalling a voucher, firing a worker, signing a contract.
 *
 * Do not use it to report an outcome — that is `Toast` (transient) or
 * `InlineFeedback` (persistent). `Alert.alert` was previously used for both, which
 * is why success messages in this product blocked the screen they were about.
 *
 * The audit found the reverse of this contract everywhere: destructive actions
 * were consistently *cheaper* than safe ones. Decrementing quantity to zero
 * deleted a cart line with no dialog; declining a company invitation destroyed it
 * with no dialog and less feedback than accepting; "remove all" was a 10px text
 * link. Meanwhile applying a promocode got a bordered button and reading a
 * contract took two nested modals.
 */
export function ConfirmDialog({
  visible,
  title,
  message,
  confirmLabel,
  cancelLabel,
  onConfirm,
  onCancel,
  tone = 'neutral',
  loading = false,
  testID,
}: ConfirmDialogProps) {
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const t = useI18n((s) => s.t);
  const c = tokens.colors;

  const anim = React.useRef(new Animated.Value(0)).current;

  useEffect(() => {
    Animated.timing(anim, {
      toValue: visible ? 1 : 0,
      duration: tokens.motion.fast,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: true,
    }).start();
  }, [visible, anim, tokens.motion.fast]);

  const handleBack = useCallback(() => {
    if (!visible) return false;
    if (!loading) onCancel();
    return true;
  }, [visible, loading, onCancel]);

  useEffect(() => {
    const sub = BackHandler.addEventListener('hardwareBackPress', handleBack);
    return () => sub.remove();
  }, [handleBack]);

  const destructive = tone === 'destructive';
  const Icon = destructive ? CircleAlert : tone === 'warning' ? TriangleAlert : Info;
  const iconColor = destructive
    ? c.status.danger.base
    : tone === 'warning'
      ? c.status.warning.base
      : c.status.info.base;

  const confirmButton = (
    <Button
      label={confirmLabel}
      onPress={onConfirm}
      loading={loading}
      variant={destructive ? 'destructive' : 'primary'}
      emphasis={destructive ? 'high' : 'normal'}
      size="md"
      hapticStyle={destructive ? 'heavy' : 'medium'}
    />
  );

  const cancelButton = (
    <Button
      label={cancelLabel ?? t('common.cancel')}
      onPress={onCancel}
      variant="ghost"
      size="md"
      disabled={loading}
    />
  );

  return (
    <Modal
      visible={visible}
      transparent
      animationType="none"
      statusBarTranslucent
      onRequestClose={() => !loading && onCancel()}
      testID={testID}
    >
      <Animated.View
        style={{
          flex: 1,
          backgroundColor: c.overlay,
          opacity: anim,
          justifyContent: 'center',
          paddingHorizontal: tokens.spacing.containerPadding,
          paddingBottom: insets.bottom,
        }}
      >
        {/* Tapping outside cancels — but never while the action is in flight. */}
        <Pressable
          style={{ position: 'absolute', top: 0, left: 0, right: 0, bottom: 0 }}
          onPress={() => !loading && onCancel()}
          accessibilityLabel={cancelLabel ?? t('common.cancel')}
          accessibilityRole="button"
        />

        <Animated.View
          accessibilityViewIsModal
          accessibilityRole="alert"
          style={{
            backgroundColor: c.surfaceElevated,
            borderRadius: tokens.radius.xl,
            padding: tokens.spacing.xl,
            gap: tokens.spacing.lg,
            ...tokens.elevation.high,
            transform: [{ scale: anim.interpolate({ inputRange: [0, 1], outputRange: [0.96, 1] }) }],
            opacity: anim,
          }}
        >
          <View style={{ gap: tokens.spacing.md }}>
            <Icon size={28} color={iconColor} />
            <Text role="heading" accessibilityRole="header">
              {title}
            </Text>
            {message ? (
              <Text role="body" tone="secondary">
                {message}
              </Text>
            ) : null}
          </View>

          {/*
            Destructive: cancel first. The confirming action must not sit where
            the user's thumb lands by reflex.
          */}
          <View style={{ gap: tokens.spacing.sm }}>
            {destructive ? cancelButton : confirmButton}
            {destructive ? confirmButton : cancelButton}
          </View>
        </Animated.View>
      </Animated.View>
    </Modal>
  );
}
