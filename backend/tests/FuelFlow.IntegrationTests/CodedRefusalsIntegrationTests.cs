using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The refusals the admin words itself leave the API carrying a stable machine code beside their
/// English sentence, so a localised panel never prints prose it cannot translate (planning #763).
/// </summary>
/// <remarks>
/// One test, one staff client, one assertion per refusal: the contract is not the wording but that
/// every endpoint the screen registers a sentence for actually sends the key that sentence is filed
/// under. A route whose code is renamed here fails this test rather than silently falling back to
/// English on a Ukrainian panel.
/// </remarks>
[Collection("Integration Tests")]
public sealed class CodedRefusalsIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const string StaffPhone = "+380000000601";

    private readonly TestDatabaseFixture _fixture;

    public CodedRefusalsIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Every_Refusal_The_Panel_Words_Carries_Its_Code()
    {
        var staff = await StaffClientAsync(StaffPhone);

        (await PostAsync(staff, "/api/admin/feedback/status", new
        {
            ids = Array.Empty<Guid>(),
            status = "New"
        })).Should().Be("feedback_no_ids");

        (await PostAsync(staff, "/api/admin/feedback/status", new
        {
            ids = new[] { Guid.NewGuid() },
            status = "Nonsense"
        })).Should().Be("feedback_unknown_status");

        (await CodeOfAsync(await staff.DeleteAsync("/api/admin/feedback"))).Should()
            .Be("feedback_delete_refused");

        (await PostAsync(staff, "/api/admin/errors/resolve", new
        {
            ids = Array.Empty<Guid>(),
            resolved = true
        })).Should().Be("errorlogs_no_ids");

        (await CodeOfAsync(await staff.PutAsJsonAsync("/api/admin/settings", new { }))).Should()
            .Be("settings_none_supplied");

        (await CodeOfAsync(await staff.PutAsync(
            "/api/admin/qa-test-access",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json")))).Should()
            .Be("qa_test_access_no_value");

        // The binder requires ExternalId and Name to be present at all; the handler is what refuses
        // the blank name and the missing brand.
        (await PostAsync(staff, "/api/admin/brand-stations", new { externalId = "code-test", name = "" }))
            .Should().Be("station_brand_and_name_required");
    }

    private static async Task<string?> PostAsync(HttpClient client, string route, object body)
        => await CodeOfAsync(await client.PostAsJsonAsync(route, body));

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>A staff client: the user is seeded with the Admin role, so the token carries the
    /// role claim every staff policy on these routes checks.</summary>
    private async Task<HttpClient> StaffClientAsync(string phoneNumber)
    {
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await context.Roles.FirstAsync(r => r.Name == SeedRoles.AdminName);
            var now = TestClock.UtcNow;

            context.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                RoleId = role.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsActive = true,
                TokenVersion = 1,
            });
            await context.SaveChangesAsync();
        }

        var client = _fixture.CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
