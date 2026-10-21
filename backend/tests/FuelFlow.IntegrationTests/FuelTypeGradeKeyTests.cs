using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_types.grade_key</c> — the grade's key, the name folded onto one spelling — and the
/// unique index that refuses two live rows of one brand that share it. Planning #349: the owner's
/// decision (2026-10-21) that "А-95" and "A-95" are one grade, and the catalogue says so once.
/// </summary>
/// <remarks>
/// <para>
/// The database half of the guarantee belongs here rather than in a unit test, because the whole
/// point is an index: <c>ux_fuel_types_brand_grade_key</c> refuses two live fuels claiming one
/// grade at one brand, and nothing in C# has to be trusted to say so.
/// </para>
/// <para>
/// The other half is the agreement: the database computes the key with
/// <c>fuelflow_fuel_grade_key(name)</c> and the application computes it with
/// <c>SharedKernel.Domain.FuelGrade</c>, and the two have to say the same thing — a constraint that
/// computes a different key from the one the application believes in would refuse a fuel the
/// application believes is a different grade. That is what
/// <see cref="TheDatabase_AndTheApplication_SayTheSameThing"/> is for.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FuelTypeGradeKeyTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelTypeGradeKeyTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static FuelTypeEntity Fuel(string id, Guid brandId, string name)
        => new()
        {
            Id = ToUuid(id),
            BrandId = brandId,
            Name = name,
            BasePrice = 50m,
            DiscountPrice = 48m,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    [Fact]
    public async Task TwoLiveFuels_CannotClaimTheSameGradeAtOneBrand()
    {
        // The owner's case, in the database: "А-95" and "A-95" are one grade, and the second row
        // is a second price on the shelf and a place a voucher can be priced against.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("grade-a", BrandIds.Okko, "А-95 (grade test)"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.FuelTypes.Add(Fuel("grade-b", BrandIds.Okko, "A-95 (grade test)"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM fuel_types WHERE id IN ('{ToUuid("grade-a")}', '{ToUuid("grade-b")}')");
    }

    [Fact]
    public async Task TheSameGradeAtTwoBrands_IsNotADuplicate()
    {
        // "A-95" is OKKO's spelling and UPG's. The key identifies a fuel *within* a brand, which
        // is why the index leads with brand_id — a global unique on the key would be wrong.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("grade-net-a", BrandIds.Okko, "A-95 (grade test)"));
        context.FuelTypes.Add(Fuel("grade-net-b", BrandIds.Upg, "A-95 (grade test)"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM fuel_types WHERE id IN ('{ToUuid("grade-net-a")}', '{ToUuid("grade-net-b")}')");
    }

    [Fact]
    public async Task TwoGradesThatAreNotTheSame_AreBothAllowed()
    {
        // The dangerous direction: a key that merged two grades would refuse a fuel the network
        // really sells, and the refusal would name neither of them.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("grade-92", BrandIds.Klo, "А 92 (grade test)"));
        context.FuelTypes.Add(Fuel("grade-95", BrandIds.Klo, "А 95 (grade test)"));
        context.FuelTypes.Add(Fuel("grade-95plus", BrandIds.Klo, "А 95+ (grade test)"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM fuel_types WHERE id IN ('{ToUuid("grade-92")}', '{ToUuid("grade-95")}', '{ToUuid("grade-95plus")}')");
    }

    [Fact]
    public async Task AnArchivedFuel_FreesItsGrade()
    {
        // Same shape as ux_fuel_types_brand_name: the index is partial on NOT is_deleted, so taking
        // a fuel out of the catalogue releases its grade for the fuel that replaces it.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("grade-arch-a", BrandIds.Wog, "ДП (grade reuse test)"));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE fuel_types SET is_deleted = true, deleted_at_utc = now() WHERE id = '{ToUuid("grade-arch-a")}'");

        context.ChangeTracker.Clear();
        context.FuelTypes.Add(Fuel("grade-arch-b", BrandIds.Wog, "ДП (grade reuse test)"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM fuel_types WHERE id IN ('{ToUuid("grade-arch-a")}', '{ToUuid("grade-arch-b")}')");
    }

    [Fact]
    public async Task TwoNamesThatSayNoGrade_DoNotCollide()
    {
        // The reason the index excludes NULL rather than treating every absent grade as one value:
        // a name of punctuation says no grade, and two of them at one brand are still two rows.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("grade-none-a", BrandIds.Upg, "---"));
        context.FuelTypes.Add(Fuel("grade-none-b", BrandIds.Upg, "..."));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM fuel_types WHERE id IN ('{ToUuid("grade-none-a")}', '{ToUuid("grade-none-b")}')");
    }

    /// <summary>
    /// The constraint and the classifier must agree, and the constraint and the application must
    /// agree. The database computes the key with <c>fuelflow_fuel_grade_key</c> and the application
    /// computes it with <c>FuelGrade.Key</c>; a difference between them would be a constraint that
    /// refuses a fuel the application believes is a different grade.
    /// </summary>
    [Fact]
    public async Task TheDatabase_AndTheApplication_SayTheSameThing()
    {
        await using var context = CreateContext();

        // Every name the catalogue holds, live and archived, plus the spellings one grade is
        // written in.
        var names = await context.FuelTypes
            .IgnoreQueryFilters()
            .Select(f => f.Name)
            .ToListAsync();

        names.AddRange(
        [
            "А-95", "A-95", "а 95", "А95", "ДП", "DP", "дп",
            "Пульс 95", "Pulls 95", "пульс95", "Пропан", "propan",
            "Газ", "ГАЗ", "LPG", "ДП ЄВРО", "ДП Euro", "А95 ЄВРО",
            "ДП Mustang+", "95 Євро5-Е10", "100 Mustang Євро5-Е0",
            "upg95", "upg100", "upgDIESEL", "EURO DIESEL", "AdBlue",
            "---", "...", "   "
        ]);

        foreach (var name in names.Distinct())
        {
            var sqlKey = await ScalarAsync(
                "SELECT COALESCE(fuelflow_fuel_grade_key(@name), '')",
                ("name", name));

            sqlKey.Should().Be(FuelGrade.Key(name) ?? "",
                $"'{name}' must fold onto the same key in the database and in the application");
        }
    }

    [Fact]
    public async Task TheColumn_IsTheDatabaseComputingIt_NotTheApplication()
    {
        // The column is GENERATED, so it is the same for every row whatever wrote it — a column the
        // application writes is a column a path can forget, and a path that forgets writes a twin.
        var generated = await ScalarAsync(
            """
            SELECT CASE a.attgenerated WHEN 's' THEN 'stored' ELSE a.attgenerated::text END
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            WHERE c.relname = 'fuel_types' AND a.attname = 'grade_key'
            """);

        generated.Should().NotBeNull("the column is not there at all");
        generated.Should().Be("stored",
            "a column the application writes is a column a path can forget");
    }

    /// <summary>
    /// One scalar, read over a plain connection: the queries here ask the catalog and the function
    /// what they are, and the catalog's types are not the ones a mapping expects.
    /// </summary>
    private async Task<string?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    /// <summary>
    /// The preflight asks the same question as the index, thirty seconds before a deploy instead of
    /// in the middle of one.
    /// </summary>
    /// <remarks>
    /// The file writes the rule out rather than calling <c>fuelflow_fuel_grade_key</c> — the function
    /// arrives with the migration that adds the column — so this is what proves the two agree: if
    /// the copy drifted, the preflight would report "ok" over a twin the deploy would refuse.
    /// </remarks>
    [Fact]
    public async Task ThePreflight_ReportsATwin_WhenOneExists()
    {
        await using var context = CreateContext();

        // The index has to go first: a twin is exactly what the index refuses, and the state this
        // checks is the state the preflight exists for.
        await context.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS ux_fuel_types_brand_grade_key;");

        try
        {
            context.FuelTypes.Add(Fuel("preflight-cyrillic", BrandIds.Klo, "А 95 (preflight test)"));
            context.FuelTypes.Add(Fuel("preflight-latin", BrandIds.Klo, "A 95 (preflight test)"));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var report = await RunPreflightAsync("018-fuel-type-grade-twins.sql");

            report.Should().Contain("А 95 (preflight test)",
                "the preflight names the row that is already there");
            report.Should().Contain("A 95 (preflight test)",
                "and the row that is the second spelling of it");
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM fuel_types WHERE name LIKE '% (preflight test)'");
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX ux_fuel_types_brand_grade_key
                    ON fuel_types (brand_id, grade_key) WHERE NOT is_deleted
                """);
        }
    }

    /// <summary>
    /// The preflight file, run against the migrated database, with psql's transaction lines taken
    /// out — the reader opens its own read-only transaction around them.
    /// </summary>
    private async Task<string> RunPreflightAsync(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "deploy", "preflight-db", fileName);
        var sql = string.Join("\n", File.ReadAllText(path)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line =>
            {
                var trimmed = line.TrimStart();
                return !trimmed.StartsWith("BEGIN;", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.StartsWith("COMMIT;", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.StartsWith("SET TRANSACTION READ ONLY", StringComparison.OrdinalIgnoreCase);
            }));

        await using var connection = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new Npgsql.NpgsqlCommand("SET TRANSACTION READ ONLY;\n" + sql, connection, transaction);

        var report = new System.Text.StringBuilder();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    if (!reader.IsDBNull(column))
                    {
                        report.Append(reader.GetValue(column)).Append(' ');
                    }
                }
            }
        }

        await transaction.RollbackAsync();
        return report.ToString();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FuelFlow.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("could not locate the backend root from " + AppContext.BaseDirectory);
        return dir!.Parent!.FullName;
    }
}
