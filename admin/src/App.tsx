import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";
import AdminScreen from "./pages/admin";
import { API_BASE_URL } from "./config/api";
import { getStoredAccessToken } from "./lib/admin-auth";
import { useTheme } from "./lib/theme-store";
import { themes } from "./lib/themes";

function getAuthHeaders(): Record<string, string> {
  const token = getStoredAccessToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

// Global fetch wrapper with .NET API base URL and auth header
const fetchWithApiBase = (url: string, options: RequestInit = {}) => {
  const fullUrl = url.startsWith('http') ? url : `${API_BASE_URL}${url}`;
  const headers = {
    ...(options.headers as Record<string, string>),
    ...getAuthHeaders(),
  };
  return fetch(fullUrl, {
    ...options,
    headers,
  });
};

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      queryFn: async ({ queryKey }) => {
        const res = await fetchWithApiBase(queryKey[0] as string);
        if (!res.ok) {
          throw new Error(`${res.status}: ${await res.text()}`);
        }
        return res.json();
      },
      refetchInterval: false,
      refetchOnWindowFocus: false,
      staleTime: 0,
      retry: false,
    },
  },
});

function App() {
  const theme = useTheme((s) => s.theme);
  const isDark = themes[theme].isDark;
  return (
    <QueryClientProvider client={queryClient}>
      <Toaster position="top-center" theme={isDark ? "dark" : "light"} />
      <AdminScreen />
    </QueryClientProvider>
  );
}

export default App;
