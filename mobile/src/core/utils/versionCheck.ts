import { Platform } from 'react-native';
import Constants from 'expo-constants';
import { BASE_URL } from '../api/apiClient';

export interface AppVersionInfo {
  minimumVersion: string;
  iosStoreUrl: string;
  androidStoreUrl: string;
}

export function parseVersion(version: string): number[] {
  return version.split('.').map(Number);
}

export function isVersionBelow(current: string, minimum: string): boolean {
  const currentParts = parseVersion(current);
  const minimumParts = parseVersion(minimum);
  const maxLen = Math.max(currentParts.length, minimumParts.length);
  for (let i = 0; i < maxLen; i++) {
    const a = currentParts[i] ?? 0;
    const b = minimumParts[i] ?? 0;
    if (a !== b) return a < b;
  }
  return false;
}

export async function fetchAppVersion(): Promise<AppVersionInfo | null> {
  try {
    const response = await fetch(`${BASE_URL}/api/app-version`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (!response.ok) return null;
    return await response.json();
  } catch {
    return null;
  }
}

export function getStoreUrl(info: AppVersionInfo): string {
  if (Platform.OS === 'ios') return info.iosStoreUrl;
  if (Platform.OS === 'android') return info.androidStoreUrl;
  return '';
}

export function getCurrentAppVersion(): string {
  return Constants.expoConfig?.version ?? '1.0.0';
}
