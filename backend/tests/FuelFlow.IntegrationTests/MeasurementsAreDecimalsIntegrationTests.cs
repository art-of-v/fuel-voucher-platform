using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A measurement is a decimal. A coordinate is a float.
/// </summary>
/// <remarks>
/// <para>
/// Two columns in the schema were <c>double precision</c> and carried a measurement:
/// <c>fuel_vouchers.verification_mismatch_percent</c> and
/// <c>feedback_messages.confidence</c>. Both are compared against a threshold - a mismatch
/// percentage against the line that separates a warning from a failure, a classifier's confidence
/// against the floor below which a message is dropped - and binary floating point cannot represent
/// 0.1 exactly. A value that reads 10.0 % and is stored as the double nearest 0.1 is a value that
/// is not the number anybody wrote down.
/// </para>
/// <para>
/// <b>Why a rule and not two columns.</b> The two are fixed by name, and the next one would not
/// be. This says what a measurement is, so the third one is caught here rather than in a report
/// that rounds.
/// </para>
/// <para>
/// <b>Why the coordinates are excluded.</b> <c>brand_stations.lat</c> and
/// <c>brand_stations.lng</c> ARE double precision, and they should be: a coordinate in degrees is
/// a float, and a decimal one would buy a rounding error and a bigger column. The rule is about
/// measurements, not about every float in the schema.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class MeasurementsAreDecimalsIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    /// <summary>
    /// The column names that hold a measurement. A percentage, a confidence, a ratio.
    /// </summary>
    private static readonly string[] MeasurementColumns =
    [
        "verification_mismatch_percent",
        "confidence",
        "margin_percent",
        "margin_uah_per_liter"
    ];

    private readonly TestDatabaseFixture _fixture;

    public MeasurementsAreDecimalsIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Every_Column_That_Carries_A_Measurement_Is_Numeric()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name || '.' || column_name || '|' || data_type
              FROM information_schema.columns
             WHERE table_schema = current_schema()
               AND (column_name LIKE '%\_percent'
                 OR column_name LIKE '%\_confidence'
                 OR column_name LIKE '%\_ratio')
             ORDER BY table_name, column_name
            """;

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        columns.Should().NotBeEmpty("the schema carries measurements");

        var floats = columns.Where(c => c.EndsWith("|double precision")).ToList();
        floats.Should().BeEmpty(
            "a measurement is compared against a threshold, and binary floating point cannot represent 0.1 exactly");
    }

    [Fact]
    public async Task The_Coordinates_Stay_Floats()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*)
              FROM information_schema.columns
             WHERE table_schema = current_schema()
               AND table_name = 'brand_stations'
               AND column_name IN ('lat', 'lng')
               AND data_type = 'double precision'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        reader.GetInt64(0).Should().Be(2,
            "a coordinate in degrees is a float, and this rule is about measurements");
    }

    [Fact]
    public async Task The_Model_Holds_A_Measurement_As_A_Decimal()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var offenders = new List<string>();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (!MeasurementColumns.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clrType != typeof(decimal))
                {
                    offenders.Add($"{entityType.ClrType.Name}.{property.Name} is a {clrType.Name}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a model that holds a measurement as a double is a model that cannot compare it exactly");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
