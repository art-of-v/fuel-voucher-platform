import React, { useCallback, useEffect, useRef } from 'react';
import {
  Animated,
  BackHandler,
  Dimensions,
  Easing,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleProp,
  View,
  ViewStyle,
} from 'react-native';
import { X } from 'lucide-react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useDesignTokens } from '../hooks/useTheme';
import { useI18n } from '../i18n';
import { IconButton } from './IconButton';
import { Text } from './Text';

export interface BottomSheetProps {
  visible: boolean;
  onClose: () => void;
  /** Sheet title. Omit only for a sheet whose content is self-describing. */
  title?: string;
  /** One line explaining what the sheet is for or what choosing does. */
  subtitle?: string;
  children: React.ReactNode;
  /**
   * A pinned action area at the bottom of the sheet, below the scroll region.
   * Use for the sheet's confirming action so it never scrolls out of reach.
   */
  footer?: React.ReactNode;
  /** Let the content scroll. Set false when the sheet hosts its own list. */
  scrollable?: boolean;
  /** Fraction of the screen the sheet may occupy. Defaults to 0.9. */
  maxHeightRatio?: number;
  /** Suppress the scrim tap-to-dismiss — for a sheet mid-transaction. */
  dismissOnBackdropPress?: boolean;
  /** Hide the close button. Only when a footer provides an explicit exit. */
  hideClose?: boolean;
  contentStyle?: StyleProp<ViewStyle>;
  testID?: string;
}

/**
 * The sheet primitive: options, pickers, secondary detail, non-blocking forms.
 *
 * Replaces four hand-rolled `Modal` implementations with four different
 * behaviours — `animationType="slide"` vs `"fade"`, scrim opacities of 0.5, 0.7,
 * 0.8 and 0.9, radii of 20 and 22, and one case
 * (`/contracts`) that stacked a second `Modal` on top of the first to show text
 * that would not fit in a `maxHeight: 180` scroll area.
 *
 * Behaviour contract:
 * - Enters from the bottom over a scrim; exits the same way. 220ms.
 * - Android hardware back closes it.
 * - Respects the bottom safe-area inset, so the last action is never under the
 *   home indicator.
 * - Caps at 90% of screen height and scrolls internally beyond that; it never
 *   clips content with an arbitrary `maxHeight`.
 * - Keyboard pushes the sheet, it does not cover it.
 * - Only the top corners are rounded (`radius.xl`), because the sheet is attached
 *   to the bottom edge.
 */
export function BottomSheet({
  visible,
  onClose,
  title,
  subtitle,
  children,
  footer,
  scrollable = true,
  maxHeightRatio,
  dismissOnBackdropPress = true,
  hideClose = false,
  contentStyle,
  testID,
}: BottomSheetProps) {
  const tokens = useDesignTokens();
  const insets = useSafeAreaInsets();
  const t = useI18n((s) => s.t);
  const c = tokens.colors;

  const slide = useRef(new Animated.Value(0)).current;
  const screenHeight = Dimensions.get('window').height;
  const maxHeight = screenHeight * (maxHeightRatio ?? tokens.chrome.sheetMaxHeightRatio);

  useEffect(() => {
    Animated.timing(slide, {
      toValue: visible ? 1 : 0,
      duration: tokens.motion.normal,
      easing: visible ? Easing.out(Easing.cubic) : Easing.in(Easing.cubic),
      useNativeDriver: true,
    }).start();
  }, [visible, slide, tokens.motion.normal]);

  const handleBack = useCallback(() => {
    if (!visible) return false;
    onClose();
    return true;
  }, [visible, onClose]);

  useEffect(() => {
    const sub = BackHandler.addEventListener('hardwareBackPress', handleBack);
    return () => sub.remove();
  }, [handleBack]);

  return (
    <Modal
      visible={visible}
      transparent
      // Animation is driven here, not by the Modal, so entry and exit match.
      animationType="none"
      statusBarTranslucent
      onRequestClose={onClose}
      testID={testID}
    >
      <View style={{ flex: 1, justifyContent: 'flex-end' }}>
        <Animated.View
          style={{
            ...StyleSheetAbsoluteFill,
            backgroundColor: c.overlay,
            opacity: slide,
          }}
        >
          <Pressable
            style={{ flex: 1 }}
            onPress={dismissOnBackdropPress ? onClose : undefined}
            accessibilityLabel={t('common.close')}
            accessibilityRole="button"
          />
        </Animated.View>

        <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
          <Animated.View
            style={[
              {
                maxHeight,
                backgroundColor: c.surfaceElevated,
                borderTopLeftRadius: tokens.radius.xl,
                borderTopRightRadius: tokens.radius.xl,
                paddingBottom: insets.bottom + tokens.spacing.lg,
                transform: [
                  {
                    translateY: slide.interpolate({
                      inputRange: [0, 1],
                      outputRange: [maxHeight, 0],
                    }),
                  },
                ],
                ...tokens.elevation.medium,
              },
            ]}
          >
            {/* Grabber — a visual affordance that this surface is dismissible. */}
            <View style={{ alignItems: 'center', paddingTop: tokens.spacing.md }}>
              <View
                style={{
                  width: tokens.chrome.sheetGrabberWidth,
                  height: 4,
                  borderRadius: tokens.radius.full,
                  backgroundColor: c.borderStrong,
                }}
              />
            </View>

            {(title || !hideClose) && (
              <View
                style={{
                  flexDirection: 'row',
                  alignItems: 'flex-start',
                  gap: tokens.spacing.md,
                  paddingHorizontal: tokens.spacing.containerPadding,
                  paddingTop: tokens.spacing.lg,
                }}
              >
                <View style={{ flex: 1, gap: tokens.spacing.xs }}>
                  {title ? (
                    <Text role="heading" accessibilityRole="header">
                      {title}
                    </Text>
                  ) : null}
                  {subtitle ? (
                    <Text role="secondary" tone="muted">
                      {subtitle}
                    </Text>
                  ) : null}
                </View>
                {hideClose ? null : (
                  <IconButton
                    icon={<X />}
                    onPress={onClose}
                    accessibilityLabel={t('common.close')}
                    size="md"
                  />
                )}
              </View>
            )}

            {scrollable ? (
              <ScrollView
                style={{ flexGrow: 0 }}
                contentContainerStyle={[
                  {
                    paddingHorizontal: tokens.spacing.containerPadding,
                    paddingVertical: tokens.spacing.lg,
                    gap: tokens.spacing.lg,
                  },
                  contentStyle,
                ]}
                keyboardShouldPersistTaps="handled"
                showsVerticalScrollIndicator={false}
              >
                {children}
              </ScrollView>
            ) : (
              <View
                style={[
                  {
                    paddingHorizontal: tokens.spacing.containerPadding,
                    paddingVertical: tokens.spacing.lg,
                    gap: tokens.spacing.lg,
                  },
                  contentStyle,
                ]}
              >
                {children}
              </View>
            )}

            {footer ? (
              <View
                style={{
                  paddingHorizontal: tokens.spacing.containerPadding,
                  paddingTop: tokens.spacing.lg,
                  borderTopWidth: 1,
                  borderTopColor: c.borderSubtle,
                  gap: tokens.spacing.md,
                }}
              >
                {footer}
              </View>
            ) : null}
          </Animated.View>
        </KeyboardAvoidingView>
      </View>
    </Modal>
  );
}

const StyleSheetAbsoluteFill = {
  position: 'absolute' as const,
  top: 0,
  left: 0,
  right: 0,
  bottom: 0,
};
