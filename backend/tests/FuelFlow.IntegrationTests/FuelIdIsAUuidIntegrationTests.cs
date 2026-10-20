using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A fuel's id is a uuid - in the column, and in the model.
/// </summary>
/// <remarks>
/// <para>
/// On 2026-10-20 the fuel's id stopped being the natural key and became an opaque uuid (planning
/// D-21, second slice). For an hour on the live box the storefront's package list and the admin's
/// fuel list answered with <c>InvalidCastException</c> - <c>"Reading as 'System.String' is not
/// supported for fields having DataTypeName 'uuid'"</c> - and 953 of them landed in
/// <c>error_logs</c>, which is the only place the outage was visible.
/// </para>
/// <para>
/// <b>Why a test and not a constraint.</b> The column's type and the model's type are two different
/// pieces of information, and the database cannot say that they disagree. A model that reads a uuid
/// column as a string is a runtime failure in the application, not a failure of the schema - so
/// nothing in the schema catches it, and CI was green while the storefront was down.
/// </para>
/// <para>
/// <b>Why it is a class and not one endpoint.</b> The defect was not in one projection; it was in
/// every projection that read that column. This asserts the rule for every fuel id the schema has,
/// so the next column that changes type says so here rather than in production.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FuelIdIsAUuidIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelIdIsAUuidIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Every_Column_That_Holds_A_Fuel_Id_Is_A_Uuid()
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
               AND (column_name = 'fuel_type_id' OR column_name LIKE '%\_fuel\_type\_id')
             ORDER BY table_name, column_name
            """;

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        columns.Should().NotBeEmpty("the schema holds fuel ids");
        columns.Should().OnlyContain(c => c.EndsWith("|uuid"),
            "a fuel id that is not a uuid is read as a string by a model that expects a Guid, and the read throws");
    }

    [Fact]
    public async Task Every_Model_Property_That_Holds_A_Fuel_Id_Is_A_Guid()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var offenders = new List<string>();
        var examined = 0;

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (!property.Name.Contains("FuelTypeId", StringComparison.Ordinal))
                {
                    continue;
                }

                examined++;

                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clrType != typeof(Guid))
                {
                    offenders.Add($"{entityType.ClrType.Name}.{property.Name} is a {clrType.Name}");
                }
            }
        }

        // A guard that examines nothing passes forever, and a guard that examines nothing is
        // worse than no guard - it reads like a check.
        examined.Should().BeGreaterThan(5, "the model holds fuel ids, and the guard has to look at them");

        offenders.Should().BeEmpty(
            "a model that reads a uuid column as a string throws on every read, and nothing in the schema says so");
    }

    [Fact]
    public async Task The_Fuel_Types_Own_Id_Is_A_Uuid_In_Both_The_Model_And_The_Column()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var modelType = context.Model.FindEntityType(typeof(FuelTypeEntity))!
            .FindProperty(nameof(FuelTypeEntity.Id))!
            .ClrType;

        modelType.Should().Be(typeof(Guid), "the model's id and the column's type have to be the same thing");

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT data_type
              FROM information_schema.columns
             WHERE table_schema = current_schema()
               AND table_name = 'fuel_types'
               AND column_name = 'id'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        reader.GetString(0).Should().Be("uuid");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
