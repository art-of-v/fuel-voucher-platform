import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Check, Loader2, Upload, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { useSession } from "@/lib/session-store";
import { formatDateTime } from "@/lib/utils";
import type {
  DismissRedemptionStatementRowResponse,
  ImportRedemptionStatementResponse,
  RedemptionDecisionResponse,
  RedemptionExceptionsResponse,
} from "@/lib/api-types";
import { toast } from "sonner";
import { redemptionRefusalMessage } from "@/lib/redemption-refusal-error";

type DecisionAction = "Confirm" | "Reject";

const SAMPLE_STATEMENT = `[
  {
    "externalId": "pump-2026-10-23-0001",
    "voucherNumber": "OKKO-0001",
    "liters": 50,
    "redeemedAtUtc": "2026-10-23T09:15:00Z",
    "note": "pump 7"
  }
]`;

/**
 * The redemption exception queue.
 *
 * <p>
 * A customer's "I used this voucher" is a claim, not a sale. The sale is posted only when the
 * supplier's evidence agrees, so this panel is where an operator confirms or rejects the claim and
 * dismisses statement lines that need no voucher action.
 */
export function RedemptionReconciliationPanel() {
  const { t } = useI18n();
  const user = useSession((s) => s.user);
  const queryClient = useQueryClient();
  const [statement, setStatement] = useState("");
  const [notes, setNotes] = useState<Record<string, string>>({});

  const queue = useQuery<RedemptionExceptionsResponse>({
    queryKey: ["/api/admin/redemptions/exceptions"],
    queryFn: () =>
      apiRequest<void, RedemptionExceptionsResponse>(
        "GET",
        "/api/admin/redemptions/exceptions",
      ),
    enabled: !!user,
    staleTime: 15_000,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({
      queryKey: ["/api/admin/redemptions/exceptions"],
    });
    void queryClient.invalidateQueries({
      queryKey: ["/api/admin/reconciliation"],
    });
  };

  const importStatement = useMutation({
    mutationFn: async () => {
      let lines: unknown;
      try {
        lines = JSON.parse(statement);
      } catch {
        throw new Error(t("redemption.invalidJson"));
      }

      if (!Array.isArray(lines) || lines.length === 0) {
        throw new Error(t("redemption.invalidJson"));
      }

      return await apiRequest<unknown, ImportRedemptionStatementResponse>(
        "POST",
        "/api/admin/redemptions/statement",
        { lines },
      );
    },
    onSuccess: (result) => {
      toast.success(
        t("redemption.importResult", result.imported, result.duplicates),
      );
      setStatement("");
      invalidate();
    },
    onError: (error: Error) => toast.error(redemptionRefusalMessage(error, t)),
  });

  const decide = useMutation({
    mutationFn: async (variables: {
      voucherId: string;
      action: DecisionAction;
      note: string;
    }) =>
      await apiRequest<unknown, RedemptionDecisionResponse>(
        "POST",
        `/api/admin/redemptions/${variables.voucherId}/decision`,
        { action: variables.action, note: variables.note || null },
      ),
    onSuccess: (_result, variables) => {
      toast.success(
        variables.action === "Confirm"
          ? t("redemption.confirmed")
          : t("redemption.rejected"),
      );
      setNotes((current) => {
        const next = { ...current };
        delete next[variables.voucherId];
        return next;
      });
      invalidate();
    },
    onError: (error: Error) => toast.error(redemptionRefusalMessage(error, t)),
  });

  const dismiss = useMutation({
    mutationFn: async (variables: { rowId: string; note: string }) =>
      await apiRequest<unknown, DismissRedemptionStatementRowResponse>(
        "POST",
        `/api/admin/redemptions/statement/${variables.rowId}/dismiss`,
        { note: variables.note || null },
      ),
    onSuccess: (_result, variables) => {
      toast.success(t("redemption.dismissed"));
      setNotes((current) => {
        const next = { ...current };
        delete next[variables.rowId];
        return next;
      });
      invalidate();
    },
    onError: (error: Error) => toast.error(redemptionRefusalMessage(error, t)),
  });

  const statusLabel = (status: string) => {
    const key = `redemption.status${status}`;
    const translated = t(key);
    return translated === key ? status : translated;
  };

  const busy = decide.isPending || dismiss.isPending;

  return (
    <section className="glass-panel p-5 space-y-5">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h3 className="text-lg font-bold">{t("redemption.title")}</h3>
          <p className="text-sm text-muted-foreground mt-1">
            {t("redemption.hint")}
          </p>
        </div>
        <div className="grid grid-cols-3 gap-2 text-center">
          <div className="rounded-lg bg-warning/10 border border-warning/30 px-3 py-2">
            <p className="text-xl font-bold">
              {queue.data?.pendingClaims ?? 0}
            </p>
            <p className="text-xs text-muted-foreground">
              {t("redemption.pendingClaims")}
            </p>
          </div>
          <div className="rounded-lg bg-destructive/10 border border-destructive/30 px-3 py-2">
            <p className="text-xl font-bold">
              {queue.data?.mismatchedClaims ?? 0}
            </p>
            <p className="text-xs text-muted-foreground">
              {t("redemption.mismatchedClaims")}
            </p>
          </div>
          <div className="rounded-lg bg-info/10 border border-info/30 px-3 py-2">
            <p className="text-xl font-bold">
              {queue.data?.statementExceptions ?? 0}
            </p>
            <p className="text-xs text-muted-foreground">
              {t("redemption.statementExceptions")}
            </p>
          </div>
        </div>
      </div>

      <div className="rounded-xl border border-border p-4 space-y-3">
        <div>
          <h4 className="font-semibold">{t("redemption.importTitle")}</h4>
          <p className="text-xs text-muted-foreground mt-1">
            {t("redemption.importHint")}
          </p>
        </div>
        <textarea
          value={statement}
          onChange={(event) => setStatement(event.target.value)}
          placeholder={SAMPLE_STATEMENT}
          spellCheck={false}
          className="glass-input w-full min-h-36 rounded-lg p-3 font-mono text-xs text-foreground"
        />
        <Button
          size="sm"
          onClick={() => importStatement.mutate()}
          disabled={importStatement.isPending || !statement.trim()}
        >
          {importStatement.isPending ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <Upload className="w-4 h-4" />
          )}
          {t("redemption.importButton")}
        </Button>
      </div>

      {queue.isLoading ? (
        <div className="flex items-center gap-2 text-muted-foreground">
          <Loader2 className="w-4 h-4 animate-spin" />
          {t("common.loading")}
        </div>
      ) : queue.isError ? (
        <p className="text-sm text-destructive">{t("redemption.loadFailed")}</p>
      ) : (
        <>
          {queue.data?.pending.length ? (
            <div className="space-y-3">
              {queue.data.pending.map((claim) => (
                <div
                  key={claim.redemptionId}
                  className={`rounded-xl border p-4 space-y-3 ${claim.hasMismatch ? "border-destructive/50 bg-destructive/5" : "border-warning/40 bg-warning/5"}`}
                >
                  <div className="flex items-start justify-between gap-3 flex-wrap">
                    <div>
                      <p className="font-semibold flex items-center gap-2">
                        {claim.hasMismatch ? (
                          <AlertTriangle className="w-4 h-4 text-destructive" />
                        ) : (
                          <AlertTriangle className="w-4 h-4 text-warning" />
                        )}
                        {claim.voucherNumber}
                      </p>
                      <p className="text-xs text-muted-foreground mt-1 font-mono">
                        {claim.redemptionId.slice(0, 8)}
                      </p>
                    </div>
                    <span
                      className={`text-xs px-2 py-1 rounded font-medium ${claim.severity === "critical" ? "bg-destructive/20 text-destructive" : "bg-warning/20 text-warning"}`}
                    >
                      {claim.severity === "critical"
                        ? t("reconciliation.critical")
                        : t("reconciliation.warning")}
                    </span>
                  </div>

                  <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-sm">
                    <div>
                      <p className="text-xs text-muted-foreground">
                        {t("redemption.liters")}
                      </p>
                      <p className="font-mono">{claim.reportedLiters} L</p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">
                        {t("redemption.redeemedAt")}
                      </p>
                      <p>{formatDateTime(claim.redeemedAtUtc)}</p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">
                        {t("redemption.reportedAt")}
                      </p>
                      <p>{formatDateTime(claim.reportedAtUtc)}</p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">
                        {t("redemption.reportedBy")}
                      </p>
                      <p className="font-mono text-xs">
                        {claim.reportedByUserId
                          ? claim.reportedByUserId.slice(0, 8)
                          : t("redemption.statementOnly")}
                      </p>
                    </div>
                  </div>

                  {claim.statementLines.length > 0 && (
                    <div className="rounded-lg border border-border overflow-hidden">
                      <table className="w-full text-xs">
                        <thead className="bg-muted">
                          <tr>
                            <th className="text-left p-2">
                              {t("redemption.statementLine")}
                            </th>
                            <th className="text-right p-2">
                              {t("redemption.liters")}
                            </th>
                            <th className="text-left p-2">
                              {t("redemption.status")}
                            </th>
                          </tr>
                        </thead>
                        <tbody>
                          {claim.statementLines.map((line) => (
                            <tr
                              key={line.rowId}
                              className="border-t border-border"
                            >
                              <td className="p-2 font-mono">
                                {line.externalId}
                              </td>
                              <td className="p-2 text-right font-mono">
                                {line.liters} L
                              </td>
                              <td className="p-2">
                                {statusLabel(line.status)}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}

                  <div className="flex flex-wrap items-center gap-2">
                    <input
                      value={notes[claim.redemptionId] ?? ""}
                      onChange={(event) =>
                        setNotes((current) => ({
                          ...current,
                          [claim.redemptionId]: event.target.value,
                        }))
                      }
                      placeholder={t("redemption.note")}
                      className="glass-input rounded-lg px-3 py-2 text-sm text-foreground flex-1 min-w-48"
                    />
                    <Button
                      size="sm"
                      disabled={busy}
                      onClick={() =>
                        decide.mutate({
                          voucherId: claim.voucherId,
                          action: "Confirm",
                          note: notes[claim.redemptionId] ?? "",
                        })
                      }
                    >
                      <Check className="w-4 h-4" />
                      {t("redemption.confirm")}
                    </Button>
                    <Button
                      size="sm"
                      variant="destructive"
                      disabled={busy}
                      onClick={() =>
                        decide.mutate({
                          voucherId: claim.voucherId,
                          action: "Reject",
                          note: notes[claim.redemptionId] ?? "",
                        })
                      }
                    >
                      <X className="w-4 h-4" />
                      {t("redemption.reject")}
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              {t("redemption.noPending")}
            </p>
          )}

          {queue.data?.statements.length ? (
            <div className="space-y-2">
              {queue.data.statements.map((row) => (
                <div
                  key={row.rowId}
                  className={`rounded-xl border p-3 space-y-2 ${row.severity === "critical" ? "border-destructive/40 bg-destructive/5" : "border-warning/40 bg-warning/5"}`}
                >
                  <div className="flex items-start justify-between gap-3 flex-wrap">
                    <div>
                      <p className="font-semibold font-mono">
                        {row.externalId}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {row.voucherNumber} · {row.liters} L ·{" "}
                        {formatDateTime(row.importedAtUtc)}
                      </p>
                      {row.note && (
                        <p className="text-xs text-muted-foreground mt-1">
                          {row.note}
                        </p>
                      )}
                    </div>
                    <span
                      className={`text-xs px-2 py-1 rounded font-medium ${row.severity === "critical" ? "bg-destructive/20 text-destructive" : "bg-warning/20 text-warning"}`}
                    >
                      {statusLabel(row.status)}
                    </span>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <input
                      value={notes[row.rowId] ?? ""}
                      onChange={(event) =>
                        setNotes((current) => ({
                          ...current,
                          [row.rowId]: event.target.value,
                        }))
                      }
                      placeholder={t("redemption.note")}
                      className="glass-input rounded-lg px-3 py-2 text-sm text-foreground flex-1 min-w-48"
                    />
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busy}
                      onClick={() =>
                        dismiss.mutate({
                          rowId: row.rowId,
                          note: notes[row.rowId] ?? "",
                        })
                      }
                    >
                      <X className="w-4 h-4" />
                      {t("redemption.dismiss")}
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              {t("redemption.noExceptions")}
            </p>
          )}
        </>
      )}
    </section>
  );
}
