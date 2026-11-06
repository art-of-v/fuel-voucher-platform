import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Ban, ShieldCheck, Ticket, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/alert-dialog";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { ROLE_USER, canSetPromoAccess } from "@/lib/roles";
import { useSession } from "@/lib/session-store";
import type { AdminUser, PromoAccessResult } from "@/lib/api-types";

/**
 * The things an operator may do to one customer: activate, revoke the second factor, change the
 * email, decide whether the promotion serves them, ban, delete.
 *
 * <p>
 * It lives apart from the table because the table is a page of rows and this is a decision with a
 * consequence: every control here posts a mutation, writes an audit row and re-draws the grid. The
 * role dropdown deliberately stayed in the table — it belongs to the role column, not to the row's
 * tail, and it is the only control the backend gates on the hierarchy rather than on a capability.
 * </p>
 */
export function UserRowActions({ row }: { row: AdminUser }) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const actorRole = useSession((s) => s.user?.role) ?? "";

  const [confirm, setConfirm] = useState<{
    id: string;
    action: "ban" | "delete";
  } | null>(null);

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["/api/admin/users"] });

  const setUserActiveMutation = useMutation({
    mutationFn: async ({
      userId,
      isActive,
    }: {
      userId: string;
      isActive: boolean;
    }) => {
      const endpoint = isActive
        ? `/api/admin/users/${userId}/activate`
        : `/api/admin/users/${userId}/deactivate`;
      await apiRequest<void, unknown>("POST", endpoint);
    },
    onSuccess: (_data, variables) => {
      invalidate();
      toast.success(
        variables.isActive ? t("users.activated") : t("users.deactivated"),
      );
    },
    onError: (err: Error) => {
      toast.error(`${t("users.toggleFailed")}: ${err.message}`);
    },
  });

  // The second factor of a staff account. Revoking it is the recovery path: the
  // authenticator phone was lost, and without a way to take the factor away the account
  // would be locked out of the panel with no way back in.
  const revokeTwoFactorMutation = useMutation({
    mutationFn: async (userId: string) => {
      await apiRequest<void, unknown>(
        "POST",
        `/api/admin/users/${userId}/two-factor/revoke`,
      );
    },
    onSuccess: () => {
      invalidate();
      toast.success(t("users.twoFactorRevoked"));
    },
    onError: (err: Error) => {
      toast.error(`${t("users.twoFactorRevokeFailed")}: ${err.message}`);
    },
  });

  const setUserEmailMutation = useMutation({
    mutationFn: async ({
      userId,
      email,
    }: {
      userId: string;
      email: string | null;
    }) => {
      return await apiRequest<
        { email: string | null },
        { pendingConfirmation: boolean }
      >("POST", `/api/admin/users/${userId}/email`, { email });
    },
    onSuccess: (data) => {
      invalidate();
      toast.success(
        data?.pendingConfirmation
          ? t("users.emailChangeSent")
          : t("users.emailCleared"),
      );
    },
    onError: (err: Error) => {
      toast.error(`${t("users.emailChangeFailed")}: ${err.message}`);
    },
  });

  // Whether the promotion is allowed to serve this customer. The control is drawn for a customer
  // account and for a Manager and above, because that is who the ruling of 2026-11-06 put the
  // decision in the hands of; the endpoint refuses anybody else regardless of what is drawn here.
  const setPromoAccessMutation = useMutation({
    mutationFn: async ({
      userId,
      granted,
    }: {
      userId: string;
      granted: boolean;
    }) => {
      return await apiRequest<{ granted: boolean }, PromoAccessResult>(
        "POST",
        `/api/admin/users/${userId}/promo-access`,
        { granted },
      );
    },
    onSuccess: (data) => {
      invalidate();
      // The state the server settled on, not the tap: the endpoint is a switch and answers with
      // the state either way, including when the ask changed nothing.
      toast.success(
        data?.granted ? t("users.promoGranted") : t("users.promoRevoked"),
      );
    },
    onError: (err: Error) => {
      toast.error(`${t("users.promoFailed")}: ${err.message}`);
    },
  });

  const setUserBannedMutation = useMutation({
    mutationFn: async ({
      userId,
      isBanned,
    }: {
      userId: string;
      isBanned: boolean;
    }) => {
      const endpoint = isBanned
        ? `/api/admin/users/${userId}/ban`
        : `/api/admin/users/${userId}/unban`;
      await apiRequest<void, unknown>("POST", endpoint);
    },
    onSuccess: (_data, variables) => {
      invalidate();
      toast.success(
        variables.isBanned ? t("users.banned") : t("users.unbanned"),
      );
    },
    onError: (err: Error) => {
      toast.error(`${t("users.banFailed")}: ${err.message}`);
    },
  });

  const deleteUserMutation = useMutation({
    mutationFn: async (userId: string) => {
      await apiRequest<void, unknown>("DELETE", `/api/admin/users/${userId}`);
    },
    onSuccess: () => {
      invalidate();
      toast.success(t("users.deleteSuccess"));
    },
    onError: (err: Error) => {
      toast.error(`${t("users.deleteFailed")}: ${err.message}`);
    },
  });

  if (row.isDeleted) return null;

  const hasPromoAccess = row.promoAccessGrantedAt !== null;

  return (
    <>
      <Button
        variant="ghost"
        size="sm"
        onClick={() =>
          setUserActiveMutation.mutate({
            userId: row.id,
            isActive: !row.isActive,
          })
        }
        disabled={setUserActiveMutation.isPending}
        className={
          row.isActive
            ? "text-warning hover:bg-warning/10"
            : "text-success hover:bg-success/10"
        }
      >
        <span className="ml-1 text-xs">
          {row.isActive ? t("users.deactivate") : t("users.activate")}
        </span>
      </Button>
      {row.hasTwoFactor && (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => revokeTwoFactorMutation.mutate(row.id)}
          disabled={revokeTwoFactorMutation.isPending}
          title={t("users.revokeTwoFactorTitle")}
          className="text-warning hover:bg-warning/10"
        >
          <span className="ml-1 text-xs">{t("users.revokeTwoFactor")}</span>
        </Button>
      )}
      <Button
        variant="ghost"
        size="sm"
        onClick={() => {
          const next = window.prompt(t("users.emailPrompt"), row.email || "");
          if (next !== null) {
            const trimmed = next.trim();
            setUserEmailMutation.mutate({
              userId: row.id,
              email: trimmed === "" ? null : trimmed,
            });
          }
        }}
        disabled={setUserEmailMutation.isPending}
        className="text-info hover:bg-info/10"
      >
        <span className="ml-1 text-xs">{t("users.editEmail")}</span>
      </Button>
      {row.role === ROLE_USER && canSetPromoAccess(actorRole) && (
        <Button
          variant="ghost"
          size="sm"
          onClick={() =>
            setPromoAccessMutation.mutate({
              userId: row.id,
              granted: !hasPromoAccess,
            })
          }
          disabled={setPromoAccessMutation.isPending}
          title={
            hasPromoAccess
              ? t("users.revokePromoTitle")
              : t("users.grantPromoTitle")
          }
          className={
            hasPromoAccess
              ? "text-warning hover:bg-warning/10"
              : "text-success hover:bg-success/10"
          }
        >
          <Ticket className="w-4 h-4" />
          <span className="ml-1 text-xs">
            {hasPromoAccess ? t("users.revokePromo") : t("users.grantPromo")}
          </span>
        </Button>
      )}
      <Button
        variant="ghost"
        size="sm"
        onClick={() => {
          if (row.isBanned) {
            setUserBannedMutation.mutate({ userId: row.id, isBanned: false });
          } else {
            setConfirm({ id: row.id, action: "ban" });
          }
        }}
        disabled={setUserBannedMutation.isPending}
        className={
          row.isBanned
            ? "text-success hover:bg-success/10"
            : "text-destructive hover:bg-destructive/10"
        }
      >
        {row.isBanned ? (
          <>
            <ShieldCheck className="w-4 h-4" />
            <span className="ml-1 text-xs">{t("users.unban")}</span>
          </>
        ) : (
          <>
            <Ban className="w-4 h-4" />
            <span className="ml-1 text-xs">{t("users.ban")}</span>
          </>
        )}
      </Button>
      <Button
        variant="ghost"
        size="sm"
        onClick={() => setConfirm({ id: row.id, action: "delete" })}
        disabled={deleteUserMutation.isPending}
        className="text-destructive hover:bg-destructive/10"
      >
        <Trash2 className="w-4 h-4" />
        <span className="ml-1 text-xs">{t("users.delete")}</span>
      </Button>

      <ConfirmDialog
        open={confirm !== null}
        onOpenChange={(open) => {
          if (!open) setConfirm(null);
        }}
        destructive
        title={confirm?.action === "ban" ? t("users.ban") : t("users.delete")}
        description={
          confirm?.action === "ban"
            ? t("users.banConfirm")
            : t("users.deleteConfirm")
        }
        confirmLabel={
          confirm?.action === "ban" ? t("users.ban") : t("users.delete")
        }
        cancelLabel={t("common.cancel")}
        onConfirm={() => {
          if (!confirm) return;
          if (confirm.action === "ban") {
            setUserBannedMutation.mutate({
              userId: confirm.id,
              isBanned: true,
            });
          } else {
            deleteUserMutation.mutate(confirm.id);
          }
        }}
      />
    </>
  );
}
