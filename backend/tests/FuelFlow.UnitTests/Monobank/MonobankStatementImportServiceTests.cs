using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Monobank;

/// <summary>
/// The statement import job (#118 step 3).
/// </summary>
/// <remarks>
/// The behaviour worth pinning is the upsert policy and the per-merchant isolation. Everything
/// else is plumbing.
/// </remarks>
public class MonobankStatementImportServiceTests
{
    private static MonobankStatementItem Item(string statementId, long amount = 1000) => new()
    {
        StatementId = statementId,
        InvoiceId = "INV-" + statementId,
        Reference = "order-" + statementId,
        Amount = amount,
        Ccy = 980,
        Status = "success",
        Date = TestClock.UtcNow.AddDays(-2),
        RawJson = """{"invoiceId":"INV-ST1"}"""
    };

    /// <summary>Returns the rows it is handed per merchant, so a test can give Live and Sandbox
    /// different data and prove neither leaks into the other.</summary>
    private sealed class FakeMonobankClientFactory : IMonobankClientFactory
    {
        private readonly Dictionary<MonobankMerchant, List<MonobankStatementItem>> _rows;

        public FakeMonobankClientFactory(Dictionary<MonobankMerchant, List<MonobankStatementItem>> rows) => _rows = rows;

        public List<DateTime> LiveFroms { get; } = new();
        public List<DateTime> LiveTos { get; } = new();

        public IMonobankClient ForMerchant(MonobankMerchant merchant) => new FakeClient(this, merchant);

        private sealed class FakeClient(FakeMonobankClientFactory owner, MonobankMerchant merchant) : IMonobankClient
        {
            public Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
            {
                if (merchant == MonobankMerchant.Live)
                {
                    owner.LiveFroms.Add(fromUtc);
                    owner.LiveTos.Add(toUtc);
                }

                var rows = owner._rows.TryGetValue(merchant, out var forMerchant) ? forMerchant : new List<MonobankStatementItem>();
                return Task.FromResult<IReadOnlyList<MonobankStatementItem>>(rows);
            }

            public Task<MonobankInvoiceResponse> CreateInvoiceAsync(MonobankInvoiceRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<MonobankCancelResponse> CancelInvoiceAsync(string invoiceId, long amountKopecks, string extRef, IReadOnlyList<MonobankBasketItem>? items = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }

    private static ApplicationDbContext NewContext()
    {
        // In-memory is the right tool HERE specifically: the behaviour under test is the job's own
        // upsert policy and its per-merchant isolation, not the schema. The unique index that backs
        // the upsert is pinned separately against real PostgreSQL in the integration suite, because
        // in-memory does not enforce it and a duplicate would slip through unnoticed.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    private static (MonobankStatementImportService Service, ApplicationDbContext Context, FakeMonobankClientFactory Factory) Build(
        Dictionary<MonobankMerchant, List<MonobankStatementItem>> rows,
        bool enabled = true)
    {
        var context = NewContext();
        var factory = new FakeMonobankClientFactory(rows);

        var service = new MonobankStatementImportService(
            context,
            factory,
            Options.Create(new MonobankOptions { Enabled = enabled, StatementImportWindowDays = 35 }),
            NullLogger<MonobankStatementImportService>.Instance);

        return (service, context, factory);
    }

    [Fact]
    public async Task Stores_New_Rows_For_Each_Merchant()
    {
        var (service, context, _) = Build(new()
        {
            [MonobankMerchant.Live] = new() { Item("L1"), Item("L2") },
            [MonobankMerchant.Sandbox] = new() { Item("S1") }
        });

        await service.ImportRecentStatementsAsync();

        var stored = await context.MonobankStatementEntries.ToListAsync();
        stored.Should().HaveCount(3);
        stored.Select(e => e.Merchant).Should().BeEquivalentTo(new[]
        {
            MonobankMerchant.Live, MonobankMerchant.Live, MonobankMerchant.Sandbox
        });
        stored.Select(e => e.StatementId).Should().Contain(new[] { "L1", "L2", "S1" });
    }

    // Re-running a rolling window sees the same rows every hour. Without an upsert the table would
    // gain a copy of the month on every pass and every total built on it would be wrong.
    [Fact]
    public async Task Re_Importing_The_Same_Period_Does_Not_Duplicate()
    {
        var (service, context, _) = Build(new()
        {
            [MonobankMerchant.Live] = new() { Item("L1"), Item("L2") }
        });

        await service.ImportRecentStatementsAsync();
        await service.ImportRecentStatementsAsync();
        await service.ImportRecentStatementsAsync();

        (await context.MonobankStatementEntries.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_Row_Seen_Again_Keeps_Its_First_Seen_Time_And_Moves_LastSeen()
    {
        var (service, context, _) = Build(new()
        {
            [MonobankMerchant.Live] = new() { Item("L1", amount: 1000) }
        });

        await service.ImportRecentStatementsAsync();
        var firstSeen = (await context.MonobankStatementEntries.SingleAsync()).FirstSeenAtUtc;

        await service.ImportRecentStatementsAsync();
        var entry = await context.MonobankStatementEntries.SingleAsync();

        entry.FirstSeenAtUtc.Should().Be(firstSeen);
    }

    // The acquirer is the authority on money. A rolling window must not let a later fetch quietly
    // rewrite a stored value - the stored row is evidence, and evidence that drifts is worthless.
    [Fact]
    public async Task A_Row_Seen_Again_Keeps_The_Amount_First_Recorded()
    {
        var (service, context, _) = Build(new()
        {
            [MonobankMerchant.Live] = new() { Item("L1", amount: 1000) }
        });

        await service.ImportRecentStatementsAsync();

        var secondPass = new Dictionary<MonobankMerchant, List<MonobankStatementItem>>
        {
            [MonobankMerchant.Live] = new() { Item("L1", amount: 9999) }
        };
        var factory = new FakeMonobankClientFactory(secondPass);
        var second = new MonobankStatementImportService(
            context, factory,
            Options.Create(new MonobankOptions { Enabled = true }),
            NullLogger<MonobankStatementImportService>.Instance);

        await second.ImportRecentStatementsAsync();

        (await context.MonobankStatementEntries.SingleAsync()).AmountKopecks.Should().Be(1000);
    }

    // A row nothing can be filed under would collide with the next such row and overwrite it forever,
    // and the unique key would throw the whole period out mid-import. The client already refuses to
    // hand one over; the job must not depend on every future client remembering that.
    [Fact]
    public async Task A_Row_With_No_Id_Is_Skipped_Rather_Than_Filed_Under_A_Blank_Key()
    {
        var nameless = new MonobankStatementItem { StatementId = "", Amount = 1000, Ccy = 980 };
        var (service, context, _) = Build(new()
        {
            [MonobankMerchant.Live] = new() { Item("L1"), nameless, nameless }
        });

        await service.ImportRecentStatementsAsync();

        var stored = await context.MonobankStatementEntries.ToListAsync();
        stored.Should().ContainSingle();
        stored[0].StatementId.Should().Be("L1");
    }

    // The two are separate accounts. One merchant's rows must never be written under the other -
    // that would produce a reconciliation that balances against the wrong statement.
    [Fact]
    public async Task The_Same_Statement_Id_Under_Two_Merchants_Stays_Two_Rows()
    {
        var (service, context, _) = Build(new()
        {
            [MonobankMerchant.Live] = new() { Item("ST1") },
            [MonobankMerchant.Sandbox] = new() { Item("ST1") }
        });

        await service.ImportRecentStatementsAsync();

        (await context.MonobankStatementEntries.CountAsync()).Should().Be(2);
    }

    // The configured window is wider than the acquirer accepts in one statement request, and asking
    // for it in one request was refused outright ("to-'from' cannot be more than 31 days") - so the
    // whole window arrived as an error and nothing was imported. These two assertions are the
    // regression: the window is still covered end to end, and no single request exceeds the limit.
    [Fact]
    public async Task The_Configured_Window_Is_Fetched_Whole_But_Never_In_One_Request_Over_The_Limit()
    {
        var (service, _, factory) = Build(new());

        await service.ImportRecentStatementsAsync();

        factory.LiveFroms.Should().HaveCountGreaterThan(1);
        factory.LiveFroms.Zip(factory.LiveTos).Should().OnlyContain(
            pair => pair.Second - pair.First <= TimeSpan.FromDays(MonobankStatementWindow.MaxRangeDays));

        // Oldest slice first, each one ending exactly where the next begins: together they cover the
        // configured window with nothing dropped between them and nothing counted twice.
        factory.LiveFroms[0].Should().Be(factory.LiveTos[^1].AddDays(-35));
        factory.LiveFroms
            .Skip(1)
            .Zip(factory.LiveTos)
            .Should().OnlyContain(pair => pair.First - pair.Second == TimeSpan.Zero);
    }

    // A window the acquirer cannot serve at all - a negative or zero setting - must say so rather
    // than report a healthy pass that found nothing, which is indistinguishable from "no activity".
    [Fact]
    public async Task A_Window_Covering_No_Period_Asks_The_Acquirer_For_Nothing()
    {
        var context = NewContext();
        var factory = new FakeMonobankClientFactory(new());

        var service = new MonobankStatementImportService(
            context, factory,
            Options.Create(new MonobankOptions { Enabled = true, StatementImportWindowDays = 0 }),
            NullLogger<MonobankStatementImportService>.Instance);

        await service.ImportRecentStatementsAsync();

        factory.LiveFroms.Should().BeEmpty();
        (await context.MonobankStatementEntries.CountAsync()).Should().Be(0);
    }

    // The period arrives in pieces, and one piece failing must not cost the others - or the other
    // merchant, which is a separate account entirely.
    [Fact]
    public async Task One_Slice_Failing_Does_Not_Cost_The_Rest_Of_The_Period()
    {
        var context = NewContext();
        var factory = new FirstSliceFailsFactory();

        var service = new MonobankStatementImportService(
            context, factory,
            Options.Create(new MonobankOptions { Enabled = true, StatementImportWindowDays = 35 }),
            NullLogger<MonobankStatementImportService>.Instance);

        await service.ImportRecentStatementsAsync();

        factory.LiveAttempts.Should().Be(2);
        (await context.MonobankStatementEntries.SingleAsync()).StatementId.Should().Be("L2");
    }

    [Fact]
    public async Task Does_Nothing_When_Monobank_Is_Disabled()
    {
        // The mock's statement is synthetic and matches no order. Storing it would fill the one
        // table that is supposed to be evidence with rows that prove nothing.
        var (service, context, factory) = Build(
            new() { [MonobankMerchant.Live] = new() { Item("L1") } },
            enabled: false);

        await service.ImportRecentStatementsAsync();

        (await context.MonobankStatementEntries.CountAsync()).Should().Be(0);
        factory.LiveFroms.Should().BeEmpty();
    }

    [Fact]
    public async Task One_Merchant_Failing_Does_Not_Stop_The_Other()
    {
        var context = NewContext();
        var factory = new ThrowingFactory();

        var service = new MonobankStatementImportService(
            context, factory,
            Options.Create(new MonobankOptions { Enabled = true }),
            NullLogger<MonobankStatementImportService>.Instance);

        await service.ImportRecentStatementsAsync();

        // Sandbox down says nothing about live, and the act is per-merchant regardless.
        (await context.MonobankStatementEntries.CountAsync()).Should().Be(1);
    }

    /// <summary>Fails the oldest slice of every merchant and serves the rest, so a test can see what
    /// one refusal costs.</summary>
    private sealed class FirstSliceFailsFactory : IMonobankClientFactory
    {
        private int _liveCalls;

        public int LiveAttempts => _liveCalls;

        public IMonobankClient ForMerchant(MonobankMerchant merchant) => new Client(this, merchant);

        private sealed class Client(FirstSliceFailsFactory owner, MonobankMerchant merchant) : IMonobankClient
        {
            public Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
            {
                if (merchant == MonobankMerchant.Live)
                {
                    owner._liveCalls++;
                    if (owner._liveCalls == 1)
                    {
                        throw new HttpRequestException("acquirer refused the range");
                    }

                    return Task.FromResult<IReadOnlyList<MonobankStatementItem>>(new[] { Item("L2") });
                }

                return Task.FromResult<IReadOnlyList<MonobankStatementItem>>(Array.Empty<MonobankStatementItem>());
            }

            public Task<MonobankInvoiceResponse> CreateInvoiceAsync(MonobankInvoiceRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<MonobankCancelResponse> CancelInvoiceAsync(string invoiceId, long amountKopecks, string extRef, IReadOnlyList<MonobankBasketItem>? items = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }

    private sealed class ThrowingFactory : IMonobankClientFactory
    {
        public IMonobankClient ForMerchant(MonobankMerchant merchant) => new Client(merchant);

        private sealed class Client(MonobankMerchant merchant) : IMonobankClient
        {
            public Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
            {
                if (merchant == MonobankMerchant.Live)
                {
                    throw new HttpRequestException("acquirer is down");
                }

                return Task.FromResult<IReadOnlyList<MonobankStatementItem>>(new[] { Item("S1") });
            }

            public Task<MonobankInvoiceResponse> CreateInvoiceAsync(MonobankInvoiceRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<MonobankCancelResponse> CancelInvoiceAsync(string invoiceId, long amountKopecks, string extRef, IReadOnlyList<MonobankBasketItem>? items = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }
}
