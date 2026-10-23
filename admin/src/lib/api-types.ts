/**
 * The shapes the admin API answers with.
 *
 * <p>
 * These are the TypeScript mirrors of the backend's DTO records, one per endpoint the admin SPA
 * reads. They exist because the money screens used to be typed `any` end to end, and `any` is what
 * let the voucher screen keep asking for field names the server had stopped sending
 * (`brands`/`fuelTypesByBrand` instead of `providers`/`fuelTypesByProvider`, and
 * `providerExpirationDate` instead of `supplierExpirationDate`). Nothing complained: an `any`
 * response is any object, and a missing field is simply `undefined` at runtime.
 *
 * <p>
 * When a backend DTO changes, the change lands HERE first - the compiler points at the screen that
 * reads the field. That is the whole point of the file.
 *
 * <p>
 * The field names are camelCase because ASP.NET Core serialises with the web defaults
 * (`JsonSerializerDefaults.Web`). The C# property names are PascalCase; the wire is not.
 */

/**
 * A page of rows and the count behind it.
 *
 * <p>
 * The backend's `PagedResult<T>`: the rows, the total number of rows the query has (not the size
 * of the page), the page itself, and the three derived figures a pager needs.
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

/** A customer's name, and nothing else - what the screens that NAME a customer need. */
export interface AdminUserOption {
  id: string;
  phone: string | null;
  firstName: string | null;
  lastName: string | null;
}

/** A row of the customers table (`GET /api/admin/users`). */
export interface AdminUser {
  id: string;
  phone: string | null;
  email: string | null;
  firstName: string | null;
  lastName: string | null;
  birthdate: string | null;
  profileImageUrl: string | null;
  /** Deliberately absent from the screen's own list: the only endpoints that wrote it are gone. */
  referredBy: string | null;
  /** Read-only and staff-only: the code an operator quotes to attribute money to this customer. */
  settlementCode: string;
  bonusBalance: number;
  isActive: boolean;
  isBanned: boolean;
  isDeleted: boolean;
  /** The backend grants this by migration or by the QA-gated registration path, never from a panel. */
  isQaAccount: boolean;
  /** Whether the account has a second factor enrolled - only a staff account can have one. */
  hasTwoFactor: boolean;
  role: string | null;
  createdAt: string;
}

/** One line of an order (`OrderLineItemDto`). */
export interface AdminOrderLine {
  id: string;
  brand: string;
  fuelTypeId: string;
  liters: number;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

/** A row of the purchases table (`GET /api/admin/purchases`). */
export interface AdminOrder {
  id: string;
  userId: string;
  brand: string;
  fuelTypeId: string;
  liters: number;
  quantity: number;
  price: number;
  status: string;
  monobankInvoiceId: string | null;
  monobankStatus: string | null;
  createdAtUtc: string;
  fulfilledAtUtc: string | null;
  voucherCount: number;
  refundableAmountKopecks: number;
  refundStatus: string | null;
  refundMonobankStatus: string | null;
  lineItems: AdminOrderLine[];
}

/** A fuel, as the catalog endpoints name it. */
export interface FuelTypeRef {
  id: string;
  name: string;
}

/**
 * A row of the voucher stock table (`GET /api/admin/vouchers`).
 *
 * <p>
 * The expiry field is `supplierExpirationDate`, not `providerExpirationDate`: the supplier is the
 * company we bought the voucher from, and the brand printed on it is a different thing.
 */
export interface AdminVoucher {
  id: string;
  status: string;
  liters: number;
  fuelType: FuelTypeRef | null;
  fuelTypeId: string;
  brand: string;
  supplierId: string | null;
  supplierName: string | null;
  /** UAH per liter, stamped by the import batch. Null = cost never entered, not zero. */
  costPerLiter: number | null;
  /** Real term printed on the supplier's document - the ceiling. */
  supplierExpirationDate: string;
  /** What the customer was sold. Diverges from the supplier term once short terms ship. */
  customerExpirationDate: string;
  voucherNumber: string;
  createdAtUtc: string;
  /** Archived by the soft delete (#217): hidden from customers, restorable from this list. */
  isDeleted: boolean;
}

/**
 * One voucher, whole (`GET /api/admin/vouchers/{id}`).
 *
 * <p>
 * The redeemable payload is here and NOT in the list: a payload is a bearer instrument, so it
 * travels only to the roles the server decides may hold it. The list screen asks for it per row,
 * the list itself never carries it.
 */
export interface AdminVoucherDetail {
  id: string;
  qrPayload: string | null;
  qrImage: string | null;
  liters: number;
  fuelTypeId: string;
  fuelType: FuelTypeRef | null;
  brand: string;
  supplierId: string | null;
  supplierName: string | null;
  costPerLiter: number | null;
  supplierExpirationDate: string;
  customerExpirationDate: string;
  voucherNumber: string;
  status: string;
  createdAtUtc: string;
  isTestData: boolean;
}

/**
 * The voucher stock list and the dropdowns that go with it.
 *
 * <p>
 * The dropdown sets are keyed by the brand the vouchers actually carry, so the operator cannot
 * pick a pairing that can never match a row.
 */
export interface AdminVoucherList {
  data: AdminVoucher[];
  total: number;
  globalTotal: number;
  /** How many emulated vouchers exist, whatever the filters say. */
  testDataTotal: number;
  fuelTypes: string[];
  /** Which fuels each brand actually has vouchers for, keyed by the brand. */
  fuelTypesByProvider: Record<string, FuelTypeRef[]>;
  /** The brands the list can be filtered by. */
  providers: string[];
  statuses: string[];
  amounts: number[];
}

/** One import batch (`GET /api/admin/voucher-imports`). */
export interface ImportBatch {
  id: string;
  fileName: string;
  pageCount: number;
  startedAtUtc: string;
  completedAtUtc: string | null;
  status: string;
  supplierId: string | null;
  supplierName: string | null;
  importedCount: number;
  duplicateCount: number;
  failedCount: number;
  decodeIntegrityFailedCount: number;
  decodeIntegrityWarningCount: number;
  voucherCount: number;
}

/** One voucher of an import batch (`GET /api/admin/voucher-imports/{id}/vouchers`). */
export interface ImportBatchVoucher {
  id: string;
  brand: string;
  fuelTypeId: string;
  fuelTypeName: string | null;
  liters: number;
  supplierExpirationDate: string;
  customerExpirationDate: string;
  voucherNumber: string;
  status: string;
  createdAtUtc: string;
  matrixMismatchPercent: number | null;
  matrixMismatchedModules: number | null;
  matrixTotalModules: number | null;
}

/** A page of an import batch's vouchers, and the size of the batch it is a page of. */
export interface ImportBatchVouchersPage {
  items: ImportBatchVoucher[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

/** One fuel's cost inside an import batch (`GET .../batch-costs`). */
export interface ImportBatchCost {
  fuelTypeId: string;
  fuelTypeName: string | null;
  brand: string;
  voucherCount: number;
  totalLiters: number;
  /** UAH per liter. Null = the batch is not costed, which is not the same as costing it zero. */
  costPerLiter: number | null;
  /** What the fuel costs across the whole pool, not just this batch. */
  blendedCostPerLiter: number | null;
}

/** One fuel's profit and loss inside an import batch (`GET .../batch-pnl`). */
export interface ImportBatchPnl {
  fuelTypeId: string;
  fuelTypeName: string | null;
  brand: string;
  vouchersIn: number;
  litersIn: number;
  vouchersSold: number;
  litersSold: number;
  vouchersRemaining: number;
  litersRemaining: number;
  vouchersExpired: number;
  litersExpired: number;
  costPerLiter: number | null;
  realizedRevenue: number;
  realizedCogs: number | null;
  realizedMargin: number | null;
  avgSalePricePerLiter: number | null;
  currentPricePerLiter: number | null;
  unrealizedMargin: number | null;
  expiredLoss: number | null;
  netRealizedResult: number | null;
}

/** One statement line attached to a pending redemption claim. */
export interface RedemptionStatementLineDetail {
  redemptionId: string;
  rowId: string;
  externalId: string;
  liters: number;
  redeemedAtUtc: string | null;
  status: string;
  note: string | null;
}

/** One claim waiting for an operator's decision. */
export interface PendingRedemptionDetail {
  redemptionId: string;
  voucherId: string;
  voucherNumber: string;
  reportedByUserId: string | null;
  reportedLiters: number;
  redeemedAtUtc: string;
  reportedAtUtc: string;
  supplierReference: string | null;
  hasMismatch: boolean;
  severity: string;
  statementLines: RedemptionStatementLineDetail[];
}

/** One statement line that needs an operator, with no live claim attached. */
export interface StatementExceptionDetail {
  rowId: string;
  externalId: string;
  voucherNumber: string;
  liters: number;
  redeemedAtUtc: string | null;
  status: string;
  voucherId: string | null;
  redemptionId: string | null;
  importedAtUtc: string;
  note: string | null;
  severity: string;
}

/** The redemption exception queue (`GET /api/admin/redemptions/exceptions`). */
export interface RedemptionExceptionsResponse {
  pendingClaims: number;
  mismatchedClaims: number;
  statementExceptions: number;
  pending: PendingRedemptionDetail[];
  statements: StatementExceptionDetail[];
}

/** One line accepted by the supplier statement import. */
export interface ImportedStatementRow {
  externalId: string;
  status: string | null;
  voucherId: string | null;
  redemptionId: string | null;
  message: string | null;
}

/** The answer of `POST /api/admin/redemptions/statement`. */
export interface ImportRedemptionStatementResponse {
  success: boolean;
  error: string | null;
  imported: number;
  duplicates: number;
  awaitingReview: number;
  exceptions: number;
  rows: ImportedStatementRow[];
}

/** The answer of a confirm/reject/reverse decision. */
export interface RedemptionDecisionResponse {
  success: boolean;
  message: string;
  errorCode: string | null;
}

/** The answer of dismissing one statement line. */
export interface DismissRedemptionStatementRowResponse {
  success: boolean;
  message: string;
  errorCode: string | null;
}

/** The counters the reconciliation screen puts at the top. */
export interface ReconciliationSummary {
  totalOrders: number;
  paidUnfulfilled: number;
  partiallyFulfilled: number;
  fulfilled: number;
  /** UAH - the margin is UAH per litre, so margin times litres is UAH. */
  totalRevenueUah: number;
  orphanVouchers: number;
  unprocessedEvents: number;
  lowInventoryProviders: number;
  importErrors7d: number;
  refundedOrders: number;
  /** Kopecks - what the money path stores and what the brand's API takes. */
  totalReceivedKopecks: number;
  totalFulfilledValueKopecks: number;
  totalRefundedKopecks: number;
}

/** One row of the three-way match: order, payment and fulfilment. */
export interface ReconciliationThreeWayMatchItem {
  orderId: string;
  userPhone: string | null;
  brand: string;
  /** The fuel's id, not its name - the screen resolves the name from the catalog. */
  fuelType: string;
  liters: number;
  quantity: number;
  totalPrice: number;
  orderStatus: string;
  monobankStatus: string | null;
  vouchersExpected: number;
  vouchersDelivered: number;
  matchStatus: string;
  daysSinceCreated: number;
  createdAtUtc: string;
  refundStatus: string | null;
  refundedKopecks: number;
}

/** One thing the reconciliation screen flags as wrong. */
export interface ReconciliationException {
  id: string;
  type: string;
  description: string;
  severity: string;
  createdAtUtc: string;
}

/** How many vouchers sit in each state. */
export interface ReconciliationVoucherFunnelItem {
  status: string;
  count: number;
  totalLiters: number;
}

/** One month of the reconciliation's revenue summary. */
export interface ReconciliationRevenueSummaryItem {
  year: number;
  month: number;
  orderCount: number;
  /** UAH - see the summary for the units. */
  revenueUah: number;
}

/** The whole reconciliation answer (`GET /api/admin/reconciliation`). */
export interface ReconciliationResponse {
  summary: ReconciliationSummary;
  threeWayMatch: PagedResult<ReconciliationThreeWayMatchItem>;
  exceptions: ReconciliationException[];
  voucherFunnel: ReconciliationVoucherFunnelItem[];
  revenueSummary: ReconciliationRevenueSummaryItem[];
}

/** The period the report covers. */
export interface ReportPeriod {
  from: string | null;
  to: string | null;
}

/** The report's headline counters. */
export interface ReportSummary {
  totalSpent: number;
  totalOrders: number;
  vouchersPurchased: number;
  vouchersUsed: number;
  totalLitersPurchased: number;
  totalLitersUsed: number;
  totalReceivedKopecks: number;
  totalFulfilledValueKopecks: number;
  totalRefundedKopecks: number;
}

/** One paid order, as the report counts it. */
export interface ReportPayment {
  orderId: string;
  amount: number;
  status: string;
  createdAtUtc: string;
  brand: string;
  /** The fuel's id. `fuelName` is what the screen shows; this is only the fallback. */
  fuelType: string;
  fuelName: string;
  liters: number;
  quantity: number;
  monobankStatus: string | null;
  monobankInvoiceId: string | null;
  fulfilledValueKopecks: number;
  refundedKopecks: number;
  refundStatus: string | null;
}

/** One redeemed voucher, as the report counts it. */
export interface ReportRedemption {
  voucherId: string;
  brand: string;
  fuelType: string;
  fuelName: string;
  liters: number;
  redeemedAt: string;
}

/** One month of the report's breakdown. */
export interface ReportMonthlyBreakdown {
  month: string;
  totalSpent: number;
  vouchersPurchased: number;
  vouchersUsed: number;
  totalLitersPurchased: number;
  totalLitersUsed: number;
  totalRefundedKopecks: number;
}

/** The whole report answer (`GET /api/admin/report`). */
export interface AdminReport {
  period: ReportPeriod;
  summary: ReportSummary;
  payments: ReportPayment[];
  redemptions: ReportRedemption[];
  monthlyBreakdown: ReportMonthlyBreakdown[];
}

/** The answer of the catalog import (`POST /api/voucher-catalog/import`). */
export interface CatalogImportResult {
  imported: number;
  failed: number;
  duplicates: number;
  errors?: {
    pageNumber: number;
    voucherNumber: string | null;
    reason: string;
  }[];
}

/** One line of a parsed supplier invoice (`POST .../parse-invoice`). */
export interface ParsedInvoiceLine {
  line: number;
  invoiceLabel: string;
  costPerLiter: number | null;
  liters: number | null;
  total: number | null;
  matched: boolean;
  fuelTypeId: string | null;
  fuelTypeName: string | null;
  brand: string | null;
  importLiters: number | null;
  warning: string | null;
}

/** The answer of the invoice parse. */
export interface ParsedInvoiceResponse {
  lines: ParsedInvoiceLine[];
  unmatchedImportFuels: string[];
  errors: { line: number; message: string }[];
}

/** A supplier, as the supplier screens name it. */
export interface AdminSupplier {
  id: string;
  name: string;
  isActive: boolean;
}

/**
 * A row of the signed contracts table (`GET /api/admin/legal-entity/contracts/signed-contracts`).
 *
 * <p>
 * The list carries no signature image, and it is a page: a handwritten signature cannot be
 * revoked and cannot be rotated, so it is a capability of its own on the server
 * (`AuthPolicies.SignaturePayload`), and the image is served per record.
 */
export interface AdminSignedContract {
  id: string;
  contractId: string;
  brandId: string | null;
  brandName: string | null;
  companyId: string | null;
  companyName: string | null;
  userId: string;
  customerName: string | null;
  signedAtUtc: string;
  expiresAtUtc: string | null;
  expired: boolean;
  termsVersion: number;
}

/**
 * One customer's handwritten signature
 * (`GET /api/admin/legal-entity/contracts/signed-contracts/{id}`).
 *
 * <p>
 * The image is here and NOT in the list, and opening it writes an audit row naming the
 * operator - so the client asks for it when the operator asks for it, and nowhere else.
 */
export interface AdminSignedContractSignature {
  id: string;
  contractId: string;
  brandId: string | null;
  brandName: string | null;
  companyId: string | null;
  companyName: string | null;
  userId: string;
  customerName: string | null;
  signedAtUtc: string;
  expiresAtUtc: string | null;
  expired: boolean;
  termsVersion: number;
  signatureData: string | null;
}
