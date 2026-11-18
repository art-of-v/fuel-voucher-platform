import { AlertTriangle, CheckCircle2, Loader2, Mail } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetTitle,
} from "@/components/ui/sheet";
import { useI18n } from "@/lib/i18n";
import { cn, formatDateTime } from "@/lib/utils";
import {
  BUSINESS_LEAD_STATUSES,
  businessLeadKeys,
  getBusinessLead,
  type BusinessLead,
  type BusinessLeadStatus,
} from "./business-leads";

interface BusinessLeadDrawerProps {
  leadId: string | null;
  fallback?: BusinessLead | null;
  statusPending: boolean;
  onOpenChange: (open: boolean) => void;
  onStatusChange: (id: string, status: BusinessLeadStatus) => void;
}

export default function BusinessLeadDrawer({
  leadId,
  fallback,
  statusPending,
  onOpenChange,
  onStatusChange,
}: BusinessLeadDrawerProps) {
  const { t } = useI18n();
  const { data, isLoading, isError } = useQuery({
    queryKey: businessLeadKeys.detail(leadId),
    queryFn: () => getBusinessLead(leadId!),
    enabled: !!leadId,
    staleTime: 30_000,
  });
  const lead = data ?? fallback;

  return (
    <Sheet open={!!leadId} onOpenChange={onOpenChange}>
      <SheetContent>
        <div className="border-b border-border p-6 pr-14">
          <SheetTitle className="text-lg font-semibold">
            {lead?.companyName ?? t("businessLeads.detail")}
          </SheetTitle>
          <SheetDescription className="mt-1 text-sm text-muted-foreground">
            {lead?.contactName ?? t("common.loading")}
          </SheetDescription>
        </div>

        <div className="flex-1 overflow-y-auto p-6">
          {isLoading && !lead && (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
              {t("common.loading")}
            </div>
          )}
          {isError && !lead && (
            <p role="alert" className="text-sm text-destructive">
              {t("businessLeads.loadFailed")}
            </p>
          )}

          {lead && (
            <div className="space-y-6">
              <section className="space-y-2 text-sm">
                <DetailRow
                  label={t("businessLeads.status")}
                  value={t(`businessLeads.status.${lead.status}`)}
                />
                <DetailRow
                  label={t("businessLeads.contact")}
                  value={lead.contactName}
                />
                <DetailRow
                  label={t("businessLeads.phone")}
                  value={lead.phone}
                  href={`tel:${lead.phone}`}
                />
                <DetailRow
                  label={t("businessLeads.email")}
                  value={lead.email ?? "—"}
                  href={lead.email ? `mailto:${lead.email}` : undefined}
                />
                <DetailRow
                  label={t("businessLeads.fleetSize")}
                  value={lead.fleetSize?.toLocaleString() ?? "—"}
                />
                <DetailRow
                  label={t("businessLeads.received")}
                  value={formatDateTime(lead.createdAtUtc)}
                />
                <DetailRow
                  label={t("businessLeads.updatedAt")}
                  value={formatDateTime(lead.updatedAtUtc)}
                />
              </section>

              <section>
                <h3 className="mb-2 text-sm font-semibold">
                  {t("businessLeads.message")}
                </h3>
                <p className="whitespace-pre-wrap break-words rounded-md border border-border bg-muted/30 p-3 text-sm text-muted-foreground">
                  {lead.message || t("businessLeads.noMessage")}
                </p>
              </section>

              <section>
                <h3 className="mb-2 text-sm font-semibold">
                  {t("businessLeads.emailDelivery")}
                </h3>
                <div
                  className={cn(
                    "flex items-start gap-2 rounded-md border p-3 text-sm",
                    lead.emailSendError
                      ? "border-destructive/40 bg-destructive/10 text-destructive"
                      : lead.emailSentAtUtc
                        ? "border-success/40 bg-success/10 text-success"
                        : "border-border bg-muted/30 text-muted-foreground",
                  )}
                >
                  {lead.emailSendError ? (
                    <AlertTriangle className="mt-0.5 h-4 w-4" />
                  ) : lead.emailSentAtUtc ? (
                    <CheckCircle2 className="mt-0.5 h-4 w-4" />
                  ) : (
                    <Mail className="mt-0.5 h-4 w-4" />
                  )}
                  <span className="break-words">
                    {lead.emailSendError ||
                      (lead.emailSentAtUtc
                        ? t(
                            "businessLeads.emailSentAt",
                            formatDateTime(lead.emailSentAtUtc),
                          )
                        : t("businessLeads.emailNotSent"))}
                  </span>
                </div>
              </section>

              <section>
                <h3 className="mb-2 text-sm font-semibold">
                  {t("businessLeads.moveTo")}
                </h3>
                <div className="flex flex-wrap gap-2">
                  {BUSINESS_LEAD_STATUSES.filter(
                    (status) => status !== lead.status,
                  ).map((status) => (
                    <Button
                      key={status}
                      type="button"
                      size="sm"
                      variant={
                        status === "Rejected" ? "destructive" : "outline"
                      }
                      disabled={statusPending}
                      onClick={() => onStatusChange(lead.id, status)}
                    >
                      {t(`businessLeads.status.${status}`)}
                    </Button>
                  ))}
                </div>
              </section>
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function DetailRow({
  label,
  value,
  href,
}: {
  label: string;
  value: string;
  href?: string;
}) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-border/60 py-2">
      <span className="text-muted-foreground">{label}</span>
      {href ? (
        <a
          className="text-right font-medium text-primary hover:underline"
          href={href}
        >
          {value}
        </a>
      ) : (
        <span className="text-right font-medium">{value}</span>
      )}
    </div>
  );
}
