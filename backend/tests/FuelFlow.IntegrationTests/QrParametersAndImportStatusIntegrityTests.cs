using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Two guarantees that the schema only claimed to have.
/// </summary>
/// <remarks>
/// <para>
/// <c>qr_parameters</c>'s unique index covered <c>(ecc_level, version, mask_pattern, encoding_mode)</c>
/// and three of those four columns are nullable. Postgres treats NULLs as distinct in a plain unique
/// btree, so the index could not reject <c>('M', NULL, NULL, NULL)</c> - which is precisely the shape
/// F-05's leak produced, one junk row per import whose PDF had an unreadable ECC level. F-05 stopped
/// the leak and left an index that never could have caught it.
/// </para>
/// <para>
/// <c>voucher_imports.status</c> was the only status column in the schema that was free text, on the
/// table whose status gates the whole intake.
/// </para>
/// <para>
/// Both run against a real PostgreSQL: the in-memory provider ignores unique indexes and CHECK
/// constraints entirely, so a test of these against it asserts nothing.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class QrParametersAndImportStatusIntegrityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public QrParametersAndImportStatusIntegrityTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The whole point. Two rows that differ only in their nullable columns are the same
    /// configuration, and before this index could not tell.
    /// </summary>
    /// <remarks>
    /// <b>The nulls have to be real.</b> A first version of this test gave both rows
    /// <c>version = 11</c> and passed - against any unique index, because a genuine duplicate on all
    /// four columns is refused by a plain btree too. It proved nothing about NULLs NOT DISTINCT. The
    /// shape has to be the one F-05's leak actually produced: <c>('M', NULL, NULL, NULL)</c> and
    /// nothing else, which an ordinary btree accepts twice.
    /// </remarks>
    [Fact]
    public async Task TwoRowsDifferingOnlyInNullColumns_AreTheSameConfiguration()
    {
        await using var context = CreateContext();
        context.QrParameters.Add(Qr("M"));
        await context.SaveChangesAsync();

        context.QrParameters.Add(Qr("M"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "this index is what stops one junk row per import, and NULLS NOT DISTINCT is what makes it able to");
    }

    [Fact]
    public async Task GenuinelyDifferentConfigurations_Coexist()
    {
        await using var context = CreateContext();

        context.QrParameters.Add(Qr("M", version: 12));
        context.QrParameters.Add(Qr("Q", version: 12));
        context.QrParameters.Add(Qr("M", version: 13));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("")]
    [InlineData("l")]
    public async Task AnEccLevelOutsideTheStandard_IsRefused(string ecc)
    {
        await using var context = CreateContext();
        context.QrParameters.Add(Qr(ecc, version: 14));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the value comes from parsing a supplier PDF and would render a QR no scanner reads");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    [InlineData(-1)]
    public async Task AVersionOutsideTheQrStandard_IsRefused(int version)
    {
        await using var context = CreateContext();
        // The version is used verbatim: every case here fails the CHECK, so nothing is committed and
        // there is no collision to avoid.
        context.QrParameters.Add(Qr("M", version: version));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("the QR standard defines versions 1 to 40");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(-1)]
    public async Task AMaskPatternOutsideTheStandard_IsRefused(int mask)
    {
        await using var context = CreateContext();
        context.QrParameters.Add(Qr("M", version: 16, maskPattern: mask));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("the standard defines eight masks, 0 to 7");
    }

    [Fact]
    public async Task AnUnrecognisedImportStatus_IsRefused()
    {
        await using var context = CreateContext();
        context.VoucherImports.Add(Import("Started"));
        await context.SaveChangesAsync();

        context.VoucherImports.Add(Import("HalfwayThroughSomethingNoOneDefined"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "a typo here is an import invisible to whatever reads the column, and it will never be picked up again");
    }

    [Fact]
    public async Task AFinishedImportWithoutAFinishTime_IsRefused()
    {
        await using var context = CreateContext();
        var import = Import("Completed");
        import.CompletedAtUtc = null;
        context.VoucherImports.Add(import);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the counters on this row are what an operator reads to decide whether to re-run");
    }

    [Fact]
    public async Task ARunningImportWithAFinishTime_IsRefused()
    {
        await using var context = CreateContext();
        var import = Import("Started");
        import.CompletedAtUtc = TestClock.UtcNow;
        context.VoucherImports.Add(import);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

/// <summary>
    /// The behavioural test above proves the index refuses the duplicate. This pins <em>why</em>, so a
    /// later migration that recreates the index the plain way fails here rather than silently
    /// restoring the defect — which is the shape of the F-05 fix itself: it stopped the leak and left
    /// the index that never could have caught it.
    /// </summary>
    /// <remarks>
    /// <c>indnullsnotdistinct</c> reads as the opposite of what it sounds like, which is worth stating
    /// because getting it backwards means declaring a correct index broken: it is <b>true</b> for an
    /// index created <c>NULLS NOT DISTINCT</c> and <b>false</b> for an ordinary unique btree. Measured
    /// directly on PG16 with both indexes on one table.
    /// </remarks>
    [Fact]
    public async Task TheUniqueIndex_ReallyIsNullsNotDistinct()
    {
        await using var context = CreateContext();

        // Found by its COLUMNS, not by its name. The table also carries its primary key, which is
        // a plain unique btree (indnullsnotdistinct = false) and would otherwise be counted here
        // and read as a failure. Naming the index instead would make this test a hostage to the
        // index naming convention - and it was: a rename of every EF-named index in the schema
        // turned this into a failure with nothing about the database having changed.
        var flags = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT i.indnullsnotdistinct::text
                  FROM pg_index i
                 WHERE i.indrelid = 'qr_parameters'::regclass
                   AND i.indisunique
                   AND NOT i.indisprimary
                """)
            .ToListAsync();

        flags.Should().ContainSingle("the QR-configuration index is the only unique one that is not the primary key");
        flags[0].Should().Be("true",
            "indnullsnotdistinct = TRUE is NULLS NOT DISTINCT; false is the plain btree that let the duplicates through");
    }

    // ---------------------------------------------------------------- fixtures

    private static QrParameters Qr(string ecc, int? version = null, int? maskPattern = null) => new()
    {
        Id = Guid.CreateVersion7(),
        EccLevel = ecc,
        Version = version,
        MaskPattern = maskPattern,
        CreatedAtUtc = TestClock.UtcNow
    };

    private static VoucherImport Import(string status) => new()
    {
        Id = Guid.CreateVersion7(),
        FileName = $"{Guid.NewGuid():N}.pdf",
        PageCount = 1,
        Status = status,
        StartedAtUtc = TestClock.UtcNow,
        CompletedAtUtc = status == "Started" ? null : TestClock.UtcNow
    };
}