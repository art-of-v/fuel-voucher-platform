import { useQuery } from "@tanstack/react-query";

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
      // Return null for now to simulate unauthenticated state or fetch from local/mock
      return null as any;
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
