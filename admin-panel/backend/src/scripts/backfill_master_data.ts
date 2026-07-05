
import 'dotenv/config';
import { autoMasterDataService } from "../features/vouchers/import/auto-master-data.service";
import { db } from "../shared/database/db";
import { vouchers } from "../shared/database/schema";
import { logger } from "../infrastructure/logging/logger";

const log = logger.child({ component: 'BackfillMasterData' });

async function main() {
    log.info("🚀 Starting Master Data Backfill from existing vouchers...");

    // 1. Get all unique combinations from existing vouchers
    const uniqueCombos = await db.selectDistinct({
        provider: vouchers.provider,
        fuelType: vouchers.fuelType,
        amount: vouchers.amount
    }).from(vouchers);

    log.info(`Found ${uniqueCombos.length} unique voucher types to process.`);

    for (const combo of uniqueCombos) {
        if (!combo.provider || !combo.fuelType || !combo.amount) {
            log.warn(`Skipping incomplete combo: ${JSON.stringify(combo)}`);
            continue;
        }

        log.info(`Processing: ${combo.provider} | ${combo.fuelType} | ${combo.amount}L`);
        await autoMasterDataService.ensureMasterData(
            combo.provider,
            combo.fuelType,
            combo.amount
        );
    }

    log.info("✅ Backfill Complete!");
    process.exit(0);
}

main().catch(err => {
    console.error("❌ Backfill Failed:", err);
    process.exit(1);
});
