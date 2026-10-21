using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The contract's validity term, against a real PostgreSQL.
/// <para>
/// The unit suite can prove the rule - that an expired contract is not signed - but it cannot
/// prove the constraint, because the InMemory provider is not a database and enforces no
/// NOT NULL. This is the part that has to be asked of the database itself: a contract cannot
/// be stored without a term, and the term cannot end before it starts.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class ContractValidityIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ContractValidityIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static Contract NewContract(DateTime? expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        LegalEntityId = Guid.NewGuid(),
        BrandId = Guid.NewGuid(),
        CreatedAtUtc = TestClock.UtcNow.AddDays(-1),
        ExpiresAtUtc = expiresAtUtc ?? default
    };

    [Fact]
    public async Task The_Column_Is_Not_Null_In_The_Database()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var isNullable = await context.Database
            .SqlQueryRaw<string>(
            """
            SELECT is_nullable
            FROM information_schema.columns
            WHERE table_name = 'contracts'
            AND column_name = 'expires_at_utc'
            """)
            .ToListAsync();

        isNullable.Should().ContainSingle().Which.Should().Be("NO");
    }

    [Fact]
    public async Task A_Contract_Whose_Term_Is_Not_A_Term_Is_Refused_By_The_Database()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // The term is not a term: it is before the contract was even created. The column is NOT
        // NULL as well - checked above - but a model that always sends a value cannot show
        // that, and this is the refusal a caller can actually cause.
        var contract = NewContract(TestClock.UtcNow.AddDays(-2));
        context.Contracts.Add(contract);

        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "a term that ends before it starts is a typo");
    }
}
