using FluentAssertions;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// #150 W1: a person may work for <b>N</b> companies. The old <c>UNIQUE(worker_user_id)</c> made a
/// second company impossible - the exact opposite of the decision - so the constraint moved to
/// <c>UNIQUE(legal_entity_id, worker_user_id)</c>.
/// <para>
/// These live in the integration suite rather than the unit suite because the thing being changed is
/// a <b>unique index</b>, and the EF Core in-memory brand enforces no indexes at all. A unit test
/// would happily insert two rows that production Postgres rejects.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class WorkerMayWorkForNCompaniesIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public WorkerMayWorkForNCompaniesIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Worker_Can_Be_A_Member_Of_Two_Different_Companies()
    {
        await using var context = CreateContext();
        await ResetAsync(context);

        var workerId = await NewWorkerAsync(context);
        context.CompanyMembers.Add(NewMember(workerId, await NewCompanyAsync(context)));
        context.CompanyMembers.Add(NewMember(workerId, await NewCompanyAsync(context)));
        await context.SaveChangesAsync();

        // Read raw: the global !IsDeleted filter is not what is under test here, the index is.
        await using var verify = CreateContext();
        (await verify.CompanyMembers.IgnoreQueryFilters().CountAsync(m => m.WorkerUserId == workerId))
            .Should().Be(2, "working for N companies is the decision #150 W1 exists to allow");
    }

    [Fact]
    public async Task A_Worker_Cannot_Be_A_Member_Of_The_Same_Company_Twice()
    {
        await using var context = CreateContext();
        await ResetAsync(context);

        var workerId = await NewWorkerAsync(context);
        var legalEntityId = await NewCompanyAsync(context);

        context.CompanyMembers.Add(NewMember(workerId, legalEntityId));
        await context.SaveChangesAsync();

        context.CompanyMembers.Add(NewMember(workerId, legalEntityId));

        var threw = false;
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            threw = true;
        }

        threw.Should().BeTrue("the composite unique index is what stops a duplicate membership in one company");
    }

    /// <summary>
    /// The partial filter has to survive the change. A fired worker leaves a soft-deleted row
    /// behind, and without <c>is_deleted = false</c> on the index that stale row would make them
    /// permanently un-invitable to the company they were just fired from.
    /// </summary>
    [Fact]
    public async Task A_Fired_Worker_Can_Join_The_Same_Company_Again()
    {
        await using var context = CreateContext();
        await ResetAsync(context);

        var workerId = await NewWorkerAsync(context);
        var legalEntityId = await NewCompanyAsync(context);

        var fired = NewMember(workerId, legalEntityId);
        fired.IsDeleted = true;
        fired.DeletedAtUtc = TestClock.UtcNow;
        context.CompanyMembers.Add(fired);
        await context.SaveChangesAsync();

        context.CompanyMembers.Add(NewMember(workerId, legalEntityId));
        await context.SaveChangesAsync();

        await using var verify = CreateContext();
        (await verify.CompanyMembers.CountAsync(m => m.WorkerUserId == workerId))
            .Should().Be(1, "the soft-deleted row must not collide with the new live membership");
    }

    /// <summary>
    /// Two live memberships, and the worker-facing query has to return both - a single-row lookup
    /// would silently drop one of the companies the person works for.
    /// </summary>
    [Fact]
    public async Task My_Memberships_Returns_Every_Company_The_Worker_Belongs_To()
    {
        await using var seed = CreateContext();
        await ResetAsync(seed);

        var workerId = await NewWorkerAsync(seed);
        seed.CompanyMembers.Add(NewMember(workerId, await NewCompanyAsync(seed)));
        seed.CompanyMembers.Add(NewMember(workerId, await NewCompanyAsync(seed)));
        seed.CompanyMembers.Add(NewMember(workerId, await NewCompanyAsync(seed)));
        await seed.SaveChangesAsync();

        await using var context = CreateContext();
        var handler = new FuelFlow.Features.Company.GetMyMemberships.GetMyMembershipsQueryHandler(context);
        var result = await handler.HandleAsync(
            new FuelFlow.Features.Company.GetMyMemberships.GetMyMembershipsQuery(workerId));

        result.Should().HaveCount(3, "the whole point of W1 is that the worker has more than one");
        result.Select(m => m.LegalEntityId).Should().OnlyHaveUniqueItems();
    }

    private static async Task<Guid> NewWorkerAsync(ApplicationDbContext context)
    {
        var workerId = Guid.NewGuid();
        context.Users.Add(NewUser(workerId));
        await context.SaveChangesAsync();
        return workerId;
    }

    /// <summary>
    /// company_members has real foreign keys to legal_entities and to users (added with the schema
    /// review), so a membership cannot be seeded without both behind it.
    /// </summary>
    private static async Task<Guid> NewCompanyAsync(ApplicationDbContext context)
    {
        var ownerId = Guid.NewGuid();
        context.Users.Add(NewUser(ownerId));

        var legalEntityId = Guid.CreateVersion7();
        context.LegalEntities.Add(new LegalEntity
        {
            Id = legalEntityId,
            UserId = ownerId,
            Name = "Company LLC",
            // Unique per company, not a shared literal: edrpou carries a unique index, and two
            // companies in this test are deliberately distinct rows.
            Edrpou = $"1{ownerId:N}"[..10],
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });
        await context.SaveChangesAsync();
        return legalEntityId;
    }

    private static User NewUser(Guid id) => new()
    {
        Id = id,
        PhoneNumber = $"+38{id:N}"[..20],
        IsActive = true,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
    };

    private static CompanyMember NewMember(Guid workerUserId, Guid legalEntityId) => new()
    {
        Id = Guid.CreateVersion7(),
        LegalEntityId = legalEntityId,
        WorkerUserId = workerUserId,
        JoinedAtUtc = TestClock.UtcNow,
    };

    private async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll)
            .Options, TestQrCipher.Instance);
}