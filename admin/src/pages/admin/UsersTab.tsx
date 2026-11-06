import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { assignableRoles } from "@/lib/roles";
import { useSession } from "@/lib/session-store";
import { formatDate } from "@/lib/utils";
import { UserRowActions } from "./UserRowActions";
import type { AdminUser, PagedResult } from "@/lib/api-types";

/** The customers table shows fifty rows at a time; the endpoint is paged to match. */
const USERS_PAGE_SIZE = 50;

/**
 * The customers table: a page of customers and the role dropdown of each one.
 *
 * <p>
 * The table is a PAGE, not the whole table. The endpoint used to answer with every customer the
 * platform had ever registered, and the table grew with every registration: the screen got slower
 * as the business did, and the server held every customer in memory to draw a screen that shows
 * fifty rows.
 * </p>
 *
 * <p>
 * What an operator may DO to a row is not here — it lives in `UserRowActions`, which owns those
 * mutations. This file owns the page: the query, the columns and the role control, which belongs to
 * a column rather than to the tail of the row.
 * </p>
 */
export default function UsersTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const user = useSession((s) => s.user);

  const [usersPage, setUsersPage] = useState(1);

  const { data: usersPageData } = useQuery<PagedResult<AdminUser>>({
    queryKey: ["/api/admin/users", usersPage],
    enabled: !!user,
    queryFn: async () => {
      return await apiRequest<void, PagedResult<AdminUser>>(
        "GET",
        `/api/admin/users?page=${usersPage}&limit=${USERS_PAGE_SIZE}`,
      );
    },
  });

  const usersList = usersPageData?.items ?? [];
  const usersTotalPages = usersPageData?.totalPages ?? 0;

  const setUserRoleMutation = useMutation({
    mutationFn: async ({ userId, role }: { userId: string; role: string }) => {
      await apiRequest<{ role: string }, unknown>(
        "POST",
        `/api/admin/users/${userId}/role`,
        { role },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/users"] });
      toast.success(t("users.roleChanged"));
    },
    onError: (err: Error) => {
      toast.error(`${t("users.roleChangeFailed")}: ${err.message}`);
    },
  });

  // Current signed-in admin's role/id. Captured here because the users table below shadows
  // `user` with its row variable, so the actor is not reachable inside the map. The id lets us
  // hide the role control on the actor's own row (the backend also rejects self-role-change).
  const actorRole = user?.role ?? "";
  const currentUserId = user?.id ?? "";

  return (
    <div className="space-y-6 animate-in fade-in duration-300">
      <div className="glass-panel overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-muted">
            <tr>
              <th className="text-left p-4">{t("table.id")}</th>
              <th className="text-left p-4">{t("table.name")}</th>
              <th className="text-left p-4">{t("table.phone")}</th>
              <th className="text-left p-4">{t("table.settlementCode")}</th>
              <th className="text-left p-4">{t("table.email")}</th>
              <th className="text-left p-4">{t("table.birthdate")}</th>
              <th className="text-left p-4">{t("table.bonusBalance")}</th>
              <th className="text-left p-4">{t("table.referredBy")}</th>
              <th className="text-left p-4">{t("table.role")}</th>
              <th className="text-left p-4">{t("table.status")}</th>
              <th className="text-left p-4">{t("users.createdAt")}</th>
              <th className="text-right p-4"></th>
            </tr>
          </thead>
          <tbody>
            {usersList.map((row) => (
              <tr
                key={row.id}
                className="border-border hover:bg-foreground/5 transition-colors"
              >
                <td className="p-4 font-mono text-xs text-muted-foreground">
                  {row.id}
                </td>
                <td className="p-4 font-bold text-foreground">
                  {row.firstName || row.lastName ? (
                    `${row.firstName || ""} ${row.lastName || ""}`.trim()
                  ) : (
                    <span className="text-muted-foreground italic">
                      No Name
                    </span>
                  )}
                </td>
                <td className="p-4">
                  <span className="inline-flex items-center gap-1.5">
                    {row.phone || (
                      <span className="text-muted-foreground italic">N/A</span>
                    )}
                    {row.isQaAccount && (
                      <span
                        title={t("users.qaBadgeTitle")}
                        className="inline-flex items-center px-1.5 py-0.5 rounded bg-primary/10 text-primary text-xs font-semibold"
                      >
                        {t("users.qaBadge")}
                      </span>
                    )}
                    {row.promoAccessGrantedAt && (
                      <span
                        title={`${t("users.promoBadgeTitle")} (${formatDate(row.promoAccessGrantedAt)})`}
                        className="inline-flex items-center px-1.5 py-0.5 rounded bg-success/10 text-success text-xs font-semibold"
                      >
                        {t("users.promoBadge")}
                      </span>
                    )}
                  </span>
                </td>
                <td className="p-4">
                  <span
                    className="inline-flex items-center px-1.5 py-0.5 rounded bg-muted font-mono text-xs font-semibold tracking-wider text-foreground"
                    title={t("users.settlementCodeTitle")}
                  >
                    {row.settlementCode}
                  </span>
                </td>
                <td className="p-4">
                  {row.email || (
                    <span className="text-muted-foreground italic">N/A</span>
                  )}
                </td>
                <td className="p-4">
                  {row.birthdate ? (
                    formatDate(row.birthdate)
                  ) : (
                    <span className="text-muted-foreground italic">N/A</span>
                  )}
                </td>
                <td className="p-4 text-primary font-bold">
                  {row.bonusBalance || 0} UAH
                </td>
                <td className="p-4 font-mono text-xs text-muted-foreground">
                  {row.referredBy || "-"}
                </td>
                <td className="p-4 font-mono text-xs text-muted-foreground">
                  {(() => {
                    // The account's REAL role - null when it has none. The SPA used to read
                    // that as 'User', which hid the state from the operator AND made the
                    // repair impossible: the dropdown showed 'User' as the current value,
                    // so picking 'User' was a no-op and the account could not be given the
                    // one role it most likely needs (planning #299).
                    const currentRole = row.role;
                    const options =
                      row.isDeleted || row.id === currentUserId
                        ? []
                        : assignableRoles(actorRole, currentRole);
                    if (options.length === 0) {
                      return (
                        <span className="capitalize">
                          {currentRole ?? t("users.noRole")}
                        </span>
                      );
                    }
                    const choices =
                      currentRole && !options.includes(currentRole)
                        ? [currentRole, ...options]
                        : options;
                    return (
                      <select
                        value={currentRole ?? ""}
                        disabled={setUserRoleMutation.isPending}
                        onChange={(e) => {
                          const role = e.target.value;
                          if (role !== (currentRole ?? "")) {
                            setUserRoleMutation.mutate({
                              userId: row.id,
                              role,
                            });
                          }
                        }}
                        className="bg-transparent border border-border rounded px-2 py-1 text-xs text-foreground"
                      >
                        {currentRole === null && (
                          <option value="">{t("users.noRole")}</option>
                        )}
                        {choices.map((r) => (
                          <option key={r} value={r}>
                            {r}
                          </option>
                        ))}
                      </select>
                    );
                  })()}
                </td>
                <td className="p-4">
                  {row.isDeleted ? (
                    <span className="inline-flex items-center px-2 py-1 rounded bg-destructive/10 text-destructive text-xs font-semibold">
                      {t("users.deleted")}
                    </span>
                  ) : row.isBanned ? (
                    <span className="inline-flex items-center px-2 py-1 rounded bg-destructive/10 text-destructive text-xs font-semibold">
                      {t("users.banned_status")}
                    </span>
                  ) : row.isActive ? (
                    <span className="inline-flex items-center px-2 py-1 rounded bg-success/10 text-success text-xs font-semibold">
                      {t("users.active")}
                    </span>
                  ) : (
                    <span className="inline-flex items-center px-2 py-1 rounded bg-warning/10 text-warning text-xs font-semibold">
                      {t("users.inactive")}
                    </span>
                  )}
                </td>
                <td className="p-4 text-muted-foreground">
                  {formatDate(row.createdAt)}
                </td>
                <td className="p-4 text-right">
                  <UserRowActions row={row} />
                </td>
              </tr>
            ))}
            {usersList.length === 0 && (
              <tr>
                <td
                  colSpan={12}
                  className="p-8 text-center text-muted-foreground"
                >
                  No users found
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {usersTotalPages > 1 && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>
            {t("users.total")}: {usersPageData?.totalCount ?? 0}
          </span>
          <div className="flex items-center gap-2">
            <Button
              variant="ghost"
              size="sm"
              disabled={usersPage <= 1}
              onClick={() => setUsersPage((p) => Math.max(1, p - 1))}
              className="h-8"
            >
              <ChevronLeft className="w-4 h-4" />
            </Button>
            <span className="text-xs">
              {usersPage} / {usersTotalPages}
            </span>
            <Button
              variant="ghost"
              size="sm"
              disabled={usersPage >= usersTotalPages}
              onClick={() => setUsersPage((p) => p + 1)}
              className="h-8"
            >
              <ChevronRight className="w-4 h-4" />
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
