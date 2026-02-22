import { sql } from "drizzle-orm";
import { pgTable, text, varchar, serial, integer, timestamp, jsonb, index, uuid, uniqueIndex } from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod";

// Session storage table for Replit Auth
export const sessions = pgTable(
  "sessions",
  {
    sid: varchar("sid").primaryKey(),
    sess: jsonb("sess").notNull(),
    expire: timestamp("expire").notNull(),
  },
  (table) => [index("IDX_session_expire").on(table.expire)],
);

// User storage table for Replit Auth
export const users = pgTable("users", {
  id: varchar("id").primaryKey().default(sql`gen_random_uuid()`),
  email: varchar("email").unique(),
  phone: varchar("phone").unique(),
  firstName: varchar("first_name"),
  lastName: varchar("last_name"),
  birthdate: varchar("birthdate"), // Format: YYYY-MM-DD
  profileImageUrl: varchar("profile_image_url"),
  createdAt: timestamp("created_at").defaultNow(),
  updatedAt: timestamp("updated_at").defaultNow(),
  // Referral System
  referralCode: varchar("referral_code").unique(),
  referredBy: varchar("referred_by"),
  bonusBalance: integer("bonus_balance").default(0),
});

export type UpsertUser = typeof users.$inferInsert;
export type User = typeof users.$inferSelect;

// Phone verification codes
export const phoneVerifications = pgTable("phone_verifications", {
  id: serial("id").primaryKey(),
  phone: varchar("phone").notNull(),
  code: varchar("code", { length: 6 }).notNull(),
  expiresAt: timestamp("expires_at").notNull(),
  verified: integer("verified").default(0),
  createdAt: timestamp("created_at").defaultNow(),
});

export const insertPhoneVerificationSchema = createInsertSchema(phoneVerifications).omit({
  id: true,
  createdAt: true,
});
export type InsertPhoneVerification = z.infer<typeof insertPhoneVerificationSchema>;
export type PhoneVerification = typeof phoneVerifications.$inferSelect;

// Stations
export const stations = pgTable("stations", {
  id: text("id").primaryKey(),
  name: text("name").notNull(),
  color: text("color").notNull().default("#00ff80"),
  logoText: text("logo_text").notNull(),
  lat: text("lat"),
  lng: text("lng"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const insertStationSchema = createInsertSchema(stations).omit({
  createdAt: true,
});
export type InsertStation = z.infer<typeof insertStationSchema>;
export type Station = typeof stations.$inferSelect;

// Fuel Types
export const fuelTypes = pgTable("fuel_types", {
  id: text("id").primaryKey(),
  name: text("name").notNull(),
  stationId: text("station_id").notNull(),
  basePrice: integer("base_price").notNull(),
  discountPrice: integer("discount_price").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const insertFuelTypeSchema = createInsertSchema(fuelTypes).omit({
  createdAt: true,
});
export type InsertFuelType = z.infer<typeof insertFuelTypeSchema>;
export type FuelType = typeof fuelTypes.$inferSelect;

// QR Codes Inventory
export const qrCodes = pgTable("qr_codes", {
  id: serial("id").primaryKey(),
  stationId: text("station_id").notNull(),
  fuelType: text("fuel_type").notNull(),
  liters: integer("liters").notNull(),
  qrCodeUrl: text("qr_code_url").notNull(),
  status: text("status").notNull().default("available"), // available, sold
  purchaseId: integer("purchase_id"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const insertQrCodeSchema = createInsertSchema(qrCodes).omit({
  id: true,
  createdAt: true,
});
export type InsertQrCode = z.infer<typeof insertQrCodeSchema>;
export type QrCode = typeof qrCodes.$inferSelect;

// Purchases
export const purchases = pgTable("purchases", {
  id: serial("id").primaryKey(),
  sessionId: text("session_id").notNull(), // Track user by session for now
  packageId: text("package_id").notNull(),
  stationId: text("station_id").notNull(),
  stationName: text("station_name").notNull(),
  fuelType: text("fuel_type").notNull(),
  fuelName: text("fuel_name").notNull(),
  liters: integer("liters").notNull(),
  price: integer("price").notNull(), // in UAH
  qrCodeId: integer("qr_code_id"),
  voucherId: uuid("voucher_id"), // Link to vouchers table
  status: text("status").notNull().default("pending"), // pending, delivered, pending_qr, failed
  stripeSessionId: text("stripe_session_id"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const insertPurchaseSchema = createInsertSchema(purchases).omit({
  id: true,
  createdAt: true,
});
export type InsertPurchase = z.infer<typeof insertPurchaseSchema>;
export type Purchase = typeof purchases.$inferSelect;

// Fuel Packages (predefined pricing)
export const fuelPackages = pgTable("fuel_packages", {
  id: text("id").primaryKey(),
  stationId: text("station_id").notNull(),
  fuelTypeId: text("fuel_type_id").notNull(),
  fuelName: text("fuel_name").notNull(),
  liters: integer("liters").notNull(),
  price: integer("price").notNull(), // in UAH
  originalPrice: integer("original_price").notNull(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const insertFuelPackageSchema = createInsertSchema(fuelPackages).omit({
  createdAt: true,
});
export type InsertFuelPackage = z.infer<typeof insertFuelPackageSchema>;
export type FuelPackage = typeof fuelPackages.$inferSelect;

// Notifications
export const notifications = pgTable("notifications", {
  id: serial("id").primaryKey(),
  userId: text("user_id").notNull(),
  title: text("title").notNull(),
  message: text("message").notNull(),
  read: integer("read").default(0), // 0 = unread, 1 = read
  createdAt: timestamp("created_at").defaultNow().notNull(),
});

export const insertNotificationSchema = createInsertSchema(notifications).omit({
  id: true,
  createdAt: true,
  read: true,
});
export type InsertNotification = z.infer<typeof insertNotificationSchema>;
export type Notification = typeof notifications.$inferSelect;

// Import Jobs
export const importJobs = pgTable("import_jobs", {
  id: uuid("id").primaryKey().defaultRandom(),
  adminId: text("admin_id"),
  totalFiles: integer("total_files").notNull(),
  processedFiles: integer("processed_files").default(0).notNull(),
  successfulFiles: integer("successful_files").default(0).notNull(),
  failedFiles: integer("failed_files").default(0).notNull(),
  duplicateVouchers: integer("duplicate_vouchers").default(0).notNull(),
  status: text("status").notNull().default("processing"),
  modelUsed: text("model_used"), // Track which Gemini model was used
  errorLog: jsonb("error_log"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export const insertImportJobSchema = createInsertSchema(importJobs);
export type ImportJob = typeof importJobs.$inferSelect;
export type InsertImportJob = typeof importJobs.$inferInsert;

// Vouchers
export const vouchers = pgTable("vouchers", {
  id: uuid("id").primaryKey().defaultRandom(),
  provider: text("provider").notNull().default("OKKO"),
  externalId: text("external_id"),
  fuelType: text("fuel_type").notNull(),
  fuelSubtype: text("fuel_subtype"),
  amount: integer("amount").notNull(),
  unit: text("unit").notNull().default("liters"),
  expirationDate: timestamp("expiration_date"),
  status: text("status").notNull().default("imported"),
  redemptionRules: text("redemption_rules"),
  imageUrl: text("image_url"), // Nullable - we don't store QR images, we generate them from qrCodeData
  qrCodeData: text("qr_code_data"), // Actual QR code content (read by Gemini from the QR code)
  originalFileName: text("original_file_name"),
  source: text("source").notNull(),
  importJobId: uuid("import_job_id").references(() => importJobs.id),
  assignedToUserId: text("assigned_to_user_id"), // Ideally references users table, but simple text for loose coupling here
  purchaseId: integer("purchase_id").references(() => purchases.id), // Link to purchase transaction
  createdAt: timestamp("created_at").defaultNow().notNull(),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
}, (t) => ({
  // Only enforce uniqueness when externalId is present
  idxProviderExternalId: uniqueIndex("idx_vouchers_provider_external_id")
    .on(t.provider, t.externalId)
    .where(sql`${t.externalId} IS NOT NULL`),
  idxStatus: index("idx_vouchers_status").on(t.status),
  idxExpiration: index("idx_vouchers_expiration").on(t.expirationDate),
}));

export const insertVoucherSchema = createInsertSchema(vouchers);
export type Voucher = typeof vouchers.$inferSelect;
export type InsertVoucher = typeof vouchers.$inferInsert;

// Orders - Decoupled from voucher availability
export const orders = pgTable("orders", {
  id: uuid("id").primaryKey().defaultRandom(),
  userId: text("user_id").notNull(),
  productType: text("product_type").notNull(), // e.g., "WOG-Diesel-50L"
  provider: text("provider").notNull(),
  fuelType: text("fuel_type").notNull(),
  liters: integer("liters").notNull(),
  quantity: integer("quantity").notNull().default(1),
  price: integer("price").notNull(), // Total price in UAH
  status: text("status").notNull().default("PENDING_FULFILLMENT"), // PENDING_FULFILLMENT, FULFILLED, REFUNDED
  stripePaymentId: text("stripe_payment_id"),
  idempotencyKey: text("idempotency_key").unique(),
  createdAt: timestamp("created_at").defaultNow().notNull(),
  fulfilledAt: timestamp("fulfilled_at"),
}, (t) => ({
  idxUserId: index("idx_orders_user_id").on(t.userId),
  idxStatus: index("idx_orders_status").on(t.status),
  idxCreatedAt: index("idx_orders_created_at").on(t.createdAt),
}));

export const insertOrderSchema = createInsertSchema(orders).omit({
  id: true,
  createdAt: true,
  fulfilledAt: true,
});
export type Order = typeof orders.$inferSelect;
export type InsertOrder = z.infer<typeof insertOrderSchema>;

// Fulfillments - Links orders to vouchers
export const fulfillments = pgTable("fulfillments", {
  id: serial("id").primaryKey(),
  orderId: uuid("order_id").notNull().references(() => orders.id),
  voucherId: uuid("voucher_id").notNull().references(() => vouchers.id),
  fulfilledAt: timestamp("fulfilled_at").defaultNow().notNull(),
}, (t) => ({
  idxOrderId: index("idx_fulfillments_order_id").on(t.orderId),
  idxVoucherId: index("idx_fulfillments_voucher_id").on(t.voucherId),
}));

export const insertFulfillmentSchema = createInsertSchema(fulfillments).omit({
  id: true,
  fulfilledAt: true,
});
export type Fulfillment = typeof fulfillments.$inferSelect;
export type InsertFulfillment = z.infer<typeof insertFulfillmentSchema>;

// Outbox - Transactional outbox for reliable event processing
export const outbox = pgTable("outbox", {
  id: serial("id").primaryKey(),
  eventType: text("event_type").notNull(), // ORDER_CREATED, VOUCHERS_IMPORTED
  payload: jsonb("payload").notNull(),
  processed: integer("processed").default(0).notNull(), // 0 = pending, 1 = processed
  processedAt: timestamp("processed_at"),
  createdAt: timestamp("created_at").defaultNow().notNull(),
}, (t) => ({
  idxProcessed: index("idx_outbox_processed").on(t.processed),
  idxCreatedAt: index("idx_outbox_created_at").on(t.createdAt),
}));

export const insertOutboxSchema = createInsertSchema(outbox).omit({
  id: true,
  createdAt: true,
  processedAt: true,
});
export type OutboxEvent = typeof outbox.$inferSelect;
export type InsertOutboxEvent = z.infer<typeof insertOutboxSchema>;
