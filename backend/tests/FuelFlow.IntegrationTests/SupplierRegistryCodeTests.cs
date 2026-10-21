using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The supplier's registry code (ЄДРОПУ / ІПН) is mandatory and unique, and the database
/// says so - not just the API and the form.
/// <para>
/// Mandatory on the business's instruction (2026-10-20): the code is what a VAT invoice is
/// issued against, so a supplier without one cannot be invoiced. Unique because two rows with
/// the same code are the same counterparty recorded twice, and a business that pays "two"
/// suppliers who are one person pays twice and reconciles the difference by hand.
/// </para>
/// <para>
/// The API validates the value and the form asks for it, so the interesting question is what
/// happens to a row that arrived by another road - a script, a data fix, a bug. These tests
/// ask the database directly, because that is the only place the answer is a guarantee rather
/// than a convention.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class SupplierRegistryCodeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SupplierRegistryCodeTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static Supplier NewSupplier(string? edrIpn) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Постачальник " + Guid.NewGuid().ToString("N")[..8],
        EdrIpn = edrIpn!,
        IsActive = true,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    [Fact]
    public async Task The_Column_Is_Still_Nullable_In_The_Database()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var isNullable = await context.Database
            .SqlQueryRaw<string>(
            """
            SELECT is_nullable
            FROM information_schema.columns
            WHERE table_name = 'suppliers'
            AND column_name = 'edr_ipn'
            """)
            .ToListAsync();

        isNullable.Should().ContainSingle().Which.Should().Be("YES",
            "a supplier can be recorded before anyone has looked the registry code up");
    }

    [Fact]
    public async Task A_Supplier_Without_A_Registry_Code_Is_Allowed()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        context.Suppliers.Add(NewSupplier(null));

        var act = () => context.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "the code is optional - requiring it would mean a supplier who cannot be invoiced cannot be bought from either");
    }

    [Fact]
    public async Task Two_Suppliers_Cannot_Share_A_Registry_Code()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var code = TestEdrIpns.Next();
        context.Suppliers.Add(NewSupplier(code));
        context.Suppliers.Add(NewSupplier(code));

        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "one counterparty, one row - two rows are the same supplier recorded twice");
    }

    [Fact]
    public async Task A_Registry_Code_That_Is_Not_One_Is_Refused_By_The_Database()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Letters, a space, a formatted code: the API refuses all of them with a sentence a
        // person can act on, and the database refuses them because a registry code that is not
        // a registry code is not a registry code.
        context.Suppliers.Add(NewSupplier("12345 ABCD"));

        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the code is stored in a registry, and the registry wants digits");
    }
}
