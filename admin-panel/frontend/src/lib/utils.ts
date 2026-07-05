import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function getApiUrl(path: string): string {
  if (path.startsWith("http")) return path;
  let baseUrl = import.meta.env.VITE_API_URL || "";
  if (import.meta.env.PROD && (!baseUrl || baseUrl.includes("fuel-flow-admin-panel-bac"))) {
    baseUrl = "https://fuel-voucher-platform.onrender.com";
  }
  return `${baseUrl}${path}`;
}

