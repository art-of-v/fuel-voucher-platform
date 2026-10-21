using FluentAssertions;
using FuelFlow.Features.Contracts.GetAdminContracts;
using FuelFlow.Features.Contracts.GetSignedContracts;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Contracts;

/// <summary>
/// The two admin contract queries, read as DTOs.
/// </summary>
/// <remarks>
/// The handlers used to answer with EF entities and the admin client read five fields the
/// server had never returned - one of them <c>status</c>, which it called
/// <c>.toLowerCase()</c> on, so the tab threw on the first real row. What is asserted here is
/// the shape the screen reads: a provider, a company, a customer, a term, and the signature
/// state.
/// </remarks>
public sealed class ContractsQueryHandlersTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly ApplicationDbContext _context;

    public ContractsQueryHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
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

    private static Brand CreateBrand()
    {
        return new Brand
        {
            Id = BrandIds.Okko, Slug = "okko",
            Name = "okko",
            LogoText = "okko",
            Color = "#22c55e",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetAdminContracts_ShouldReturnTheShapeTheScreenReads()
    {
        var user = CreateUser(UserId, "+380991111111");
        var legalEntity = CreateLegalEntity(Guid.NewGuid(), user.Id);
        var brand = CreateBrand();

        _context.Users.Add(user);
        _context.LegalEntities.Add(legalEntity);
        _context.Brands.Add(brand);

        var earlier = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            BrandId = brand.Id,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddYears(1)
        };
        var later = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            BrandId = brand.Id,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddYears(1)
        };

        _context.Contracts.AddRange(earlier, later);
        await _context.SaveChangesAsync();

        var handler = new GetAdminContractsQueryHandler(_context);
        var query = new GetAdminContractsQuery();

        var contracts = await handler.HandleAsync(query);

        contracts.Should().HaveCount(2);
        contracts[0].Id.Should().Be(later.Id);
        contracts[1].Id.Should().Be(earlier.Id);
        contracts.Should().OnlyContain(c => c.BrandName == brand.Name);
        contracts[0].CompanyName.Should().Be(legalEntity.Name);
        // The customer's phone number is what identifies them when they have no name.
        contracts[0].CustomerName.Should().Be(user.PhoneNumber);
        contracts[0].Expired.Should().BeFalse();
        contracts[0].Signed.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminContracts_ShouldSayThatAContractIsSigned()
    {
        var user = CreateUser(UserId, "+380991111111");
        var legalEntity = CreateLegalEntity(Guid.NewGuid(), user.Id);
        var brand = CreateBrand();

        _context.Users.Add(user);
        _context.LegalEntities.Add(legalEntity);
        _context.Brands.Add(brand);

        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            BrandId = brand.Id,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddYears(1)
        };

        _context.Contracts.Add(contract);
        _context.UserContracts.Add(new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ContractId = contract.Id,
            SignatureData = "sig-1",
            SignedAtUtc = DateTime.UtcNow.AddHours(-1)
        });
        await _context.SaveChangesAsync();

        var handler = new GetAdminContractsQueryHandler(_context);

        var contracts = await handler.HandleAsync(new GetAdminContractsQuery());

        contracts.Should().HaveCount(1);
        contracts[0].Signed.Should().BeTrue();
        contracts[0].SignedAtUtc.Should().NotBeNull();
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
    public async Task GetSignedContracts_ShouldReturnTheShapeTheScreenReads()
    {
        var user = CreateUser(UserId, "+380991111111");
        var legalEntity = CreateLegalEntity(Guid.NewGuid(), user.Id);
        var brand = CreateBrand();
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            BrandId = brand.Id,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddYears(1)
        };

        _context.Users.Add(user);
        _context.LegalEntities.Add(legalEntity);
        _context.Brands.Add(brand);
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
        signedContracts.Should().OnlyContain(uc => uc.ContractId == contract.Id);
        signedContracts[0].CompanyName.Should().Be(legalEntity.Name);
        signedContracts[0].CustomerName.Should().Be(user.PhoneNumber);
        signedContracts[0].SignatureData.Should().Be("sig-2");
        signedContracts[0].Expired.Should().BeFalse();
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
