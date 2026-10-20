using System.Data;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// One rule, two languages: how a fuel's natural key becomes a uuid.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is asserted against a real database.</b> The conversion migration maps the rows that
/// carried a natural key with Postgres's own <c>md5('fuelflow-fuel:' || id)::uuid</c>, and the
/// runtime maps the same key with the same rule in C#. If the two ever disagree, a row written
/// before the conversion and a row written after it are two different fuels with the same name -
/// and nothing in either language would say so. The only way to hold two languages to one rule is
/// to run both.
/// </para>
/// <para>
/// <b>Why the seed is part of it.</b> A freshly migrated database holds the ten seeded fuels, and
/// their ids are the ones the mapping produces. If the seed and the migration disagreed, the
/// database would carry a fuel under an id the runtime would never produce - invisible until an
/// import looked for it by name and found a twin.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FuelIdMapperIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelIdMapperIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The keys the seed installs, and the names it installs them under. A change to either is a
    /// change to the seed - and the seed and the mapping have to agree about it.
    /// </summary>
    public static TheoryData<string, string> SeededFuels => new()
    {
        { "okko-dp", "ДП ЄВРО" },
        { "okko-95", "A-95" },
        { "okko-p95", "Pulls 95" },
        { "okko-pulls-dp", "ДП PULLS" },
        { "okko-gas", "ГАЗ" },
        { "wog-dp", "ДП Mustang" },
        { "wog-95", "A-95 Mustang" },
        { "wog-95-euro", "A 95 EURO" },
        { "wog-100", "Mustang 100" },
        { "wog-gas", "ГАЗ" },
    };

    [Theory]
    [MemberData(nameof(SeededFuels))]
    public async Task TheMapping_AgreesWithPostgres(string naturalKey, string name)
    {
        await using var context = CreateContext();

        // The hex, not the uuid: EF's raw query scans the text for `:name` parameters, and the
        // comparison is easier to read when both sides are the same 32 characters.
        // Npgsql directly, not EF's raw query: EF scans a raw command for `:name` parameters, and a
        // command that carries a literal md5 of a key is easier to trust when nothing rewrites it.
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT md5('{NaturalKeyPrefix}{naturalKey}')", connection);
        var fromPostgres = (string)(await command.ExecuteScalarAsync())!;

        fromPostgres.Should().Be(
            ToUuid(naturalKey).ToString("N"),
            "the migration maps a key with md5('fuelflow-fuel:' || id)::uuid and the runtime maps it with the same rule");
    }

    [Theory]
    [MemberData(nameof(SeededFuels))]
    public async Task TheSeededFuel_CarriesTheIdTheMappingProduces(string naturalKey, string name)
    {
        await using var context = CreateContext();

        // By the id the mapping produces, not by the name: two networks sell a "ГАЗ", and the name
        // is not what tells them apart.
        var fuel = await context.FuelTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == ToUuid(naturalKey));

        fuel.Should().NotBeNull("a freshly migrated catalogue holds the seed's fuels under the ids the mapping produces");
        fuel!.Name.Should().Be(name);
    }

    [Fact]
    public void AKey_AlwaysLandsOnTheSameUuid_AndAUuidIsLeftAlone()
    {
        // The property the migration relies on: a row written before the conversion and a row written
        // after it are the same row, and a uuid that is already one is not mapped a second time.
        ToUuid("okko-95").Should().Be(ToUuid("okko-95"));
        ToUuid(ToUuid("okko-95").ToString()).Should().Be(
            ToUuid("okko-95"),
            "a uuid is already the id - mapping it again would give a different row");
    }
}
