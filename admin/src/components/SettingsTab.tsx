import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Settings as SettingsIcon, Save, Loader2, AlertTriangle, ShieldCheck, Palette, Trash2, Database, RefreshCw } from "lucide-react";
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

interface OrderCleanupDto {
  enabled: boolean;
  retentionDays: number;
}

interface DataRetentionDto {
  enabled: boolean;
}

interface VoucherRenewalTierDto {
  term: string;
  enabled: boolean;
  ratePerLiterUah: number;
  offerable: boolean;
}

interface VoucherRenewalDto {
  enabled: boolean;
  triggerThresholdDays: number;
  tiers: VoucherRenewalTierDto[];
}

interface SettingsDto {
  autoRefund: AutoRefundDto;
  orderCleanup: OrderCleanupDto;
  dataRetention: DataRetentionDto;
  voucherRenewal: VoucherRenewalDto;
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
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-warning/10 border border-warning/20 text-sm text-warning">
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
                className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white shadow-sm transition-transform ${enabled ? "translate-x-5" : ""}`}
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

      <OrderCleanupCard />

      <DataRetentionCard />

      <VoucherRenewalCard />

      <QaTestAccessCard />

      <AppearanceCard />
    </div>
  );
}

/**
 * Appearance / colour-theme picker. Persists to localStorage only (theme-store.ts);
 * there is no per-account preference on the server, so the choice is per-browser and
 * shared by every staff viewer on that machine. Offers all 8 themes (5 dark, 3 light);
 * per-user server-side persistence is deferred (CurrentUser has no prefs field yet).
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

/**
 * Abandoned-order cleanup switch. Controls the nightly OrderCleanupService, which permanently
 * hard-deletes soft-deleted + Cancelled orders older than the retention window. Shares the
 * /api/admin/settings query with the auto-refund card (TanStack dedupes the fetch) but saves
 * only its own section, so toggling one never clobbers the other. The delete is irreversible,
 * hence the explicit caution while enabled and the fail-safe default of off on the server.
 */
function OrderCleanupCard() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [enabled, setEnabled] = useState(false);
  const [retentionDays, setRetentionDays] = useState(30);
  const [loaded, setLoaded] = useState(false);

  const { data, isLoading } = useQuery<SettingsDto>({
    queryKey: ["/api/admin/settings"],
    queryFn: async () => apiRequest<any, SettingsDto>("GET", "/api/admin/settings"),
  });

  useEffect(() => {
    if (data?.orderCleanup && !loaded) {
      setEnabled(data.orderCleanup.enabled);
      setRetentionDays(data.orderCleanup.retentionDays);
      setLoaded(true);
    }
  }, [data, loaded]);

  const saveMutation = useMutation({
    mutationFn: async () =>
      apiRequest<any, { success: boolean }>("PUT", "/api/admin/settings", {
        orderCleanup: { enabled, retentionDays: Math.max(1, Math.round(retentionDays) || 1) },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/settings"] });
      setLoaded(false);
      toast.success(t('settings.saved'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-4">
        <Loader2 className="w-4 h-4 animate-spin" />
        {t('common.loading')}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Trash2 className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t('settings.orderCleanupTitle')}</h2>
      </div>

      <div className="bg-card border border-border rounded-xl p-6 max-w-xl">
        <p className="text-xs text-muted-foreground mb-5">{t('settings.orderCleanupWhat')}</p>

        {enabled ? (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-destructive/10 border border-destructive/20 text-sm text-destructive">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.orderCleanupEnabledNote')}</span>
          </div>
        ) : (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-warning/10 border border-warning/20 text-sm text-warning">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.orderCleanupDisabledNote')}</span>
          </div>
        )}

        <div className="space-y-6">
          <div className="flex items-center justify-between gap-4">
            <div>
              <p className="font-medium text-sm">{t('settings.enableOrderCleanup')}</p>
              <p className="text-xs text-muted-foreground mt-1">{t('settings.enableOrderCleanupHint')}</p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={enabled}
              onClick={() => setEnabled(!enabled)}
              className={`relative w-11 h-6 rounded-full transition-colors shrink-0 ${enabled ? "bg-primary" : "bg-muted border border-border"}`}
            >
              <span
                className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white shadow-sm transition-transform ${enabled ? "translate-x-5" : ""}`}
              />
            </button>
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t('settings.retentionPeriod')}
            </label>
            <Input
              type="number"
              min={1}
              max={3650}
              value={retentionDays}
              onChange={(e) => setRetentionDays(parseInt(e.target.value, 10) || 0)}
              className="h-9 w-40"
            />
            <p className="text-xs text-muted-foreground">{t('settings.retentionPeriodHint')}</p>
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

/**
 * Data-retention switch for high-churn operational tables. Controls the nightly DataRetentionService,
 * which prunes spent OTPs, dead refresh tokens, processed outbox events, read notifications, aged
 * error logs and stale push tokens — each past its own fixed window. Orders are deliberately out of
 * scope (their irreversible purge is the separate OrderCleanup switch above). Shares the
 * /api/admin/settings query and saves only its own section. Off = dry-run (counts only, deletes
 * nothing); on = the nightly job deletes eligible rows. Windows are fixed in v1, so there is no
 * day input — only the enable toggle.
 */
function DataRetentionCard() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [enabled, setEnabled] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const { data, isLoading } = useQuery<SettingsDto>({
    queryKey: ["/api/admin/settings"],
    queryFn: async () => apiRequest<any, SettingsDto>("GET", "/api/admin/settings"),
  });

  useEffect(() => {
    if (data?.dataRetention && !loaded) {
      setEnabled(data.dataRetention.enabled);
      setLoaded(true);
    }
  }, [data, loaded]);

  const saveMutation = useMutation({
    mutationFn: async () =>
      apiRequest<any, { success: boolean }>("PUT", "/api/admin/settings", {
        dataRetention: { enabled },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/settings"] });
      setLoaded(false);
      toast.success(t('settings.saved'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-4">
        <Loader2 className="w-4 h-4 animate-spin" />
        {t('common.loading')}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Database className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t('settings.dataRetentionTitle')}</h2>
      </div>

      <div className="bg-card border border-border rounded-xl p-6 max-w-xl">
        <p className="text-xs text-muted-foreground mb-5">{t('settings.dataRetentionWhat')}</p>

        {enabled ? (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-destructive/10 border border-destructive/20 text-sm text-destructive">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.dataRetentionEnabledNote')}</span>
          </div>
        ) : (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-warning/10 border border-warning/20 text-sm text-warning">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.dataRetentionDisabledNote')}</span>
          </div>
        )}

        <div className="space-y-6">
          <div className="flex items-center justify-between gap-4">
            <div>
              <p className="font-medium text-sm">{t('settings.enableDataRetention')}</p>
              <p className="text-xs text-muted-foreground mt-1">{t('settings.enableDataRetentionHint')}</p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={enabled}
              onClick={() => setEnabled(!enabled)}
              className={`relative w-11 h-6 rounded-full transition-colors shrink-0 ${enabled ? "bg-primary" : "bg-muted border border-border"}`}
            >
              <span
                className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white shadow-sm transition-transform ${enabled ? "translate-x-5" : ""}`}
              />
            </button>
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

/**
 * Paid voucher renewal/replacement config. Controls the feature flag, the trigger window (how many
 * days of remaining validity surface the renew/replace option, expired vouchers always qualify) and
 * the per-term price in UAH per litre. A term is only offered to customers when it is both enabled
 * AND priced above zero (the server's IsOfferable rule), so an admin can enable a term before pricing
 * it without accidentally selling it for free. Shares the /api/admin/settings query and saves only its
 * own section. Off = the option never appears in the app, whatever the prices.
 */
function VoucherRenewalCard() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [enabled, setEnabled] = useState(false);
  const [thresholdDays, setThresholdDays] = useState(14);
  const [tiers, setTiers] = useState<VoucherRenewalTierDto[]>([]);
  const [loaded, setLoaded] = useState(false);

  const { data, isLoading } = useQuery<SettingsDto>({
    queryKey: ["/api/admin/settings"],
    queryFn: async () => apiRequest<any, SettingsDto>("GET", "/api/admin/settings"),
  });

  useEffect(() => {
    if (data?.voucherRenewal && !loaded) {
      setEnabled(data.voucherRenewal.enabled);
      setThresholdDays(data.voucherRenewal.triggerThresholdDays);
      setTiers(data.voucherRenewal.tiers.map((tier) => ({ ...tier })));
      setLoaded(true);
    }
  }, [data, loaded]);

  const saveMutation = useMutation({
    mutationFn: async () =>
      apiRequest<any, { success: boolean }>("PUT", "/api/admin/settings", {
        voucherRenewal: {
          enabled,
          triggerThresholdDays: Math.max(1, Math.round(thresholdDays) || 1),
          tiers: tiers.map((tier) => ({
            term: tier.term,
            enabled: tier.enabled,
            ratePerLiterUah: Math.max(0, tier.ratePerLiterUah || 0),
          })),
        },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/settings"] });
      setLoaded(false);
      toast.success(t('settings.saved'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const termLabel = (code: string) => {
    const value = code.slice(0, -1);
    const unit = code.endsWith("w") ? t('settings.voucherRenewalUnitWeek') : t('settings.voucherRenewalUnitMonth');
    return `${value} ${unit}`;
  };

  const updateTier = (index: number, patch: Partial<VoucherRenewalTierDto>) => {
    setTiers((prev) => prev.map((tier, i) => (i === index ? { ...tier, ...patch } : tier)));
  };

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-4">
        <Loader2 className="w-4 h-4 animate-spin" />
        {t('common.loading')}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <RefreshCw className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t('settings.voucherRenewalTitle')}</h2>
      </div>

      <div className="bg-card border border-border rounded-xl p-6 max-w-xl">
        <p className="text-xs text-muted-foreground mb-5">{t('settings.voucherRenewalWhat')}</p>

        {enabled ? (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-success/10 border border-success/20 text-sm text-success">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.voucherRenewalEnabledNote')}</span>
          </div>
        ) : (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-warning/10 border border-warning/20 text-sm text-warning">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.voucherRenewalDisabledNote')}</span>
          </div>
        )}

        <div className="space-y-6">
          <div className="flex items-center justify-between gap-4">
            <div>
              <p className="font-medium text-sm">{t('settings.enableVoucherRenewal')}</p>
              <p className="text-xs text-muted-foreground mt-1">{t('settings.enableVoucherRenewalHint')}</p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={enabled}
              onClick={() => setEnabled(!enabled)}
              className={`relative w-11 h-6 rounded-full transition-colors shrink-0 ${enabled ? "bg-primary" : "bg-muted border border-border"}`}
            >
              <span
                className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white shadow-sm transition-transform ${enabled ? "translate-x-5" : ""}`}
              />
            </button>
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t('settings.voucherRenewalThreshold')}
            </label>
            <Input
              type="number"
              min={1}
              max={365}
              value={thresholdDays}
              onChange={(e) => setThresholdDays(parseInt(e.target.value, 10) || 0)}
              className="h-9 w-40"
            />
            <p className="text-xs text-muted-foreground">{t('settings.voucherRenewalThresholdHint')}</p>
          </div>

          <div className="flex flex-col gap-2">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t('settings.voucherRenewalTiersTitle')}
            </label>
            <p className="text-xs text-muted-foreground -mt-1 mb-1">{t('settings.voucherRenewalTiersHint')}</p>
            <div className="flex flex-col gap-2">
              {tiers.map((tier, index) => (
                <div key={tier.term} className="flex items-center gap-3">
                  <button
                    type="button"
                    role="switch"
                    aria-checked={tier.enabled}
                    onClick={() => updateTier(index, { enabled: !tier.enabled })}
                    className={`relative w-9 h-5 rounded-full transition-colors shrink-0 ${tier.enabled ? "bg-primary" : "bg-muted border border-border"}`}
                  >
                    <span
                      className={`absolute top-0.5 left-0.5 w-4 h-4 rounded-full bg-white shadow-sm transition-transform ${tier.enabled ? "translate-x-4" : ""}`}
                    />
                  </button>
                  <span className="text-sm font-medium w-16 shrink-0">{termLabel(tier.term)}</span>
                  <Input
                    type="number"
                    min={0}
                    step="0.01"
                    value={tier.ratePerLiterUah}
                    onChange={(e) => updateTier(index, { ratePerLiterUah: parseFloat(e.target.value) || 0 })}
                    className="h-8 w-28"
                    aria-label={`${termLabel(tier.term)} — ${t('settings.voucherRenewalRateHeader')}`}
                  />
                  <span className="text-xs text-muted-foreground w-20 shrink-0">{t('settings.voucherRenewalRateHeader')}</span>
                  <span
                    className={`ml-auto text-[11px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full ${tier.enabled && tier.ratePerLiterUah > 0 ? "bg-success/15 text-success" : "bg-muted text-muted-foreground"}`}
                  >
                    {tier.enabled && tier.ratePerLiterUah > 0 ? t('settings.voucherRenewalOffered') : t('settings.voucherRenewalNotOffered')}
                  </span>
                </div>
              ))}
            </div>
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
          className={`ml-2 text-[11px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full ${enabled ? "bg-success/15 text-success" : "bg-muted text-muted-foreground"}`}
        >
          {enabled ? t('settings.qaStatusEnabled') : t('settings.qaStatusDisabled')}
        </span>
      </div>

      <div className="bg-card border border-border rounded-xl p-6 max-w-xl">
        <p className="text-xs text-muted-foreground mb-5">{t('settings.qaWhat')}</p>

        {!data.configured && (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-destructive/10 border border-destructive/20 text-sm text-destructive">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
            <span>{t('settings.qaNotConfigured')}</span>
          </div>
        )}

        {!enabled && data.configured && (
          <div className="flex items-start gap-2 mb-5 px-4 py-3 rounded-lg bg-warning/10 border border-warning/20 text-sm text-warning">
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
                className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white shadow-sm transition-transform ${enabled ? "translate-x-5" : ""}`}
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
