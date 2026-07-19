import * as ExpoHaptics from 'expo-haptics';
import { Platform } from 'react-native';

export const Haptics = {
  impactAsync: async (style: ExpoHaptics.ImpactFeedbackStyle) => {
    if (Platform.OS === 'web') return;
    try {
      await ExpoHaptics.impactAsync(style);
    } catch {
      // Silently fail on simulators without haptic support
    }
  },

  notificationAsync: async (type: ExpoHaptics.NotificationFeedbackType) => {
    if (Platform.OS === 'web') return;
    try {
      await ExpoHaptics.notificationAsync(type);
    } catch {
      // Silently fail on simulators without haptic support
    }
  },

  selectionAsync: async () => {
    if (Platform.OS === 'web') return;
    try {
      await ExpoHaptics.selectionAsync();
    } catch {
      // Silently fail
    }
  },

  ImpactFeedbackStyle: ExpoHaptics.ImpactFeedbackStyle,
  NotificationFeedbackType: ExpoHaptics.NotificationFeedbackType,
};
