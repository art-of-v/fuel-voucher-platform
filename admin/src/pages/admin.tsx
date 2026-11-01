import { useEffect } from "react";
import { bootSession, signOutForInactivity } from "@/lib/session";
import { startIdleLock } from "@/lib/idle-lock";
import { useSession } from "@/lib/session-store";
import AdminDashboard from "./admin/AdminDashboard";
import AdminLogin from "./admin/AdminLogin";
import { BlockedScreen, BootingScreen } from "./admin/SessionScreens";

/**
 * The admin SPA's entry point: a session gate and the dashboard behind it.
 *
 * <p>
 * This used to be one 4 945-line file with everything in it - the sign-in form, the sidebar, the
 * voucher table, the reconciliation and the report - all inside a single component, and every
 * answer from the API typed `any`. It is now three things: this gate, the dashboard, and one
 * component per screen under `pages/admin/`.
 *
 * <p>
 * The gate is the one place that decides what the operator sees. There is no "signed in" flag
 * here to disagree with the session store: the store's own status is the answer, and the three
 * states below it are the three answers it can give.
 */
export default function AdminScreen() {
  const status = useSession((s) => s.status);
  const blocker = useSession((s) => s.blocker);

  useEffect(() => {
    // The single place the app decides what a session is worth. With no session marker this
    // settles on "anonymous" without touching the network, which is what guarantees the login
    // form can always spend its own share of the per-minute refresh budget.
    bootSession();
  }, []);

  useEffect(() => {
    // An unattended panel is a live staff session until something says otherwise. The lock sits
    // here, over the authenticated state, so it starts when the dashboard mounts and stops the
    // moment the session ends for any reason.
    if (status !== "authenticated") return;
    const lock = startIdleLock({ onLock: () => void signOutForInactivity() });
    return () => lock.stop();
  }, [status]);

  if (status === "booting") {
    return <BootingScreen />;
  }

  if (blocker) {
    return <BlockedScreen blocker={blocker} />;
  }

  if (status !== "authenticated") {
    return <AdminLogin />;
  }

  return <AdminDashboard />;
}
