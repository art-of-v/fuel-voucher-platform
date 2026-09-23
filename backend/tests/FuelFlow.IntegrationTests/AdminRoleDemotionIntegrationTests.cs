using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserRole;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.SharedModels;
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
/// Regression coverage for the demotion-session-revocation bug (Scenario J): demoting a staff
/// user must revoke ALL their sessions immediately. The bug lived in the interaction between
/// the real ApplicationDbContext (global NoTracking) and two chained handlers, so a mocked
/// unit test could not reproduce it — it surfaces only against a real Postgres, which is what
/// these Testcontainers tests exercise. The handler is resolved from DI so SetUserRole and
/// LogoutEverywhere share one request-scoped DbContext, exactly as in a real request.
/// </summary>
[Collection("Integration Tests")]
public class AdminRoleDemotionIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    public AdminRoleDemotionIntegrationTests(TestDatabaseFixture fixture) => _ = fixture;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    [Fact]
    public async Task DemoteStaffUser_RevokesAllSessions_BumpsTokenVersion_AndRejectsOldRefreshToken()
    {
        // Arrange: a Manager with one live session (refresh token pinned RoleNameAtIssue=Manager
        // + active device) and TokenVersion at a known, non-default value.
        var rawRefreshToken = $"raw-{Guid.NewGuid():N}";
        Guid userId;
        const int startingTokenVersion = 5;

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var managerRole = await context.Roles.FirstAsync(r => r.Name == "Manager");
            var now = DateTime.UtcNow;

            var user = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "+15550009001",
                RoleId = managerRole.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsActive = true,
                TokenVersion = startingTokenVersion,
            };
            context.Users.Add(user);
            userId = user.Id;

            context.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                DeviceId = "demote-device-1",
                RoleNameAtIssue = "Manager",
                Token = SecretsHasher.Hash(rawRefreshToken),
                ExpiresAtUtc = now.AddDays(14),
                CreatedAtUtc = now,
                IsRevoked = false,
            });

            context.Devices.Add(new Device
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DeviceId = "demote-device-1",
                PublicKey = "test-public-key",
                Status = DeviceStatus.Active,
                CreatedAt = now,
                LastSeenAt = now,
            });

            await context.SaveChangesAsync();
        }

        // Act: demote Manager -> User through the real handler. A fresh scope = a fresh
        // request-scoped DbContext — the exact wiring that used to throw an EF identity-map
        // conflict when LogoutEverywhere re-fetched the already-tracked user untracked.
        using (var scope = Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<SetUserRoleCommandHandler>();
            var result = await handler.HandleAsync(
                new SetUserRoleCommand(userId, "User", Guid.NewGuid(), "Acting PO", "ProductOwner"),
                CancellationToken.None);

            // Before the fix this was Success=false (InvalidOperationException surfaced as 400).
            result.Success.Should().BeTrue();
        }

        // Assert: role flipped, every session torn down, all access tokens invalidated.
        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var updated = await context.Users.Include(u => u.Role).FirstAsync(u => u.Id == userId);
            updated.Role!.Name.Should().Be("User");
            updated.TokenVersion.Should().Be(startingTokenVersion + 1);

            var tokens = await context.RefreshTokens.Where(t => t.UserId == userId).ToListAsync();
            tokens.Should().NotBeEmpty();
            tokens.Should().OnlyContain(t => t.IsRevoked && t.RevokedAtUtc != null);

            var devices = await context.Devices.Where(d => d.UserId == userId).ToListAsync();
            devices.Should().OnlyContain(d => d.Status == DeviceStatus.Revoked);
        }

        // End-to-end: the demoted session's refresh token can no longer mint an access token.
        var client = CreateClient();
        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh", new RefreshTokenCommand(rawRefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PromoteUser_LeavesLiveSessionIntact()
    {
        // Guards the isDemotion boundary: a PROMOTION must NOT revoke the live session (§16 —
        // new authorization applies only to a new session; the existing one keeps its old role).
        var rawRefreshToken = $"raw-{Guid.NewGuid():N}";
        Guid userId;
        const int startingTokenVersion = 3;

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userRole = await context.Roles.FirstAsync(r => r.Name == "User");
            var now = DateTime.UtcNow;

            var user = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = "+15550009002",
                RoleId = userRole.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsActive = true,
                TokenVersion = startingTokenVersion,
            };
            context.Users.Add(user);
            userId = user.Id;

            context.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                DeviceId = "promote-device-1",
                RoleNameAtIssue = "User",
                Token = SecretsHasher.Hash(rawRefreshToken),
                ExpiresAtUtc = now.AddDays(14),
                CreatedAtUtc = now,
                IsRevoked = false,
            });

            await context.SaveChangesAsync();
        }

        using (var scope = Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<SetUserRoleCommandHandler>();
            var result = await handler.HandleAsync(
                new SetUserRoleCommand(userId, "Manager", Guid.NewGuid(), "Acting PO", "ProductOwner"),
                CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var updated = await context.Users.Include(u => u.Role).FirstAsync(u => u.Id == userId);
            updated.Role!.Name.Should().Be("Manager");
            // Unchanged: a promotion does not revoke, so TokenVersion must not move.
            updated.TokenVersion.Should().Be(startingTokenVersion);

            var tokens = await context.RefreshTokens.Where(t => t.UserId == userId).ToListAsync();
            tokens.Should().OnlyContain(t => !t.IsRevoked);
        }

        // The pre-existing session still refreshes; it stays pinned to its old "User" role via
        // RoleNameAtIssue, so the promotion lands only on the next fresh login.
        var client = CreateClient();
        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh", new RefreshTokenCommand(rawRefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
