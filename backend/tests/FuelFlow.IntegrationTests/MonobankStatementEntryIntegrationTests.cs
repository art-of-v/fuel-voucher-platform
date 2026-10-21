using FluentAssertions;
using FuelFlow.Features.Acquiring;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The <c>monobank_statement_entries</c> table against real PostgreSQL (#118 step 2).
/// </summary>
/// <remarks>
/// <para>
/// Only the constraints matter here, and only PostgreSQL can prove them - an in-memory brand
/// enforces neither the unique index nor the column mapping, so a test written against it would
/// pass on a schema that cannot actually store these rows.
/// </para>
/// <para>
/// The one constraint worth this much ceremony is the unique key being scoped by merchant: the
/// sandbox and the live merchant are separate accounts that can legitimately report the same
/// acquirer statement id, and a key on <c>statement_id</c> alone would make the second merchant's
/// import collide with the first and throw.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class MonobankStatementEntryIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MonobankStatementEntryIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static MonobankStatementEntry Entry(
        MonobankMerchant merchant,
        string statementId,
        long? amountKopecks = 1000) => new()
        {
            Id = Guid.NewGuid(),
            Merchant = merchant,
            StatementId = statementId,
            InvoiceId = "INV-" + statementId,
            Reference = "order-" + statementId,
            // The payment purpose as the acquirer shows it on the receipt. Evidence for a dispute,
            // never a match key.
            Destination = "purpose-" + statementId,
            AmountKopecks = amountKopecks,
            Ccy = 980,
            Status = "settled",
            AcquirerCreatedAtUtc = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc),
            RawPayload = """{"statementId":"ST1","amount":1000}""",
            FirstSeenAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task An_Entry_Round_Trips_With_Its_Columns()
    {
        await ResetAsync();

        var id = await SaveAsync(Entry(MonobankMerchant.Live, "ST1", amountKopecks: 123456));

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var loaded = await context.MonobankStatementEntries.SingleAsync(e => e.Id == id);

        // Kopecks, not a decimal: the reconciler compares these against our own kopeck-exact
        // amounts, and a round-trip through decimal is where the 978.57/979 bug class starts.
        loaded.AmountKopecks.Should().Be(123456);
        loaded.Merchant.Should().Be(MonobankMerchant.Live);
        loaded.StatementId.Should().Be("ST1");
        loaded.Ccy.Should().Be(980);
        // The destination is stored beside the reference, not instead of it: the act needs both.
        loaded.Destination.Should().Be("purpose-ST1");
        loaded.Reference.Should().Be("order-ST1");
        loaded.RawPayload.Should().Contain("ST1");
        loaded.AcquirerCreatedAtUtc.Should().Be(new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task The_Same_Statement_Id_Cannot_Be_Stored_Twice_For_One_Merchant()
    {
        await ResetAsync();
        await SaveAsync(Entry(MonobankMerchant.Live, "ST1"));

        // The idempotency guarantee for re-importing a period. Without the unique key, re-running an
        // import would silently double every row and the period total would come out doubled.
        var act = () => SaveAsync(Entry(MonobankMerchant.Live, "ST1"));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task The_Same_Statement_Id_Is_Allowed_Under_A_Different_Merchant()
    {
        await ResetAsync();

        await SaveAsync(Entry(MonobankMerchant.Live, "ST1"));
        await SaveAsync(Entry(MonobankMerchant.Sandbox, "ST1"));

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Sandbox and live are separate Monobank accounts and can report the same acquirer id.
        // A key on statement_id alone would reject this second row and take the import down.
        (await context.MonobankStatementEntries.CountAsync(e => e.StatementId == "ST1"))
            .Should().Be(2);
    }

    [Fact]
    public async Task A_Null_Amount_And_A_Null_Reference_Are_Accepted()
    {
        await ResetAsync();

        // The acquirer is the authority and reports partial rows; refusing to store them would mean
        // losing exactly the entries a reconciliation most needs to see.
        var entry = Entry(MonobankMerchant.Live, "ST-partial", amountKopecks: null);
        entry.AmountKopecks = null;
        entry.InvoiceId = null;
        entry.Reference = null;
        entry.RawPayload = null;

        var id = await SaveAsync(entry);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var loaded = await context.MonobankStatementEntries.SingleAsync(e => e.Id == id);

        loaded.AmountKopecks.Should().BeNull();
        loaded.Reference.Should().BeNull();
        loaded.RawPayload.Should().BeNull();
    }

    private async Task<Guid> SaveAsync(MonobankStatementEntry entry)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.MonobankStatementEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    private async Task ResetAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "monobank_statement_entries" RESTART IDENTITY CASCADE""");
    }
}