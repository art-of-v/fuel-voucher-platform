using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Empties the tables a payment test writes, so tests inside one class cannot see each other's rows.
/// </summary>
/// <remarks>
/// The per-class database clone gives isolation between classes but not between tests *within* a
/// class, and that matters more than usual here: the reconciliation poller selects every
/// <c>PendingPayment</c> order in the age window and asserts on <c>OutboxEvents.Should().BeEmpty()</c>.
/// Left alone, one test's order would be recovered by the next test's poll, and the empty-table
/// assertions would pass or fail depending on execution order.
/// <para>
/// <c>TRUNCATE</c> rather than <c>DELETE</c> deliberately: a posted journal entry's lines cannot be
/// deleted - that is the rule the immutability trigger enforces - and these tests genuinely post
/// entries. TRUNCATE does not fire row-level triggers, so the reset is possible where a cleanup by
/// DELETE would be refused. The class's whole database is discarded afterwards anyway, so nothing
/// here is destroying anything a later test needs.
/// </para>
/// </remarks>
public static class PaymentTestReset
{
    private static readonly string[] Tables =
    [
        "journal_lines",
        "journal_entries",
        "document_sequences",
        "accounting_periods",
        "outbox_events",
        "webhook_events",
        "payments",
        "orders",
        "users",
    ];

    public static async Task RunAsync(ApplicationDbContext context)
    {
        // TRUNCATE is not free and the first test on a fresh clone has nothing to clear, so check
        // the tables a settled payment actually touches before paying for the truncate.
        if (!await context.Orders.AnyAsync()
            && !await context.JournalEntries.AnyAsync()
            && !await context.Payments.AnyAsync())
        {
            return;
        }

        foreach (var table in Tables)
        {
            await context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {table} CASCADE;");
        }
    }
}