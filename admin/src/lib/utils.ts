import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function getApiUrl(path: string): string {
  if (path.startsWith("http")) return path;
  let baseUrl = import.meta.env.VITE_API_URL || "";
  if (import.meta.env.PROD && !baseUrl) {
    baseUrl = "https://fuel-voucher-platform.onrender.com";
  }
  return `${baseUrl}${path}`;
}

export function formatDate(dateString: string | number | Date): string {
  const d = new Date(dateString);
  const day = String(d.getDate()).padStart(2, '0');
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const year = d.getFullYear();
  return `${day}.${month}.${year}`;
}

