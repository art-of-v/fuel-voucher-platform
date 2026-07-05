/**
 * Database Migration Runner
 * 
 * Executes SQL migration files in order.
 */

import { db } from '../src/shared/database/db';
import fs from 'fs';
import path from 'path';
import { sql } from 'drizzle-orm';

const MIGRATIONS_DIR = path.join(__dirname, '../migrations');

interface Migration {
    id: number;
    filename: string;
    executed_at: Date;
}

async function runMigrations() {
    console.log('Starting database migrations...');

    // Create migrations tracking table if it doesn't exist
    await db.execute(sql`
        CREATE TABLE IF NOT EXISTS schema_migrations (
            id SERIAL PRIMARY KEY,
            filename VARCHAR(255) UNIQUE NOT NULL,
            executed_at TIMESTAMP DEFAULT NOW()
        )
    `);

    // Get list of executed migrations
    const executedMigrations = await db.execute<Migration>(sql`
        SELECT filename FROM schema_migrations ORDER BY id
    `);

    const executedFiles = new Set(
        executedMigrations.rows.map((m: any) => m.filename)
    );

    // Get all migration files
    const migrationFiles = fs
        .readdirSync(MIGRATIONS_DIR)
        .filter(f => f.endsWith('.sql'))
        .sort();

    console.log(`Found ${migrationFiles.length} migration files`);
    console.log(`${executedFiles.size} already executed`);

    // Execute pending migrations
    for (const filename of migrationFiles) {
        if (executedFiles.has(filename)) {
            console.log(`✓ Skipping ${filename} (already executed)`);
            continue;
        }

        console.log(`→ Executing ${filename}...`);

        const filepath = path.join(MIGRATIONS_DIR, filename);
        const migrationSQL = fs.readFileSync(filepath, 'utf-8');

        try {
            // Execute migration in transaction
            await db.transaction(async (tx) => {
                // Execute the migration SQL
                await tx.execute(sql.raw(migrationSQL));

                // Record migration as executed
                await tx.execute(sql`
                    INSERT INTO schema_migrations (filename)
                    VALUES (${filename})
                `);
            });

            console.log(`✓ Successfully executed ${filename}`);
        } catch (error: any) {
            console.error(`✗ Failed to execute ${filename}:`, error.message);
            throw error;
        }
    }

    console.log('✓ All migrations completed successfully');
}

// Run migrations
runMigrations()
    .then(() => {
        console.log('Migration process completed');
        process.exit(0);
    })
    .catch((error) => {
        console.error('Migration process failed:', error);
        process.exit(1);
    });
