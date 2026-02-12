import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"
import Constants from 'expo-constants';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

const DEFAULT_API_URL = "http://localhost:4000"; // Fallback for local dev

export function getApiUrl(path: string) {
  if (path.startsWith("http")) return path;

  const baseUrl = process.env.EXPO_PUBLIC_API_URL || Constants.expoConfig?.extra?.apiUrl || DEFAULT_API_URL;

  // Ensure we don't end up with double slashes if path starts with /
  const cleanPath = path.startsWith("/") ? path : `/${path}`;
  // Remove trailing slash from base if present
  const cleanBase = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
  return `${cleanBase}${cleanPath}`;
}

export async function apiRequest(
  method: string,
  url: string,
  data?: unknown
): Promise<Response> {
  const res = await fetch(getApiUrl(url), {
    method,
    headers: data ? { "Content-Type": "application/json" } : undefined,
    body: data ? JSON.stringify(data) : undefined,
  });
  if (!res.ok) {
    throw new Error(`API Error: ${res.statusText}`);
  }
  return res;
}

export function normalizeFuelName(name: string): string {
  const n = name.toLowerCase().trim();
  const map: Record<string, string> = {
    'дп євро': 'diesel',
    'дп': 'diesel',
    'diesel': 'diesel',
    'dp': 'diesel',
    'дп euro': 'diesel',
    'a-95': 'a-95',
    'а-95': 'a-95',
    '95': 'a-95',
    'a-95 євро': 'a-95',
    'а-95 євро': 'a-95',
    'mustang 95': 'a-95 mustang',
    'a-95 mustang': 'a-95 mustang',
    'mustang diesel': 'diesel mustang',
    'diesel mustang': 'diesel mustang',
    'dp mustang': 'diesel mustang',
    'pulls 95': 'a-95 pulls',
    'a-95 pulls': 'a-95 pulls',
    'pills 95': 'a-95 pulls',
    'upg-100': 'upg-100',
    '100': 'upg-100',
    'gas': 'gas',
    'lpg': 'gas',
    'газ': 'gas'
  };

  return map[n] || n;
}
