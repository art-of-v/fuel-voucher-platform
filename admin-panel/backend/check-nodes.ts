import 'dotenv/config';
import { db } from './src/shared/database/db';
import { stationNodes } from './src/shared/database/schema';
import { sql } from 'drizzle-orm';

async function main() {
    const total = await db.select({ count: sql<string>`count(*)` }).from(stationNodes);
    const sample = await db.select().from(stationNodes).limit(5);
    console.log('Total nodes:', total[0].count);
    console.log('\nSample records:');
    sample.forEach(n => console.log(JSON.stringify({ id: n.id, name: n.name, address: n.address, lat: n.lat, lng: n.lng })));

    const defaultCoords = await db.select({ count: sql<string>`count(*)` }).from(stationNodes).where(sql`lat = '50.4501'`);
    console.log('\nNodes with default Kyiv coords (not geocoded):', defaultCoords[0].count);
    process.exit(0);
}

main().catch(e => { console.error(e); process.exit(1); });
