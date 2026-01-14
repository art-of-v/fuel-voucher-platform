import { useQuery } from "@tanstack/react-query";

// Development mode: Auto-authenticate with mock user
const DEV_MODE = import.meta.env.DEV;
const MOCK_USER = {
  id: 'dev-user-123',
  name: 'Dev User',
  email: 'dev@example.com',
  phone: '+380501234567'
};

export function useAuth() {
  // Development mode disabled - require real authentication
  // if (DEV_MODE) {
  //   return {
  //     user: MOCK_USER,
  //     isLoading: false,
  //     isAuthenticated: true,
  //     authType: 'dev' as const,
  //   };
  // }

  // Check real auth
  const { data: replitUser, isLoading: replitLoading } = useQuery({
    queryKey: ["/api/auth/user"],
    retry: false,
  });

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
