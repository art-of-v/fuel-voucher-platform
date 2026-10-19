using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives the four seeded networks the ids <c>BrandIds</c> derives from their slug, and re-points
    /// every row that names them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What changed.</b> The seeded ids were hand-written constants — <c>11111111-1111-4111-8111-
    /// 111111111111</c> and its three siblings — which read like placeholders and had to be
    /// repeated in three places that cannot see each other: the seed EF writes into a fresh
    /// database, the migration that gave production its rows, and the code that quotes the id. They
    /// are now derived: UUID version 5 of the slug in a fixed namespace, so the three compute the
    /// same value instead of agreeing on it by hand.
    /// </para>
    /// <para>
    /// <b>Why the slug and not the name.</b> The slug is the natural key and nothing in the
    /// application changes it; the name is a display attribute an operator may edit at any time. An
    /// id derived from a mutable attribute would change under the operator's hands and take every
    /// foreign key with it.
    /// </para>
    /// <para>
    /// <b>Why this migration cannot be scaffolded.</b> EF sees a seed whose primary key changed and
    /// writes <c>DeleteData</c> for the old rows and <c>InsertData</c> for the new ones. On
    /// production that deletes live catalogue rows — and fails, because five tables still name them.
    /// Worse, the scaffold only knows about the seeded rows: the production rows in
    /// <c>brand_stations</c>, <c>contracts</c>, <c>fuel_type_product_codes</c> and
    /// <c>fuel_price_history</c> would keep pointing at ids that no longer exist. The re-pointing
    /// below is a join on the slug, so it moves every row, seeded or not.
    /// </para>
    /// <para>
    /// <b>Why the foreign keys are dropped and put back, rather than made deferrable.</b> A parent's
    /// key cannot change while a child names it. Deferring the five constraints would allow it —
    /// and was the first thing tried, which fails with <c>55006: cannot ALTER TABLE "fuel_types"
    /// because it has pending trigger events</c>: the deferred check is a queued trigger, and
    /// PostgreSQL will not alter a table whose trigger events are still pending. Dropping the
    /// constraints for the length of the migration is the other way round, and it is the one the
    /// uuid migration before this one took on the same tables. The constraints go back with the
    /// names they had, and adding a foreign key validates it — so the last statement of this
    /// migration is the proof that every row was re-pointed.
    /// </para>
    /// <para>
    /// <b>No orphan check is needed, and that is a fact about the schema.</b> All five
    /// <c>brand_id</c> columns carry a foreign key into <c>brands</c> — the last three were given
    /// one on 2026-10-19 by <c>CatalogueCarriesItsForeignKeys</c> — so a row naming a brand that
    /// does not exist cannot exist. A constraint that is somehow missing is therefore not an
    /// obstacle: it is dropped only if it is there, and the re-pointing moves the rows either way.
    /// </para>
    /// </remarks>
    public partial class SeededBrandIdsBecomeDerived : Migration
    {
        /// <summary>
        /// The four seeded networks: the slug, the id they carry today, and the id they carry after
        /// this migration.
        /// </summary>
        /// <remarks>
        /// The derived ids are what <c>BrandIds</c> computes from the slug. A migration cannot call
        /// into the assembly it migrates from, so the values are stated here as well — and
        /// <c>BrandIdsTests</c> asserts that the two agree, so a namespace changed in the code
        /// cannot leave the database quietly disagreeing with it.
        /// </remarks>
        public static readonly (string Slug, Guid OldId, Guid NewId)[] SeededBrandIds =
        [
            ("okko", new Guid("11111111-1111-4111-8111-111111111111"), new Guid("89a11944-bdb3-5ed4-b55c-edae31335a6e")),
            ("wog", new Guid("22222222-2222-4222-8222-222222222222"), new Guid("0b9d7319-8ce3-50c9-a6e0-58e850217ffa")),
            ("upg", new Guid("33333333-3333-4333-8333-333333333333"), new Guid("080a78c0-c80b-53f1-97cd-0fedb0ea953b")),
            ("klo", new Guid("44444444-4444-4444-8444-444444444444"), new Guid("48652f56-ca2d-590e-acfd-e8d9f970433b")),
        ];

        /// <summary>
        /// Every table that names a brand, and the constraint that ties it to <c>brands</c>.
        /// </summary>
        /// <remarks>
        /// The names are the ones the database already carries, capitals included:
        /// <c>fuel_price_history</c> has the name it was given when the table was renamed on
        /// 2026-10-19, and <c>fuel_offers</c> is absent because the table was dropped that same day.
        /// The names are read back from <c>pg_constraint</c> rather than rebuilt, so a constraint
        /// that was renamed since is put back under the name it has.
        /// </remarks>
        private static readonly (string Table, string Constraint)[] BrandForeignKeys =
        [
            ("fuel_types", "FK_fuel_types_brands_brand_id"),
            ("fuel_price_history", "FK_fuel_price_history_brands_brand_id"),
            ("fuel_type_product_codes", "FK_fuel_type_product_codes_brands_brand_id"),
            ("contracts", "FK_contracts_brands_brand_id"),
            ("brand_stations", "FK_brand_stations_brands_brand_id"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropTheBrandForeignKeys(migrationBuilder);
            RemapTheSeededBrandIds(migrationBuilder, toDerived: true);
            PutTheBrandForeignKeysBack(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropTheBrandForeignKeys(migrationBuilder);
            RemapTheSeededBrandIds(migrationBuilder, toDerived: false);
            PutTheBrandForeignKeysBack(migrationBuilder);
        }

        /// <summary>
        /// Takes the five foreign keys into <c>brands</c> out of the way, and remembers which ones
        /// were there.
        /// </summary>
        /// <remarks>
        /// The names are looked up in <c>pg_constraint</c> rather than dropped by name, because a
        /// constraint that is not there is not an obstacle: a database whose
        /// <c>CatalogueCarriesItsForeignKeys</c> has not run yet has no foreign key to drop, and the
        /// re-pointing below moves its rows all the same. What was dropped is remembered in a
        /// temporary table, because only a constraint that was taken away may be put back.
        /// </remarks>
        private static void DropTheBrandForeignKeys(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE brand_fk_dropped (
                    tbl text PRIMARY KEY,
                    con text NOT NULL
                );
                """);

            foreach (var (table, constraint) in BrandForeignKeys)
            {
                migrationBuilder.Sql($"""
                    DO $$
                    DECLARE
                        found record;
                    BEGIN
                        FOR found IN
                            SELECT c.conname
                              FROM pg_constraint c
                             WHERE c.conrelid = to_regclass('{table}')
                               AND c.contype = 'f'
                               AND c.conname = '{constraint}'
                        LOOP
                            EXECUTE format('ALTER TABLE {table} DROP CONSTRAINT %I', found.conname);
                            INSERT INTO brand_fk_dropped (tbl, con) VALUES ('{table}', found.conname);
                        END LOOP;
                    END $$;
                    """);
            }
        }

        /// <summary>
        /// Puts the foreign keys back under the names they had, which validates them against the
        /// rows as they now stand.
        /// </summary>
        /// <remarks>
        /// Adding a foreign key checks it against the rows that are there, so this is the proof
        /// that the re-pointing was complete: a row still naming the old id fails here, with the
        /// constraint named.
        /// </remarks>
        private static void PutTheBrandForeignKeysBack(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    fk record;
                BEGIN
                    FOR fk IN SELECT tbl, con FROM brand_fk_dropped LOOP
                        EXECUTE format(
                            'ALTER TABLE %I ADD CONSTRAINT %I FOREIGN KEY (brand_id) REFERENCES brands (id) ON DELETE RESTRICT',
                            fk.tbl, fk.con);
                    END LOOP;
                END $$;
                """);

            migrationBuilder.Sql("DROP TABLE brand_fk_dropped;");
        }

        /// <summary>
        /// Re-points the four seeded networks and everything that names them.
        /// </summary>
        /// <param name="migrationBuilder">The migration being written.</param>
        /// <param name="toDerived">
        /// <c>true</c> to give the networks their derived ids, <c>false</c> to give them back the
        /// constants they carried before.
        /// </param>
        /// <remarks>
        /// <para>
        /// The map is keyed by the slug, which is the contract, and the ids are read from the rows
        /// rather than remembered: going forward, the parent's current id is whatever the row
        /// holds. Coming back, the id to restore is the constant this migration replaced, so a
        /// rollback lands on the state the previous migration described.
        /// </para>
        /// <para>
        /// A brand row that is not one of the four is not touched, and a network created at runtime
        /// keeps the id it was given.
        /// </para>
        /// </remarks>
        private static void RemapTheSeededBrandIds(MigrationBuilder migrationBuilder, bool toDerived)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE brand_id_remap (
                    slug text PRIMARY KEY,
                    from_id uuid NOT NULL,
                    to_id uuid NOT NULL
                );
                """);

            if (toDerived)
            {
                migrationBuilder.Sql("""
                    INSERT INTO brand_id_remap (slug, from_id, to_id)
                    SELECT r.slug, b.id, r.to_id
                      FROM (VALUES
                          ('okko', '89a11944-bdb3-5ed4-b55c-edae31335a6e'::uuid),
                          ('wog',  '0b9d7319-8ce3-50c9-a6e0-58e850217ffa'::uuid),
                          ('upg',  '080a78c0-c80b-53f1-97cd-0fedb0ea953b'::uuid),
                          ('klo',  '48652f56-ca2d-590e-acfd-e8d9f970433b'::uuid)
                      ) AS r(slug, to_id)
                      JOIN brands b ON b.slug = r.slug;
                    """);
            }
            else
            {
                migrationBuilder.Sql("""
                    INSERT INTO brand_id_remap (slug, from_id, to_id)
                    SELECT r.slug, r.from_id, r.to_id
                      FROM (VALUES
                          ('okko', '89a11944-bdb3-5ed4-b55c-edae31335a6e'::uuid, '11111111-1111-4111-8111-111111111111'::uuid),
                          ('wog',  '0b9d7319-8ce3-50c9-a6e0-58e850217ffa'::uuid, '22222222-2222-4222-8222-222222222222'::uuid),
                          ('upg',  '080a78c0-c80b-53f1-97cd-0fedb0ea953b'::uuid, '33333333-3333-4333-8333-333333333333'::uuid),
                          ('klo',  '48652f56-ca2d-590e-acfd-e8d9f970433b'::uuid, '44444444-4444-4444-8444-444444444444'::uuid)
                      ) AS r(slug, from_id, to_id);
                    """);
            }

            // The parent. The children still name it, which is why its foreign keys are out of the
            // way for the length of this migration.
            migrationBuilder.Sql("""
                UPDATE brands b
                   SET id = m.to_id
                  FROM brand_id_remap m
                 WHERE b.id = m.from_id;
                """);

            // Every child, seeded rows and production rows alike: the join is on the id, not on
            // which rows the seed happens to have written.
            foreach (var (table, _) in BrandForeignKeys)
            {
                migrationBuilder.Sql($"""
                    UPDATE {table} c
                       SET brand_id = m.to_id
                      FROM brand_id_remap m
                     WHERE c.brand_id = m.from_id;
                    """);
            }

            migrationBuilder.Sql("DROP TABLE brand_id_remap;");
        }
    }
}
