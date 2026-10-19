using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Planning D-21, first slice: <c>brands</c> and <c>brand_stations</c> stop being keyed by a text
    /// natural key and start being keyed by a uuid, and the natural key moves to a column that
    /// survives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hand-written, because EF cannot scaffold this.</b> Scaffolding the model diff produces a
    /// migration that <c>DeleteData</c>s the four seeded brand rows and then <c>AlterColumn</c>s
    /// <c>text</c> to <c>uuid</c> with no <c>USING</c> clause. On production that deletes live
    /// catalogue and then fails, inside one transaction, so nothing happens and the next deploy tries
    /// again. Worse, a scaffold has no way to know that <c>'okko'</c> is this particular uuid: EF
    /// generates type changes, and the value mapping is the whole point.
    /// </para>
    /// <para>
    /// <b>What the data looks like before and after.</b> Before: <c>brands.id</c> is <c>'okko'</c>, and
    /// six other tables carry that same string in a <c>brand_id</c> column. After: <c>brands.id</c> is
    /// a uuid, <c>brands.slug</c> holds <c>'okko'</c>, and those six columns hold the uuid. The slug is
    /// what a supplier file, an invoice and an operator still say, so nothing outside the database
    /// changes what it writes — only what the database joins on.
    /// </para>
    /// <para>
    /// <b>The four seeded ids are fixed values, not generated.</b> They are stated in
    /// <c>BrandIds</c> and repeated here, because a fresh database's seed and a migrated database's
    /// rows must agree: if the migration invented its own uuids, every environment would price the
    /// same fuel against a different brand row, and the seeded offers would point at ids no
    /// application code knows. A network the seed does not know about — UPG arrives from the admin
    /// import — gets <c>gen_random_uuid()</c>, which is fine because nothing outside the database
    /// quotes it.
    /// </para>
    /// <para>
    /// <b>The orphan check is the point.</b> Every backfill below is a join, so a row naming a brand
    /// that does not exist would silently get <c>NULL</c> in a <c>NOT NULL</c> column and fail with
    /// <c>23502</c> — a column name and no context. The four checks that run first turn that into a
    /// named table and column. Four of these six columns carry no foreign key (see the finding recorded
    /// on the model), so nothing but this check is standing between a typo and a broken catalogue.
    /// </para>
    /// <para>
    /// <b>Why <c>brand_stations</c> gains <c>external_id</c>.</b> Its id was not an identity at all:
    /// <c>StationNodeCoordinates.DeriveId</c> builds it from the brand's code and the coordinates, which
    /// is what makes re-importing a network's АЗК file heal its rows instead of duplicating them. A
    /// surrogate id would silently turn every re-import into a second copy of the whole network, so the
    /// derived value moves to <c>external_id</c> and the unique index moves to
    /// <c>(brand_id, external_id)</c>. The old id values are copied verbatim, so an existing file that
    /// carries its own ids still matches.
    /// </para>
    /// </remarks>
    public partial class CatalogueBrandsBecomeUuid : Migration
    {
        /// <summary>
        /// The natural key that names each seeded network, matched to the uuid
        /// <c>BrandIds</c> hands out. Any row whose slug is not in this list gets a generated uuid.
        /// </summary>
        private const string SeededBrandIdMap = """
            WHEN 'okko' THEN '11111111-1111-4111-8111-111111111111'::uuid
            WHEN 'wog'  THEN '22222222-2222-4222-8222-222222222222'::uuid
            WHEN 'upg'  THEN '33333333-3333-4333-8333-333333333333'::uuid
            WHEN 'klo'  THEN '44444444-4444-4444-8444-444444444444'::uuid
            """;

        /// <summary>
        /// Every table with a <c>brand_id</c>, and whether the column is nullable. The orphan check
        /// walks exactly this list, so a seventh caller cannot be added without being checked.
        /// </summary>
        private static readonly (string Table, string Column)[] BrandForeignKeyColumns =
        [
            ("fuel_types", "brand_id"),
            ("fuel_offers", "brand_id"),
            ("fuel_offer_prices", "brand_id"),
            ("fuel_type_product_codes", "brand_id"),
            ("contracts", "brand_id"),
            ("brand_stations", "brand_id"),
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            GuardAgainstOrphanedBrandReferences(migrationBuilder);
            DropLengthChecksThatCannotSurviveAUuid(migrationBuilder);
            DropTheForeignKeysIntoBrands(migrationBuilder);
            GiveBrandsASlugBeforeLosingTheOneTheyWereKeyedBy(migrationBuilder);
            ConvertBrandsIdToUuid(migrationBuilder);

            // Before the foreign keys, not after: the index rebuild that closes
            // ConvertEveryBrandForeignKeyToUuid creates ux_brand_stations_brand_external on
            // (brand_id, external_id), and external_id is added by the next two steps. The station's
            // own columns do not depend on any brand_id, so they can safely go first.
            GiveBrandStationsAnExternalIdBeforeLosingTheOneTheyWereKeyedBy(migrationBuilder);
            ConvertBrandStationIdToUuid(migrationBuilder);

            ConvertEveryBrandForeignKeyToUuid(migrationBuilder);
            PutTheConstraintsBack(migrationBuilder);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of Up: children first, while brands.id is still the uuid they point at, then
            // brands itself. A uuid cannot be turned back into 'okko' — only the slug can — so this
            // reads the slug rather than inventing the inverse, and it never deletes a brand row, which
            // is the one thing a rollback of this must never do. (A scaffolded Down would.)
            foreach (var (name, table) in new[]
            {
                ("FK_brand_stations_brands_brand_id", "brand_stations"),
                ("FK_contracts_brands_brand_id", "contracts"),
            })
            {
                migrationBuilder.Sql($"""ALTER TABLE {table} DROP CONSTRAINT IF EXISTS "{name}";""");
            }

            foreach (var (table, column) in BrandForeignKeyColumns)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD COLUMN brand_id_old text;

                    UPDATE {table} AS child
                       SET brand_id_old = b.slug
                      FROM brands AS b
                     WHERE b.id = child.{column};

                    ALTER TABLE {table} DROP COLUMN {column};
                    ALTER TABLE {table} RENAME COLUMN brand_id_old TO {column};
                    """);
            }

            // The station id comes back from external_id, which is the derived value Up moved out of
            // it — there is no slug for a station to be looked up by.
            migrationBuilder.Sql("""
                ALTER TABLE brand_stations ADD COLUMN id_old text;
                UPDATE brand_stations SET id_old = external_id;
                ALTER TABLE brand_stations DROP COLUMN id;
                ALTER TABLE brand_stations RENAME COLUMN id_old TO id;
                ALTER TABLE brand_stations DROP COLUMN external_id;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE brands ADD COLUMN id_old text;
                UPDATE brands SET id_old = slug;
                ALTER TABLE brands DROP CONSTRAINT IF EXISTS ck_brands_slug_length;
                ALTER TABLE brands DROP COLUMN id;
                ALTER TABLE brands RENAME COLUMN id_old TO id;
                ALTER TABLE brands DROP COLUMN slug;
                """);

            PutTheConstraintsBackAsTextChecks(migrationBuilder);
        }

        /// <summary>
        /// Refuses to run when a <c>brand_id</c> names a brand that does not exist.
        /// </summary>
        /// <remarks>
        /// The backfills are joins, so an unmatched row ends up <c>NULL</c> in a column the next
        /// statement declares <c>NOT NULL</c>, and Postgres answers <c>23502 cannot insert a NULL into
        /// column brand_id</c> — which names a column and says nothing about which row or which table
        /// in a database with six of them. This says it up front, with the table and the count.
        /// <para>
        /// Four of the six columns carry no foreign key to <c>brands</c>. Nothing else in the schema is
        /// standing between a bad value here and a catalogue that silently stops matching a network.
        /// </para>
        /// </remarks>
        private static void GuardAgainstOrphanedBrandReferences(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column) in BrandForeignKeyColumns)
            {
                migrationBuilder.Sql($"""
                    DO $$
                    DECLARE orphans bigint;
                    BEGIN
                        SELECT count(*) INTO orphans
                          FROM {table} AS child
                          LEFT JOIN brands AS b ON b.id = child.{column}
                         WHERE child.{column} IS NOT NULL
                           AND b.id IS NULL;

                        IF orphans > 0 THEN
                            RAISE EXCEPTION
                                '{table}.{column} has % row(s) naming a brand that does not exist, so it cannot be re-pointed at a uuid. Fix the orphans before running this migration.', orphans;
                        END IF;
                    END $$;
                    """);
            }
        }

        /// <summary>
        /// Drops the <c>length(brand_id) &lt;= 100</c> family, which cannot be evaluated against a uuid.
        /// </summary>
        /// <remarks>
        /// Postgres resolves the constraint expression when the column type changes, and
        /// <c>length(uuid)</c> does not exist — so leaving them in place turns a clean data migration
        /// into a DDL error naming a function. They are re-added in
        /// <see cref="PutTheConstraintsBack"/>, against the slug, which is the column that still
        /// deserves a bound.
        /// </remarks>
        private static void DropLengthChecksThatCannotSurviveAUuid(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, table) in new[]
            {
                ("ck_brands_id_length", "brands"),
                ("ck_brand_stations_id_length", "brand_stations"),
                ("ck_fuel_types_brand_id_length", "fuel_types"),
                ("ck_fuel_offers_brand_id_length", "fuel_offers"),
                ("ck_fuel_offer_prices_brand_id_length", "fuel_offer_prices"),
                ("ck_fuel_type_product_codes_brand_id_length", "fuel_type_product_codes"),
                ("ck_contracts_brand_id_length", "contracts"),
                ("ck_brand_stations_brand_id_length", "brand_stations"),
            })
            {
                migrationBuilder.Sql($"""ALTER TABLE {table} DROP CONSTRAINT IF EXISTS "{name}";""");
            }
        }

        /// <summary>
        /// Drops both foreign keys that point at <c>brands</c>: the one on <c>brand_stations</c> and
        /// the one on <c>contracts</c>.
        /// </summary>
        /// <remarks>
        /// Two, not one, and both were found by asking the running schema rather than by reading the
        /// entity configurations: <c>pg_constraint</c> is the authority, and a configuration can name
        /// a relationship the model never had. Postgres refuses to change the type of a column a
        /// foreign key references, and refuses to re-point the referencing side while the constraint
        /// stands, so both have to go before <c>brands.id</c> is altered.
        /// <para>
        /// <b>The names are quoted, and that is not decoration.</b> EF stores these two in the database
        /// with their original capitals (<c>quote_ident</c> returns
        /// <c>"FK_contracts_brands_brand_id"</c>), while an unquoted identifier in SQL folds to lower
        /// case and matches nothing. <c>DROP CONSTRAINT IF EXISTS</c> then skips silently, the next
        /// statement fails with <c>cannot be implemented</c>, and the error names the constraint rather
        /// than the quoting mistake that caused it. Quoting is what makes this step real rather than
        /// advisory.
        /// </para>
        /// <para>
        /// The other four columns carry no constraint at all, which is why
        /// <see cref="GuardAgainstOrphanedBrandReferences"/> exists: nothing else would notice a value
        /// that names no brand until a query silently stopped matching.
        /// </para>
        /// </remarks>
        private static void DropTheForeignKeysIntoBrands(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, table) in new[]
            {
                ("FK_brand_stations_brands_brand_id", "brand_stations"),
                ("FK_contracts_brands_brand_id", "contracts"),
            })
            {
                migrationBuilder.Sql($"""ALTER TABLE {table} DROP CONSTRAINT IF EXISTS "{name}";""");
            }
        }

        /// <summary>
        /// Copies the text id into a new <c>slug</c> column, before the id it names is altered away.
        /// </summary>
        /// <remarks>
        /// Nullable and unfilled at first so the copy is a single statement rather than a rewrite, and
        /// so the unique index is created after the copy rather than before. The CHECK is not added
        /// here either: it is added with everything else, in <see cref="PutTheConstraintsBack"/>.
        /// </remarks>
        private static void GiveBrandsASlugBeforeLosingTheOneTheyWereKeyedBy(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE brands ADD COLUMN slug varchar(100);");
            migrationBuilder.Sql("UPDATE brands SET slug = id;");
        }

        /// <summary>
        /// Re-points <c>brands.id</c> at a uuid, one row at a time through the slug it just gained.
        /// </summary>
        /// <remarks>
        /// <c>USING</c> is mandatory: Postgres has no implicit <c>text</c>-to-<c>uuid</c> cast, and a
        /// migration without one fails with <c>column cannot be cast automatically</c>. The expression
        /// is a <c>CASE</c> rather than <c>id::uuid</c> so the four seeded networks land on the uuids
        /// <c>BrandIds</c> states — see the class remark on why those are fixed values.
        /// </remarks>
        private static void ConvertBrandsIdToUuid(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE brands
                    ALTER COLUMN id TYPE uuid
                    USING (CASE slug
                    {SeededBrandIdMap}
                    ELSE gen_random_uuid()
                    END);
                """);

            migrationBuilder.Sql("ALTER TABLE brands ALTER COLUMN slug SET NOT NULL;");
        }

        /// <summary>
        /// Re-points every <c>brand_id</c> at the same uuid its brand now has.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Add, copy, drop, rename — not <c>ALTER COLUMN ... TYPE</c>.</b> The obvious one-liner
        /// <c>ALTER ... TYPE uuid USING (SELECT ... FROM brands ...)</c> is not valid Postgres: the
        /// transform expression cannot contain a subquery, and the answer is
        /// <c>ERROR: cannot use subquery in transform expression</c>. Verified against 16.15 while
        /// writing this, which is the only reason it is stated rather than assumed.
        /// </para>
        /// <para>
        /// The price is that dropping a column takes its indexes with it, so they are recreated by
        /// hand below. That is a real cost and the reason the list is a table rather than a loop —
        /// <see cref="BrandForeignKeyColumns"/> carries, per table, exactly the indexes the model
        /// declares on its <c>brand_id</c>, so a column that gains one cannot be added here without
        /// this file being told. <c>MigrationDiscoveryTests</c> is the backstop for a name that drifts.
        /// </para>
        /// </remarks>
        private static void ConvertEveryBrandForeignKeyToUuid(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column) in BrandForeignKeyColumns)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE {table} ADD COLUMN brand_id_new uuid;

                    UPDATE {table} AS child
                       SET brand_id_new = b.id
                      FROM brands AS b
                     WHERE b.slug = child.{column};

                    ALTER TABLE {table} DROP COLUMN {column};
                    ALTER TABLE {table} RENAME COLUMN brand_id_new TO {column};

                    -- The replacement column is born nullable, and renaming does not change that, so
                    -- without this every brand_id would quietly stop matching the model — which says
                    -- required — and nothing would object until a later migration diffed the snapshot.
                    ALTER TABLE {table} ALTER COLUMN {column} SET NOT NULL;
                    """);
            }

            RecreateTheIndexesThatTheDroppedColumnsTookWithThem(migrationBuilder);
        }

        /// <summary>
        /// The indexes this migration brings into existence, and nothing else.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only indexes on columns this migration <b>adds</b> are created here. Every index on an
        /// existing <c>brand_id</c> column survives the <c>ALTER COLUMN ... TYPE uuid</c> untouched —
        /// PostgreSQL keeps an index across a type change when the operator class still applies — so
        /// recreating them would be wrong twice over: it would leave the originals in place under
        /// their existing names <em>and</em> add duplicates under EF's, and the older migrations
        /// that later <c>Down</c> expect those originals by name
        /// (<c>20261018140426_StationsBecomeBrands</c> renames <c>IX_fuel_types_brand_id</c> back to
        /// <c>IX_fuel_types_station_id</c>). A rolling back past this migration would abort on a
        /// relation it can no longer find.
        /// </para>
        /// <para>
        /// Names, column orders, uniqueness and partial filters match the entity configurations
        /// (<c>FuelTypeEntityConfiguration</c>, <c>FuelOfferConfiguration</c>,
        /// <c>FuelTypeProductCodeConfiguration</c>, <c>BrandStationConfiguration</c>). A unique index
        /// rebuilt without its filter is a different index: <c>ux_fuel_types_brand_name</c> exists so
        /// two live fuels cannot share a name at one network, and without <c>NOT is_deleted</c> it would
        /// forbid two archived ones as well.
        /// </para>
        /// </remarks>
        private static void RecreateTheIndexesThatTheDroppedColumnsTookWithThem(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_brands_slug" ON brands (slug);

                CREATE UNIQUE INDEX IF NOT EXISTS ux_brand_stations_brand_external
                    ON brand_stations (brand_id, external_id);
                """);
        }

        /// <summary>
        /// Copies each station's derived id into <c>external_id</c>, before the id it holds is altered.
        /// </summary>
        /// <remarks>
        /// This is the whole reason the station key is split — see the class remark. The copy is
        /// verbatim, so a source file that carries its own <c>external_id</c> still matches the rows a
        /// previous import created.
        /// </remarks>
        private static void GiveBrandStationsAnExternalIdBeforeLosingTheOneTheyWereKeyedBy(
            MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE brand_stations ADD COLUMN external_id varchar(100);");
            migrationBuilder.Sql("UPDATE brand_stations SET external_id = id;");
        }

        /// <summary>
        /// Gives each station a surrogate id. Nothing outside the database quotes one.
        /// </summary>
        /// <remarks>
        /// Unlike the brand ids, these are generated rather than fixed: a station is created by an
        /// operator or an import, both of which read the id back from the response, and there is no
        /// seeded station (the four demo АЗК were removed in <c>RemoveSeedStationNodes</c>). So there
        /// is no value for a fixture to agree with, and inventing four would be noise.
        /// </remarks>
        private static void ConvertBrandStationIdToUuid(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE brand_stations ALTER COLUMN id TYPE uuid USING gen_random_uuid();");
            migrationBuilder.Sql("ALTER TABLE brand_stations ALTER COLUMN external_id SET NOT NULL;");
        }

        /// <summary>
        /// Re-adds what was dropped: the foreign key, the bounds, and the unique index.
        /// </summary>
        /// <remarks>
        /// <c>ck_brands_slug_length</c> replaces <c>ck_brands_id_length</c> — the bound moves from the
        /// key to the slug, which is the column a human still types and the one that could grow into a
        /// paragraph. <c>brands.slug</c> is unique, so two networks cannot claim one code; the model
        /// states it as a unique index and this recreates exactly that.
        /// </remarks>
        private static void PutTheConstraintsBack(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE brands ADD CONSTRAINT ck_brands_slug_length CHECK (length(slug) <= 100);

                ALTER TABLE brand_stations
                    ADD CONSTRAINT "FK_brand_stations_brands_brand_id"
                    FOREIGN KEY (brand_id) REFERENCES brands (id) ON DELETE RESTRICT;

                ALTER TABLE contracts
                    ADD CONSTRAINT "FK_contracts_brands_brand_id"
                    FOREIGN KEY (brand_id) REFERENCES brands (id) ON DELETE RESTRICT;
                """);
        }

        /// <summary>
        /// The bounds and indexes as they were, against a text key again. Used only by <see cref="Down"/>.
        /// </summary>
        /// <remarks>
        /// Only the constraints this migration dropped are put back.
        /// <c>ck_brands_name_length</c> is deliberately absent: nothing here ever removed it, and
        /// re-adding an existing constraint fails with <c>42710</c> — which aborts the whole
        /// rollback, so the list has to be "what Up took" rather than "what the schema should have".
        /// </para>
        /// <para>
        /// The two primary keys <em>are</em> here, and their absence is not cosmetic: a primary key is
        /// attached to the column it constrains, so dropping <c>brands.id</c> and
        /// <c>brand_stations.id</c> took <c>PK_brands</c> and <c>PK_brand_stations</c> with them.
        /// Re-adding a foreign key onto a referenced table with no unique constraint fails with
        /// <c>42830 there is no unique constraint matching given keys</c> — the rollback would abort
        /// with both the data restored and the schema still uuid, which is the worst of the two.
        /// </para>
        /// </remarks>
        private static void PutTheConstraintsBackAsTextChecks(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE brands ADD CONSTRAINT PK_brands PRIMARY KEY (id);
                ALTER TABLE brand_stations ADD CONSTRAINT PK_brand_stations PRIMARY KEY (id);

                ALTER TABLE brands ADD CONSTRAINT ck_brands_id_length CHECK (length(id) <= 100);

                ALTER TABLE brand_stations ADD CONSTRAINT ck_brand_stations_id_length CHECK (length(id) <= 100);
                ALTER TABLE brand_stations ADD CONSTRAINT ck_brand_stations_brand_id_length CHECK (length(brand_id) <= 100);
                ALTER TABLE fuel_types ADD CONSTRAINT ck_fuel_types_brand_id_length CHECK (length(brand_id) <= 100);
                ALTER TABLE fuel_offers ADD CONSTRAINT ck_fuel_offers_brand_id_length CHECK (length(brand_id) <= 100);
                ALTER TABLE fuel_offer_prices ADD CONSTRAINT ck_fuel_offer_prices_brand_id_length CHECK (length(brand_id) <= 100);
                ALTER TABLE fuel_type_product_codes ADD CONSTRAINT ck_fuel_type_product_codes_brand_id_length CHECK (length(brand_id) <= 100);
                ALTER TABLE contracts ADD CONSTRAINT ck_contracts_brand_id_length CHECK (length(brand_id) <= 100);

                -- No index is recreated here either. Every index this migration is about sits on a
                -- brand_id column, which is altered rather than dropped, and PostgreSQL keeps an
                -- index across a type change; recreating it would leave the original in place and
                -- add a second one under another name - which is what made rolling back past this
                -- migration abort inside StationsBecomeBrands.Down, which renames those originals
                -- back by name. The one index genuinely born here rides the external id column,
                -- which Down drops, and is deliberately not restored.

                ALTER TABLE brand_stations
                    ADD CONSTRAINT "FK_brand_stations_brands_brand_id"
                    FOREIGN KEY (brand_id) REFERENCES brands (id) ON DELETE RESTRICT;
                ALTER TABLE contracts
                    ADD CONSTRAINT "FK_contracts_brands_brand_id"
                    FOREIGN KEY (brand_id) REFERENCES brands (id) ON DELETE RESTRICT;
                """);
        }
    }
}
