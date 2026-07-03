import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function getApiUrl(path: string): string {
  if (path.startsWith("http")) return path;
  const baseUrl = import.meta.env.VITE_API_URL || (import.meta.env.PROD ? "https://fuel-voucher-platform.onrender.com" : "");
  return `${baseUrl}${path}`;
}

