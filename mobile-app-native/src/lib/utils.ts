import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

import { Platform } from 'react-native';

// const ENV_API_URL = process.env.EXPO_PUBLIC_API_URL;
const ENV_API_URL = "http://192.168.0.103:4000";
const LOCALHOST = Platform.OS === 'android' ? '10.0.2.2' : 'localhost';
const BASE_URL = ENV_API_URL || `http://${LOCALHOST}:4000`;

export async function apiRequest(
  method: string,
  url: string,
  data?: unknown
): Promise<Response> {
  const fullUrl = url.startsWith('http') ? url : `${BASE_URL}${url}`;
  const res = await fetch(fullUrl, {
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
    // Diesel variants
    'дп євро': 'diesel',
    'дп': 'diesel',
    'diesel': 'diesel',
    'dp': 'diesel',
    'дп euro': 'diesel',

    // A-95 variants
    'a-95': 'a-95',
    'а-95': 'a-95', // Cyrillic A
    '95': 'a-95',
    'a-95 євро': 'a-95',
    'а-95 євро': 'a-95',

    // Branded
    'mustang 95': 'a-95 mustang',
    'a-95 mustang': 'a-95 mustang',
    'mustang diesel': 'diesel mustang',
    'diesel mustang': 'diesel mustang',
    'dp mustang': 'diesel mustang',
    'pulls 95': 'a-95 pulls',
    'a-95 pulls': 'a-95 pulls',
    'pills 95': 'a-95 pulls',

    // UPG/Others
    'upg-100': 'upg-100',
    '100': 'upg-100',
    'gas': 'gas',
    'lpg': 'gas',
    'газ': 'gas'
  };

  return map[n] || n;
}
