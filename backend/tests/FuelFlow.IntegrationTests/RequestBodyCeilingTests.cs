using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The request-body ceiling, against the running application (planning finding S-15,
/// issue #624).
/// </summary>
/// <remarks>
/// <para>
/// The ceiling used to be 25 MB <i>globally</i>, because 25 MB is what a voucher-PDF import
/// needs. A global ceiling is a ceiling on every route, so every endpoint - the anonymous ones
/// included - could be handed 25 MB of body, and the global rate limit of 300 requests a minute
/// per source address turns that into about 7.5 GB a minute of buffering against a container
/// with an 896 MB memory limit.
/// </para>
/// <para>
/// These tests are the acceptance criterion of the finding: a body over the ceiling gets 413,
/// and the import route still takes the file it is sized for.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class RequestBodyCeilingTests : IClassFixture<TestDatabaseFixture>
{
    private const int OneMebibyte = 1024 * 1024;

    private readonly TestDatabaseFixture _fixture;

    public RequestBodyCeilingTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The running server carries the ceiling, not just the source: the number is read out of
    /// the configuration the host actually booted with, so it cannot be raised without this
    /// test failing.
    /// </summary>
    [Fact]
    public void The_Running_Server_Carries_The_Global_Ceiling()
    {
        using var scope = _fixture.Services.CreateScope();

        var kestrel = scope.ServiceProvider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        kestrel.Limits.MaxRequestBodySize.Should().Be(OneMebibyte,
            "the body ceiling is what a JSON body needs; the routes that take a file raise it for themselves");

        var form = scope.ServiceProvider.GetRequiredService<IOptions<FormOptions>>().Value;
        form.MultipartBodyLengthLimit.Should().Be(OneMebibyte,
            "the multipart reader's ceiling is the same number, or it would be the effective one");
    }

    /// <summary>
    /// The finding's first half: a body over the ceiling is refused, on an endpoint anyone can
    /// reach. <c>POST /api/support/messages</c> is anonymous on purpose - it is the public
    /// contact form - which is exactly why it must not be the endpoint that buffers 25 MB.
    /// </summary>
    [Fact]
    public async Task An_Oversized_Body_Is_Refused_With_413()
    {
        var client = _fixture.CreateClient();
        var body = new string('x', 2 * OneMebibyte);

        var response = await client.PostAsync(
            "/api/support/messages",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        // The refusal is the one envelope every other failure of this API uses, so a client
        // reads it the way it reads everything else.
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        document.GetProperty("status").GetInt32().Should().Be(413);
        document.GetProperty("title").GetString().Should().Be("Payload Too Large");
        document.GetProperty("code").GetString().Should().Be("request_body_too_large");
        document.GetProperty("instance").GetString().Should().Be("/api/support/messages");
    }

    /// <summary>
    /// A body within the ceiling still reaches the endpoint: the ceiling refuses the oversized,
    /// it does not refuse everything.
    /// </summary>
    [Fact]
    public async Task A_Body_Within_The_Ceiling_Still_Reaches_The_Endpoint()
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsync(
            "/api/support/messages",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge);
    }

    /// <summary>
    /// The finding's second half: the import route still takes the file it is sized for. The
    /// route declares its own ceiling, and a file over the global one is delivered to the
    /// endpoint - which refuses it for what it IS (it is not a PDF), not for how big it is.
    /// </summary>
    [Fact]
    public async Task The_Import_Still_Takes_A_File_Larger_Than_The_Global_Ceiling()
    {
        var client = await StaffClientAsync("+380995550901");

        var file = new byte[2 * OneMebibyte];
        Array.Fill(file, (byte)'x');

        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(part, "file", "vouchers.pdf");
        content.Add(new StringContent(Guid.NewGuid().ToString()), "supplierId");

        var response = await client.PostAsync("/api/voucher-catalog/import", content);

        response.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge,
            "the import route raises the ceiling for itself - a 2 MiB file must reach the endpoint");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Only PDF files are supported",
            "the file was delivered and read: the endpoint refused its content, not its size");
    }

    /// <summary>
    /// A staff client: the user is seeded WITH its role, so the token carries the role claim
    /// the VoucherWrite policy checks.
    /// </summary>
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
