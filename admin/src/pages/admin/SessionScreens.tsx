import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { bootSession } from "@/lib/session";
import { useSession, type SessionBlocker } from "@/lib/session-store";

/**
 * The screens that stand in for the dashboard when there is no session.
 *
 * <p>
 * They live in their own file because they are the only part of the admin that is not the
 * dashboard: a spinner while the session is being decided, and a reason why it cannot be.
 */

/** Nothing has been decided yet. Never block on this - the decision is one refresh away. */
export function BootingScreen() {
  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4 relative">
      <div className="aurora-bg" aria-hidden="true">
        <div className="aurora-blob aurora-blob--green" />
        <div className="aurora-blob aurora-blob--cyan" />
      </div>
      <Loader2 className="w-8 h-8 animate-spin text-primary relative z-10" />
    </div>
  );
}

/**
 * The session is unavailable for a reason that is not a sign-out.
 *
 * <p>
 * A rate limit is not an outage, and the difference matters: the old screen said "you are still
 * signed in" over a self-inflicted 429, and its retry button spent another unit of the very
 * budget that produced the 429. So: name it, wait it out, and only re-check once the server's own
 * `Retry-After` has elapsed.
 */
export function BlockedScreen({ blocker }: { blocker: SessionBlocker }) {
  // The countdown ticks on its own. Reading the clock during render instead would freeze the
  // number until something else happened to re-render the screen, which is how it used to read:
  // a stuck "wait 42s" that never became "try again".
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

  const retryAt = blocker.reason === "rate_limited" ? blocker.retryAt : 0;
  const remaining = Math.max(0, Math.ceil((retryAt - now) / 1000));

  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4 relative">
      <div className="aurora-bg" aria-hidden="true">
        <div className="aurora-blob aurora-blob--green" />
        <div className="aurora-blob aurora-blob--cyan" />
      </div>
      <div className="glass-panel p-8 w-full max-sm relative z-10 text-center">
        <h1 className="text-2xl font-bold mb-4">
          <span className="glass-text-gradient">
            {blocker.reason === "rate_limited"
              ? "Too many attempts"
              : "Server unavailable"}
          </span>
        </h1>
        <p className="text-sm mb-6 text-muted-foreground">
          {blocker.reason === "rate_limited" ? (
            <>
              You have tried to restore this session several times in a row.
              Wait{" "}
              <span className="text-primary font-semibold">{remaining}s</span>{" "}
              and it will clear on its own - nothing is wrong with your account.
            </>
          ) : (
            <>
              Can't reach the server right now. This usually clears up in a few
              seconds after a deploy.
            </>
          )}
        </p>
        <Button
          onClick={() => {
            useSession.getState().unblock();
            bootSession();
          }}
          disabled={remaining > 0}
          className="w-full"
        >
          {remaining > 0 ? `Try again in ${remaining}s` : "Try again"}
        </Button>
      </div>
    </div>
  );
}
