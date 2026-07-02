import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";
import AdminScreen from "./pages/admin";
import { API_BASE_URL } from "./config/api";

// Global fetch wrapper with .NET API base URL
const fetchWithApiBase = (url: string, options: RequestInit = {}) => {
  const fullUrl = url.startsWith('http') ? url : `${API_BASE_URL}${url}`;
  return fetch(fullUrl, {
    ...options,
    credentials: "include",
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
      staleTime: Infinity,
      retry: false,
    },
  },
});

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <Toaster position="top-center" theme="dark" />
      <AdminScreen />
    </QueryClientProvider>
  );
}

export default App;
