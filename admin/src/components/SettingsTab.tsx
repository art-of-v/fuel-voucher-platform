import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Settings as SettingsIcon,
  Save,
  Loader2,
  AlertTriangle,
  ShieldCheck,
  Palette,
  Trash2,
  Database,
  RefreshCw,
  CalendarX,
  Clock,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { apiRequest } from "@/lib/api-client";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";
import { ThemeSwitcher } from "@/components/ThemeSwitcher";
import { DecimalSettingInput } from "@/components/DecimalSettingInput";
import {
  marginWarningFor,
  type VoucherTermFuelDto,
  type MarginWarning,
} from "@/components/settings/marginWarning";

interface AutoRefundDto {
  enabled: boolean;
  delayDays: number;
}

interface DeletedUnpaidOrderCleanupDto {
  enabled: boolean;
  retentionDays: number;
}

interface DataRetentionDto {
  enabled: boolean;
}

interface ExpiredVoucherLossDto {
  enabled: boolean;
}

interface VoucherTermTierDto {
  term: string;
  enabled: boolean;
  discountPerLiterUah: number;
  offerable: boolean;
}

interface VoucherTermDto {
  enabled: boolean;
  tiers: VoucherTermTierDto[];
  /** Margin a discount must leave per litre, UAH. Reported by the server so it cannot drift. */
  marginFloorUah: number;
  /** Per-fuel margins, thinnest first. */
  fuels: VoucherTermFuelDto[];
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
  deletedUnpaidOrderCleanup: DeletedUnpaidOrderCleanupDto;
  dataRetention: DataRetentionDto;
  expiredVoucherLoss: ExpiredVoucherLossDto;
  voucherRenewal: VoucherRenewalDto;
  voucherTerm: VoucherTermDto;
}

/**
 * One screen, one save.
 *
 * Six cards used to each own their query, their state and their own PUT. That produced six identical
 * "Save" buttons for one endpoint, so it was never obvious which one saved what — and an operator who
 * toggled a switch had to guess. It also produced a worse bug: after saving, each card reset its `loaded`
 * flag, its effect immediately re-seeded local state from the *stale* query data, and by the time the
 * refetch landed the flag was set again so the fresh values were ignored. The screen visibly rolled back
 * to the previous values, and only a hard reload showed what had actually been saved.
 *
 * So the state lives here, the save is one request carrying every section, and a save waits for the
 * refetch before re-seeding from it. A partial PUT still cannot clobber another section, and now there is
 * nothing to clobber with.
 */
export default function SettingsTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery<SettingsDto>({
    queryKey: ["/api/admin/settings"],
    queryFn: async () =>
      apiRequest<any, SettingsDto>("GET", "/api/admin/settings"),
  });

  const [autoRefund, setAutoRefund] = useState({
    enabled: false,
    delayDays: 7,
  });
  const [deletedUnpaidOrderCleanup, setDeletedUnpaidOrderCleanup] = useState({
    enabled: false,
    retentionDays: 30,
  });
  const [dataRetention, setDataRetention] = useState(false);
  const [expiredVoucherLoss, setExpiredVoucherLoss] = useState(false);
  const [renewal, setRenewal] = useState<{
    enabled: boolean;
    thresholdDays: number;
    tiers: VoucherRenewalTierDto[];
  }>({
    enabled: false,
    thresholdDays: 14,
    tiers: [],
  });
  const [termSale, setTermSale] = useState<{
    enabled: boolean;
    tiers: VoucherTermTierDto[];
    marginFloorUah: number;
    fuels: VoucherTermFuelDto[];
  }>({
    enabled: false,
    tiers: [],
    marginFloorUah: 0.5,
    fuels: [],
  });

  // Seed from the server's payload, whenever it changes. The server is the only writer of `data`, so this
  // never has to guess whether an arriving payload is fresh or a replay — which is what made the previous
  // version roll the screen back after a save.
  useEffect(() => {
    if (!data) return;
    setAutoRefund({
      enabled: data.autoRefund.enabled,
      delayDays: data.autoRefund.delayDays,
    });
    setDeletedUnpaidOrderCleanup({
      enabled: data.deletedUnpaidOrderCleanup.enabled,
      retentionDays: data.deletedUnpaidOrderCleanup.retentionDays,
    });
    setDataRetention(data.dataRetention.enabled);
    setExpiredVoucherLoss(data.expiredVoucherLoss.enabled);
    setRenewal({
      enabled: data.voucherRenewal.enabled,
      thresholdDays: data.voucherRenewal.triggerThresholdDays,
      tiers: data.voucherRenewal.tiers.map((tier) => ({ ...tier })),
    });
    setTermSale({
      enabled: data.voucherTerm.enabled,
      tiers: data.voucherTerm.tiers.map((tier) => ({ ...tier })),
      marginFloorUah: data.voucherTerm.marginFloorUah,
      fuels: data.voucherTerm.fuels ?? [],
    });
  }, [data]);

  // Unsaved work, compared against the server's copy rather than tracked with a dirty flag: an edit that
  // lands back on its original value is not unsaved work, and a flag would keep claiming otherwise.
  const isDirty =
    !!data &&
    sectionsDiffer(data, {
      autoRefund,
      deletedUnpaidOrderCleanup,
      dataRetention,
      expiredVoucherLoss,
      renewal,
      termSale,
    });

  const saveMutation = useMutation({
    mutationFn: async () =>
      apiRequest<any, { success: boolean }>("PUT", "/api/admin/settings", {
        autoRefund: {
          enabled: autoRefund.enabled,
          delayDays: Math.max(1, Math.round(autoRefund.delayDays) || 1),
        },
        deletedUnpaidOrderCleanup: {
          enabled: deletedUnpaidOrderCleanup.enabled,
          retentionDays: Math.max(
            1,
            Math.round(deletedUnpaidOrderCleanup.retentionDays) || 1,
          ),
        },
        dataRetention: { enabled: dataRetention },
        expiredVoucherLoss: { enabled: expiredVoucherLoss },
        voucherRenewal: {
          enabled: renewal.enabled,
          triggerThresholdDays: Math.max(
            1,
            Math.round(renewal.thresholdDays) || 1,
          ),
          tiers: renewal.tiers.map((tier) => ({
            term: tier.term,
            enabled: tier.enabled,
            ratePerLiterUah: Math.max(0, tier.ratePerLiterUah || 0),
          })),
        },
        voucherTerm: {
          enabled: termSale.enabled,
          tiers: termSale.tiers.map((tier) => ({
            term: tier.term,
            enabled: tier.enabled,
            discountPerLiterUah: Math.max(0, tier.discountPerLiterUah || 0),
          })),
        },
      }),
    onSuccess: async () => {
      // Await the refetch. Invalidate-and-forget is what let the stale payload win the race and roll the
      // screen back to the values the operator had just replaced.
      await queryClient.invalidateQueries({
        queryKey: ["/api/admin/settings"],
      });
      toast.success(t("settings.saved"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (isLoading || !data) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-8">
        <Loader2 className="w-5 h-5 animate-spin" />
        {t("common.loading")}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* The screen's single save. Sticky so it stays reachable no matter how far down the ladder the
          operator has scrolled — the reason a card-level button went unnoticed in the first place. */}
      <div className="sticky top-0 z-10 flex items-center gap-3 bg-background/95 backdrop-blur py-3 border-b border-border">
        <SettingsIcon className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t("settings.title")}</h2>
        {isDirty && (
          <span className="text-[11px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full bg-warning/15 text-warning">
            {t("settings.unsavedChanges")}
          </span>
        )}
        <Button
          onClick={() => saveMutation.mutate()}
          disabled={saveMutation.isPending || !isDirty}
          className="ml-auto"
        >
          {saveMutation.isPending ? (
            <Loader2 className="w-4 h-4 mr-1 animate-spin" />
          ) : (
            <Save className="w-4 h-4 mr-1" />
          )}
          {isDirty ? t("settings.saveChanges") : t("settings.saved2")}
        </Button>
      </div>

      {/* Two columns on a wide screen instead of one narrow column with half the page empty. */}
      <div className="grid gap-6 xl:grid-cols-2 items-start">
        <AutoRefundCard
          enabled={autoRefund.enabled}
          delayDays={autoRefund.delayDays}
          onChange={(patch) => setAutoRefund((prev) => ({ ...prev, ...patch }))}
        />

        <DeletedUnpaidOrderCleanupCard
          enabled={deletedUnpaidOrderCleanup.enabled}
          retentionDays={deletedUnpaidOrderCleanup.retentionDays}
          onChange={(patch) =>
            setDeletedUnpaidOrderCleanup((prev) => ({ ...prev, ...patch }))
          }
        />

        <DataRetentionCard
          enabled={dataRetention}
          onChange={(enabled) => setDataRetention(enabled)}
        />

        <ExpiredVoucherLossCard
          enabled={expiredVoucherLoss}
          onChange={(enabled) => setExpiredVoucherLoss(enabled)}
        />

        <VoucherRenewalCard
          enabled={renewal.enabled}
          thresholdDays={renewal.thresholdDays}
          tiers={renewal.tiers}
          onChange={(patch) => setRenewal((prev) => ({ ...prev, ...patch }))}
        />

        <VoucherTermSaleCard
          enabled={termSale.enabled}
          tiers={termSale.tiers}
          marginFloorUah={termSale.marginFloorUah}
          fuels={termSale.fuels}
          onChange={(patch) => setTermSale((prev) => ({ ...prev, ...patch }))}
        />
      </div>

      {/* QA access lives behind its own endpoint with its own role check, so it keeps its own immediate
          toggle: folding it into the screen-level save would mean a change that is already a single
          keystroke away from being applied would instead sit waiting for a general save. */}
      <div className="grid gap-6 xl:grid-cols-2 items-start">
        <QaTestAccessCard />
        <AppearanceCard />
      </div>
    </div>
  );
}

type LocalSettings = {
  autoRefund: { enabled: boolean; delayDays: number };
  deletedUnpaidOrderCleanup: { enabled: boolean; retentionDays: number };
  dataRetention: boolean;
  expiredVoucherLoss: boolean;
  renewal: {
    enabled: boolean;
    thresholdDays: number;
    tiers: VoucherRenewalTierDto[];
  };
  termSale: { enabled: boolean; tiers: VoucherTermTierDto[] };
};

function sectionsDiffer(server: SettingsDto, local: LocalSettings): boolean {
  if (server.autoRefund.enabled !== local.autoRefund.enabled) return true;
  if (server.autoRefund.delayDays !== local.autoRefund.delayDays) return true;
  if (server.deletedUnpaidOrderCleanup.enabled !== local.deletedUnpaidOrderCleanup.enabled) return true;
  if (server.deletedUnpaidOrderCleanup.retentionDays !== local.deletedUnpaidOrderCleanup.retentionDays)
    return true;
  if (server.dataRetention.enabled !== local.dataRetention) return true;
  if (server.expiredVoucherLoss.enabled !== local.expiredVoucherLoss)
    return true;
  if (server.voucherRenewal.enabled !== local.renewal.enabled) return true;
  if (
    server.voucherRenewal.triggerThresholdDays !== local.renewal.thresholdDays
  )
    return true;
  if (server.voucherTerm.enabled !== local.termSale.enabled) return true;

  const ratesDiffer = server.voucherRenewal.tiers.some((tier) => {
    const mine = local.renewal.tiers.find(
      (candidate) => candidate.term === tier.term,
    );
    return (
      !mine ||
      mine.enabled !== tier.enabled ||
      mine.ratePerLiterUah !== tier.ratePerLiterUah
    );
  });
  if (ratesDiffer) return true;

  return server.voucherTerm.tiers.some((tier) => {
    const mine = local.termSale.tiers.find(
      (candidate) => candidate.term === tier.term,
    );
    return (
      !mine ||
      mine.enabled !== tier.enabled ||
      mine.discountPerLiterUah !== tier.discountPerLiterUah
    );
  });
}

/** The switch used by every card, in one place so six copies cannot drift apart. */
function Toggle({
  checked,
  onChange,
}: {
  checked: boolean;
  onChange: () => void;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={onChange}
      className={`relative w-11 h-6 rounded-full transition-colors shrink-0 ${checked ? "bg-primary" : "bg-muted border border-border"}`}
    >
      <span
        className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full shadow-sm transition-transform ${checked ? "translate-x-5 bg-primary-foreground" : "bg-foreground"}`}
      />
    </button>
  );
}

/** Title + body chrome shared by every settings card. */
function Card({
  icon,
  title,
  children,
}: {
  icon: React.ReactNode;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        {icon}
        <h2 className="text-xl font-bold">{title}</h2>
      </div>
      <div className="bg-card border border-border rounded-xl p-6">
        {children}
      </div>
    </div>
  );
}

/** The banner a card shows while its feature is on or off, so a destructive switch is never a surprise. */
function StateBanner({
  tone,
  children,
}: {
  tone: "on" | "off" | "danger";
  children: React.ReactNode;
}) {
  const classes =
    tone === "danger"
      ? "bg-destructive/10 border-destructive/20 text-destructive"
      : tone === "on"
        ? "bg-success/10 border-success/20 text-success"
        : "bg-warning/10 border-warning/20 text-warning";

  return (
    <div
      className={`flex items-start gap-2 mb-5 px-4 py-3 rounded-lg border text-sm ${classes}`}
    >
      <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
      <span>{children}</span>
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
        <h2 className="text-xl font-bold">{t("appearance.title")}</h2>
      </div>

      <div className="bg-card border border-border rounded-xl p-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">{t("appearance.theme")}</p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("appearance.themeHint")}
            </p>
          </div>
          <ThemeSwitcher />
        </div>
      </div>
    </div>
  );
}

function AutoRefundCard({
  enabled,
  delayDays,
  onChange,
}: {
  enabled: boolean;
  delayDays: number;
  onChange: (patch: { enabled?: boolean; delayDays?: number }) => void;
}) {
  const { t } = useI18n();

  return (
    <Card
      icon={<SettingsIcon className="w-5 h-5 text-primary" />}
      title={t("settings.autoRefund")}
    >
      {!enabled && (
        <StateBanner tone="off">{t("settings.disabledNote")}</StateBanner>
      )}

      <div className="space-y-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">
              {t("settings.enableAutoRefund")}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("settings.enableAutoRefundHint")}
            </p>
          </div>
          <Toggle
            checked={enabled}
            onChange={() => onChange({ enabled: !enabled })}
          />
        </div>

        <div className="flex flex-col gap-1">
          <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
            {t("settings.gracePeriod")}
          </label>
          <DecimalSettingInput
            type="number"
            inputMode="numeric"
            min={1}
            max={3650}
            value={delayDays}
            onCommit={(value) =>
              onChange({ delayDays: Math.max(1, Math.round(value ?? 0) || 1) })
            }
            className="h-9 w-40"
            aria-label={t("settings.gracePeriod")}
          />
          <p className="text-xs text-muted-foreground">
            {t("settings.gracePeriodHint")}
          </p>
        </div>
      </div>
    </Card>
  );
}

/**
 * Abandoned-order cleanup switch. Controls the nightly DeletedUnpaidOrderCleanupService, which permanently
 * hard-deletes soft-deleted + Cancelled orders older than the retention window. The delete is
 * irreversible, hence the explicit caution while enabled and the fail-safe default of off on the server.
 */
function DeletedUnpaidOrderCleanupCard({
  enabled,
  retentionDays,
  onChange,
}: {
  enabled: boolean;
  retentionDays: number;
  onChange: (patch: { enabled?: boolean; retentionDays?: number }) => void;
}) {
  const { t } = useI18n();

  return (
    <Card
      icon={<Trash2 className="w-5 h-5 text-primary" />}
      title={t("settings.deletedUnpaidOrderCleanupTitle")}
    >
      <p className="text-xs text-muted-foreground mb-5">
        {t("settings.deletedUnpaidOrderCleanupWhat")}
      </p>

      <StateBanner tone={enabled ? "danger" : "off"}>
        {enabled
          ? t("settings.deletedUnpaidOrderCleanupEnabledNote")
          : t("settings.deletedUnpaidOrderCleanupDisabledNote")}
      </StateBanner>

      <div className="space-y-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">
              {t("settings.enableDeletedUnpaidOrderCleanup")}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("settings.enableDeletedUnpaidOrderCleanupHint")}
            </p>
          </div>
          <Toggle
            checked={enabled}
            onChange={() => onChange({ enabled: !enabled })}
          />
        </div>

        <div className="flex flex-col gap-1">
          <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
            {t("settings.retentionPeriod")}
          </label>
          <DecimalSettingInput
            type="number"
            inputMode="numeric"
            min={1}
            max={3650}
            value={retentionDays}
            onCommit={(value) =>
              onChange({
                retentionDays: Math.max(1, Math.round(value ?? 0) || 1),
              })
            }
            className="h-9 w-40"
            aria-label={t("settings.retentionPeriod")}
          />
          <p className="text-xs text-muted-foreground">
            {t("settings.retentionPeriodHint")}
          </p>
        </div>
      </div>
    </Card>
  );
}

/**
 * Data-retention switch for high-churn operational tables. Controls the nightly DataRetentionService,
 * which prunes spent OTPs, dead refresh tokens, processed outbox events, read notifications, aged
 * error logs and stale push tokens — each past its own fixed window. Orders are deliberately out of
 * scope (their irreversible purge is the separate DeletedUnpaidOrderCleanup switch above). Off = dry-run (counts only,
 * deletes nothing); on = the nightly job deletes eligible rows. Windows are fixed in v1, so there is no
 * day input — only the enable toggle.
 */
function DataRetentionCard({
  enabled,
  onChange,
}: {
  enabled: boolean;
  onChange: (enabled: boolean) => void;
}) {
  const { t } = useI18n();

  return (
    <Card
      icon={<Database className="w-5 h-5 text-primary" />}
      title={t("settings.dataRetentionTitle")}
    >
      <p className="text-xs text-muted-foreground mb-5">
        {t("settings.dataRetentionWhat")}
      </p>

      <StateBanner tone={enabled ? "danger" : "off"}>
        {enabled
          ? t("settings.dataRetentionEnabledNote")
          : t("settings.dataRetentionDisabledNote")}
      </StateBanner>

      <div className="space-y-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">
              {t("settings.enableDataRetention")}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("settings.enableDataRetentionHint")}
            </p>
          </div>
          <Toggle checked={enabled} onChange={() => onChange(!enabled)} />
        </div>
      </div>
    </Card>
  );
}

/**
 * Expired-unsold voucher loss-booking switch. Controls the nightly ExpiredVoucherLossService, which
 * retires operator-owned stock that lapsed past its expiration date (Imported / VerifiedWithWarnings /
 * Available) to Expired, so per-batch P&L books its cost as a realised loss instead of phantom future
 * margin. Customer-owned (Assigned) vouchers are never touched — their expiry is the paid renewal flow's
 * concern. Off = dry-run (logs the loss it would book, mutates nothing); on = the nightly job flips
 * eligible vouchers. No day input — eligibility is simply "past the printed expiration date".
 */
function ExpiredVoucherLossCard({
  enabled,
  onChange,
}: {
  enabled: boolean;
  onChange: (enabled: boolean) => void;
}) {
  const { t } = useI18n();

  return (
    <Card
      icon={<CalendarX className="w-5 h-5 text-primary" />}
      title={t("settings.expiredLossTitle")}
    >
      <p className="text-xs text-muted-foreground mb-5">
        {t("settings.expiredLossWhat")}
      </p>

      <StateBanner tone={enabled ? "danger" : "off"}>
        {enabled
          ? t("settings.expiredLossEnabledNote")
          : t("settings.expiredLossDisabledNote")}
      </StateBanner>

      <div className="space-y-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">
              {t("settings.enableExpiredLoss")}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("settings.enableExpiredLossHint")}
            </p>
          </div>
          <Toggle checked={enabled} onChange={() => onChange(!enabled)} />
        </div>
      </div>
    </Card>
  );
}

function termLabelFor(t: (key: string) => string, code: string) {
  const value = code.slice(0, -1);
  const unit = code.endsWith("w")
    ? t("settings.voucherRenewalUnitWeek")
    : t("settings.voucherRenewalUnitMonth");
  return `${value} ${unit}`;
}

/** The per-term ladder, shared by renewal (a rate) and purchase (a discount). */
function TierLadder<T extends { term: string; enabled: boolean }>({
  title,
  hint,
  unitLabel,
  isOfferable,
  rows,
  onToggle,
  field,
  onField,
  warningFor,
}: {
  title: string;
  hint: string;
  unitLabel: string;
  isOfferable: (row: T) => boolean;
  rows: T[];
  onToggle: (term: string) => void;
  field: (row: T) => number;
  onField: (term: string, value: number) => void;
  /** What a discount on this row would cost the business, or undefined when it is safe. */
  warningFor?: (row: T) => MarginWarning | undefined;
}) {
  const { t } = useI18n();

  return (
    <div className="flex flex-col gap-2">
      <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
        {title}
      </label>
      <p className="text-xs text-muted-foreground -mt-1 mb-1">{hint}</p>
      <div className="flex flex-col gap-2">
        {rows.map((row) => {
          const offered = isOfferable(row);
          const warning = warningFor?.(row);
          return (
            <div key={row.term} className="flex flex-col gap-1">
              <div className="flex items-center gap-3">
                <Toggle
                  checked={row.enabled}
                  onChange={() => onToggle(row.term)}
                />
                <span className="text-sm font-medium w-16 shrink-0">
                  {termLabelFor(t, row.term)}
                </span>
                <DecimalSettingInput
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="0.01"
                  value={field(row)}
                  onCommit={(value) =>
                    onField(row.term, Math.max(0, value ?? 0))
                  }
                  className="h-8 w-28"
                  aria-label={`${termLabelFor(t, row.term)} - ${unitLabel}`}
                />
                <span className="text-xs text-muted-foreground w-20 shrink-0">
                  {unitLabel}
                </span>
                <span
                  className={`ml-auto text-[11px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full ${offered ? "bg-success/15 text-success" : "bg-muted text-muted-foreground"}`}
                >
                  {offered
                    ? t("settings.voucherRenewalOffered")
                    : t("settings.voucherRenewalNotOffered")}
                </span>
              </div>
              {warning && (
                <div
                  className={`flex items-start gap-1.5 ml-11 text-[11px] ${warning.tone === "blocked" ? "text-destructive" : "text-warning"}`}
                >
                  <AlertTriangle className="w-3.5 h-3.5 mt-px shrink-0" />
                  <span>
                    {warning.tone === "blocked"
                      ? t(
                          "settings.marginBlocked",
                          String(warning.fuelCount),
                          String(warning.totalFuels),
                          warning.worst,
                        )
                      : t(
                          "settings.marginThin",
                          String(warning.fuelCount),
                          String(warning.totalFuels),
                          warning.worst,
                          warning.remaining.toFixed(2),
                        )}
                  </span>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

/**
 * Paid voucher renewal/replacement config. Controls the feature flag, the trigger window (how many
 * days of remaining validity surface the renew/replace option, expired vouchers always qualify) and
 * the per-term price in UAH per litre. A term is only offered to customers when it is both enabled
 * AND priced above zero (the server's IsOfferable rule), so an admin can enable a term before pricing
 * it without accidentally selling it for free. Off = the option never appears in the app, whatever the
 * prices.
 */
function VoucherRenewalCard({
  enabled,
  thresholdDays,
  tiers,
  onChange,
}: {
  enabled: boolean;
  thresholdDays: number;
  tiers: VoucherRenewalTierDto[];
  onChange: (patch: {
    enabled?: boolean;
    thresholdDays?: number;
    tiers?: VoucherRenewalTierDto[];
  }) => void;
}) {
  const { t } = useI18n();

  const updateTier = (term: string, patch: Partial<VoucherRenewalTierDto>) =>
    onChange({
      tiers: tiers.map((tier) =>
        tier.term === term ? { ...tier, ...patch } : tier,
      ),
    });

  return (
    <Card
      icon={<RefreshCw className="w-5 h-5 text-primary" />}
      title={t("settings.voucherRenewalTitle")}
    >
      <p className="text-xs text-muted-foreground mb-5">
        {t("settings.voucherRenewalWhat")}
      </p>

      <StateBanner tone={enabled ? "on" : "off"}>
        {enabled
          ? t("settings.voucherRenewalEnabledNote")
          : t("settings.voucherRenewalDisabledNote")}
      </StateBanner>

      <div className="space-y-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">
              {t("settings.enableVoucherRenewal")}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("settings.enableVoucherRenewalHint")}
            </p>
          </div>
          <Toggle
            checked={enabled}
            onChange={() => onChange({ enabled: !enabled })}
          />
        </div>

        <div className="flex flex-col gap-1">
          <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
            {t("settings.voucherRenewalThreshold")}
          </label>
          <DecimalSettingInput
            type="number"
            inputMode="numeric"
            min={1}
            max={365}
            value={thresholdDays}
            onCommit={(value) =>
              onChange({
                thresholdDays: Math.max(1, Math.round(value ?? 0) || 1),
              })
            }
            className="h-9 w-40"
            aria-label={t("settings.voucherRenewalThreshold")}
          />
          <p className="text-xs text-muted-foreground">
            {t("settings.voucherRenewalThresholdHint")}
          </p>
        </div>

        <TierLadder
          title={t("settings.voucherRenewalTiersTitle")}
          hint={t("settings.voucherRenewalTiersHint")}
          unitLabel={t("settings.voucherRenewalRateHeader")}
          rows={tiers}
          isOfferable={(row) => row.enabled && row.ratePerLiterUah > 0}
          field={(row) => row.ratePerLiterUah}
          onField={(term, value) =>
            updateTier(term, { ratePerLiterUah: value })
          }
          onToggle={(term) =>
            updateTier(term, {
              enabled: !tiers.find((tier) => tier.term === term)?.enabled,
            })
          }
        />
      </div>
    </Card>
  );
}

/**
 * Purchase term ladder: how much off the litre price a customer gets for committing to a shorter validity.
 *
 * Kept off by default. A discount bigger than the litre price is refused at checkout rather than clamped
 * silently, so the preview here says "not offered" instead of promising a price that cannot be sold.
 */
function VoucherTermSaleCard({
  enabled,
  tiers,
  marginFloorUah,
  fuels,
  onChange,
}: {
  enabled: boolean;
  tiers: VoucherTermTierDto[];
  marginFloorUah: number;
  fuels: VoucherTermFuelDto[];
  onChange: (patch: {
    enabled?: boolean;
    tiers?: VoucherTermTierDto[];
  }) => void;
}) {
  const { t } = useI18n();

  const updateTier = (term: string, patch: Partial<VoucherTermTierDto>) =>
    onChange({
      tiers: tiers.map((tier) =>
        tier.term === term ? { ...tier, ...patch } : tier,
      ),
    });

  return (
    <Card
      icon={<Clock className="w-5 h-5 text-primary" />}
      title={t("settings.voucherTermSaleTitle")}
    >
      <p className="text-xs text-muted-foreground mb-5">
        {t("settings.voucherTermSaleWhat")}
      </p>

      <StateBanner tone={enabled ? "on" : "off"}>
        {enabled
          ? t("settings.voucherTermSaleEnabledNote")
          : t("settings.voucherTermSaleDisabledNote")}
      </StateBanner>

      <div className="space-y-6">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="font-medium text-sm">
              {t("settings.enableVoucherTermSale")}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              {t("settings.enableVoucherTermSaleHint")}
            </p>
          </div>
          <Toggle
            checked={enabled}
            onChange={() => onChange({ enabled: !enabled })}
          />
        </div>

        <TierLadder
          title={t("settings.voucherTermSaleTiersTitle")}
          hint={t("settings.voucherTermSaleTiersHint")}
          unitLabel={t("settings.voucherTermSaleDiscountHeader")}
          rows={tiers}
          isOfferable={(row) => row.enabled && row.discountPerLiterUah > 0}
          field={(row) => row.discountPerLiterUah}
          onField={(term, value) =>
            updateTier(term, { discountPerLiterUah: value })
          }
          warningFor={(row) =>
            marginWarningFor(row.discountPerLiterUah, marginFloorUah, fuels)
          }
          onToggle={(term) =>
            updateTier(term, {
              enabled: !tiers.find((tier) => tier.term === term)?.enabled,
            })
          }
        />
      </div>
    </Card>
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
    queryFn: async () =>
      apiRequest<any, QaTestAccessDto>("GET", "/api/admin/qa-test-access"),
  });

  const toggleMutation = useMutation({
    mutationFn: async (next: boolean) =>
      apiRequest<{ enabled: boolean }, QaTestAccessDto>(
        "PUT",
        "/api/admin/qa-test-access",
        { enabled: next },
      ),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/qa-test-access"],
      });
      toast.success(t("settings.qaSaved"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (isLoading || !data) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-4">
        <Loader2 className="w-4 h-4 animate-spin" />
        {t("common.loading")}
      </div>
    );
  }

  const enabled = data.enabled;
  const lastChanged = data.updatedAtUtc
    ? new Date(data.updatedAtUtc).toLocaleString()
    : null;

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <ShieldCheck className="w-5 h-5 text-primary" />
        <h2 className="text-xl font-bold">{t("settings.qaTitle")}</h2>
        <span
          className={`ml-2 text-[11px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full ${enabled ? "bg-success/15 text-success" : "bg-muted text-muted-foreground"}`}
        >
          {enabled
            ? t("settings.qaStatusEnabled")
            : t("settings.qaStatusDisabled")}
        </span>
      </div>

      <div className="bg-card border border-border rounded-xl p-6">
        <p className="text-xs text-muted-foreground mb-5">
          {t("settings.qaWhat")}
        </p>

        {!data.configured && (
          <StateBanner tone="danger">
            {t("settings.qaNotConfigured")}
          </StateBanner>
        )}

        {!enabled && data.configured && (
          <StateBanner tone="off">{t("settings.qaDisabledNote")}</StateBanner>
        )}

        <div className="space-y-5">
          <div className="flex items-center justify-between gap-4">
            <div>
              <p className="font-medium text-sm">{t("settings.qaEnable")}</p>
              <p className="text-xs text-muted-foreground mt-1">
                {t("settings.qaEnableHint")}
              </p>
            </div>
            <Toggle
              checked={enabled}
              onChange={() => {
                if (!toggleMutation.isPending) toggleMutation.mutate(!enabled);
              }}
            />
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("settings.qaPhone")}
            </label>
            <code className="text-sm font-mono">{data.phoneNumber}</code>
          </div>

          {lastChanged && (
            <p className="text-xs text-muted-foreground">
              {t("settings.qaLastChanged")}: {lastChanged}
              {data.updatedByUserName ? ` · ${data.updatedByUserName}` : ""}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
