import { apiRequest } from "@/lib/api-client";

export const BUSINESS_LEAD_STATUSES = [
  "New",
  "Contacted",
  "Qualified",
  "Converted",
  "Rejected",
] as const;

export type BusinessLeadStatus = (typeof BUSINESS_LEAD_STATUSES)[number];
export type BusinessLeadStatusFilter = "active" | "all" | BusinessLeadStatus;

export interface BusinessLead {
  id: string;
  companyName: string;
  contactName: string;
  phone: string;
  email: string | null;
  fleetSize: number | null;
  message: string | null;
  status: BusinessLeadStatus;
  emailSentAtUtc: string | null;
  emailSendError: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface BusinessLeadPage {
  total: number;
  items: BusinessLead[];
}

export interface BusinessLeadFacets {
  total: number;
  active: number;
  newCount: number;
  counts: Partial<Record<BusinessLeadStatus, number>>;
}

export interface BusinessLeadFilters {
  offset: number;
  limit: number;
  search: string;
  status: BusinessLeadStatusFilter;
  from: string;
  to: string;
}

export interface BusinessLeadMutationResponse {
  success: boolean;
  affected: number;
}

export const businessLeadKeys = {
  all: ["admin", "businessLeads"] as const,
  lists: ["admin", "businessLeads", "list"] as const,
  list: (query: string) => ["admin", "businessLeads", "list", query] as const,
  facets: ["admin", "businessLeads", "facets"] as const,
  detail: (id: string | null) =>
    ["admin", "businessLeads", "detail", id] as const,
  attention: ["admin", "businessLeads", "attention"] as const,
};

export function buildBusinessLeadQuery(filters: BusinessLeadFilters): string {
  const params = new URLSearchParams({
    offset: String(filters.offset),
    limit: String(filters.limit),
  });

  if (filters.search.trim()) params.set("search", filters.search.trim());
  if (filters.status !== "active") params.set("status", filters.status);
  if (filters.from) params.set("from", new Date(filters.from).toISOString());
  if (filters.to) {
    params.set("to", new Date(`${filters.to}T23:59:59`).toISOString());
  }

  return params.toString();
}

export function getBusinessLeads(query: string): Promise<BusinessLeadPage> {
  return apiRequest<never, BusinessLeadPage>(
    "GET",
    `/api/admin/business-leads?${query}`,
  );
}

export async function getBusinessLeadFacets(): Promise<BusinessLeadFacets> {
  const payload = await apiRequest<never, unknown>(
    "GET",
    "/api/admin/business-leads/facets",
  );
  return normalizeFacets(payload);
}

export async function getBusinessLead(id: string): Promise<BusinessLead> {
  const payload = await apiRequest<never, unknown>(
    "GET",
    `/api/admin/business-leads/${id}`,
  );

  if (isRecord(payload) && "item" in payload) {
    return payload.item as BusinessLead;
  }
  return payload as BusinessLead;
}

export function updateBusinessLeadStatus(
  ids: string[],
  status: BusinessLeadStatus,
): Promise<BusinessLeadMutationResponse> {
  return apiRequest<
    { ids: string[]; status: BusinessLeadStatus },
    BusinessLeadMutationResponse
  >("POST", "/api/admin/business-leads/status", {
    ids,
    status,
  });
}

function normalizeFacets(payload: unknown): BusinessLeadFacets {
  const source = isRecord(payload) ? payload : {};
  const counts: Partial<Record<BusinessLeadStatus, number>> = {};
  const rawCounts = isRecord(source.counts) ? source.counts : null;

  for (const status of BUSINESS_LEAD_STATUSES) {
    const direct = rawCounts?.[status] ?? rawCounts?.[status.toLowerCase()];
    const count = readNumber(direct);
    if (count !== null) counts[status] = count;
  }

  if (Array.isArray(source.statuses)) {
    for (const item of source.statuses) {
      if (!isRecord(item)) continue;
      const status = readStatus(item.status ?? item.name);
      const count = readNumber(item.count);
      if (status && count !== null) counts[status] = count;
    }
  }

  const newCount =
    readNumber(source.newCount) ?? readNumber(source.new) ?? counts.New ?? 0;
  const countedTotal = BUSINESS_LEAD_STATUSES.reduce(
    (sum, status) => sum + (counts[status] ?? 0),
    0,
  );
  const countedActive =
    (counts.New ?? 0) + (counts.Contacted ?? 0) + (counts.Qualified ?? 0);

  return {
    total: readNumber(source.total) ?? countedTotal,
    active:
      readNumber(source.active) ??
      readNumber(source.activeCount) ??
      countedActive,
    newCount,
    counts,
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function readStatus(value: unknown): BusinessLeadStatus | null {
  return typeof value === "string" &&
    BUSINESS_LEAD_STATUSES.includes(value as BusinessLeadStatus)
    ? (value as BusinessLeadStatus)
    : null;
}
