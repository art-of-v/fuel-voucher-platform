import { useQuery } from "@tanstack/react-query";
import { apiRequest } from "./api-client";
import { useSession } from "./session-store";
import type { AdminUserOption, FuelTypeRef } from "./api-types";

/**
 * The two lookups more than one screen needs.
 *
 * <p>
 * They live here rather than in a screen because three screens read them: the purchases table
 * (the buyer's name, the fuel's name), the report (the customer filter and the reconciliation
 * act's customer line), and the reconciliation's three-way match (the fuel's name). React Query
 * keys them by the path, so every screen that asks shares one request rather than one each.
 */

/** Every customer's name, and nothing else. */
export function useAdminUserOptions() {
  const user = useSession((s) => s.user);
  return useQuery<AdminUserOption[]>({
    queryKey: ["/api/admin/users/options"],
    enabled: !!user,
    queryFn: async () => {
      return await apiRequest<void, AdminUserOption[]>(
        "GET",
        "/api/admin/users/options",
      );
    },
  });
}

/** The fuel catalog: the id every screen carries, and the name it shows. */
export function useAdminFuelTypes() {
  const user = useSession((s) => s.user);
  return useQuery<FuelTypeRef[]>({
    queryKey: ["/api/admin/fuel-types"],
    enabled: !!user,
    queryFn: async () => {
      return await apiRequest<void, FuelTypeRef[]>(
        "GET",
        "/api/admin/fuel-types",
      );
    },
  });
}
