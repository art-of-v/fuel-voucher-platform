import { useQuery } from "@tanstack/react-query";

export function useAuth() {
  // Check Replit Auth
  const { data: replitUser, isLoading: replitLoading } = useQuery({
    queryKey: ["/api/auth/user"],
    retry: false,
  });

  // Check Phone Auth
  const { data: phoneUser, isLoading: phoneLoading } = useQuery({
    queryKey: ["/api/auth/phone/user"],
    retry: false,
  });

  const user = replitUser || phoneUser;
  const isLoading = replitLoading || phoneLoading;
  const authType = replitUser ? 'replit' : phoneUser ? 'phone' : null;

  return {
    user,
    isLoading,
    isAuthenticated: !!user,
    authType,
  };
}
