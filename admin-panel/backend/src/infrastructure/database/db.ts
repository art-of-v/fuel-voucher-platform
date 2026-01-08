import { drizzle } from "drizzle-orm/node-postgres";
import pg from "pg";
import * as schema from "./schema";

const { Pool } = pg;

console.log('[DB_INIT] Initializing database connection...');
console.log('[DB_INIT] DATABASE_URL present:', !!process.env.DATABASE_URL);
if (process.env.DATABASE_URL) {
  // Log masked URL for debugging to verify host/port
  const maskedv = process.env.DATABASE_URL.replace(/:[^:@]*@/, ':****@');
  console.log('[DB_INIT] URL:', maskedv);
}

export const pool = process.env.DATABASE_URL ? new Pool({
  connectionString: process.env.DATABASE_URL,
}) : null;

if (!pool) {
  console.error('[DB_INIT] Pool creation failed: DATABASE_URL is missing');
} else {
  console.log('[DB_INIT] Pool created successfully');
}

export const db = pool ? drizzle(pool, { schema }) : (null as any);
