import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { toast } from "sonner";

interface HeldPaymentDismissProps {
  paymentId: string;
  /** Called after the server accepted the dismissal, so the list can refetch. */
  onDismissed: () => void;
}

// Not a delete: the money is booked and matched to the statement, so only the queue forgets it.
export default function HeldPaymentDismiss({
  paymentId,
  onDismissed,
}: HeldPaymentDismissProps) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");

  const dismiss = useMutation({
    mutationFn: (body: { reason: string }) =>
      apiRequest<{ reason: string }, void>(
        "POST",
        `/api/admin/payments/${paymentId}/dismiss`,
        body,
      ),
    onSuccess: () => {
      setOpen(false);
      setReason("");
      onDismissed();
      toast.success(t("heldPayments.dismissed"));
    },
    // The server's refusal says what is wrong with this row (not succeeded, already on an order),
    // and replacing it with "something went wrong" would hide the one sentence that explains it.
    onError: (e: unknown) =>
      toast.error(e instanceof Error ? e.message : String(e)),
  });

  if (!open) {
    return (
      <Button size="sm" variant="ghost" onClick={() => setOpen(true)}>
        {t("heldPayments.dismiss")}
      </Button>
    );
  }

  return (
    <div className="space-y-2 min-w-56">
      <p className="text-xs text-muted-foreground">
        {t("heldPayments.dismissHint")}
      </p>
      <Input
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        placeholder={t("heldPayments.dismissPlaceholder")}
        maxLength={200}
      />
      <div className="flex items-center gap-2">
        <Button
          size="sm"
          variant="destructive"
          disabled={dismiss.isPending || !reason.trim()}
          onClick={() => dismiss.mutate({ reason })}
        >
          {t("heldPayments.dismissConfirm")}
        </Button>
        <Button
          size="sm"
          variant="ghost"
          onClick={() => {
            setOpen(false);
            setReason("");
          }}
        >
          {t("common.cancel")}
        </Button>
      </div>
    </div>
  );
}
