import * as ExpoHaptics from 'expo-haptics';
import { Platform } from 'react-native';

/**
 * Safe Haptics Utility
 */
export const Haptics = {
    impactAsync: async (style: ExpoHaptics.ImpactFeedbackStyle) => {
        if (Platform.OS === 'web') return;
        
        // Quiet the noisy iOS Simulator logs if not useful
        // In dev/sim environment, haptics often fail or spam console with POSIX errors
        try {
            ExpoHaptics.impactAsync(style).catch(() => {});
        } catch (e) {}
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

    ImpactFeedbackStyle: ExpoHaptics.ImpactFeedbackStyle,
    NotificationFeedbackType: ExpoHaptics.NotificationFeedbackType
};
