using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_types.supplier_code</c> — the network's own code for a grade, unique per network among
/// live rows. Planning #349, step 2: the stable half of a fuel's identity, before anything imports it.
/// </summary>
/// <remarks>
/// <para>
/// The database half of the guarantee belongs here rather than in a unit test, because the whole point
/// is an index: <c>ux_fuel_types_station_supplier_code</c> refuses two live fuels claiming one code at
/// one network, and nothing in C# has to be trusted to say so.
/// </para>
/// <para>
/// Two properties that are easy to get wrong and expensive to discover late, so both are stated here:
/// <b>null does not collide with null</b> (a fuel entered by hand has no code, and two of them at one
/// station must both fit) and <b>the same code at two networks is fine</b> (WOG and OKKO both sell
/// A-95; the code is per network because the network is what spells it).
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FuelTypeSupplierCodeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelTypeSupplierCodeTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static FuelTypeEntity Fuel(string id, string stationId, string name, string? code)
        => new()
        {
            Id = id,
            StationId = stationId,
            Name = name,
            SupplierCode = code,
            BasePrice = 50m,
            DiscountPrice = 48m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task TwoLiveFuels_CannotClaimTheSameCodeAtOneNetwork()
    {
        // The ambiguity the import would otherwise resolve arbitrarily: one code, two fuels, and
        // whichever row it picks becomes the fuel a voucher gets priced against.
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        context.FuelTypes.Add(Fuel("code-a", "okko", "ДП ЄВРО (code test)", "9018"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.FuelTypes.Add(Fuel("code-b", "okko", "ДП ЄВРО (code test 2)", "9018"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_types WHERE id IN ('code-a', 'code-b')");
    }

    [Fact]
    public async Task TheSameCode_AtTwoNetworks_IsNotADuplicate()
    {
        // "A-95" is OKKO's spelling and WOG's. The code identifies a fuel *within* a network, which
        // is why the index leads with station_id and why a global unique on the code would be wrong.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("net-a", "okko", "A-95 (code test)", "A-95"));
        context.FuelTypes.Add(Fuel("net-b", "wog", "A-95 Mustang (code test)", "A-95"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_types WHERE id IN ('net-a', 'net-b')");
    }

    [Fact]
    public async Task TwoFuelsWithNoCode_DoNotCollide()
    {
        // The reason the index excludes NULL rather than treating every absent code as one value: a
        // hand-entered fuel and a rehearsal fuel are both legitimately code-less.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("nocode-a", "upg", "Паливо без коду (code test)", null));
        context.FuelTypes.Add(Fuel("nocode-b", "upg", "Ще одне без коду (code test)", null));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_types WHERE id IN ('nocode-a', 'nocode-b')");
    }

    [Fact]
    public async Task AnArchivedFuel_FreesItsCode()
    {
        // Same shape as ux_fuel_types_station_name: the index is partial on NOT is_deleted, so taking a
        // fuel out of the catalogue releases its code for the fuel that replaces it.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("arch-code-a", "klo", "ДП (code reuse test)", "9018"));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE fuel_types SET is_deleted = true, deleted_at_utc = now() WHERE id = 'arch-code-a'");

        context.ChangeTracker.Clear();
        context.FuelTypes.Add(Fuel("arch-code-b", "klo", "ДП (code reuse test)", "9018"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM fuel_types WHERE id IN ('arch-code-a', 'arch-code-b')");
    }

    [Fact]
    public async Task TheColumnStartsEmpty_Everywhere()
    {
        // Documents the migration's decision rather than its effect: nothing was backfilled, because a
        // code guessed from a fuel name is the wrong identity, and a unique index then enforces the
        // wrong one. If this ever fails, someone backfilled by name and every such value is a guess.
        await using var context = CreateContext();

        var withCode = await context.FuelTypes
            .IgnoreQueryFilters()
            .CountAsync(f => f.SupplierCode != null);

        withCode.Should().Be(0, "the code is filled by hand or by the catalogue import, never derived from the name");
    }

    [Fact]
    public async Task AnOverlongCode_IsRefused()
    {
        // 50 is generous for "Pulls 95" or a product code, but the column is indexed and unique, so it
        // is bounded rather than free text — the same call as every other catalog column (F-24).
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("long-code", "okko", "Довгий код (code test)", new string('x', 51)));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_types WHERE id = 'long-code'");
    }
}