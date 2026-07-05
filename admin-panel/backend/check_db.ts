import { drizzle } from 'drizzle-orm/node-postgres';
import pg from 'pg';
import * as schema from './src/shared/database/schema';

const pool = new pg.Pool({
  connectionString: process.env.DATABASE_URL,
  ssl: { rejectUnauthorized: false }
});

const db = drizzle(pool, { schema });

async function check() {
  console.log('--- USERS ---');
  const u = await db.select().from(schema.users);
  console.table(u.map(x => ({ id: x.id, phone: x.phone })));

  console.log('--- DEVICES ---');
  const d = await db.select().from(schema.devices);
  console.table(d.map(x => ({ userId: x.userId, devId: x.deviceId })));

  console.log('--- COMPANIES ---');
  const c = await db.select().from(schema.companies);
  console.table(c.map(x => ({ userId: x.userId, name: x.name, edrpou: x.edrpou })));
  
  process.exit(0);
}

check().catch(e => { console.error(e); process.exit(1); });
