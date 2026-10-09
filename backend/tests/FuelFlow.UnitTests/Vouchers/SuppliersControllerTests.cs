using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.Suppliers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// A supplier is an external counterparty we settle with, not a fuel brand and not one of our own
/// companies. These tests pin the two rules that follow from that: only the name is mandatory (an ФОП
/// has an ІПН, an LLC has an ЄДРОПУ, so nothing else can be universally required), and "removal" is a
/// deactivation so vouchers already issued against the supplier keep naming a real counterparty.
/// </summary>
public sealed class SuppliersControllerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly SuppliersController _controller;

    public SuppliersControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _controller = new SuppliersController(_context, new ProviderEventService(_context))
        {
            // No signed-in user: RecordAsync short-circuits, which is fine for the behaviour under test.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private Supplier Seed(string name, bool isActive = true)
    {
        var now = DateTime.UtcNow;
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = isActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _context.Suppliers.Add(supplier);
        _context.SaveChanges();
        return supplier;
    }

    private static CreateSupplierRequest ValidRequest(string name = "ФОП Стретович Микола") => new()
    {
        Name = name,
        LegalForm = "ФОП",
        Phone = "+380501234567",
        Email = "stretovych@example.com",
        EdrIpn = "1234567890",
        Rnkrr = "12345",
        Address = "м. Київ, вул. Хрещатик, 1",
        Notes = "Основний постачальник"
    };

    [Fact]
    public async Task Create_ShouldAcceptSupplierWithOnlyName()
    {
var result = await _controller.Create(new CreateSupplierRequest { Name = "ФОП Стретович Микола" });

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<SupplierDto>().Subject;
        dto.Name.Should().Be("ФОП Стретович Микола");
        dto.LegalForm.Should().BeNull();
        dto.IsActive.Should().BeTrue();

        (await _context.Suppliers.SingleAsync()).Name.Should().Be("ФОП Стретович Микола");
    }

    [Fact]
    public async Task Create_ShouldTrimWhitespaceFromRequisites()
    {
        var request = ValidRequest();
        request.Name = "  ФОП Стретович Микола  ";
        request.EdrIpn = " 1234567890 ";

        var result = await _controller.Create(request);

        var dto = (SupplierDto)((OkObjectResult)result.Result!).Value!;
        dto.Name.Should().Be("ФОП Стретович Микола");
        dto.EdrIpn.Should().Be("1234567890");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_ShouldRejectMissingName(string? name)
    {
        var result = await _controller.Create(new CreateSupplierRequest { Name = name });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await _context.Suppliers.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("12345678")]   // ЄДРОПУ, 8 digits
    [InlineData("1234567890")] // ІПН, 10 digits
    public async Task Create_ShouldAcceptEightToTenDigitEdrIpn(string edrIpn)
    {
        var request = ValidRequest();
        request.EdrIpn = edrIpn;

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Theory]
    [InlineData("1234567")]    // 7 digits
    [InlineData("12345678901")] // 11 digits
    [InlineData("12345ab")]
    public async Task Create_ShouldRejectMalformedEdrIpn(string edrIpn)
    {
        var request = ValidRequest();
        request.EdrIpn = edrIpn;

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_ShouldRejectRnkrrThatIsNotFiveDigits()
    {
        var request = ValidRequest();
        request.Rnkrr = "1234";

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_ShouldRejectEmailWithoutAtSign()
    {
        var request = ValidRequest();
        request.Email = "stretovych.example.com";

        var result = await _controller.Create(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_ShouldRejectLegalFormOutsideTheKnownList()
    {
        var request = ValidRequest();
        request.LegalForm = "Приватний підприємець";

        var result = await _controller.Create(request);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task List_ShouldHideInactiveSuppliersUnlessAsked()
    {
        Seed("Активний постачальник");
        Seed("Деактивований постачальник", isActive: false);

        var activeOnly = (List<SupplierDto>)((OkObjectResult)(await _controller.List()).Result!).Value!;
        activeOnly.Should().ContainSingle().Which.Name.Should().Be("Активний постачальник");

        var all = (List<SupplierDto>)((OkObjectResult)(await _controller.List(includeInactive: true)).Result!).Value!;
        all.Should().HaveCount(2);
    }

    [Fact]
    public void LegalForms_ShouldExposeTheKnownForms()
    {
        // Ok(...) implicitly converts to ActionResult<T>, so the payload sits on .Result.
        var action = _controller.LegalFormOptions();
        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;

        var forms = (IReadOnlyList<string>)ok.Value!;
        forms.Should().Contain(new[] { "ФОП", "ТОВ" });
    }

    [Fact]
    public async Task Update_ShouldChangeRequisitesAndKeepIdStable()
    {
        var seeded = Seed("Старе ім'я");

        var result = await _controller.Update(seeded.Id, new UpdateSupplierRequest
        {
            Name = "Нова назва",
            LegalForm = "ТОВ",
            EdrIpn = "12345678"
        });

        var dto = (SupplierDto)((OkObjectResult)result.Result!).Value!;
        dto.Id.Should().Be(seeded.Id);
        dto.Name.Should().Be("Нова назва");
        dto.LegalForm.Should().Be("ТОВ");

        var stored = await _context.Suppliers.SingleAsync();
        stored.Name.Should().Be("Нова назва");
        stored.LegalForm.Should().Be("ТОВ");
    }

    [Fact]
    public async Task Update_ShouldNotChangeIsActiveWhenNotProvided()
    {
        var seeded = Seed("Постачальник", isActive: false);

        await _controller.Update(seeded.Id, new UpdateSupplierRequest { Name = "Перейменований" });

        (await _context.Suppliers.SingleAsync()).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_UnknownSupplier_ShouldReturnNotFound()
    {
        var result = await _controller.Update(Guid.NewGuid(), new UpdateSupplierRequest { Name = "Хтось" });

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Update_ShouldRejectInvalidRequisitesAndLeaveStoredRowUntouched()
    {
        var seeded = Seed("Постачальник");

        var result = await _controller.Update(seeded.Id, new UpdateSupplierRequest
        {
            Name = "Перейменований",
            Rnkrr = "123"
        });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await _context.Suppliers.SingleAsync()).Name.Should().Be("Постачальник");
    }

    [Fact]
    public async Task Deactivate_ShouldFlagInactiveAndKeepTheRow()
    {
        var seeded = Seed("Постачальник");

        var result = await _controller.Deactivate(seeded.Id);

        result.Should().BeOfType<NoContentResult>();

        var stored = await _context.Suppliers.SingleAsync();
        stored.IsActive.Should().BeFalse();
        // The row itself must survive: vouchers and exchanges already reference it.
        stored.Should().NotBeNull();
    }

    [Fact]
    public async Task Deactivate_ShouldBeIdempotent()
    {
        var seeded = Seed("Постачальник", isActive: false);

        var result = await _controller.Deactivate(seeded.Id);

        result.Should().BeOfType<NoContentResult>();
        (await _context.Suppliers.SingleAsync()).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_UnknownSupplier_ShouldReturnNotFound()
    {
        var result = await _controller.Deactivate(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Reactivate_ShouldBringBackADeactivatedSupplier()
    {
        var seeded = Seed("Постачальник", isActive: false);

        var result = await _controller.Reactivate(seeded.Id);

        var dto = (SupplierDto)((OkObjectResult)result.Result!).Value!;
        dto.IsActive.Should().BeTrue();
        (await _context.Suppliers.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_UnknownSupplier_ShouldReturnNotFound()
    {
        var result = await _controller.Reactivate(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task List_ShouldOrderByName()
    {
        // Deliberately ASCII: the ordering is done by the provider's collation, so Cyrillic-vs-Latin
        // names sort differently under the Windows and Linux ICU data and would make this flaky.
        Seed("Zulu supplier");
        Seed("Alpha supplier");

        var rows = (List<SupplierDto>)((OkObjectResult)(await _controller.List()).Result!).Value!;

        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("Alpha supplier");
        rows[1].Name.Should().Be("Zulu supplier");
    }
}