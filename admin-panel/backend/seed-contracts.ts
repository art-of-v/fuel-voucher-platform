import 'dotenv/config';
import { db } from './src/shared/database/db.ts';
import { contracts } from './src/shared/database/schema.ts';

async function seed() {
    console.log('Seeding demo contract...');
    try {
        await db.insert(contracts).values({
            id: '00000000-0000-0000-0000-000000000001',
            title: 'Типовий договір купівлі-продажу пального',
            content: 'Це приклад контракту на купівлю пального. Юридична особа зобов\'язується виконувати всі умови договору.',
            version: '1.0.0',
            status: 'ACTIVE'
        }).onConflictDoNothing();
        console.log('Seed success!');
    } catch (e) {
        console.error('Seed error:', e);
    }
    process.exit(0);
}

seed();
