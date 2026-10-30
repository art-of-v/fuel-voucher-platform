using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>POST /api/admin/ledger/entries</c>, end to end (planning #795).
/// </summary>
/// <remarks>
/// The service behaviour is covered in <see cref="ManualJournalEntryTests"/>; these tests are about
/// the HTTP edge: who may call it, where the operator comes from, and how a request that is not a
/// posting is answered. The last one matters in particular - an unbalanced entry has to come back as
/// a 400 with the reason, not as a 500, because the caller can fix it.
/// </remarks>
[Collection("Integration Tests")]
public sealed class ManualJournalEntryEndpointTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ManualJournalEntryEndpointTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private static int _codeSeq = 7000;
    private static int _dateSeq;

    /// <summary>Unique account codes and months: the class shares one database and never cleans up.</summary>
    private static string NewCode() => Interlocked.Increment(ref _codeSeq).ToString();

    private static DateOnly NewDate() => new DateOnly(2036, 1, 1).AddMonths(Interlocked.Increment(ref _dateSeq));

    private async Task<(string Debit, string Credit)> SeedAccountsAsync()
    {
        await using var context = _fixture.CreateOwnerContext();

        var debit = NewCode();
        var credit = NewCode();

        foreach (var (code, type, side) in new[]
                 {
                     (debit, "Vat", "Debit"),
                     (credit, "Revenue", "Credit"),
                 })
        {
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, is_postable, is_active) " +
                $"VALUES ('{Guid.NewGuid()}', '{code}', 'Рахунок {code}', '{type}', '{side}', true, true)");
        }

        return (debit, credit);
    }

    private static object Body(DateOnly date, string reference, decimal debit, decimal credit, string debitCode, string creditCode)
        => new
        {
            entryDate = date.ToString("yyyy-MM-dd"),
            reference,
            description = "D3: VAT deduction moved from 703 to 704",
            lines = new[]
            {
                new { accountCode = debitCode, debitUah = debit, creditUah = 0m },
                new { accountCode = creditCode, debitUah = 0m, creditUah = credit },
            },
        };

    [Fact]
    public async Task An_Operator_Posts_And_Replaying_The_Reference_Returns_The_Same_Entry()
    {
        var (debitCode, creditCode) = await SeedAccountsAsync();
        var (client, userId) = await StaffClientAsync("+380000000701");
        var date = NewDate();
        var reference = $"endpoint-{Guid.NewGuid():N}";
        var body = Body(date, reference, 2298.69m, 2298.69m, debitCode, creditCode);

        var first = await client.PostAsJsonAsync("/api/admin/ledger/entries", body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var posted = await first.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = posted.GetProperty("entryId").GetGuid();
        posted.GetProperty("entryNo").GetInt64().Should().BeGreaterThan(0);
        posted.GetProperty("totalUah").GetDecimal().Should().Be(2298.69m);

        // The operator is the session's, not the body's: the route answers with the person the
        // journal will be asked about.
        posted.GetProperty("postedByUserId").GetGuid().Should().Be(userId);

        var replay = await client.PostAsJsonAsync("/api/admin/ledger/entries", body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("entryId").GetGuid()
            .Should().Be(entryId, "a retried request must land on the entry the first one wrote");

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await context.JournalEntries
            .Include(e => e.Lines)
            .SingleAsync(e => e.Id == entryId);

        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.SourceEvent.Should().Be("manual.adjustment");
        entry.SourceType.Should().Be(JournalSourceType.Adjustment);
        entry.PostedByUserId.Should().Be(userId);
        entry.PostedAtUtc.Should().NotBeNull();
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Sum(l => l.DebitUah).Should().Be(2298.69m);
        entry.Lines.Sum(l => l.CreditUah).Should().Be(2298.69m);

        var sameReference = await context.JournalEntries
            .CountAsync(e => e.SourceEvent == "manual.adjustment" && e.SourceId == entry.SourceId);
        sameReference.Should().Be(1);
    }

    [Fact]
    public async Task An_Unbalanced_Entry_Is_A_400_And_Not_A_500()
    {
        var (debitCode, creditCode) = await SeedAccountsAsync();
        var (client, _) = await StaffClientAsync("+380000000702");

        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/entries",
            Body(NewDate(), $"endpoint-{Guid.NewGuid():N}", 100m, 99.99m, debitCode, creditCode));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Contain("does not balance");
        problem.GetProperty("code").GetString().Should().Be("ledger_posting_failed");
    }

    [Fact]
    public async Task A_Customer_Cannot_Post_A_Manual_Entry()
    {
        // Reading the books is Staff; writing them is the money capability, and a customer has
        // neither (#795).
        var (debitCode, creditCode) = await SeedAccountsAsync();
        var client = await CustomerClientAsync("+380000000703");

        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/entries",
            Body(NewDate(), $"endpoint-{Guid.NewGuid():N}", 100m, 100m, debitCode, creditCode));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>The e2e suites' login: send a code, verify with the development code.</summary>
    private async Task<HttpClient> AuthenticatedClientAsync(string phoneNumber)
    {
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

    private Task<HttpClient> CustomerClientAsync(string phoneNumber) => AuthenticatedClientAsync(phoneNumber);

    /// <summary>A staff client whose role satisfies the money capability, with the user's id known.</summary>
    private async Task<(HttpClient Client, Guid UserId)> StaffClientAsync(string phoneNumber)
    {
        var userId = Guid.NewGuid();

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await context.Roles.FirstAsync(r => r.Name == SeedRoles.AdminName);
            var now = TestClock.UtcNow;

            context.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = phoneNumber,
                RoleId = role.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsActive = true,
                TokenVersion = 1,
            });
            await context.SaveChangesAsync();
        }

        return (await AuthenticatedClientAsync(phoneNumber), userId);
    }
}
