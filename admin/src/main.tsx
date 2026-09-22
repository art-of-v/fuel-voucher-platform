import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import * as Sentry from "@sentry/react";
import App from "./App";
import { initSentry } from "./lib/sentry";
import "./index.css";

initSentry();

// Deliberately English and i18n-free: a render crash can take the i18n store (Zustand)
// down with it, so the fallback must not call t(). It mirrors the app's other full-screen
// states (src/pages/admin.tsx) so it doesn't look broken. When Sentry is unconfigured the
// boundary still renders the fallback — it just doesn't report.
function CrashFallback() {
  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4">
      <div className="glass-panel p-8 w-full max-w-sm text-center">
        <h1 className="text-xl font-bold mb-3">Something went wrong</h1>
        <p className="text-sm text-muted-foreground mb-6">
          The admin panel hit an unexpected error. Reloading usually fixes it.
        </p>
        <button
          type="button"
          onClick={() => window.location.reload()}
          className="bg-primary text-primary-foreground rounded-md px-4 py-2 text-sm font-medium"
        >
          Reload
        </button>
      </div>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Sentry.ErrorBoundary fallback={<CrashFallback />}>
      <App />
    </Sentry.ErrorBoundary>
  </StrictMode>
);
