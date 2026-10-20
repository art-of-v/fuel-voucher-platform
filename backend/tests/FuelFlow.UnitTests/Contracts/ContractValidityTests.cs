using FluentAssertions;
using FuelFlow.Features.Contracts;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Contracts;

/// <summary>
/// A contract has a validity term, and it is mandatory — on the business's instruction
/// (2026-10-20): a contract without a term is not a contract, it is a record that something
/// once happened.
/// <para>
/// The term is only worth having if it stops something. It stops a signature: a contract that
/// has already run out is not signed, because a signature outliving the agreement it is a
/// signature of is a contract that still binds.
/// </para>
/// </summary>
public sealed class ContractValidityTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public ContractValidityTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public void A_Contract_That_Has_Not_Expired_Can_Be_Signed()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        ContractValidity.CanBeSigned(now.AddDays(1), now).Should().BeTrue();
        ContractValidity.IsExpired(now.AddDays(1), now).Should().BeFalse();
    }

    [Fact]
    public void A_Contract_That_Has_Expired_Cannot_Be_Signed()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        ContractValidity.CanBeSigned(now.AddDays(-1), now).Should().BeFalse();
        ContractValidity.IsExpired(now.AddDays(-1), now).Should().BeTrue();
    }

    [Fact]
    public void A_Contract_Expires_On_Its_Last_Moment()
    {
        // The boundary: a contract whose term ends exactly now has ended. The alternative -
        // treating the end of the day as still valid - would mean a contract that runs one
        // instant past the moment it ends.
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        ContractValidity.CanBeSigned(now, now).Should().BeFalse();
        ContractValidity.CanBeSigned(now.AddTicks(1), now).Should().BeTrue();
    }

}
