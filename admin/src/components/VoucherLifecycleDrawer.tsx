import { useQuery } from "@tanstack/react-query";
import {
  ArrowRight,
  History,
  Loader2,
  MapPin,
  Banknote,
  ScrollText,
} from "lucide-react";

import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { formatDateTime } from "@/lib/utils";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetTitle,
} from "@/components/ui/sheet";

// Mirrors VoucherLifecycleResponse on the backend (System.Text.Json camelCase).
interface LifecycleEntry {
  source: "movement" | "event" | "money";
  type: string;
  occurredAtUtc: string;
  from: string | null;
  to: string | null;
  previousCustomerExpiration: string | null;
  newCustomerExpiration: string | null;
  termCode: string | null;
  amountUah: number | null;
  costDeltaUah: number | null;
  actorKind: string | null;
  actorUserId: string | null;
  actorName: string | null;
  reason: string | null;
  externalRef: string | null;
  orderId: string | null;
}

interface LifecycleResponse {
  voucherId: string;
  voucherNumber: string | null;
  status: string;
  liters: number;
  brand: string;
  isArchived: boolean;
  supplierId: string | null;
  supplierName: string | null;
  costPerLiter: number | null;
  supplierExpirationDate: string;
  customerExpirationDate: string;
  importJobId: string | null;
  importFileName: string | null;
  importedAtUtc: string | null;
  rootVoucherId: string;
  lineage: string[];
  entries: LifecycleEntry[];
}

/**
 * One voucher, whole.
 *
 * <p>
 * This is the reader the D-13 record was built for and the surface
 * {@code VOUCHER_LIFECYCLE.md} asks for: the paper's custody, everything done to it, and the paid
 * renewals in its lineage, in one list ordered by time.
 * </p>
 *
 * <p>
 * <b>Three sources, three icons, never merged into one noun.</b> A movement says where the paper went
 * ({@link MapPin}), an event says what was done to it ({@link ScrollText}), and a paid renewal says
 * what the customer handed over ({@link Banknote}). The backend deliberately returns three different
 * vocabularies, because they mean different things and one invented noun would hide which was which.
 * </p>
 *
 * <p>
 * <b>The author is shown when it resolves and nothing is invented when it does not.</b> {@code
 * actor_user_id} is FK-free so an audit row outlives its author, so a lookup may come back empty. The
 * row then shows the kind alone — "the system" — rather than a placeholder name, because a
 * placeholder would put a person where the truth is that there was none.
 * </p>
 */
export function VoucherLifecycleDrawer({
  voucherId,
  voucherNumber,
  onOpenChange,
}: {
  voucherId: string | null;
  voucherNumber?: string | null;
  onOpenChange: (open: boolean) => void;
}) {
  const { t } = useI18n();

  const { data, isLoading, isError } = useQuery<LifecycleResponse>({
    queryKey: ["/api/admin/vouchers", voucherId, "lifecycle"],
    enabled: !!voucherId,
    queryFn: () =>
      apiRequest<unknown, LifecycleResponse>(
        "GET",
        `/api/admin/vouchers/${encodeURIComponent(voucherId!)}/lifecycle`
      ),
    staleTime: 30_000,
  });

  return (
    <Sheet open={!!voucherId} onOpenChange={onOpenChange}>
      <SheetContent>
        <div className="border-b border-border p-6 pr-14">
          <SheetTitle className="flex items-center gap-2 text-lg font-semibold">
            <History className="h-4 w-4 text-muted-foreground" />
            {t("voucherLifecycle.title")}
          </SheetTitle>
          <SheetDescription className="mt-1 text-sm text-muted-foreground">
            {data?.voucherNumber ?? voucherNumber ?? t("voucherLifecycle.loading")}
          </SheetDescription>
        </div>

        <div className="flex-1 overflow-y-auto p-6">
          {isLoading && (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
              {t("common.loading")}
            </div>
          )}

          {isError && (
            <p className="text-sm text-destructive">{t("voucherLifecycle.loadFailed")}</p>
          )}

          {data && (
            <>
              <Header data={data} />

              <h3 className="mb-3 mt-6 text-sm font-semibold">
                {t("voucherLifecycle.timeline")}
              </h3>

              {data.entries.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  {t("voucherLifecycle.empty")}
                </p>
              ) : (
                <ol className="relative space-y-4 border-l border-border pl-5">
                  {data.entries.map((entry, index) => (
                    <li key={`${entry.source}-${entry.type}-${entry.occurredAtUtc}-${index}`}>
                      <Entry entry={entry} />
                    </li>
                  ))}
                </ol>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function Header({ data }: { data: LifecycleResponse }) {
  const { t } = useI18n();

  return (
    <div className="space-y-2 text-sm">
      {data.isArchived && (
        <p className="rounded-md border border-border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
          {t("voucherLifecycle.archivedNote")}
        </p>
      )}
      <Row label={t("vouchers.brand")} value={data.brand} />
      <Row label={t("vouchers.volume")} value={`${data.liters}`} />
      <Row label={t("vouchers.status")} value={data.status} />
      <Row label={t("vouchers.supplier")} value={data.supplierName ?? t("voucherLifecycle.unknown")} />
      <Row
        label={t("vouchers.costPerLiter")}
        value={data.costPerLiter === null ? t("voucherLifecycle.notRecorded") : `${data.costPerLiter}`}
      />
      <Row label={t("vouchers.expires")} value={data.supplierExpirationDate} />
      <Row
        label={t("voucherLifecycle.importedFrom")}
        value={data.importFileName ?? t("voucherLifecycle.unknown")}
      />
      {data.lineage.length > 1 && (
        <Row
          label={t("voucherLifecycle.lineage")}
          value={`${data.lineage.length} ${t("voucherLifecycle.lineageVouchers")}`}
        />
      )}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-right font-medium">{value}</span>
    </div>
  );
}

function Entry({ entry }: { entry: LifecycleEntry }) {
  const { t } = useI18n();

  const icon =
    entry.source === "movement" ? (
      <MapPin className="h-3.5 w-3.5" />
    ) : entry.source === "money" ? (
      <Banknote className="h-3.5 w-3.5" />
    ) : (
      <ScrollText className="h-3.5 w-3.5" />
    );

  return (
    <div className="relative">
      <span className="absolute -left-[1.6rem] top-0.5 rounded-full border border-border bg-background p-1 text-muted-foreground">
        {icon}
      </span>

      <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
        <span className="text-sm font-medium">{wordFor(t, entry, entry.type)}</span>
        <span className="text-xs text-muted-foreground">{formatDateTime(entry.occurredAtUtc)}</span>
      </div>

      {(entry.from || entry.to) && (
        <p className="mt-1 flex items-center gap-1.5 text-xs text-muted-foreground">
          {entry.from && <span>{wordFor(t, entry, entry.from)}</span>}
          {entry.from && entry.to && <ArrowRight className="h-3 w-3" />}
          {entry.to && <span>{wordFor(t, entry, entry.to)}</span>}
        </p>
      )}

      {entry.newCustomerExpiration && (
        <p className="mt-1 text-xs text-muted-foreground">
          {t("voucherLifecycle.term")}: {entry.previousCustomerExpiration} →{" "}
          {entry.newCustomerExpiration}
          {entry.termCode ? ` (${entry.termCode})` : ""}
        </p>
      )}

      {entry.amountUah !== null && (
        <p className="mt-1 text-xs text-muted-foreground">
          {t("voucherLifecycle.paid")}: {entry.amountUah}
        </p>
      )}

      <p className="mt-1 text-xs text-muted-foreground">{authorFor(t, entry)}</p>

      {entry.reason && <p className="mt-1 text-xs italic">{entry.reason}</p>}
    </div>
  );
}

/**
 * The backend's own vocabulary, passed through {@code t()} rather than translated on the way out.
 *
 * <p>
 * The key IS the word: {@code t()} falls back to the key when a locale has no entry, so a movement
 * kind or event type nobody has localised yet still reads as itself rather than as a blank. That is
 * the honest failure for an audit row — an operator seeing "recall" is better served than one seeing
 * nothing, and far better than one seeing an invented label for a fact the database asserts.
 * </p>
 */
function wordFor(t: (key: string, ...params: string[]) => string, entry: LifecycleEntry, word: string): string {
  const prefix =
    entry.source === "movement"
      ? "voucherLifecycle.kind"
      : entry.source === "event"
        ? "voucherLifecycle.event"
        : "voucherLifecycle.renewal";
  return t(`${prefix}.${word}`);
}

function authorFor(t: (key: string, ...params: string[]) => string, entry: LifecycleEntry): string {
  if (entry.actorKind === "system" || (!entry.actorKind && !entry.actorUserId)) {
    return t("voucherLifecycle.system");
  }
  if (entry.actorName) return entry.actorName;
  // The id is still there and still answerable, so it is shown rather than replaced by a placeholder.
  return entry.actorUserId
    ? t("voucherLifecycle.actorById", entry.actorUserId)
    : t("voucherLifecycle.system");
}
