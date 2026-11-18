using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Brands;
using FuelFlow.Features.BusinessLeads;
using FuelFlow.Features.BusinessLeads.GetAdminBusinessLeadById;
using FuelFlow.Features.BusinessLeads.GetAdminBusinessLeadFacets;
using FuelFlow.Features.BusinessLeads.GetAdminBusinessLeads;
using FuelFlow.Features.BusinessLeads.GetBusinessLeadAttentionCount;
using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.Features.BusinessLeads.UpdateBusinessLeadStatus;
using FuelFlow.Persistence;
using FuelFlow.Features.Settings;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

[Collection("Integration Tests")]
public sealed class BusinessLeadIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private static readonly Guid StaffId = Guid.Parse("22222222-3333-4444-5555-666666666666");

    public BusinessLeadIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Public_form_validates_before_storing_and_accepts_a_valid_lead()
    {
        using var client = _fixture.CreateClient();
        var marker = Guid.NewGuid().ToString("N");

        var broken = await client.PostAsJsonAsync("/api/business-leads", new
        {
            companyName = "",
            contactName = "",
            phone = "0970011771"
        });
        broken.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var accepted = await client.PostAsJsonAsync("/api/business-leads", new
        {
            companyName = $"Fleet {marker}",
            contactName = "Iryna",
            phone = "+380970011771",
            email = "iryna@example.com",
            fleetSize = 24,
            message = "Monthly fleet fuel"
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await using var context = CreateContext();
        var lead = await context.BusinessLeads.SingleAsync(l => l.CompanyName == $"Fleet {marker}");
        lead.Status.Should().Be(BusinessLeadStatus.New);
        lead.FleetSize.Should().Be(24);
    }

    [Fact]
    public async Task Honeypot_is_silently_discarded()
    {
        using var client = _fixture.CreateClient();
        var marker = Guid.NewGuid().ToString("N");

        var response = await client.PostAsJsonAsync("/api/business-leads", new
        {
            companyName = $"Bot {marker}",
            contactName = "Bot",
            phone = "+380970011771",
            website = "https://spam.test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var context = CreateContext();
        (await context.BusinessLeads.AnyAsync(l => l.CompanyName == $"Bot {marker}"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Admin_default_list_is_active_and_status_updates_are_idempotent()
    {
        var active = await Seed(BusinessLeadStatus.New, "Active fleet");
        var rejected = await Seed(BusinessLeadStatus.Rejected, "Rejected fleet");

        await using (var context = CreateContext())
        {
            var controller = Controller(context);
            var list = Read<BusinessLeadListResponse>(await controller.GetAll(ct: default));
            list.Items.Should().Contain(i => i.Id == active);
            list.Items.Should().NotContain(i => i.Id == rejected);

            var first = Read<BusinessLeadMutationResponse>(await controller.UpdateStatus(new()
            {
                Ids = [active],
                Status = nameof(BusinessLeadStatus.Contacted)
            }, default));
            first.Affected.Should().Be(1);
        }

        await using (var context = CreateContext())
        {
            var second = Read<BusinessLeadMutationResponse>(await Controller(context).UpdateStatus(new()
            {
                Ids = [active],
                Status = nameof(BusinessLeadStatus.Contacted)
            }, default));
            second.Affected.Should().Be(0);
        }
    }

    [Fact]
    public async Task Unknown_status_filter_is_a_400_and_detail_count_facets_are_available()
    {
        var id = await Seed(BusinessLeadStatus.New, "Searchable fleet");
        await using var context = CreateContext();
        var controller = Controller(context);

        (await controller.GetAll(status: "nonsense", ct: default))
            .Should().BeOfType<BadRequestObjectResult>();
        Read<BusinessLeadDetailResponse>(await controller.GetById(id)).Item.Id.Should().Be(id);
        Read<BusinessLeadAttentionResponse>(await controller.GetAttentionCount()).Count.Should().BeGreaterThan(0);
        Read<BusinessLeadFacetsResponse>(await controller.GetFacets()).Statuses.Should().Contain("New");
    }

    private AdminBusinessLeadsController Controller(ApplicationDbContext context)
    {
        var controller = new AdminBusinessLeadsController(
            new GetAdminBusinessLeadsQueryHandler(context),
            new GetAdminBusinessLeadByIdQueryHandler(context),
            new GetAdminBusinessLeadFacetsQueryHandler(context),
            new GetBusinessLeadAttentionCountQueryHandler(context),
            new UpdateBusinessLeadStatusCommandHandler(context, new AuditEventService(context)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, StaffId.ToString()), new Claim(ClaimTypes.Name, "Staff")],
                    "test"))
            }
        };
        return controller;
    }

    private async Task<Guid> Seed(BusinessLeadStatus status, string company)
    {
        var id = Guid.CreateVersion7();
        await using var context = CreateContext();
        context.BusinessLeads.Add(new BusinessLead
        {
            Id = id,
            CompanyName = company,
            ContactName = "Iryna",
            Phone = "+380970011771",
            Status = status,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static T Read<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        return (T)((OkObjectResult)result).Value!;
    }

    private ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options,
        TestQrCipher.Instance);
}

[Collection("Integration Tests")]
public sealed class BusinessLeadRateLimitIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public BusinessLeadRateLimitIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Sixth_submission_from_one_IP_is_rate_limited()
    {
        using var client = _fixture.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var accepted = await client.PostAsJsonAsync("/api/business-leads", new
            {
                companyName = $"Rate-limit fleet {i}",
                contactName = "Iryna",
                phone = "+380970011771"
            });
            accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var refused = await client.PostAsJsonAsync("/api/business-leads", new
        {
            companyName = "Rate-limit fleet 6",
            contactName = "Iryna",
            phone = "+380970011771"
        });

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.Should().NotBeNull();
    }
}
