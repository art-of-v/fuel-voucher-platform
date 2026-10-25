import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { LionMark } from "@/components/LionMark";
import { sendCode, signInWithCode, signInWithTwoFactor } from "@/lib/session";
import { hasSessionMarker, useSession } from "@/lib/session-store";
import { STAFF_ROLES } from "@/lib/roles";
import { type CurrentUser } from "@/lib/admin-auth";
import { useTheme } from "@/lib/theme-store";
import { themes } from "@/lib/themes";
import { errorMessage } from "@/lib/error-message";

// The app is Ukrainian-only for now, so every admin's phone starts with the same country code.
// Prefill it so the operator types only the subscriber digits instead of re-entering "+380" on
// every sign-in.
const UA_DIAL_PREFIX = "+380";

/** The outstanding second factor of a staff sign-in. */
interface TwoFactorState {
  challenge: string;
  needsEnrollment: boolean;
  enrollmentSecret?: string | null;
}

/**
 * The sign-in form, and the gate that decides whether the signed-in account may use the admin at
 * all.
 *
 * <p>
 * The dashboard is admin-only, so a token for any other role is discarded here and this screen
 * explains why. It used to live inside the dashboard's own component, with the form's state
 * interleaved with the dashboard's; on its own it is one thing with one job.
 * </p>
 *
 * <p>
 * A staff sign-in has TWO factors. The code that arrives by SMS is the first one, and it does
 * not sign anybody in: the backend answers with a challenge and the screen asks for the code
 * from the authenticator app. The first time, that means enrolling one - the screen shows the
 * secret to add to the app, and the code the app then produces is what proves it.
 * </p>
 */
export default function AdminLogin() {
  const theme = useTheme((s) => s.theme);
  const status = useSession((s) => s.status);
  const blocker = useSession((s) => s.blocker);

  const [loginPhone, setLoginPhone] = useState(UA_DIAL_PREFIX);
  const [loginCode, setLoginCode] = useState("");
  const [loginStep, setLoginStep] = useState<"phone" | "code" | "two_factor">(
    "phone",
  );
  const [twoFactor, setTwoFactor] = useState<TwoFactorState | null>(null);
  const [twoFactorCode, setTwoFactorCode] = useState("");
  const [loginLoading, setLoginLoading] = useState(false);
  const [loginError, setLoginError] = useState("");
  const [unauthorized, setUnauthorized] = useState(false);

  const handleSendCode = async () => {
    setLoginLoading(true);
    setLoginError("");
    try {
      await sendCode(loginPhone);
      setLoginStep("code");
    } catch (e: unknown) {
      setLoginError(errorMessage(e) || "Failed to send code");
    }
    setLoginLoading(false);
  };

  // Single funnel once we have the current user. The dashboard is admin-only, so a token for any
  // other role is discarded here and the login screen explains why.
  const acceptUser = (u: CurrentUser) => {
    if (!u.role || !STAFF_ROLES.includes(u.role)) {
      useSession.getState().signOut();
      setUnauthorized(true);
      return;
    }
    setUnauthorized(false);
    useSession.getState().authenticate(u);
  };

  const handleVerifyCode = async () => {
    setLoginLoading(true);
    setLoginError("");
    setUnauthorized(false);
    try {
      const outcome = await signInWithCode(loginPhone, loginCode);
      if (outcome.status === "two_factor") {
        // The first factor is proven; the sign-in continues at the second one.
        setTwoFactor({
          challenge: outcome.challenge,
          needsEnrollment: outcome.needsEnrollment,
          enrollmentSecret: outcome.enrollmentSecret,
        });
        setTwoFactorCode("");
        setLoginStep("two_factor");
        // This branch returns, so the trailing setLoginLoading(false) below never runs —
        // and a spinner that survives the step change keeps the second code's button
        // disabled forever. Reset it here, before handing the screen to the second factor.
        setLoginLoading(false);
        return;
      }
      acceptUser(outcome.user);
    } catch (e: unknown) {
      setLoginError(errorMessage(e) || "Failed to verify code");
    }
    setLoginLoading(false);
  };

  const handleVerifyTwoFactor = async () => {
    if (!twoFactor) return;
    setLoginLoading(true);
    setLoginError("");
    setUnauthorized(false);
    try {
      const user = await signInWithTwoFactor(
        twoFactor.challenge,
        twoFactorCode,
      );
      acceptUser(user);
    } catch (e: unknown) {
      setLoginError(
        errorMessage(e) || "Failed to verify the authenticator code",
      );
    }
    setLoginLoading(false);
  };

  const resetToPhone = () => {
    setLoginStep("phone");
    setLoginError("");
    setTwoFactor(null);
    setTwoFactorCode("");
    setLoginPhone(UA_DIAL_PREFIX);
    setLoginCode("");
  };

  // The "this account may not use the admin" notice is a temporary answer to a temporary
  // question: it clears itself so the next attempt starts from a clean form.
  useEffect(() => {
    if (status === "anonymous" && unauthorized) {
      const id = setTimeout(() => setUnauthorized(false), 5000);
      return () => clearTimeout(id);
    }
  }, [status, unauthorized]);

  useEffect(() => {
    if (!unauthorized) return;
    if (blocker) return;
    if (!hasSessionMarker()) return;
    // A session marker that the API refuses is a genuinely unknown state, not a sign-out:
    // show the login form rather than silently clearing a cookie we cannot see (it is
    // HttpOnly) and stranding the operator with no way back in.
    useSession.getState().signOut();
  }, [unauthorized, blocker]);

  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4 relative">
      <div className="aurora-bg" aria-hidden="true">
        <div className="aurora-blob aurora-blob--green" />
        <div className="aurora-blob aurora-blob--cyan" />
        <div className="aurora-blob aurora-blob--teal" />
      </div>
      <div className="glass-panel p-8 w-full max-w-sm relative z-10">
        {/* Forge themes show the chrome lion above the wordmark; the other
            themes keep the plain wordmark they have always had. */}
        {themes[theme].forge && (
          <div className="flex justify-center mb-4">
            <LionMark size={76} />
          </div>
        )}
        <h1 className="text-2xl font-bold mb-6 text-center">
          <span className="glass-text-gradient">Admin Login</span>
        </h1>

        {unauthorized ? (
          <>
            <p className="text-center text-sm mb-6 text-muted-foreground">
              This account is not authorized to use the admin dashboard. Sign in
              with an administrator account, or use the mobile app for purchases
              and vouchers.
            </p>
            <Button
              onClick={() => {
                setUnauthorized(false);
                resetToPhone();
              }}
              className="w-full"
            >
              Use a different account
            </Button>
          </>
        ) : (
          <>
            {loginStep === "phone" ? (
              <>
                <Input
                  placeholder="+380XXXXXXXXX"
                  value={loginPhone}
                  onChange={(e) => setLoginPhone(e.target.value)}
                  className="mb-4"
                />
                <Button
                  onClick={handleSendCode}
                  disabled={
                    loginLoading ||
                    loginPhone.trim().length <= UA_DIAL_PREFIX.length
                  }
                  className="w-full"
                >
                  {loginLoading ? (
                    <Loader2 className="animate-spin" />
                  ) : (
                    "Send Code"
                  )}
                </Button>
              </>
            ) : loginStep === "code" ? (
              <>
                <p className="text-muted-foreground text-sm mb-4 text-center">
                  Code sent to {loginPhone}
                </p>
                <Input
                  placeholder="000000"
                  value={loginCode}
                  onChange={(e) => setLoginCode(e.target.value)}
                  className="mb-4"
                />
                <Button
                  onClick={handleVerifyCode}
                  disabled={loginLoading || !loginCode}
                  className="w-full"
                >
                  {loginLoading ? (
                    <Loader2 className="animate-spin" />
                  ) : (
                    "Verify Code"
                  )}
                </Button>
                <Button
                  variant="ghost"
                  className="w-full mt-2"
                  onClick={resetToPhone}
                >
                  Use a different account
                </Button>
              </>
            ) : (
              <>
                {twoFactor?.needsEnrollment && (
                  <div className="mb-4 rounded border border-border bg-muted/40 p-3">
                    <p className="text-sm font-semibold mb-2">
                      Set up your authenticator
                    </p>
                    <p className="text-xs text-muted-foreground mb-2">
                      Add this key to your authenticator app (Google
                      Authenticator, Authy, 1Password), then type the code it
                      shows.
                    </p>
                    <code className="block break-all font-mono text-xs bg-background rounded p-2 select-all">
                      {twoFactor.enrollmentSecret}
                    </code>
                  </div>
                )}
                <p className="text-sm font-semibold text-center mb-1">
                  Step 2 of 2 — the authenticator code
                </p>
                <p className="text-muted-foreground text-sm mb-4 text-center">
                  The SMS code is confirmed. Now open your authenticator app and
                  enter the fresh 6-digit code it shows — not the code from the
                  SMS.
                </p>
                <Input
                  placeholder="000000"
                  inputMode="numeric"
                  maxLength={6}
                  value={twoFactorCode}
                  onChange={(e) =>
                    setTwoFactorCode(e.target.value.replace(/\D/g, ""))
                  }
                  className="mb-4"
                />
                <Button
                  onClick={handleVerifyTwoFactor}
                  disabled={loginLoading || twoFactorCode.length !== 6}
                  className="w-full"
                >
                  {loginLoading ? (
                    <Loader2 className="animate-spin" />
                  ) : (
                    "Verify Code"
                  )}
                </Button>
                <Button
                  variant="ghost"
                  className="w-full mt-2"
                  onClick={resetToPhone}
                >
                  Use a different account
                </Button>
              </>
            )}

            {loginError && (
              <p className="text-destructive text-sm mt-4 text-center">
                {loginError}
              </p>
            )}
          </>
        )}
      </div>
    </div>
  );
}
