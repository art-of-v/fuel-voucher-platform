import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"
import Constants from 'expo-constants';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

export function getApiUrl(path: string) {
  if (path.startsWith("http")) return path;

  const baseUrl = process.env.EXPO_PUBLIC_API_URL || Constants.expoConfig?.extra?.apiUrl;
  if (!baseUrl) {
    // Should not happen in prod with .env correctly loaded
    console.warn("API_URL missing, fallback to relative path");
    return path;
  }

  // Ensure we don't end up with double slashes if path starts with /
  const cleanPath = path.startsWith("/") ? path : `/${path}`;
  // Remove trailing slash from base if present
  const cleanBase = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
  return `${cleanBase}${cleanPath}`;
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
    'a 95 euro': 'a-95 euro',
    'а 95 євро': 'a-95 euro',
    'upg-100': 'upg-100',
    '100': 'upg-100',
    'gas': 'gas',
    'lpg': 'gas',
    'газ': 'gas'
  };

  return map[n] || n;
}
