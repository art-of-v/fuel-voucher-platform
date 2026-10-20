using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// An account with no role at all is a reachable state, and it used to be a dead one.
/// <para>
/// <c>users.role_id</c> is nullable and the backfill did not cover every row. The hierarchy's
/// guards answered "no" for such an account before the ProductOwner's short-circuit, so NOBODY
/// could activate it, deactivate it, re-role it, change its email or ban it: the account was
/// stuck, and the admin SPA offered four buttons that all answered 403. The refusal also carried
/// no body, so the operator saw a toast with nothing after the colon.
/// </para>
/// <para>
/// What is fixed: the ProductOwner - whose absolute rights the null guard contradicted - can
/// manage such an account, and every refusal now says why.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class RolelessAccountIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    public RolelessAccountIntegrationTests(TestDatabaseFixture fixture) => _ = fixture;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    [Fact]
    public async Task A_ProductOwner_Can_Deactivate_An_Account_That_Has_No_Role()
    {
        var targetId = await SeedRolelessUserAsync("+15550007001");
        var client = await StaffClientAsync("+15550007002", SeedRoles.ProductOwnerName);

        var response = await client.PostAsync($"/api/admin/users/{targetId}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var target = await context.Users.FirstAsync(u => u.Id == targetId);
        target.IsActive.Should().BeFalse();
        target.RoleId.Should().BeNull("the account is still roleless - it was deactivated, not re-roled");
    }

    [Fact]
    public async Task A_ProductOwner_Can_Give_An_Account_That_Has_No_Role_A_Role()
    {
        // This is the way out of the state: an account with no role can be given one.
        var targetId = await SeedRolelessUserAsync("+15550007003");
        var client = await StaffClientAsync("+15550007004", SeedRoles.ProductOwnerName);

        var response = await client.PostAsJsonAsync($"/api/admin/users/{targetId}/role", new { role = "User" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var target = await context.Users.Include(u => u.Role).FirstAsync(u => u.Id == targetId);
        target.Role.Should().NotBeNull("the account is no longer outside the hierarchy");
        target.Role!.Name.Should().Be("User");
    }

    [Fact]
    public async Task A_Manager_Is_Refused_And_The_Refusal_Says_Why()
    {
        var targetId = await SeedRolelessUserAsync("+15550007005");
        var client = await StaffClientAsync("+15550007006", SeedRoles.ManagerName);

        var response = await client.PostAsync($"/api/admin/users/{targetId}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The refusal used to be an empty body: the operator saw "failed to change the user's
        // status" and had nothing to act on. Now it says who can.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("no role");
        body.Should().Contain("ProductOwner");
    }

    [Fact]
    public async Task A_Manager_Cannot_Reach_An_Account_That_Has_No_Role_At_All()
    {
        // The same refusal, reached through the role endpoint: a Manager could not re-role the
        // account either, so the account was stuck for good.
        var targetId = await SeedRolelessUserAsync("+15550007007");
        var client = await StaffClientAsync("+15550007008", SeedRoles.ManagerName);

        var response = await client.PostAsJsonAsync($"/api/admin/users/{targetId}/role", new { role = "User" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Users.FirstAsync(u => u.Id == targetId)).RoleId.Should().BeNull();
    }

    /// <summary>
    /// An account the hierarchy cannot place: the row exists, and its role does not.
    /// </summary>
    private async Task<Guid> SeedRolelessUserAsync(string phone)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            TokenVersion = 1,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// A staff client: the user is seeded WITH its role, so the token minted by the login carries
    /// the role claim the "Staff" policy checks.
    /// </summary>
    private async Task<HttpClient> StaffClientAsync(string phone, string roleName)
    {
        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await context.Roles.FirstAsync(r => r.Name == roleName);
            var now = DateTime.UtcNow;

            context.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phone,
                RoleId = role.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsActive = true,
                TokenVersion = 1,
            });
            await context.SaveChangesAsync();
        }

        var client = CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber = phone, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
