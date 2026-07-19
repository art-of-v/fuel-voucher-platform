import { View, Text, Pressable, Animated, StyleSheet } from 'react-native';
import { useRouter, Redirect } from 'expo-router';
import { User, Phone } from 'lucide-react-native';
import { PhoneAuth } from '../src/components/phone-auth';
import { PageLayout } from '../src/components/page-layout';
import { GridBackground } from '../src/components/grid-background';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useStore } from '../src/core/state/appStore';
import { Haptics } from '../src/core/utils/haptics';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { useI18n } from '../src/core/i18n';
import { useRef, useState } from 'react';

export default function LandingScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const { login } = useStore();
  const storeAuth = useStore(state => state.isAuthenticated);
  const { isAuthenticated: hookAuth, isLoading } = useAuth();
  const isAuthenticated = storeAuth || hookAuth;
  const authScale = useRef(new Animated.Value(1)).current;

  if (isAuthenticated && !isLoading) {
    return <Redirect href="/" />;
  }

  return (
    <PageLayout background={<GridBackground />} disableScroll>
      <View style={{ flex: 1, justifyContent: 'center', paddingTop: 40 }}>
        <PhoneAuth
          onSuccess={() => {
            login();
            router.replace('/');
          }}
        />
      </View>
    </PageLayout>
  );
}
