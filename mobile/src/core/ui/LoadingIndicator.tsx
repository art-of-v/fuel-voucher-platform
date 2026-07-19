import React from 'react';
import { View, ActivityIndicator, Text, StyleSheet } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';

interface LoadingIndicatorProps {
  message?: string;
  fullScreen?: boolean;
  size?: 'small' | 'large';
}

export function LoadingIndicator({
  message,
  fullScreen = false,
  size = 'large',
}: LoadingIndicatorProps) {
  const tokens = useDesignTokens();

  const content = (
    <View style={styles.content}>
      <ActivityIndicator size={size} color={tokens.colors.primary} />
      {message && (
        <Text
          style={[
            styles.message,
            { color: tokens.colors.primary, fontFamily: 'Rajdhani-Bold' },
          ]}
        >
          {message}
        </Text>
      )}
    </View>
  );

  if (fullScreen) {
    return (
      <View style={[styles.fullScreen, { backgroundColor: tokens.colors.background }]}>
        {content}
      </View>
    );
  }

  return content;
}

const styles = StyleSheet.create({
  fullScreen: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  content: {
    alignItems: 'center',
    justifyContent: 'center',
    padding: 20,
  },
  message: {
    fontSize: 10,
    marginTop: 16,
    letterSpacing: 2,
    textTransform: 'uppercase',
    textAlign: 'center',
  },
});
