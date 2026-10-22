using System.Data.Common;
using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The money path has to survive a commit that fails transiently — planning issue #610.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's retrying execution strategy is enabled on the Npgsql provider, so a commit that fails
/// transiently is retried rather than failing the request. That is only safe if the transaction is
/// replay-safe, and the two ways it used not to be are worth naming:
/// </para>
/// <list type="bullet">
/// <item>
/// EF accepts the tracked changes <b>before</b> the commit, so a replayed save found nothing to
/// write: the empty transaction committed and the strategy reported success for a write that never
/// landed. The write is now kept pending until the commit is through.
/// </item>
/// <item>
/// The redemption's own record — the event naming who redeemed the voucher — was written by the
/// attempt that failed, so the retry wrote a second one. One handover of one paper is one row,
/// however many times the commit was retried.
/// </item>
/// </list>
/// <para>
/// The failure is injected at the worst possible moment: the commit itself. Everything before it
/// succeeded, and the answer never came back — the shape of a dropped connection on the way out.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class TheMoneyPathSurvivesAFailedCommit : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public TheMoneyPathSurvivesAFailedCommit(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ARedemption_WhoseCommitFailsTransiently_IsAppliedExactlyOnce()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedAssignedVoucherAsync(userId, voucherId);

        var interceptor = new FailTheFirstCommit();

        await using var context = CreateContext(interceptor);
        var handler = new MarkVoucherAsUsedCommandHandler(
            context,
            TestJournal.For(context),
            NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);

        var response = await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId));

        interceptor.Failures.Should().Be(1,
            "the commit has to fail once, or the test proves nothing about the retry");

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Voucher marked as used");

        await using var verify = CreateContext();

        var status = await verify.FuelVouchers
            .Where(v => v.Id == voucherId)
            .Select(v => v.Status)
            .SingleAsync();

        status.Should().Be(VoucherStatus.Used,
            "the redemption is applied by the retry — the failed attempt wrote nothing");

        (await verify.VoucherEvents.CountAsync(e => e.VoucherId == voucherId))
            .Should().Be(1,
                "one handover of one paper is one row, however many times the commit was retried");
    }

    /// <summary>
    /// A voucher in somebody's hands, with the purchase it came out of behind it
    /// (<c>ck_voucher_held_has_order</c>).
    /// </summary>
    private async Task SeedAssignedVoucherAsync(Guid userId, Guid voucherId)
    {
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetDataAsync(seed);

        seed.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        seed.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{voucherId:N}"[..20],
            QrPayload = $"payload-{voucherId:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = SeedPurchaseOrder(seed, userId),
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await seed.SaveChangesAsync();
    }

    private static Guid SeedPurchaseOrder(ApplicationDbContext ctx, Guid userId)
    {
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 0,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        return orderId;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    /// <summary>
    /// The context the handler runs on. The retrying execution strategy is configured here, the way
    /// <c>DatabaseSetup</c> configures it for the app: without it there is no retry, and the test
    /// would be proving nothing.
    /// </summary>
    private ApplicationDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null));

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new ApplicationDbContext(builder.Options, TestQrCipher.Instance);
    }

    /// <summary>
    /// Fails the first commit with a transient error — the shape of a dropped connection on the way
    /// out of the database.
    /// </summary>
    /// <remarks>
    /// The failure is thrown before the COMMIT is sent, so the transaction is still open and the
    /// helper's dispose rolls it back: the retry has to make the write again. A
    /// <see cref="TimeoutException"/> is what Npgsql's strategy classifies as transient
    /// (<c>NpgsqlTransientExceptionDetector</c>).
    /// </remarks>
    private sealed class FailTheFirstCommit : DbTransactionInterceptor
    {
        private int _attempts;

        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                Failures++;
                throw new TimeoutException("The commit did not make it back to the client.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
