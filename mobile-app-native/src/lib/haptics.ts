import * as ExpoHaptics from 'expo-haptics';
import { Platform } from 'react-native';

/**
 * Safe Haptics Utility
 * Wraps Expo Haptics to prevent crashes on platforms or environments (like certain simulators)
 * where the native module might be missing or unlinked.
 */
export const Haptics = {
    impactAsync: async (style: ExpoHaptics.ImpactFeedbackStyle) => {
        if (Platform.OS === 'web') return;
        try {
            // We don't await here to keep the UI snappy, but we catch rejections
            ExpoHaptics.impactAsync(style).catch(() => {
                // Silently ignore failures when native module is missing or fails (e.g. simulator)
            });
        } catch (e) {
            // Synchronous catch for completeness
        }
    },

    notificationAsync: async (type: ExpoHaptics.NotificationFeedbackType) => {
        if (Platform.OS === 'web') return;
        try {
            ExpoHaptics.notificationAsync(type).catch(() => { });
        } catch (e) { }
    },

    selectionAsync: async () => {
        if (Platform.OS === 'web') return;
        try {
            ExpoHaptics.selectionAsync().catch(() => { });
        } catch (e) { }
    },

    // Expose the styles for convenience
    ImpactFeedbackStyle: ExpoHaptics.ImpactFeedbackStyle,
    NotificationFeedbackType: ExpoHaptics.NotificationFeedbackType
};
