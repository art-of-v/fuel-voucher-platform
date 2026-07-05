import 'dotenv/config';
import { db } from '../shared/database/db';
import { stationNodes } from '../shared/database/schema';
import { sql, isNull, and, ne } from 'drizzle-orm';

async function main() {
    const results = await db.select({ count: sql<number>`count(*)` })
        .from(stationNodes)
        .where(and(ne(stationNodes.lat, '50.4501'), isNull(stationNodes.city)));

    console.log('Records geocoded but missing city:', results[0].count);

    if (results[0].count > 0) {
        console.log('Updating records to city: "Київ"...');
        await db.update(stationNodes)
            .set({ city: 'Київ' })
            .where(and(ne(stationNodes.lat, '50.4501'), isNull(stationNodes.city)));
        console.log('Update complete.');
    }
    process.exit(0);
}

main().catch(console.error);
