import { useEffect } from "react";
import { bootSession } from "@/lib/session";
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
