import { View } from 'react-native';
import { useRouter, Redirect } from 'expo-router';
import { PageLayout } from '../src/components/page-layout';
import { GridBackground } from '../src/components/grid-background';
import { useStore } from '../src/core/state/appStore';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { PhoneAuthForm } from '../src/features/auth/components/PhoneAuthForm';

export default function LandingScreen() {
  const router = useRouter();
  const { login } = useStore();
  const storeAuth = useStore(state => state.isAuthenticated);
  const { isAuthenticated: hookAuth, isLoading } = useAuth();
  const isAuthenticated = storeAuth || hookAuth;

  if (isAuthenticated && !isLoading) {
    return <Redirect href="/" />;
  }

  return (
    <PageLayout background={<GridBackground />} disableScroll>
      <View style={{ flex: 1, justifyContent: 'center', paddingTop: 40 }}>
        {/*
          Was `PhoneAuth` (src/components/phone-auth.tsx), a second, older
          implementation of this exact flow that hand-rolled its own inputs,
          buttons and error text and hardcoded ~20 Ukrainian strings in a
          four-language app. `PhoneAuthForm` — already the sign-in used at
          checkout — calls the same five endpoints with the same payloads via
          `useLogin`, so this is a like-for-like swap that also gets the shared
          field/button states and translated copy. The old file is deleted.
        */}
        <PhoneAuthForm
          onSuccess={() => {
            login();
            router.replace('/');
          }}
        />
      </View>
    </PageLayout>
  );
}
