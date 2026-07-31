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

export function formatDate(dateString: string | number | Date | null | undefined): string {
  if (!dateString) return '—';
  const d = new Date(dateString);
  if (isNaN(d.getTime())) return '—';
  const day = String(d.getDate()).padStart(2, '0');
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const year = d.getFullYear();
  return `${day}.${month}.${year}`;
}

export function formatDateTime(dateString: string | number | Date | null | undefined): string {
  if (!dateString) return '—';
  const d = new Date(dateString);
  if (isNaN(d.getTime())) return '—';
  const day = String(d.getDate()).padStart(2, '0');
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const year = d.getFullYear();
  const hours = String(d.getHours()).padStart(2, '0');
  const minutes = String(d.getMinutes()).padStart(2, '0');
  const seconds = String(d.getSeconds()).padStart(2, '0');
  return `${day}.${month}.${year} ${hours}:${minutes}:${seconds}`;
}

type Translator = (key: string, ...params: string[]) => string;

function localizeChange(change: string, t: Translator): string {
  let m = change.match(/^name (.+) → (.+)$/);
  if (m) return t('history.changeName', m[1], m[2]);
  m = change.match(/^supplier (.+) → (.+)$/);
  if (m) return t('history.changeSupplier', m[1], m[2]);
  m = change.match(/^margin (.+) → (.+)$/);
  if (m) return t('history.changeMargin', m[1], m[2]);
  m = change.match(/^final (.+) → (.+)$/);
  if (m) return t('history.changeFinal', m[1], m[2]);
  m = change.match(/^logo (.+) → (.+)$/);
  if (m) return t('history.changeLogo', m[1], m[2]);
  m = change.match(/^color (.+) → (.+)$/);
  if (m) return t('history.changeColor', m[1], m[2]);
  return change;
}

export function localizeEventSummary(summary: string, t: Translator): string {
  let m = summary.match(/^Created provider (.+)$/);
  if (m) return t('history.providerCreated', m[1]);
  m = summary.match(/^Deleted provider (.+)$/);
  if (m) return t('history.providerDeleted', m[1]);
  m = summary.match(/^Updated provider (.+)$/);
  if (m) return t('history.providerUpdated', m[1]);
  m = summary.match(/^(.+): nominals changed \[(.+)\]$/);
  if (m) return t('history.nominalsChanged', m[1], m[2]);
  m = summary.match(/^(.+) \/ (.+): added at (.+) UAH\/L$/);
  if (m) return t('history.fuelAdded', m[1], m[2], m[3]);
  m = summary.match(/^(.+ \/ .+): removed$/);
  if (m) return t('history.fuelRemoved', m[1], m[2]);
  m = summary.match(/^(.+ \/ .+): updated$/);
  if (m) return t('history.fuelUpdated', m[1], m[2]);
  m = summary.match(/^(.+ \/ .+): (.+)$/);
  if (m) return `${m[1]} / ${m[2]}: ${m[3].split(", ").map(c => localizeChange(c, t)).join(", ")}`;
  m = summary.match(/^(.+): (.+)$/);
  if (m) return `${m[1]}: ${m[2].split(", ").map(c => localizeChange(c, t)).join(", ")}`;
  return summary;
}

