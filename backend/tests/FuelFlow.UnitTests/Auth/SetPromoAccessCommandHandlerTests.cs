using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetPromoAccess;
using FuelFlow.Features.Auth.AdminUser.SetUserRole;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// The rules of the switch that decides which customers may be served on the promotional route.
/// </summary>
/// <remarks>
/// <para>
/// The owner's ruling of 2026-11-06 is a split of the customer base into two groups, and a normal
/// customer is in the group that cannot. What the mobile app shows is a consequence of the column and
/// not the rule: these tests are about when the column may be written, because a switch that opens a
/// route with no acquirer invoice behind the sale is not a profile field.
/// </para>
/// <para>
/// The capability that admits a person to this endpoint at all is proved over HTTP in
/// <c>AuthorizePolicySurfaceTests</c>; what is proved here is what the write itself refuses even when
/// the caller is allowed to ask.
/// </para>
/// </remarks>
public sealed class SetPromoAccessCommandHandlerTests
{
    [Fact]
    public async Task Granting_RecordsTheInstant_AndWhoDidIt()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName);
        var actorId = Guid.NewGuid();

        var result = await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(target.Id, true, actorId, "Irina Manager", SeedRoles.ManagerName),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Granted.Should().BeTrue();
        result.GrantedAtUtc.Should().NotBeNull(
            "the instant is the flag, so a grant that recorded no time is a grant nobody can date");

        var audit = await context.Set<AuditEventOutbox>().SingleAsync();
        audit.EventType.Should().Be("PromoAccessGranted");
        audit.ChangedByUserId.Should().Be(actorId);
        audit.NewValue.Should().Contain("\"granted\":true");
    }

    [Fact]
    public async Task Revoking_ClearsTheInstant()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName);
        var handler = CreateHandler(context);
        var actorId = Guid.NewGuid();

        await handler.HandleAsync(
            new SetPromoAccessCommand(target.Id, true, actorId, "Actor", SeedRoles.AdminName), CancellationToken.None);

        var revoked = await handler.HandleAsync(
            new SetPromoAccessCommand(target.Id, false, actorId, "Actor", SeedRoles.AdminName), CancellationToken.None);

        revoked.Granted.Should().BeFalse();
        revoked.GrantedAtUtc.Should().BeNull();
        (await context.Users.FirstAsync(u => u.Id == target.Id)).PromoAccessGrantedAtUtc.Should().BeNull();

        // Two decisions, two rows: an account switched on and off twice is a history, and the current
        // state alone cannot answer who turned it off and when. Each row is read by what it says.
        //
        // It used to be read by where it sorted, and that claim was never true. The assertion ordered
        // the outbox by `Id` and required grant-then-revoke; `Id` is `Guid.CreateVersion7()`, whose time
        // field resolves to a millisecond, and both rows are written inside one. Measured on 5000
        // back-to-back pairs: all 5000 share the timestamp bits, and 2458 of them (49.2%) sort the wrong
        // way — so the test was asserting the random half of the guid, and it reddened `main`'s run for
        // `f2a326cc` when the coin finally landed the other side. The suite cannot widen that
        // millisecond: the clock is frozen for the whole assembly (finding C-10) and its classes run in
        // parallel, so advancing it from inside one test would advance it for all of them. Nor does
        // anything else promise that order — the panel reads these rows by `ChangedAtUtc` and keeps `Id`
        // only as a pagination tie-break, on a comment that says "many audit rows share one timestamp"
        // (`GetAuditEventsQueryHandler.cs:58-61`).
        var rows = await context.Set<AuditEventOutbox>().ToListAsync();

        rows.Should().HaveCount(2,
            "one row per press — a revocation that overwrote the grant would leave a history of one");

        var grantedEvent = rows.Should().ContainSingle(e => e.EventType == "PromoAccessGranted").Which;
        grantedEvent.NewValue.Should().Contain("\"granted\":true");

        var revokedEvent = rows.Should().ContainSingle(e => e.EventType == "PromoAccessRevoked").Which;
        revokedEvent.NewValue.Should().Contain("\"granted\":false");
        revokedEvent.ChangedByUserId.Should().Be(actorId,
            "the question a history answers is who turned it off, and the row has to carry that");
    }

    [Fact]
    public async Task ASwitchAlreadyInTheAskedForState_WritesNoDecision()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName);
        var handler = CreateHandler(context);

        var first = await handler.HandleAsync(
            new SetPromoAccessCommand(target.Id, true, Guid.NewGuid(), "Actor", SeedRoles.ManagerName),
            CancellationToken.None);

        var again = await handler.HandleAsync(
            new SetPromoAccessCommand(target.Id, true, Guid.NewGuid(), "Actor", SeedRoles.ManagerName),
            CancellationToken.None);

        again.Success.Should().BeTrue("the answer is the state, and the state is the asked-for one");
        again.GrantedAtUtc.Should().Be(first.GrantedAtUtc, "the second call did not re-grant anything");
        (await context.Set<AuditEventOutbox>().CountAsync()).Should().Be(1,
            "an audit row says a person decided something, and nobody decided twice");
    }

    [Fact]
    public async Task Nobody_MaySetItOnTheirOwnAccount()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var actor = await AddUserAsync(context, SeedRoles.UserName, "+380991234568");

        var result = await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(actor.Id, true, actor.Id, "Self", SeedRoles.ManagerName),
            CancellationToken.None);

        result.Forbidden.Should().BeTrue();
        result.Error.Should().Contain("your own account");
        (await context.Users.FirstAsync(u => u.Id == actor.Id)).PromoAccessGrantedAtUtc.Should().BeNull();
        (await context.Set<AuditEventOutbox>().AnyAsync()).Should().BeFalse(
            "a refused write has no decision to record");
    }

    [Fact]
    public async Task AStaffAccount_CannotHoldIt()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var manager = await AddUserAsync(context, SeedRoles.ManagerName);

        var result = await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(manager.Id, true, Guid.NewGuid(), "Actor", SeedRoles.AdminName),
            CancellationToken.None);

        result.Forbidden.Should().BeTrue(
            "a staff account with the switch on is a person who can have themselves served outside the acquirer");
        result.Error.Should().Contain("staff");
        (await context.Users.FirstAsync(u => u.Id == manager.Id)).PromoAccessGrantedAtUtc.Should().BeNull();
    }

    /// <summary>
    /// The ceiling the ruling of 2026-11-07 puts over the type rule: the ProductOwner is the one
    /// account nobody above him can take the route away from, so the switch has to work on himself.
    /// </summary>
    [Fact]
    public async Task TheProductOwner_MaySetItOnTheirOwnAccount()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var owner = await AddUserAsync(context, SeedRoles.ProductOwnerName, "+380991234569");

        var result = await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(owner.Id, true, owner.Id, "Owner", SeedRoles.ProductOwnerName),
            CancellationToken.None);

        result.Success.Should().BeTrue("the ProductOwner answers to nobody, so no second person can press it");
        (await context.Users.FirstAsync(u => u.Id == owner.Id)).PromoAccessGrantedAtUtc.Should().NotBeNull();
        (await context.Set<AuditEventOutbox>().SingleAsync()).EventType.Should().Be("PromoAccessGranted",
            "the ruling widens who may be served, not who answers for it");
    }

    [Fact]
    public async Task TheProductOwner_MayGrantItToAStaffAccount()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var manager = await AddUserAsync(context, SeedRoles.ManagerName);

        var result = await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(manager.Id, true, Guid.NewGuid(), "Owner", SeedRoles.ProductOwnerName),
            CancellationToken.None);

        result.Success.Should().BeTrue("the type rule is the ProductOwner's to waive; nothing above him holds it");
        (await context.Users.FirstAsync(u => u.Id == manager.Id)).PromoAccessGrantedAtUtc.Should().NotBeNull();
    }

    /// <summary>
    /// The waiver is the ProductOwner's alone. An Admin is above a Manager and still may not put the
    /// route on a peer, because the two-person rule is what the refusal is for.
    /// </summary>
    [Fact]
    public async Task AnAdmin_StillCannotGrantItToStaff()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var manager = await AddUserAsync(context, SeedRoles.ManagerName);

        var result = await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(manager.Id, true, Guid.NewGuid(), "Admin", SeedRoles.AdminName),
            CancellationToken.None);

        result.Forbidden.Should().BeTrue();
        result.Error.Should().Contain("staff");
    }

    [Fact]
    public async Task AnAccountWithNoRole_IsToldWhatToDoAboutIt()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var roleless = await AddUserAsync(context, roleName: null);

        var result = await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(roleless.Id, true, Guid.NewGuid(), "Actor", SeedRoles.AdminName),
            CancellationToken.None);

        result.Forbidden.Should().BeTrue();
        result.Error.Should().Contain("no role",
            "a roleless row is already unreachable through the rest of this surface, so the refusal has to say what fixes it");
    }

    [Fact]
    public async Task NotFoundIsRefusedAsNotFound_AndNotAsAForbidden()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);

        var result = await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(Guid.NewGuid(), true, Guid.NewGuid(), "Actor", SeedRoles.AdminName),
            CancellationToken.None);

        result.NotFound.Should().BeTrue();
        result.Forbidden.Should().BeFalse("an operator needs to know the account is gone, not that they lack rights");
    }

    /// <summary>
    /// The invariant the grant rests on, proved from the other side: promotion access belongs to a
    /// customer account, so an account that stops being one stops holding it. Without this, a
    /// promoted staffer keeps a column the checkout reads and the panel can no longer reach.
    /// </summary>
    [Fact]
    public async Task BecomingStaff_TakesTheAccessAway()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName);

        await CreateHandler(context).HandleAsync(
            new SetPromoAccessCommand(target.Id, true, Guid.NewGuid(), "Actor", SeedRoles.ManagerName),
            CancellationToken.None);

        var roleHandler = new SetUserRoleCommandHandler(
            context,
            NullLogger<SetUserRoleCommandHandler>.Instance,
            new AuditEventService(context),
            new LogoutEverywhereCommandHandler(context, NullLogger<LogoutEverywhereCommandHandler>.Instance));

        var promoted = await roleHandler.HandleAsync(
            new SetUserRoleCommand(target.Id, SeedRoles.ManagerName, Guid.NewGuid(), "Owner", SeedRoles.ProductOwnerName),
            CancellationToken.None);

        promoted.Success.Should().BeTrue();
        (await context.Users.FirstAsync(u => u.Id == target.Id)).PromoAccessGrantedAtUtc.Should().BeNull();

        // The role row carries the fact rather than a second audit row: the person who changed the
        // role is the person who took the access away, and two rows would read as two decisions.
        var roleChange = await context.Set<AuditEventOutbox>()
            .SingleAsync(e => e.EventType == "RoleChanged");
        roleChange.NewValue.Should().Contain("\"promoAccessTaken\":true");
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options,
        TestQrCipher.Instance);

    private static SetPromoAccessCommandHandler CreateHandler(ApplicationDbContext context) => new(
        context,
        NullLogger<SetPromoAccessCommandHandler>.Instance,
        new AuditEventService(context));

    private static async Task SeedRolesAsync(ApplicationDbContext context)
    {
        context.Roles.AddRange(
            new Role { Id = SeedRoles.ProductOwnerId, Name = SeedRoles.ProductOwnerName, Level = 100, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.AdminRoleId, Name = SeedRoles.AdminName, Level = 50, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.ManagerRoleId, Name = SeedRoles.ManagerName, Level = 10, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, Level = 0, CreatedAtUtc = TestClock.UtcNow });
        await context.SaveChangesAsync();
    }

    private static async Task<User> AddUserAsync(ApplicationDbContext context, string? roleName, string? phone = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone ?? "+380991234567",
            RoleId = RoleIdFor(roleName),
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        if (RoleIdFor(roleName) is { } roleId)
            user.Role = await context.Roles.FirstAsync(r => r.Id == roleId);

        return user;
    }

    /// <summary>
    /// Every role listed, and a name nobody recognised throws — the rule the sibling role tests use,
    /// so a typo cannot quietly resolve to the customer row and make a staff test assert about a
    /// customer.
    /// </summary>
    private static Guid? RoleIdFor(string? roleName) => roleName switch
    {
        null => null,
        SeedRoles.ProductOwnerName => SeedRoles.ProductOwnerId,
        SeedRoles.AdminName => SeedRoles.AdminRoleId,
        SeedRoles.ManagerName => SeedRoles.ManagerRoleId,
        SeedRoles.UserName => SeedRoles.UserRoleId,
        _ => throw new ArgumentOutOfRangeException(nameof(roleName), roleName, "Unmapped role name"),
    };
}
