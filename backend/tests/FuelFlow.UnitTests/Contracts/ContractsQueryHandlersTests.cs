using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Contracts.GetAdminContracts;
using FuelFlow.Features.Contracts.GetSignedContracts;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
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
            CreatedAtUtc = TestClock.UtcNow.AddHours(-1),
            ExpiresAtUtc = TestClock.UtcNow.AddYears(1)
        };
        var later = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            BrandId = brand.Id,
            CreatedAtUtc = TestClock.UtcNow,
            ExpiresAtUtc = TestClock.UtcNow.AddYears(1)
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
            CreatedAtUtc = TestClock.UtcNow,
            ExpiresAtUtc = TestClock.UtcNow.AddYears(1)
        };

        _context.Contracts.Add(contract);
        _context.UserContracts.Add(new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ContractId = contract.Id,
            SignatureData = "sig-1",
            SignedAtUtc = TestClock.UtcNow.AddHours(-1)
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
            CreatedAtUtc = TestClock.UtcNow,
            ExpiresAtUtc = TestClock.UtcNow.AddYears(1)
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
            SignedAtUtc = TestClock.UtcNow.AddHours(-2)
        };
        var later = new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ContractId = contract.Id,
            SignatureData = "sig-2",
            SignedAtUtc = TestClock.UtcNow.AddHours(-1)
        };

        _context.UserContracts.AddRange(earlier, later);
        await _context.SaveChangesAsync();

        var handler = new GetSignedContractsQueryHandler(_context);
        var query = new GetSignedContractsQuery();

        var page = await handler.HandleAsync(query);

        page.TotalCount.Should().Be(2);
        page.Items.Should().HaveCount(2);
        page.Items[0].Id.Should().Be(later.Id);
        page.Items[1].Id.Should().Be(earlier.Id);
        page.Items.Should().OnlyContain(uc => uc.ContractId == contract.Id);
        page.Items[0].CompanyName.Should().Be(legalEntity.Name);
        page.Items[0].CustomerName.Should().Be(user.PhoneNumber);
        page.Items[0].Expired.Should().BeFalse();
    }

    [Fact]
    public async Task GetSignedContracts_ShouldCarryNoSignatureImage()
    {
        // The image is a capability of its own (planning #622, S-13): a signature cannot be
        // revoked and cannot be rotated, so the list must not carry it - otherwise one call is a
        // harvest of every customer's handwriting.
        var user = CreateUser(UserId, "+380991111111");
        var legalEntity = CreateLegalEntity(Guid.NewGuid(), user.Id);
        var brand = CreateBrand();
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LegalEntityId = legalEntity.Id,
            BrandId = brand.Id,
            CreatedAtUtc = TestClock.UtcNow,
            ExpiresAtUtc = TestClock.UtcNow.AddYears(1)
        };

        _context.Users.Add(user);
        _context.LegalEntities.Add(legalEntity);
        _context.Brands.Add(brand);
        _context.Contracts.Add(contract);
        _context.UserContracts.Add(new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ContractId = contract.Id,
            SignatureData = "sig-1",
            SignedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();

        var handler = new GetSignedContractsQueryHandler(_context);

        var page = await handler.HandleAsync(new GetSignedContractsQuery());

        var json = JsonSerializer.Serialize(page.Items);
        json.Should().NotContain("signatureData");
        json.Should().NotContain("sig-1", "the image itself must not travel with the row");
    }

    [Fact]
    public async Task GetSignedContracts_ShouldBeAPageAndNotTheWholeTable()
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
            CreatedAtUtc = TestClock.UtcNow,
            ExpiresAtUtc = TestClock.UtcNow.AddYears(1)
        };

        _context.Users.Add(user);
        _context.LegalEntities.Add(legalEntity);
        _context.Brands.Add(brand);
        _context.Contracts.Add(contract);

        for (var i = 0; i < 3; i++)
        {
            _context.UserContracts.Add(new UserContract
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ContractId = contract.Id,
                SignatureData = $"sig-{i}",
                SignedAtUtc = TestClock.UtcNow.AddMinutes(-i)
            });
        }

        await _context.SaveChangesAsync();

        var handler = new GetSignedContractsQueryHandler(_context);

        var page = await handler.HandleAsync(new GetSignedContractsQuery(Page: 1, Limit: 2));

        page.Items.Should().HaveCount(2, "the list is a page, not the whole table");
        page.TotalCount.Should().Be(3, "the count is the size of the table, not of the page");
        page.PageSize.Should().Be(2);
        page.Page.Should().Be(1);
    }

    [Fact]
    public async Task GetSignedContracts_ShouldReturnEmpty_WhenNone()
    {
        var handler = new GetSignedContractsQueryHandler(_context);
        var query = new GetSignedContractsQuery();

        var page = await handler.HandleAsync(query);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }
}
