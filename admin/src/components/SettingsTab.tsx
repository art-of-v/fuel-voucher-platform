import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Settings as SettingsIcon, Save, Loader2, AlertTriangle, ShieldCheck, Palette } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { apiRequest } from "@/lib/api-client";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";
import { ThemeSwitcher } from "@/components/ThemeSwitcher";

interface AutoRefundDto {
  enabled: boolean;
  delayDays: number;
}

interface SettingsDto {
  autoRefund: AutoRefundDto;
}

export default function SettingsTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [enabled, setEnabled] = useState(false);
  const [delayDays, setDelayDays] = useState(7);
  const [loaded, setLoaded] = useState(false);

  const { data, isLoading } = useQuery<SettingsDto>({
    queryKey: ["/api/admin/settings"],
    queryFn: async () => {
      return await apiRequest<any, SettingsDto>("GET", "/api/admin/settings");
    }
  });

  useEffect(() => {
    if (data && !loaded) {
      setEnabled(data.autoRefund.enabled);
      setDelayDays(data.autoRefund.delayDays);
      setLoaded(true);
    }
  }, [data, loaded]);

  const saveMutation = useMutation({
    mutationFn: async () => {
      return await apiRequest<any, { success: boolean }>("PUT", "/api/admin/settings", {
        autoRefund: { enabled, delayDays: Math.max(1, Math.round(delayDays) || 1) }
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/settings"] });
      setLoaded(false);
      toast.success(t('settings.saved'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-8">
        <Loader2 className="w-5 h-5 animate-spin" />
        {t('common.loading')}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-2">
        <SettingsIcon className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t('settings.autoRefund')}</h2>
      </div>

      <div className="bg-card border border-border rounded-xl p-6 max-w-xl">
        {!enabled && (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-yellow-500/10 border border-yellow-500/20 text-sm text-yellow-300">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.disabledNote')}</span>
          </div>
        )}

        <div className="space-y-6">
          <div className="flex items-center justify-between gap-4">
            <div>
              <p className="font-medium text-sm">{t('settings.enableAutoRefund')}</p>
              <p className="text-xs text-muted-foreground mt-1">{t('settings.enableAutoRefundHint')}</p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={enabled}
              onClick={() => setEnabled(!enabled)}
              className={`relative w-11 h-6 rounded-full transition-colors shrink-0 ${enabled ? "bg-primary" : "bg-muted border border-border"}`}
            >
              <span
                className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white transition-transform ${enabled ? "translate-x-5" : ""}`}
              />
            </button>
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t('settings.gracePeriod')}
            </label>
            <Input
              type="number"
              min={1}
              max={3650}
              value={delayDays}
              onChange={(e) => setDelayDays(parseInt(e.target.value, 10) || 0)}
              className="h-9 w-40"
            />
            <p className="text-xs text-muted-foreground">{t('settings.gracePeriodHint')}</p>
          </div>

          <div className="flex justify-end">
            <Button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}>
              {saveMutation.isPending ? <Loader2 className="w-4 h-4 mr-1 animate-spin" /> : <Save className="w-4 h-4 mr-1" />}
              {t('settings.save')}
            </Button>
          </div>
        </div>
      </div>

      <QaTestAccessCard />

      <AppearanceCard />
    </div>
  );
}

/**
 * Appearance / colour-theme picker. Persists to localStorage only (theme-store.ts);
 * there is no per-account preference on the server, so the choice is per-browser and
 * shared by every staff viewer on that machine. Phase 1 offers the 5 dark themes.
 */
function AppearanceCard() {
  const { t } = useI18n();

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Palette className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t('appearance.title')}</h2>
      </div>

      <div className="bg-card border border-border rounded-xl p-6 max-w-xl">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">{t('appearance.theme')}</p>
            <p className="text-xs text-muted-foreground mt-1">{t('appearance.themeHint')}</p>
          </div>
          <ThemeSwitcher />
        </div>
      </div>
    </div>
  );
}

interface QaTestAccessDto {
  enabled: boolean;
  configured: boolean;
  phoneNumber: string;
  updatedAtUtc: string | null;
  updatedByUserName: string | null;
}

/**
 * QA App-Store test-access switch. Status is shown to any staff viewer, but the toggle PUT is
 * authorized server-side (Admin/ProductOwner only); a Manager who lacks the role simply gets a 403
 * from the API. The QA verification code is never fetched or shown here — the panel only reflects
 * whether access is on and whether a code is configured on the server.
 */
function QaTestAccessCard() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery<QaTestAccessDto>({
    queryKey: ["/api/admin/qa-test-access"],
    queryFn: async () => apiRequest<any, QaTestAccessDto>("GET", "/api/admin/qa-test-access"),
  });

  const toggleMutation = useMutation({
    mutationFn: async (next: boolean) =>
      apiRequest<{ enabled: boolean }, QaTestAccessDto>("PUT", "/api/admin/qa-test-access", { enabled: next }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/qa-test-access"] });
      toast.success(t('settings.qaSaved'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (isLoading || !data) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-4">
        <Loader2 className="w-4 h-4 animate-spin" />
        {t('common.loading')}
      </div>
    );
  }

  const enabled = data.enabled;
  const lastChanged = data.updatedAtUtc ? new Date(data.updatedAtUtc).toLocaleString() : null;

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <ShieldCheck className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t('settings.qaTitle')}</h2>
        <span
          className={`ml-2 text-[11px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full ${enabled ? "bg-emerald-500/15 text-emerald-300" : "bg-muted text-muted-foreground"}`}
        >
          {enabled ? t('settings.qaStatusEnabled') : t('settings.qaStatusDisabled')}
        </span>
      </div>

      <div className="bg-card border border-border rounded-xl p-6 max-w-xl">
        <p className="text-xs text-muted-foreground mb-5">{t('settings.qaWhat')}</p>

        {!data.configured && (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-red-500/10 border border-red-500/20 text-sm text-red-300">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.qaNotConfigured')}</span>
          </div>
        )}

        {!enabled && data.configured && (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-yellow-500/10 border border-yellow-500/20 text-sm text-yellow-300">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.qaDisabledNote')}</span>
          </div>
        )}

        <div className="space-y-5">
          <div className="flex items-center justify-between gap-4">
            <div>
              <p className="font-medium text-sm">{t('settings.qaEnable')}</p>
              <p className="text-xs text-muted-foreground mt-1">{t('settings.qaEnableHint')}</p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={enabled}
              disabled={toggleMutation.isPending}
              onClick={() => toggleMutation.mutate(!enabled)}
              className={`relative w-11 h-6 rounded-full transition-colors shrink-0 disabled:opacity-50 ${enabled ? "bg-primary" : "bg-muted border border-border"}`}
            >
              <span
                className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white transition-transform ${enabled ? "translate-x-5" : ""}`}
              />
            </button>
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t('settings.qaPhone')}
            </label>
            <code className="text-sm font-mono">{data.phoneNumber}</code>
          </div>

          {lastChanged && (
            <p className="text-xs text-muted-foreground">
              {t('settings.qaLastChanged')}: {lastChanged}
              {data.updatedByUserName ? ` · ${data.updatedByUserName}` : ""}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
