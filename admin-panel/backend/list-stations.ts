import 'dotenv/config';
import { db } from './src/shared/database/db';
import { stations } from './src/shared/database/schema';

async function main() {
    const allStations = await db.select().from(stations);
    console.log('Main Stations (Brands):');
    allStations.forEach(s => console.log(JSON.stringify({ id: s.id, name: s.name, lat: s.lat, lng: s.lng })));
    process.exit(0);
}

main().catch(console.error);
