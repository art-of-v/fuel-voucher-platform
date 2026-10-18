using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <see cref="DataKindMigrationTests"/> checks the shape of the schema after <c>EnforceDataKind</c>
/// runs. This class checks the part that shape cannot show: what the migration does to rows that
/// were <b>already there</b>.
/// <para>
/// The fixture builds its template by replaying every migration from zero, so the database is
/// essentially empty by the time <c>EnforceDataKind</c> applies — 30 seeded packages and no vouchers
/// at all. Production is the opposite: 586 vouchers and 116 packages, none of them in
/// <c>SeedData</c>. A migration that copies nothing passes cleanly there and then destroys the entire
/// QA catalogue on the next deploy.
/// </para>
/// <para>
/// So these tests roll the schema back past the migration, insert rows through the OLD
/// <c>is_test_data</c> column, and roll forward. That is the production sequence exactly, and it is
/// the only way to observe the copy.
/// </para>
/// <para>
/// Its own <see cref="IClassFixture{T}"/>, deliberately: migrating backwards leaves the schema
/// without <c>data_kind</c> for the duration, and no other test in this class's database may observe
/// that state. Every test returns the schema to head in a <c>finally</c> — otherwise one failure
/// cascades into the next, and the second failure would be reported against innocent code.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class DataKindBackfillTests : IClassFixture<TestDatabaseFixture>
{
    /// <summary>
    /// The migration immediately before <c>EnforceDataKind</c> — the newest schema that still has
    /// <c>is_test_data</c> and not <c>data_kind</c>. Rolling back to the last one rather than to zero
    /// keeps each test to two statements instead of replaying ninety migrations.
    /// </summary>
    private const string BeforeDataKind = "20261014225424_AddOrderHistoryAndPricingBasis";

    private readonly TestDatabaseFixture _fixture;

    public DataKindBackfillTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RowsThatAlreadyExisted_KeepTheirValue_AndTheirValueIsCopied()
    {
        var qaVoucher = Guid.NewGuid();
        var realVoucher = Guid.NewGuid();
        var qaPackage = $"bk-keep-qa-{Guid.NewGuid():N}";
        var realPackage = $"bk-keep-real-{Guid.NewGuid():N}";

        await WithSchemaFrom(BeforeDataKind, async context =>
        {
            // Written the way production wrote them: through is_test_data, and not one of them is in
            // SeedData.FuelPackages. These are exactly the rows the scaffolded migration would have
            // left sitting on ''.
            //
            // 'okko-0001-bk95' rather than 'okko-95': the offer key is now the fuel type id alone
            // (DropTheStationPrefixFromOfferKeys), so an offer for 'okko-95' at a second station
            // beside the seeded one is no longer representable - and this test rolls the schema BACK
            // and forward, so it walks through that migration and would hit its collision guard. The
            // station/fuel pair is invented either way, and inventing one that survives the re-key is
            // what this line is about.
            //
            // Parameters are unquoted on purpose - a placeholder inside '' stays a string literal
            // and the cast to uuid fails with 22P02 on the parameter itself.
#pragma warning disable EF1002 // identifiers and values, not SQL fragments; all from this file
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO fuel_vouchers (id, brand, fuel_type_id, liters, supplier_expiration_date,
                                           customer_expiration_date, voucher_number, qr_payload,
                                           created_at_utc, updated_at_utc, status, is_test_data, is_deleted)
                VALUES
                  ({0}, 'okko', 'okko-95', 10, '2027-01-01', '2027-01-01', 'BK-KEEP-QA', 'BK-KEEP-QA-QR',
                   now(), now(), 'Available', true, false),
                  ({1}, 'okko', 'okko-95', 20, '2027-01-01', '2027-01-01', 'BK-KEEP-REAL', 'BK-KEEP-REAL-QR',
                   now(), now(), 'Available', false, false)
                """, qaVoucher, realVoucher);

            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO fuel_types (id, name, station_id, base_price, discount_price,
                                        created_at_utc, updated_at_utc, is_deleted)
                VALUES ('okko-0001-bk95', '95', 'okko-0001', 500, 520, now(), now(), false)
                ON CONFLICT (id) DO NOTHING
                """);

            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO fuel_packages (id, station_id, fuel_type_id, fuel_name, liters, price,
                                           original_price, created_at_utc, updated_at_utc,
                                           is_test_data, is_deleted)
                VALUES
                  ({0}, 'okko-0001', 'okko-0001-bk95', '95 (QA)', 10, 100, 120, now(), now(), true, false),
                  ({1}, 'okko-0001', 'okko-0001-bk95', '95', 20, 500, 520, now(), now(), false, false)
                """, qaPackage, realPackage);
#pragma warning restore EF1002
        });

        await using var verify = CreateContext();

        // Row count first. The failure mode is not "wrong value" but "no row", and a count assertion
        // says so in a way a value assertion cannot.
        (await CountAsync(verify, "SELECT count(*) FROM fuel_vouchers WHERE voucher_number LIKE 'BK-KEEP-%'"))
            .Should().Be(2, "the old column is dropped only after the new one has been filled from it");

        (await CountAsync(verify, "SELECT count(*) FROM fuel_offer_volumes WHERE id LIKE 'bk-keep-%'"))
            .Should().Be(2);

        // One string column rather than a two-column projection: Npgsql will not map a composite type out
        // of a query at all - "not supported for fields having DataTypeName 'uuid'", and then the
        // same complaint about 'text' for a ValueTuple<string,string>. Concatenating in SQL keeps the
        // correlation between the row and its value, which is the part worth asserting.
        var voucherKinds = await ReadKeysAsync(verify, "fuel_vouchers", "voucher_number", "'BK-KEEP-%'");
        voucherKinds.Should().BeEquivalentTo(new[]
        {
            "BK-KEEP-QA|qa",      // flagged before, QA after
            "BK-KEEP-REAL|real"  // unflagged before, real after
        });

        // Read from fuel_offer_volumes, not fuel_packages. The rows are still seeded into the old
        // table - it is the only shape that exists at BeforeDataKind - but the split gave each
        // volume the id of the package row it replaced, so the value is asserted where it now
        // lives. That makes this test cover two migrations rather than one: the boolean-to-name
        // rename AND the collapse into a volume.
        var volumeKinds = await ReadVolumeKindsAsync(verify, "'bk-keep-%'");
        volumeKinds.Should().BeEquivalentTo(new[]
        {
            $"{qaPackage}|qa",
            $"{realPackage}|real"
        });
    }

    [Fact]
    public async Task Soft_Deleted_Rows_Are_Copied_Too()
    {
        // is_deleted is orthogonal to the pool: a tombstone is still a row with provenance, and a
        // restore (UnarchiveVoucher) brings it back. Losing the flag on the way through would turn an
        // archived QA voucher into an archived REAL voucher, which is the one direction this whole
        // refactor exists to prevent.
        var archived = Guid.NewGuid();

        await WithSchemaFrom(BeforeDataKind, async context =>
        {
#pragma warning disable EF1002
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO fuel_vouchers (id, brand, fuel_type_id, liters, supplier_expiration_date,
                                           customer_expiration_date, voucher_number, qr_payload,
                                           created_at_utc, updated_at_utc, status, is_test_data, is_deleted)
                VALUES ({0}, 'okko', 'okko-95', 10, '2027-01-01', '2027-01-01', 'BK-ARCHIVED', 'BK-ARCH-QR',
                        now(), now(), 'Expired', true, true)
                """, archived);
#pragma warning restore EF1002
        });

        await using var verify = CreateContext();

        var kind = await verify.Database
            .SqlQueryRaw<string>(
                """
                SELECT data_kind AS "Value"
                FROM fuel_vouchers
                WHERE voucher_number = 'BK-ARCHIVED'
                """).ToListAsync();

        kind.Should().ContainSingle().Which.Should().Be("qa",
            "a tombstone is still a row with provenance, and a restore brings it back");
    }

    [Fact]
    public async Task Rolling_Back_Restores_The_Boolean_With_The_Right_Polarity()
    {
        // Down() is the same operation run backwards, and the naive version of it writes every row
        // false - which would silently un-flag the whole QA catalogue the first time anybody rolled
        // this back. Rehearsing the round trip is what makes Down() trustworthy: it is the one half of
        // a hand-written migration that never runs in production, and therefore never tested by it.
        var qaVoucher = Guid.NewGuid();

        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();

#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_vouchers (id, brand, fuel_type_id, liters, supplier_expiration_date,
                                       customer_expiration_date, voucher_number, qr_payload, qr_payload_sha256,
                                       created_at_utc, updated_at_utc, status, data_kind, is_deleted)
            VALUES ({0}, 'okko', 'okko-95', 10, '2027-01-01', '2027-01-01', 'BK-ROUND', 'BK-ROUND-QR', sha256(convert_to('BK-ROUND-QR','UTF8')),
                    now(), now(), 'Available', 'qa', false)
            """, qaVoucher);
#pragma warning restore EF1002

        try
        {
            await migrator.MigrateAsync(BeforeDataKind);

            var flag = await context.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT is_test_data
                    FROM fuel_vouchers
                    WHERE voucher_number = 'BK-ROUND'
                    """).ToListAsync();

            flag.Should().ContainSingle().Which.Should().BeTrue("data_kind = 'qa' means is_test_data = true");
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> against the schema as it stood at <paramref name="migration"/>,
    /// then brings the database back to head whatever happens. The body therefore sees the old
    /// columns and the assertions that follow see the new ones.
    /// </summary>
    private async Task WithSchemaFrom(string migration, Func<ApplicationDbContext, Task> body)
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(migration);
            await body(context);
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// A single count, as a number. <c>ExecuteSqlRawAsync</c> would return rows-affected (-1 for a
    /// SELECT), which compares equal to nothing and passes against any row count at all.
    /// </summary>
    private static async Task<int> CountAsync(ApplicationDbContext context, string sql)
    {
        var result = await context.Database.SqlQueryRaw<int>(sql).ToListAsync();
        return result.Single();
    }

    /// <summary>"key|kind" for every row matching the filter — see the note on the caller.</summary>
    /// <summary>
    /// "volumeId|pool" for every matching volume. A join, because <c>data_kind</c> is a property of
    /// the OFFER: reading it off the volume is not a shorter query, it is a wrong one.
    /// </summary>
    private static async Task<List<string>> ReadVolumeKindsAsync(
        ApplicationDbContext context,
        string filter)
    {
        var sql = $"""
            SELECT v.id || '|' || o.data_kind
            FROM fuel_offer_volumes v
            JOIN fuel_offers o ON o.id = v.offer_id
            WHERE v.id LIKE {filter}
            """;
        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }

    private static async Task<List<string>> ReadKeysAsync(
        ApplicationDbContext context,
        string table,
        string keyColumn,
        string filter)
    {
        var sql = $"SELECT {keyColumn} || '|' || data_kind FROM {table} WHERE {keyColumn} LIKE {filter}";
        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }
}
