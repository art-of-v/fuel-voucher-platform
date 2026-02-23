/**
 * Geocode station_nodes using Nominatim (OpenStreetMap).
 *
 * Nominatim rate limit: 1 req/sec max.
 * Addresses in this table have no city — we try with city hint first, then bare.
 *
 * Usage:
 *   CITY_HINT="Київ" npx tsx geocode-stations.ts
 *   CITY_HINT="Львів" npx tsx geocode-stations.ts
 *
 * Run once per city group. Check which addresses need which city by looking at the data.
 */

import 'dotenv/config';
import { db } from './src/shared/database/db';
import { stationNodes } from './src/shared/database/schema';
import { sql, eq } from 'drizzle-orm';

const NOMINATIM = 'https://nominatim.openstreetmap.org/search';
const DEFAULT_LAT = '50.4501';
const CITY_HINT = process.env.CITY_HINT || 'Київ';
const DRY_RUN = process.env.DRY_RUN === '1';

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));

async function geocode(address: string, city: string): Promise<{ lat: string; lng: string } | null> {
    // Try with city first
    const queries = [
        `${address}, ${city}, Україна`,
        `${address}, Україна`,
        address,
    ];

    for (const q of queries) {
        const url = `${NOMINATIM}?q=${encodeURIComponent(q)}&format=json&limit=1&countrycodes=ua`;
        const res = await fetch(url, {
            headers: { 'User-Agent': 'FuelFlow/1.0 (fuelflow.app)' },
        });

        if (!res.ok) {
            console.warn(`  Nominatim error ${res.status} for "${q}"`);
            continue;
        }

        const data = await res.json() as { lat: string; lon: string; display_name: string }[];
        if (data.length > 0) {
            console.log(`  ✓ "${q}" → ${data[0].lat}, ${data[0].lon} (${data[0].display_name.substring(0, 60)})`);
            return { lat: data[0].lat, lng: data[0].lon };
        } else {
            console.log(`  ✗ No result for "${q}"`);
        }

        await sleep(1100); // Nominatim allows max 1 req/sec
    }

    return null;
}

async function main() {
    // Get all nodes that still have the default placeholder coordinates
    const nodes = await db.select()
        .from(stationNodes)
        .where(sql`lat = ${DEFAULT_LAT}`);

    console.log(`\n🗺  Geocoding ${nodes.length} station nodes with city hint: "${CITY_HINT}"`);
    console.log(DRY_RUN ? '⚠️  DRY RUN — no DB writes\n' : '📝  Writing to DB\n');

    let success = 0;
    let failed = 0;

    for (const node of nodes) {
        if (!node.address) {
            console.log(`[SKIP] ${node.id} — no address`);
            failed++;
            continue;
        }

        console.log(`\n[${nodes.indexOf(node) + 1}/${nodes.length}] ${node.name}`);
        console.log(`  Address: ${node.address}`);

        const coords = await geocode(node.address, CITY_HINT);

        if (coords) {
            if (!DRY_RUN) {
                await db.update(stationNodes)
                    .set({ lat: coords.lat, lng: coords.lng })
                    .where(eq(stationNodes.id, node.id));
            }
            success++;
        } else {
            console.log(`  ❌ Failed to geocode "${node.address}"`);
            failed++;
        }

        // Always wait between requests regardless of cache
        await sleep(1100);
    }

    console.log(`\n✅ Done: ${success} geocoded, ${failed} failed`);
    process.exit(0);
}

main().catch(e => { console.error(e); process.exit(1); });
