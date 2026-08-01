using FluentAssertions;
using FuelFlow.Features.Contracts.GetAdminContracts;
using FuelFlow.Features.Contracts.GetSignedContracts;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Contracts;

public sealed class ContractsQueryHandlersTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly ApplicationDbContext _context;

    public ContractsQueryHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static User CreateUser(Guid id, string phone)
    {
        return new User
        {
            Id = id,
            PhoneNumber = phone,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static LegalEntity CreateLegalEntity(Guid id, Guid userId)
    {
        return new LegalEntity
        {
            Id = id,
            UserId = userId,
            Name = "ТОВ Тест",
            Edrpou = "12345678",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static Station CreateStation()
    {
        return new Station
        {
            Id = "okko",
            Name = "OKKO",
            LogoText = "OKKO",
            Color = "#22c55e",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetAdminContracts_ShouldReturnContractsWithNavs()
    {
        var user = CreateUser(UserId, "+380991111111");
        var legalEntity = CreateLegalEntity(Guid.NewGuid(), user.Id);
        var station = CreateStation();

        _context.Users.Add(user);
        _context.LegalEntities.Add(legalEntity);
        _context.Stations.Add(station);

        var earlier = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            StationId = station.Id,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        var later = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            StationId = station.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Contracts.AddRange(earlier, later);
        await _context.SaveChangesAsync();

        var handler = new GetAdminContractsQueryHandler(_context);
        var query = new GetAdminContractsQuery();

        var contracts = await handler.HandleAsync(query);

        contracts.Should().HaveCount(2);
        contracts[0].Id.Should().Be(later.Id);
        contracts[1].Id.Should().Be(earlier.Id);
        contracts.Should().OnlyContain(c => c.User != null && c.Entity != null && c.Station != null);
        contracts[0].User!.Id.Should().Be(user.Id);
        contracts[0].Entity!.Name.Should().Be(legalEntity.Name);
        contracts[0].Station!.Name.Should().Be(station.Name);
    }

    [Fact]
    public async Task GetAdminContracts_ShouldReturnEmpty_WhenNone()
    {
        var handler = new GetAdminContractsQueryHandler(_context);
        var query = new GetAdminContractsQuery();

        var contracts = await handler.HandleAsync(query);

        contracts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSignedContracts_ShouldReturnContractsOrderedBySignedAt()
    {
        var user = CreateUser(UserId, "+380991111111");
        var legalEntity = CreateLegalEntity(Guid.NewGuid(), user.Id);
        var station = CreateStation();
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            StationId = station.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.LegalEntities.Add(legalEntity);
        _context.Stations.Add(station);
        _context.Contracts.Add(contract);

        var earlier = new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ContractId = contract.Id,
            SignatureData = "sig-1",
            SignedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        var later = new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ContractId = contract.Id,
            SignatureData = "sig-2",
            SignedAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        _context.UserContracts.AddRange(earlier, later);
        await _context.SaveChangesAsync();

        var handler = new GetSignedContractsQueryHandler(_context);
        var query = new GetSignedContractsQuery();

        var signedContracts = await handler.HandleAsync(query);

        signedContracts.Should().HaveCount(2);
        signedContracts[0].Id.Should().Be(later.Id);
        signedContracts[1].Id.Should().Be(earlier.Id);
        signedContracts.Should().OnlyContain(uc => uc.User != null && uc.Contract != null);
        signedContracts[0].User!.Id.Should().Be(user.Id);
        signedContracts[0].Contract!.Id.Should().Be(contract.Id);
    }

    [Fact]
    public async Task GetSignedContracts_ShouldReturnEmpty_WhenNone()
    {
        var handler = new GetSignedContractsQueryHandler(_context);
        var query = new GetSignedContractsQuery();

        var signedContracts = await handler.HandleAsync(query);

        signedContracts.Should().BeEmpty();
    }
}
