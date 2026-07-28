import { apiFetch } from '../../../core/api/apiClient';

export interface ReportPeriod {
  from: string | null;
  to: string | null;
}

export interface ReportSummary {
  totalSpent: number;
  totalOrders: number;
  vouchersPurchased: number;
  vouchersUsed: number;
  totalLitersPurchased: number;
  totalLitersUsed: number;
}

export interface PaymentEntry {
  orderId: string;
  amount: number;
  status: string;
  createdAtUtc: string;
  provider: string;
  fuelType: string;
  liters: number;
  quantity: number;
  monobankStatus?: string;
  monobankInvoiceId?: string;
}

export interface RedemptionEntry {
  voucherId: string;
  provider: string;
  fuelType: string;
  fuelName: string;
  liters: number;
  redeemedAt: string;
}

export interface MonthlyBreakdown {
  month: string;
  totalSpent: number;
  vouchersPurchased: number;
  vouchersUsed: number;
  totalLitersPurchased: number;
  totalLitersUsed: number;
}

export interface ReportData {
  period: ReportPeriod;
  summary: ReportSummary;
  payments: PaymentEntry[];
  redemptions: RedemptionEntry[];
  monthlyBreakdown: MonthlyBreakdown[];
}

export async function getMyReport(
  fromDate?: string,
  toDate?: string,
): Promise<ReportData> {
  const params = new URLSearchParams();
  if (fromDate) params.set('fromDate', fromDate);
  if (toDate) params.set('toDate', toDate);
  const qs = params.toString();
  const response = await apiFetch(`/api/report${qs ? '?' + qs : ''}`);
  if (!response.ok) {
    if (response.status === 401) throw new Error('Unauthorized');
    throw new Error('Failed to fetch report');
  }
  return response.json();
}
