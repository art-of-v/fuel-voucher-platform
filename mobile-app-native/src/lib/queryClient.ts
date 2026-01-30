import { QueryClient, QueryFunction } from "@tanstack/react-query";
import { Platform } from 'react-native';

// const ENV_API_URL = process.env.EXPO_PUBLIC_API_URL;
const ENV_API_URL = "http://192.168.0.103:4000";
const LOCALHOST = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
const BASE_URL = Platform.OS === 'web'
  ? '' // Absolute on web causes CORS issues with Nginx proxy, use relative
  : (ENV_API_URL || `http://${LOCALHOST}:4000`);

async function throwIfResNotOk(res: Response) {
  if (!res.ok) {
    const text = (await res.text()) || res.statusText;
    throw new Error(`${res.status}: ${text}`);
  }
}

export async function apiRequest(
  method: string,
  url: string,
  data?: unknown | undefined,
): Promise<Response> {
  const fullUrl = url.startsWith('http') ? url : `${BASE_URL}${url}`;
  const res = await fetch(fullUrl, {
    method,
    headers: data ? { "Content-Type": "application/json" } : {},
    body: data ? JSON.stringify(data) : undefined,
    credentials: "include",
  });

  await throwIfResNotOk(res);
  return res;
}

type UnauthorizedBehavior = "returnNull" | "throw";
export const getQueryFn: <T>(options: {
  on401: UnauthorizedBehavior;
}) => QueryFunction<T> =
  ({ on401: unauthorizedBehavior }) =>
    async ({ queryKey }) => {
      // queryKey[0] is often the URL path
      const path = queryKey.join("/");
      const fullUrl = path.startsWith('http') ? path : `${BASE_URL}${path}`;

      const res = await fetch(fullUrl, {
        credentials: "include",
      });

      if (unauthorizedBehavior === "returnNull" && res.status === 401) {
        return null;
      }

      await throwIfResNotOk(res);
      return await res.json();
    };

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      queryFn: getQueryFn({ on401: "throw" }),
      refetchInterval: false,
      refetchOnWindowFocus: false,
      staleTime: Infinity,
      retry: false,
    },
    mutations: {
      retry: false,
    },
  },
});
