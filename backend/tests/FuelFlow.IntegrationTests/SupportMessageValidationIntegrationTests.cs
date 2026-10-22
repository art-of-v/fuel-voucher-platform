using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The public support form refuses a broken command, and stores nothing when it does.
///
/// <para>
/// The rules behind the refusal used to be a FluentValidation validator behind a global
/// filter - the validation layer the code-quality review found empty (C-5). The package is
/// gone and the rules are an explicit check, so the endpoint's answer is what this test
/// pins: a 400 whose body names the fields that broke, and no row in the database.
/// </para>
/// <para>
/// The endpoint is anonymous and rate-limited (five messages per hour per IP), so this class
/// posts twice and no more.
/// </para>
/// <para>
/// Both tests share the class's own database, and xUnit runs them in an order it chooses - so
/// the assertions are scoped to the row they are about rather than to the table's count.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class SupportMessageValidationIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SupportMessageValidationIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ABrokenCommand_IsRefused_AndNothingIsStored()
    {
        using var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/support/messages", new
        {
            email = "not-an-email",
            message = "",
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest,
            "the form's rules are checked before anything is stored");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Email", "the body names the field that broke");
        body.Should().Contain("Message");

        await using var context = CreateContext();
        (await context.SupportMessages.CountAsync(m => m.Email == "not-an-email")).Should().Be(
            0,
            "a refused message is not a message: nothing may reach the table");
    }

    [Fact]
    public async Task AGoodCommand_IsAccepted_AndStored()
    {
        using var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/support/messages", new
        {
            email = "customer@example.com",
            message = "Оплата пройшла, талона немає.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await using var context = CreateContext();
        var stored = await context.SupportMessages.SingleAsync(m => m.Email == "customer@example.com");
        stored.Message.Should().Contain("Оплата пройшла");
    }

    /// <summary>
    /// The per-class clone the fixture made, not the container's default database.
    /// </summary>
    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
