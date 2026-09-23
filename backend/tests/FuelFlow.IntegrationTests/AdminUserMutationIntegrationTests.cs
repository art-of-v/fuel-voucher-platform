using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserActive;
using FuelFlow.Features.Auth.AdminUser.SetUserBanned;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Users.UpdateUser;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for the admin/user mutation handlers that mutate a loaded entity under
/// the global NoTracking default (DatabaseSetup). InMemory cannot model the identity-map / tracking
/// nuance those handlers depend on — the same class of gap that hid the demotion-revocation bug —
/// so these run against a Testcontainers Postgres and resolve each handler from DI, exactly as a
/// real request does. All three handlers share ONE container via the <see cref="TestDatabaseFixture"/>
/// class fixture, so the coverage stays cheap.
/// </summary>
[Collection("Integration Tests")]
public class AdminUserMutationIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    public AdminUserMutationIntegrationTests(TestDatabaseFixture fixture) => _ = fixture;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    // ---- seeding helpers (each opens its own scope, mirroring the per-request DbContext) --------

    private async Task<Guid> SeedUserAsync(
        string roleName, string phone,
        bool isActive = true, bool isBanned = false, int tokenVersion = 1,
        string? firstName = null, string? lastName = null, DateOnly? birthdate = null)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await context.Roles.FirstAsync(r => r.Name == roleName);
        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = role.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = isActive,
            IsBanned = isBanned,
            TokenVersion = tokenVersion,
            FirstName = firstName,
            LastName = lastName,
            Birthdate = birthdate,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task AddLiveSessionAsync(Guid userId, string roleNameAtIssue, string rawRefreshToken, string deviceId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = Guid.NewGuid(),
            DeviceId = deviceId,
            RoleNameAtIssue = roleNameAtIssue,
            Token = SecretsHasher.Hash(rawRefreshToken),
            ExpiresAtUtc = now.AddDays(14),
            CreatedAtUtc = now,
            IsRevoked = false,
        });

        context.Devices.Add(new Device
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceId = deviceId,
            PublicKey = "test-public-key",
            Status = DeviceStatus.Active,
            CreatedAt = now,
            LastSeenAt = now,
        });

        await context.SaveChangesAsync();
    }

    // ---- SetUserBannedCommandHandler ------------------------------------------------------------

    [Fact]
    public async Task BanUser_RevokesSessionsAndDevices_BumpsTokenVersion_AndRejectsRefreshToken()
    {
        // A ban is a hard revocation: flag set, TokenVersion bumped, every refresh token and active
        // device torn down. This is the multi-entity, tracked-mutation path InMemory can't vouch for.
        var raw = $"raw-{Guid.NewGuid():N}";
        const int startingTokenVersion = 7;
        var userId = await SeedUserAsync(SeedRoles.ManagerName, "+15550010001", tokenVersion: startingTokenVersion);
        await AddLiveSessionAsync(userId, SeedRoles.ManagerName, raw, "ban-device-1");

        using (var scope = Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<SetUserBannedCommandHandler>();
            var result = await handler.HandleAsync(
                new SetUserBannedCommand(userId, true, Guid.NewGuid(), "Acting PO", SeedRoles.ProductOwnerName),
                CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updated = await context.Users.FirstAsync(u => u.Id == userId);
            updated.IsBanned.Should().BeTrue();
            updated.TokenVersion.Should().Be(startingTokenVersion + 1);

            var tokens = await context.RefreshTokens.Where(t => t.UserId == userId).ToListAsync();
            tokens.Should().OnlyContain(t => t.IsRevoked && t.RevokedAtUtc != null);

            var devices = await context.Devices.Where(d => d.UserId == userId).ToListAsync();
            devices.Should().OnlyContain(d => d.Status == DeviceStatus.Revoked);
        }

        // End-to-end: the banned user's refresh token can no longer mint an access token.
        var client = CreateClient();
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenCommand(raw));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BanUser_IsIdempotent_WhenAlreadyBanned()
    {
        // Re-banning an already-banned user is a no-op early return: it must NOT run a second
        // revocation cycle, so TokenVersion stays put.
        const int startingTokenVersion = 4;
        var userId = await SeedUserAsync(
            SeedRoles.ManagerName, "+15550010002", isBanned: true, tokenVersion: startingTokenVersion);

        using (var scope = Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<SetUserBannedCommandHandler>();
            var result = await handler.HandleAsync(
                new SetUserBannedCommand(userId, true, Guid.NewGuid(), "Acting PO", SeedRoles.ProductOwnerName),
                CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updated = await context.Users.FirstAsync(u => u.Id == userId);
            updated.IsBanned.Should().BeTrue();
            updated.TokenVersion.Should().Be(startingTokenVersion);
        }
    }

    [Fact]
    public async Task BanUser_RejectsBanningTheLastActiveProductOwner()
    {
        // The last-owner guard runs a real COUNT over Postgres. This container seeds no other
        // ProductOwner, so the single seeded PO is the last one and the ban must be refused
        // BEFORE any mutation — otherwise the platform could be locked out of staff management.
        var userId = await SeedUserAsync(SeedRoles.ProductOwnerName, "+15550010003");

        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SetUserBannedCommandHandler>();
        var result = await handler.HandleAsync(
            new SetUserBannedCommand(userId, true, Guid.NewGuid(), "Acting PO", SeedRoles.ProductOwnerName),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("last active ProductOwner");

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var target = await context.Users.FirstAsync(u => u.Id == userId);
        target.IsBanned.Should().BeFalse("the guard must fire before the entity is mutated");
    }

    // ---- SetUserActiveCommandHandler ------------------------------------------------------------

    [Fact]
    public async Task DeactivateUser_PersistsInactiveFlag_WithoutRevokingSession()
    {
        // Deactivation is the deliberate opposite of a ban: the flag must persist (the NoTracking
        // write path), but TokenVersion is untouched and no token is revoked — an inactive user
        // keeps their session and loses only payment eligibility (RefreshTokenCommandHandler §is_active).
        var raw = $"raw-{Guid.NewGuid():N}";
        const int startingTokenVersion = 6;
        var userId = await SeedUserAsync(SeedRoles.UserName, "+15550010004", tokenVersion: startingTokenVersion);
        await AddLiveSessionAsync(userId, SeedRoles.UserName, raw, "active-device-1");

        using (var scope = Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<SetUserActiveCommandHandler>();
            var result = await handler.HandleAsync(
                new SetUserActiveCommand(userId, false, Guid.NewGuid(), "Acting Manager", SeedRoles.ManagerName),
                CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updated = await context.Users.FirstAsync(u => u.Id == userId);
            updated.IsActive.Should().BeFalse();
            updated.TokenVersion.Should().Be(startingTokenVersion);

            var tokens = await context.RefreshTokens.Where(t => t.UserId == userId).ToListAsync();
            tokens.Should().OnlyContain(t => !t.IsRevoked);
        }

        // End-to-end: a deactivated user's existing session still refreshes.
        var client = CreateClient();
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenCommand(raw));
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeactivateUser_IsIdempotent_WhenAlreadyInactive()
    {
        var userId = await SeedUserAsync(SeedRoles.UserName, "+15550010005", isActive: false);

        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SetUserActiveCommandHandler>();
        var result = await handler.HandleAsync(
            new SetUserActiveCommand(userId, false, Guid.NewGuid(), "Acting Manager", SeedRoles.ManagerName),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Users.FirstAsync(u => u.Id == userId)).IsActive.Should().BeFalse();
    }

    // ---- UpdateUserCommandHandler ---------------------------------------------------------------

    [Fact]
    public async Task UpdateUser_PersistsProfileFields_UnderNoTrackingContext()
    {
        // Unlike the admin handlers, this one loads the user WITHOUT .AsTracking() and relies on an
        // explicit _context.Users.Update(user) to mark it Modified. Verify that write path actually
        // persists on real Postgres — the exact NoTracking subtlety InMemory papers over.
        var userId = await SeedUserAsync(
            SeedRoles.UserName, "+15550010006",
            firstName: "Old", lastName: "Name", birthdate: new DateOnly(1990, 1, 1));

        UpdateUserResponse response;
        using (var scope = Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<UpdateUserCommandHandler>();
            response = await handler.HandleAsync(
                new UpdateUserCommand(
                    userId.ToString(), Email: null, FirstName: "New", LastName: "Person",
                    Birthdate: new DateOnly(1985, 6, 15), ProfileImageUrl: "https://cdn.example/pic.png"),
                CancellationToken.None);
        }

        response.FirstName.Should().Be("New");
        response.EmailChangePending.Should().BeFalse();

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reloaded = await context.Users.FirstAsync(u => u.Id == userId);
            reloaded.FirstName.Should().Be("New");
            reloaded.LastName.Should().Be("Person");
            reloaded.Birthdate.Should().Be(new DateOnly(1985, 6, 15));
            reloaded.ProfileImageUrl.Should().Be("https://cdn.example/pic.png");
        }
    }
}
