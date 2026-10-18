using FluentAssertions;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for <c>payments.order_id</c> being nullable, and for the audit constraint
/// that had to widen with it.
/// <para>
/// The domain change is one sentence — money can arrive before the order it is for — and it touches
/// two things that had been true by construction: every payment had an order, and every capture was
/// evidence an acquirer produced so no human ever touched it. Both are now conditional, and both are
/// enforced in the database rather than by convention.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class PaymentWithoutOrderTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    public PaymentWithoutOrderTests(TestDatabaseFixture fixture) => _ = fixture;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    private ApplicationDbContext NewContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    private static string NewPhone() => "+380" + Random.Shared.Next(100000000, 999999999).ToString();

    private async Task<Guid> SeedUserAsync()
    {
        using var context = NewContext();
        var roleId = await context.Roles
            .Where(r => r.Name == SeedRoles.UserName)
            .Select(r => r.Id)
            .FirstAsync();
        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = NewPhone(),
            RoleId = roleId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// A payment declared by a person rather than reported by an acquirer. Succeeded immediately,
    /// because the money is already in the account - there is no invoice to wait for.
    /// </summary>
    private async Task<Guid> InsertDeclaredAsync(Guid? userId, Guid? authorId = null, string? authorName = null)
    {
        using var context = NewContext();

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Kind = PaymentKind.Capture,
            Provider = PaymentProviders.Declared,
            UserId = userId,
            AmountUah = 1500.00m,
            AmountKopecks = 150000,
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = DateTime.UtcNow,
            CreatedByUserId = authorId ?? userId,
            CreatedByUserName = authorName ?? "finance",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment.Id;
    }

    // --------------------------------------------------------------------------------------------
    // The column
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_declared_payment_can_exist_with_no_order()
    {
        var userId = await SeedUserAsync();

        var paymentId = await InsertDeclaredAsync(userId);

        using var context = NewContext();
        var stored = await context.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);

        stored.OrderId.Should().BeNull("money does not wait for an order");
        stored.UserId.Should().Be(userId, "we know whose money it is even before there is an order");
        stored.Status.Should().Be(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task A_payment_can_exist_with_neither_an_order_nor_a_customer()
    {
        // The unallocated case: the money is real and belongs to nobody yet. This is a legitimate row,
        // not a gap to be closed - it is what the operator's worklist is for, and forbidding it would
        // push the money somewhere with less evidence than this.
        var paymentId = await InsertDeclaredAsync(userId: null);

        using var context = NewContext();
        var stored = await context.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);

        stored.OrderId.Should().BeNull();
        stored.UserId.Should().BeNull();
    }

    [Fact]
    public async Task The_order_id_column_is_nullable_in_the_database()
    {
        using var context = NewContext();

        var isNullable = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT is_nullable
                FROM information_schema.columns
                WHERE table_name = 'payments'
                  AND column_name = 'order_id'
                """)
            .ToListAsync();

        isNullable.Should().ContainSingle().Which.Should().Be("YES");
    }

    [Fact]
    public async Task A_user_cannot_be_hard_deleted_while_their_money_is_held()
    {
        // Restrict, for the same reason the order FK is Restrict: a customer's own ledger has to keep
        // naming a real customer. Hard-deleting one must fail loudly rather than leave a balance
        // pointing at nobody.
        var userId = await SeedUserAsync();
        await InsertDeclaredAsync(userId);

        using var context = NewContext();
        var act = async () => await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM users WHERE id = {userId}");

        await act.Should().ThrowAsync<Exception>();
    }

    // --------------------------------------------------------------------------------------------
    // The widened audit constraint
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_declared_capture_may_carry_the_person_who_declared_it()
    {
        // This is the whole reason the constraint was scoped: a declared payment exists because a
        // person said money arrived, so an author is required, and the pre-widening rule refused it.
        var userId = await SeedUserAsync();

        var act = async () => await InsertDeclaredAsync(userId);

        await act.Should().NotThrowAsync("unattributable money is worse than money with no order");
    }

    [Fact]
    public async Task An_acquirer_capture_still_may_not_carry_an_author()
    {
        // The guard the original constraint was written for, and it must survive the widening: an
        // acquirer-driven capture is evidence the provider produced and no human touched. If this ever
        // starts passing, something is writing an approval onto provider evidence.
        using var context = NewContext();
        var roleId = await context.Roles.Where(r => r.Name == SeedRoles.UserName).Select(r => r.Id).FirstAsync();
        var now = DateTime.UtcNow;

        var customer = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = NewPhone(),
            RoleId = roleId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
        };
        context.Users.Add(customer);

        context.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            Kind = PaymentKind.Capture,
            Provider = PaymentProviders.Monobank,
            UserId = customer.Id,
            AmountUah = 100.00m,
            AmountKopecks = 10000,
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = now,
            CreatedByUserId = customer.Id,
            CreatedByUserName = "someone",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "ck_payments_refund_audit_only_on_refund still guards acquirer captures");
    }

    // --------------------------------------------------------------------------------------------
    // The worklist index
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Unallocated_succeeded_payments_are_indexed_for_the_operator()
    {
        // Nothing else in the system will ever surface these: no order, so no order report, no
        // reconciliation, no lifecycle job. Without a dedicated index they are found by remembering
        // to look, which is how held money goes missing for a month.
        using var context = NewContext();

        var count = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT count(*)
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND indexname = 'ix_payments_unallocated'
                """)
            .ToListAsync();

        count.Should().ContainSingle().Which.Should().Be(1);
    }

    // --------------------------------------------------------------------------------------------
    // The migration's own Down()
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Down() cannot restore NOT NULL without inventing an order id, and the only value available is
    /// the zero GUID - which passes the constraint and points at nothing. So it refuses, and this
    /// asserts the refusal happens with a sentence rather than a bare constraint violation. A Down()
    /// that quietly corrupts money on the way down is worse than one that stops.
    /// </summary>
    [Fact]
    public async Task Rolling_back_refuses_while_money_is_held_with_no_order()
    {
        const string BeforeThisMigration = "20261018095217_SupplierAcquisitionSource";

        using var context = NewContext();
        var migrator = context.Database.GetService<IMigrator>();

        try
        {
            // The database is at head, where an orderless payment is legal. Migrating to the previous
            // migration runs this migration's Down(), which has to refuse.
            var userId = await SeedUserAsync();
            await InsertDeclaredAsync(userId);

            var act = async () => await migrator.MigrateAsync(BeforeThisMigration);

            (await act.Should().ThrowAsync<Exception>())
                .WithMessage("*will not invent an order id*");
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }
}