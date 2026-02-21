import { useQuery } from "@tanstack/react-query";
import { BASE_URL } from "../lib/api";

export interface User {
  id: string;
  phone?: string;
  firstName?: string;
  lastName?: string;
  email?: string;
  birthdate?: string;
  vehicleMake?: string;
  vehicleModel?: string;
  vehiclePlate?: string;
  vehicleFuelType?: string;
  profileImageUrl?: string;
}

export function useAuth() {
  // Check phone auth only (email/Replit auth removed)
  const { data: phoneUser, isLoading: phoneLoading } = useQuery<User>({
    queryKey: ["/api/auth/phone/user"],
    queryFn: async () => {
      const response = await fetch(`${BASE_URL}/api/auth/phone/user`, {
        credentials: "include",
      });
      if (!response.ok) return null;
      return response.json();
    },
    retry: false,
  });

  return {
    user: phoneUser,
    isLoading: phoneLoading,
    isAuthenticated: !!phoneUser,
    authType: phoneUser ? 'phone' as const : null,
  };
}
