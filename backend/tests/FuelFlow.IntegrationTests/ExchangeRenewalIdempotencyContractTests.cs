using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The two keys that make a voucher exchange and an operator renewal idempotent (review F-10).
/// </summary>
/// <remarks>
/// <para>
/// The handler checks first and the database refuses second. The check gives the operator a readable
/// message for the ordinary double-click; the key is the guarantee for the case the check cannot
/// cover - two operators confirming the same vouchers at the same moment, where the check and the
/// write are not one atomic step.
/// </para>
/// <para>
/// These tests are about the KEY, not about the handler: they write the rows the handler would write
/// and ask the database whether it would take a second one. A key the database does not have is not
/// an idempotency guarantee, it is a convention - and the review's F-10 was filed because the
/// columns once carried the convention and nothing else.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ExchangeRenewalIdempotencyContractTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ExchangeRenewalIdempotencyContractTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task A_Voucher_Cannot_Be_Retired_Into_Two_Exchanges()
    {
        var oldVoucherId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetAsync(seed);

            seed.Suppliers.Add(SeedSupplier(supplierId));
            seed.FuelVouchers.Add(Stock(oldVoucherId, supplierId));
            seed.MovementBatches.Add(new MovementBatch
            {
                Id = batchId,
                SupplierId = supplierId,
                SurchargeUah = 0m,
                OpenedByUserId = Guid.NewGuid(),
                OpenedAtUtc = DateTime.UtcNow,
                ConfirmedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using (var first = CreateContext())
        {
            first.VoucherExchanges.Add(ExchangeRow(batchId, oldVoucherId, supplierId));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext();
        second.VoucherExchanges.Add(ExchangeRow(batchId, oldVoucherId, supplierId));
        var act = () => second.SaveChangesAsync();

        var violation = (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which;

        violation.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
            "a voucher retired into an exchange is retired once - the second row is the double-retire");
        violation.ConstraintName.Should().Be("ix_voucher_exchanges_old_voucher_id");
    }

    [Fact]
    public async Task A_Renewal_To_The_Same_Date_Cannot_Be_Recorded_Twice()
    {
        var voucherId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetAsync(seed);

            seed.Suppliers.Add(SeedSupplier(supplierId));
            seed.FuelVouchers.Add(Stock(voucherId, supplierId));
            await seed.SaveChangesAsync();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var first = CreateContext())
        {
            first.OperatorVoucherRenewals.Add(RenewalRow(voucherId, today, today.AddMonths(1)));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext();
        second.OperatorVoucherRenewals.Add(RenewalRow(voucherId, today, today.AddMonths(1)));
        var act = () => second.SaveChangesAsync();

        var violation = (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which;

        violation.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
            "the same renewal to the same date is the same settlement row, recorded twice");
        violation.ConstraintName.Should().Be("ix_operator_voucher_renewals_voucher_id_new_expiration");
    }

    [Fact]
    public async Task A_Voucher_May_Be_Renewed_Again_To_A_Later_Date()
    {
        var voucherId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetAsync(seed);

            seed.Suppliers.Add(SeedSupplier(supplierId));
            seed.FuelVouchers.Add(Stock(voucherId, supplierId));
            await seed.SaveChangesAsync();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var first = CreateContext())
        {
            first.OperatorVoucherRenewals.Add(RenewalRow(voucherId, today, today.AddMonths(1)));
            await first.SaveChangesAsync();
        }

        // The business answer the key was built around: a voucher may be renewed any number of times.
        // What the key refuses is the SAME renewal twice, not a second, later renewal.
        await using var second = CreateContext();
        second.OperatorVoucherRenewals.Add(RenewalRow(voucherId, today, today.AddMonths(2)));
        var act = () => second.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static Supplier SeedSupplier(Guid supplierId)
        => new()
        {
            Id = supplierId,
            Name = $"Idempotency supplier {supplierId:N}"[..40],
            EdrIpn = TestEdrIpns.Next(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static FuelVoucher Stock(Guid id, Guid supplierId)
        => new()
        {
            Id = id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            VoucherNumber = $"IDEM-{id:N}"[..16],
            QrPayload = $"idem-{id:N}",
            Status = VoucherStatus.Available,
            SupplierId = supplierId,
            CostPerLiter = 30m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static VoucherExchange ExchangeRow(Guid batchId, Guid oldVoucherId, Guid supplierId)
        => new()
        {
            Id = Guid.NewGuid(),
            ExchangeBatchId = batchId,
            OldVoucherId = oldVoucherId,
            FuelTypeId = ToUuid("okko-95"),
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            SurchargeUah = 0m,
            SupplierId = supplierId,
            ActingUserId = Guid.NewGuid(),
            ActingUserName = "Operator",
            CreatedAtUtc = DateTime.UtcNow
        };

    private static OperatorVoucherRenewal RenewalRow(Guid voucherId, DateOnly oldExpiration, DateOnly newExpiration)
        => new()
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            CustomerUserId = Guid.NewGuid(),
            Branch = "extend",
            TermCode = "1m",
            OldExpiration = oldExpiration,
            NewExpiration = newExpiration,
            SurchargeUah = 0m,
            ActingUserId = Guid.NewGuid(),
            ActingUserName = "Operator",
            CreatedAtUtc = DateTime.UtcNow
        };

    private static async Task ResetAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "voucher_exchanges", "operator_voucher_renewals", "movement_batches", "purchase_batches", "voucher_imports", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "suppliers", "users", "provider_event_outbox" RESTART IDENTITY CASCADE""");
    }
}