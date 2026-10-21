using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetWorkerUsageReport;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The worker usage report (epic #103 S5, #150) against a real PostgreSQL database.
/// </summary>
/// <remarks>
/// Pattern A - the controller is driven directly with a fabricated <see cref="HttpContext"/>, so the
/// identity claim is under the test's control and the "only my own vouchers" property is testable.
/// Real Postgres rather than InMemory because the report aggregates over filtered rows, which is
/// exactly the kind of query an in-memory brand answers differently.
/// </remarks>
[Collection("Integration Tests")]
public sealed class WorkerUsageReportIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public WorkerUsageReportIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Report_SplitsReceivedUsedAndRemaining_ForTheCallingWorker()
    {
        var workerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, workerId);
            SeedCompany(seed, companyId, workerId);
            Issue(seed, companyId, workerId, VoucherStatus.Assigned, 10m);
            Issue(seed, companyId, workerId, VoucherStatus.Assigned, 20m);
            Issue(seed, companyId, workerId, VoucherStatus.Used, 5m);
            await seed.SaveChangesAsync();
        }

        var report = await GetReportAsync(workerId, companyId);

        report.Items.Should().HaveCount(3);
        report.Totals.Count.Should().Be(3);
        report.Totals.LitersReceived.Should().Be(35m);
        report.Totals.LitersUsed.Should().Be(5m);
        report.Totals.LitersRemaining.Should().Be(30m);
        report.Totals.CountUsed.Should().Be(1);
        report.Totals.CountRemaining.Should().Be(2);
    }

    [Fact]
    public async Task Report_NeverReturnsAnotherWorkersVouchers()
    {
        // The security property, and the reason the endpoint takes no user id from the caller.
        var meId = Guid.NewGuid();
        var colleagueId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, meId);
            SeedUser(seed, colleagueId);
            SeedCompany(seed, companyId, meId);
            Issue(seed, companyId, meId, VoucherStatus.Assigned, 10m, "MINE");
            Issue(seed, companyId, colleagueId, VoucherStatus.Assigned, 99m, "THEIRS");
            await seed.SaveChangesAsync();
        }

        var report = await GetReportAsync(meId, companyId);

        report.Items.Should().ContainSingle();
        report.Items[0].VoucherNumber.Should().Be("MINE");
        report.Totals.LitersReceived.Should().Be(10m, "a colleague's 99 L must not leak into my totals");
    }

    [Fact]
    public async Task Report_IsScopedToTheCompanyAskedFor()
    {
        // A worker may work for several companies (#150 decision 1); one company's fuel must not
        // appear under another's.
        var workerId = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, workerId);
            SeedCompany(seed, companyA, workerId);
            SeedCompany(seed, companyB, workerId);
            Issue(seed, companyA, workerId, VoucherStatus.Assigned, 10m, "A-1");
            Issue(seed, companyB, workerId, VoucherStatus.Assigned, 70m, "B-1");
            await seed.SaveChangesAsync();
        }

        var report = await GetReportAsync(workerId, companyA);

        report.LegalEntityId.Should().Be(companyA);
        report.Items.Should().ContainSingle();
        report.Items[0].VoucherNumber.Should().Be("A-1");
    }

    [Fact]
    public async Task Report_CarriesTheIssuedAndUsedDates()
    {
        // The point of the two columns: a received date that a later block or recall does not move.
        var workerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var issuedAt = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var usedAt = new DateTime(2026, 3, 9, 8, 30, 0, DateTimeKind.Utc);

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, workerId);
            SeedCompany(seed, companyId, workerId);
            var voucher = Issue(seed, companyId, workerId, VoucherStatus.Used, 10m);
            voucher.IssuedAtUtc = issuedAt;
            voucher.UsedAtUtc = usedAt;
            await seed.SaveChangesAsync();
        }

        var report = await GetReportAsync(workerId, companyId);

        var row = report.Items.Should().ContainSingle().Subject;
        row.IssuedAtUtc.Should().Be(issuedAt);
        row.UsedAtUtc.Should().Be(usedAt);
        row.IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task Report_ExcludesTheUndistributedCompanyPool()
    {
        // Stock the company bought but never handed over is not the worker's usage.
        var workerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, workerId);
            SeedCompany(seed, companyId, workerId);
            Issue(seed, companyId, workerId, VoucherStatus.Assigned, 10m);
            seed.FuelVouchers.Add(NewUnheldVoucher(companyId, 500m));
            await seed.SaveChangesAsync();
        }

        var report = await GetReportAsync(workerId, companyId);

        report.Items.Should().ContainSingle();
        report.Totals.LitersReceived.Should().Be(10m);
    }

    [Fact]
    public async Task Report_RejectsAnEmptyCompanyId()
    {
        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
        }

        var controller = new GetWorkerUsageReportController(CreateContext());
        controller.ControllerContext = new ControllerContext { HttpContext = BuildContext(Guid.NewGuid()) };

        var result = await controller.Get(Guid.Empty, cancellationToken: CancellationToken.None);

        // ActionResult<T> boxes the BadRequest, so unwrap rather than asserting on the outer type.
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// The list is a page; the totals are not.
    /// </summary>
    /// <remarks>
    /// The totals used to be computed from the rows the endpoint returned, which was the same number
    /// only because the endpoint returned every row. A total that described the page would be a
    /// different number on every page - and the totals are the part the worker actually reads.
    /// </remarks>
    [Fact]
    public async Task Report_Pages_The_List_And_Keeps_The_Totals_Over_Every_Voucher()
    {
        var workerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, workerId);
            SeedCompany(seed, companyId, workerId);
            Issue(seed, companyId, workerId, VoucherStatus.Assigned, 10m);
            Issue(seed, companyId, workerId, VoucherStatus.Assigned, 20m);
            Issue(seed, companyId, workerId, VoucherStatus.Used, 5m);
            await seed.SaveChangesAsync();
        }

        var controller = new GetWorkerUsageReportController(CreateContext());
        controller.ControllerContext = new ControllerContext { HttpContext = BuildContext(workerId) };

        var result = await controller.Get(companyId, page: 1, pageSize: 2);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var report = (GetWorkerUsageReportResponse)ok.Value!;

        report.Items.Should().HaveCount(2, "the page holds what was asked for");
        report.Totals.Count.Should().Be(3, "the totals describe every voucher the worker holds, not the page");
        report.Totals.LitersReceived.Should().Be(35m);
        report.Totals.LitersUsed.Should().Be(5m);

        controller.HttpContext.Response.Headers["X-Total-Count"].ToString().Should().Be("3");
        controller.HttpContext.Response.Headers["X-Has-Next-Page"].ToString().Should().Be("true");
    }

    [Fact]
    public async Task Report_Clamps_A_Page_Size_That_Asks_For_Everything()
    {
        var workerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, workerId);
            SeedCompany(seed, companyId, workerId);
            Issue(seed, companyId, workerId, VoucherStatus.Assigned, 10m);
            await seed.SaveChangesAsync();
        }

        var controller = new GetWorkerUsageReportController(CreateContext());
        controller.ControllerContext = new ControllerContext { HttpContext = BuildContext(workerId) };

        var result = await controller.Get(companyId, page: 1, pageSize: 100_000);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var report = (GetWorkerUsageReportResponse)ok.Value!;

        report.Items.Should().HaveCount(1, "there is only one voucher");
        report.Totals.Count.Should().Be(1);
    }

    private async Task<GetWorkerUsageReportResponse> GetReportAsync(Guid userId, Guid companyId)
    {
        var controller = new GetWorkerUsageReportController(CreateContext());
        controller.ControllerContext = new ControllerContext { HttpContext = BuildContext(userId) };

        var result = await controller.Get(companyId, cancellationToken: CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        return (GetWorkerUsageReportResponse)ok.Value!;
    }

    private static DefaultHttpContext BuildContext(Guid userId)
    {
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
                "test"));
        return context;
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private static void SeedUser(ApplicationDbContext seed, Guid id)
        => seed.Users.Add(new User
        {
            Id = id,
            PhoneNumber = $"+38{id:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

    private static void SeedCompany(ApplicationDbContext seed, Guid id, Guid ownerId)
        => seed.LegalEntities.Add(new LegalEntity
        {
            Id = id,
            UserId = ownerId,
            Name = "QA Company",
            // edrpou is globally unique (see #128), so a shared literal would collide the moment a
            // test seeds two companies.
            Edrpou = $"{id:N}"[..8],
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

    /// <summary>
    /// A voucher issued to a worker, with the fulfilment order it arrived on. Real Postgres enforces
    /// both <c>FK_legal_entities_users_user_id</c> and <c>ck_voucher_held_has_order</c>, so a voucher
    /// sitting with somebody cannot be seeded without the row behind it.
    /// </summary>
    /// <param name="number">Defaults to a fresh unique number: <c>voucher_number</c> is uniquely
    /// indexed, so a fixed default collides as soon as a test issues more than one.</param>
    private static FuelVoucher Issue(
        ApplicationDbContext seed,
        Guid companyId,
        Guid workerId,
        VoucherStatus status,
        decimal liters,
        string? number = null)
    {
        var orderId = Guid.NewGuid();
        seed.Orders.Add(new Order
        {
            Id = orderId,
            UserId = workerId,
            Price = 0,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = TestClock.UtcNow
        });

        var voucher = NewUnheldVoucher(companyId, liters, number ?? $"V-{Guid.NewGuid():N}");
        voucher.Status = status;
        voucher.WorkerUserId = workerId;
        voucher.AssignedToUserId = workerId;
        voucher.OrderId = orderId;
        voucher.IssuedAtUtc = TestClock.UtcNow;

        seed.FuelVouchers.Add(voucher);
        return voucher;
    }

    /// <summary>Company stock: bought by the company, handed to nobody.</summary>
    private static FuelVoucher NewUnheldVoucher(Guid companyId, decimal liters, string number = "POOL")
        => new()
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-dp"),
            Liters = liters,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            VoucherNumber = number,
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Available,
            LegalEntityId = companyId,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
}