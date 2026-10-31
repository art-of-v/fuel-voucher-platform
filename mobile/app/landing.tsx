import { View } from 'react-native';
import { useRouter, Redirect, useLocalSearchParams } from 'expo-router';
import { GridPageLayout, GridBackground } from '../src/core/ui';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useStore } from '../src/core/state/appStore';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { PhoneAuthForm } from '../src/features/auth/components/PhoneAuthForm';
import { Onboarding } from '../src/features/onboarding/components/Onboarding';

export default function LandingScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { login, hasCompletedOnboarding, completeOnboarding } = useStore();
  const storeAuth = useStore((state) => state.isAuthenticated);
  const { isAuthenticated: hookAuth, isLoading } = useAuth();
  const isAuthenticated = storeAuth || hookAuth;

  // A sign-in code that arrived by push lands here through the tap routing (see
  // src/core/notifications/notificationResponse): the phone and the code come with the
  // navigation, so the form starts on the code step instead of asking for both again. The
  // deadline comes with them when the push carried the code's lifetime - a banner tapped an
  // hour later opens the form already knowing the digits are dead.
  const { otpPhone, otpCode, otpExpiresAt } = useLocalSearchParams<{
    otpPhone?: string;
    otpCode?: string;
    otpExpiresAt?: string;
  }>();
  const codeExpiresAt = otpExpiresAt ? Number(otpExpiresAt) : undefined;

  if (isAuthenticated && !isLoading) {
    return <Redirect href="/" />;
  }

  return (
    <GridPageLayout background={<GridBackground />} disableScroll>
      {hasCompletedOnboarding ? (
        <View style={{ flex: 1, justifyContent: 'center', paddingTop: tokens.spacing['4xl'] }}>
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
            initialPhone={otpPhone}
            initialCode={otpCode}
            initialCodeExpiresAt={
              typeof codeExpiresAt === 'number' && Number.isFinite(codeExpiresAt)
                ? codeExpiresAt
                : undefined
            }
            onSuccess={() => {
              login();
              router.replace('/');
            }}
          />
        </View>
      ) : (
        /*
         * First launch only (persisted in appStore). Apple 5.1.1 requires the
         * data collected at registration — here, the phone number — to be
         * disclosed before the sign-up screen. The last onboarding slide is
         * exactly that disclosure; completing it flips the persisted flag so
         * returning users never see it again.
         */
        <Onboarding onFinish={completeOnboarding} />
      )}
    </GridPageLayout>
  );
}
