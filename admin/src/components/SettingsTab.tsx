import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Settings as SettingsIcon, Save, Loader2, AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { apiRequest } from "@/lib/api-client";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";

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
    </div>
  );
}
