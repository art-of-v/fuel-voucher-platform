using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the integrity rules the database review moved out of application code and into the
/// schema, against a real PostgreSQL container. Each test asserts a rule that the database now
/// refuses to break on its own — a cascade that used to silently destroy paid-for paper, a
/// CHECK that used to be raw SQL invisible to EF, an FK that used to be nothing at all.
///
/// These are deliberately database-level tests. Asserting them through a handler would only
/// prove the handler is still correct; the point of each change is that the database now holds
/// the rule even when a future handler forgets it.
/// </summary>
[Collection("Integration Tests")]
public sealed class DatabaseIntegrityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DatabaseIntegrityTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Catalog display columns were unbounded <c>text</c>. A length limit is only worth having if the
    /// database actually enforces it, so this writes through raw SQL and expects the write to fail -
    /// going through EF would only prove EF sent a short string.
    /// </summary>
    [Fact]
    public async Task An_Overlong_Station_Type_Is_Refused_By_The_Column()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO stations (id, name, color, logo_text, station_type, sort_order, created_at_utc, updated_at_utc, is_deleted)
            VALUES ('bounds-probe', 'Probe', '#00ff80', 'PRB', {0}, 999, NOW(), NOW(), false)
            """, new string('x', 300));

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.MessageText.Should().Contain("character varying(64)",
            "the column limit, not some unrelated constraint, is what must stop this");
    }

    /// <summary>
    /// The counterpart, and the reason this was not simply "make every text column varchar": a
    /// digital signature, a raw acquirer payload and a stack trace have no meaningful upper bound,
    /// and truncating any of them destroys the only value the column holds. These must stay
    /// <c>text</c>; a future sweep that "tidies up the remaining text columns" would quietly break
    /// the audit trail, so the exception is pinned here rather than left to memory.
    /// </summary>
    [Theory]
    [InlineData("user_contracts", "signature_data")]
    [InlineData("monobank_statement_entries", "raw_payload")]
    [InlineData("error_logs", "stack_trace")]
    [InlineData("devices", "public_key")]
    public async Task Genuinely_Unbounded_Columns_Stay_Text(string table, string column)
    {
        await using var context = CreateContext();

        var sql = """
            SELECT data_type FROM information_schema.columns
            WHERE table_schema='public' AND table_name={0} AND column_name={1}
            """;
        var act = () => context.Database
            .SqlQueryRaw<string>(sql, table, column)
            .ToListAsync();

        var rows = await act();
        rows.Should().ContainSingle();
        rows[0].Should().Be("text",
            $"{table}.{column} holds a signature/payload/trace with no real upper bound - bounding it would destroy the audit trail");
    }

    /// <summary>
    /// The catalog keys are natural strings, deliberately not UUIDs, and stay <c>text</c> rather than
    /// <c>varchar</c> so that converting a primary key's type never forces a foreign-key refactor.
    /// The trade is only acceptable if something actually bounds them, which is what these prove.
    /// Raw SQL: going through EF would only prove EF sent a short string.
    /// </summary>
    [Fact]
    public async Task An_Overlong_Catalog_Key_Is_Refused()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO stations (id, name, color, logo_text, sort_order, created_at_utc, updated_at_utc, is_deleted)
            VALUES ({0}, 'Too Long', '#00ff80', 'TL', 999, NOW(), NOW(), false)
            """, new string('y', 150));

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.MessageText.Should().Contain("ck_stations_id_length",
            "the catalog key limit, not some unrelated constraint, is what must stop this");
    }

    [Theory]
    [InlineData("stations", "ck_stations_id_length")]
    [InlineData("fuel_types", "ck_fuel_types_id_length")]
    [InlineData("station_nodes", "ck_station_nodes_id_length")]
    [InlineData("fuel_packages", "ck_fuel_packages_id_length")]
    [InlineData("fuel_vouchers", "ck_fuel_vouchers_fuel_type_id_length")]
    [InlineData("purchase_batches", "ck_purchase_batches_fuel_type_id_length")]
    [InlineData("voucher_exchanges", "ck_voucher_exchanges_fuel_type_id_length")]
    public async Task Every_Catalog_Key_Has_Its_Length_Constraint(string table, string constraint)
    {
        await using var context = CreateContext();

        var sql = """
            SELECT count(*) FROM pg_constraint
            WHERE contype='c' AND conname={0}
            """;
        var found = await context.Database
            .SqlQueryRaw<int>(sql, constraint)
            .ToListAsync();

        found.Should().ContainSingle().Which.Should().Be(1,
            $"{table}.* must stay bounded - a natural key with no limit is how a slug becomes a paragraph");
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // "SSL Mode=Disable" is not cosmetic here: DatabaseSetup.BuildConnectionString
            // appends SSL Mode=Require to any string that does not carry one, which a container
            // on localhost cannot satisfy.
            .UseNpgsql(_fixture.ConnectionString + ";SSL Mode=Disable")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static User NewUser(string phone) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = phone,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static FuelVoucher NewVoucher(string number, string qr, string fuelTypeId = "okko-dp") => new()
    {
        Id = Guid.NewGuid(),
        Provider = "okko",
        FuelTypeId = fuelTypeId,
        Liters = 10m,
        ProviderExpirationDate = new DateOnly(2027, 1, 1),
        CustomerExpirationDate = new DateOnly(2027, 1, 1),
        VoucherNumber = number,
        QrPayload = qr,
        Status = VoucherStatus.Available,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    // ── F-01: the cascades that could destroy paid-for paper ───────────────────────────────

    /// <summary>
    /// The sharpest edge in the schema. Deleting a fuel type used to cascade straight into
    /// fuel_vouchers and destroy every voucher sold against it — paper the customer had already
    /// paid for, and the only record of what they bought.
    /// </summary>
    [Fact]
    public async Task Deleting_A_FuelType_With_Vouchers_Is_Refused()
    {
        var voucherId = Guid.NewGuid();
        const string fuelTypeId = "okko-gas";

        await using var context = CreateContext();
        context.FuelVouchers.Add(NewVoucher("cascade-guard", "cascade-qr", fuelTypeId));
        await context.SaveChangesAsync();

        // Straight SQL, deliberately: the application path is not the thing under test. A DELETE
        // from anywhere — a handler, a script, a support psql session — is what has to fail.
        var act = () => context.Database.ExecuteSqlRawAsync(
            """DELETE FROM fuel_types WHERE id = {0}""", fuelTypeId);

        // PostgresException, not DbUpdateException: these three go through ExecuteSqlRaw, which
        // surfaces the provider exception directly rather than wrapping it the way SaveChanges does.
        await act.Should().ThrowAsync<PostgresException>();

        await using var verify = CreateContext();
        (await verify.FuelVouchers.IgnoreQueryFilters().CountAsync(v => v.VoucherNumber == "cascade-guard"))
            .Should().Be(1, "the voucher is the customer's paid-for paper and must survive");
    }

    /// <summary>
    /// A refund is a money record. Under CASCADE, deleting the order took the refund with it and
    /// nothing said so - "we paid this back, for this, under this order" would simply stop being
    /// answerable.
    /// <para>
    /// Today the application's status predicate already prevents this (a refund flips the order to
    /// Refunded, never Cancelled), so the constraint is not load-bearing yet. That is exactly why it
    /// is worth having: it turns "the application currently gets this right" into "the database
    /// refuses if it ever stops getting this right".
    /// </para>
    /// </summary>
    [Fact]
    public async Task Deleting_A_Refunded_Order_Is_Refused()
    {
        var user = NewUser("+38000000001");
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Price = 100m,
            Status = OrderStatus.Refunded,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await using (var seed = CreateContext())
        {
            seed.Users.Add(user);
            seed.Orders.Add(order);
            seed.Refunds.Add(new Refund
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                UserId = user.Id,
                Amount = 100L,
                InvoiceId = "INV-DBINT-1",
                ExtRef = "R1",
                Status = RefundStatus.Completed,
                IsAutomatic = false,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var act = () => context.Database.ExecuteSqlRawAsync("""DELETE FROM orders WHERE id = {0}""", order.Id);

        await act.Should().ThrowAsync<PostgresException>();

        await using var verify = CreateContext();
        (await verify.Refunds.CountAsync(r => r.OrderId == order.Id))
            .Should().Be(1, "the refund is the only record that money went out, and it must survive");
    }

    /// <summary>
    /// The same guarantee for the self-serve renewal: <c>voucher_renewal_items</c> records what the
    /// customer paid and how the term moved, so deleting the order must not erase the payment.
    /// </summary>
    [Fact]
    public async Task Deleting_A_renewal_Order_With_Renewal_Items_Is_Refused()
    {
        var user = NewUser("+38000000002");
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Price = 500m,
            Kind = OrderKind.Renewal,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await using (var seed = CreateContext())
        {
            // source_voucher_id is a RESTRICT FK to a real voucher, so the renewal item needs one.
            var source = NewVoucher("renewal-source", "renewal-source-qr");
            seed.FuelVouchers.Add(source);
            seed.Users.Add(user);
            seed.Orders.Add(order);
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                SourceVoucherId = source.Id,
                AmountPaid = 500m,
                PreviousCustomerExpiration = new DateOnly(2026, 12, 1),
                NewCustomerExpiration = new DateOnly(2027, 1, 1),
                CreatedAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var act = () => context.Database.ExecuteSqlRawAsync("""DELETE FROM orders WHERE id = {0}""", order.Id);

        await act.Should().ThrowAsync<PostgresException>();

        await using var verify = CreateContext();
        (await verify.VoucherRenewalItems.CountAsync(i => i.OrderId == order.Id))
            .Should().Be(1, "the renewal item is what the customer paid for, and it must survive");
    }

    /// <summary>
    /// The three-hop chain: users → legal_entities → contracts → user_contracts, ending at a
    /// digital signature. Restricting the first hop is what makes the rest unreachable.
    /// </summary>
    [Fact]
    public async Task Deleting_A_User_With_Signed_Contracts_Is_Refused()
    {
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var contractId = Guid.NewGuid();

        await using var context = CreateContext();
        var user = new User
        {
            Id = userId,
            PhoneNumber = "+38000555002",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var entity = new LegalEntity
        {
            Id = entityId,
            UserId = userId,
            Name = "Test LLC",
            Edrpou = "1234567890",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var contract = new Contract
        {
            Id = contractId,
            UserId = userId,
            LegalEntityId = entityId,
            StationId = "okko",
            CreatedAtUtc = DateTime.UtcNow
        };
        var signature = new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ContractId = contractId,
            SignatureData = "signature-bytes",
            SignedAtUtc = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.LegalEntities.Add(entity);
        context.Contracts.Add(contract);
        context.UserContracts.Add(signature);
        await context.SaveChangesAsync();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """DELETE FROM users WHERE id = {0}""", userId);

        await act.Should().ThrowAsync<PostgresException>();

        await using var verify = CreateContext();
        (await verify.UserContracts.CountAsync(u => u.ContractId == contractId))
            .Should().Be(1, "a signature outlives the account that gave it");
    }

    // ── F-06: CHECK constraints the model now owns ───────────────────────────────────────────

    [Fact]
    public async Task A_Negative_Liters_Voucher_Is_Refused_By_The_Database()
    {
        await using var context = CreateContext();
        var voucher = NewVoucher("negative-litres", "negative-qr");
        voucher.Liters = -5m;
        context.FuelVouchers.Add(voucher);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_Negative_Price_Order_Is_Refused_By_The_Database()
    {
        await using (var seed = CreateContext())
        {
            seed.Users.Add(NewUser("+38000555010"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            UserId = context.Users.Select(u => u.Id).First(),
            Price = -100m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// A voucher may not be promised past the provider's own printed term. Zero-litre fuel is
    /// never a real product either.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_NonPositive_Liters_Voucher_Is_Refused(double liters)
    {
        await using var context = CreateContext();
        var voucher = NewVoucher($"liters-{liters}", $"liters-qr-{liters}");
        voucher.Liters = (decimal)liters;
        context.FuelVouchers.Add(voucher);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// The rule that used to exist only as hand-written SQL invisible to EF. It is the one
    /// constraint the schema is named for, so it is worth pinning that the model now owns it.
    /// </summary>
    [Fact]
    public async Task A_Held_Voucher_Without_An_Order_Is_Refused()
    {
        await using var context = CreateContext();
        var voucher = NewVoucher("held-no-order", "held-no-order-qr");
        voucher.Status = VoucherStatus.Assigned;   // held, but order_id is still null
        context.FuelVouchers.Add(voucher);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ── F-09 / F-14: foreign keys that did not exist ─────────────────────────────────────────

    /// <summary>
    /// orders.user_id was NOT NULL, indexed, and filtered on by every order query — with no FK.
    /// A row pointing at a user that does not exist is now unrepresentable.
    /// </summary>
    [Fact]
    public async Task An_Order_For_A_Nonexistent_User_Is_Refused()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO orders (id, user_id, price, status, kind, created_at_utc, updated_at_utc, is_deleted)
            VALUES ({0}, {1}, 100, 'PendingPayment', 'Purchase', now(), now(), false)
            """,
            Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<PostgresException>();
    }

    // ── Text-stored enums: readable, but only safe once the database knows the set ─────────────
    //
    // A closed enum stored as text survives adding a member, which an int does not - but an
    // unguarded text column accepts any string, and a value that matches no enum member cannot
    // round-trip back through EF. Each of these is the same rule applied consistently.

    /// <summary>
/// Why the CHECK matters, demonstrated rather than asserted.
///
/// The compiler stops you writing <c>OrderStatus.PendingPaymnt</c>. Once the enum is stored as
/// text that protection is gone: the database accepts whatever string arrives, and the failure
/// moves to the moment EF reads the row back and cannot turn it into an enum. Without the
/// constraint one bad row poisons every list endpoint that touches the table.
/// </summary>
    [Fact]
    public async Task Without_The_Check_An_Unknown_Status_Arrives_Back_As_An_Exception()
    {
        await using (var seed = CreateContext())
        {
            seed.Users.Add(NewUser("+38000555050"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var userId = context.Users.Select(u => u.Id).First();
        var orderId = Guid.NewGuid();

        // Inserted as raw SQL with the constraint bypassed - the "before" state.
        await context.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE orders DROP CONSTRAINT IF EXISTS ck_order_status;
            INSERT INTO orders (id, user_id, price, status, kind, created_at_utc, updated_at_utc, is_deleted)
            VALUES ({0}, {1}, 100, 'PendingPaymnt', 'Purchase', now(), now(), false);
            """, orderId, userId);

        // Reading it back is where the damage lands.
        var read = async () => await context.Orders.Where(o => o.Id == orderId).ToListAsync();
        await read.Should().ThrowAsync<Exception>(
            "the value is not an OrderStatus member, so materialising it cannot succeed");

        // Leave the schema as it was for the tests that follow.
        await context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM orders WHERE id = {0};
            ALTER TABLE orders ADD CONSTRAINT ck_order_status
                CHECK (status IN ('PendingPayment', 'Paid', 'PendingFulfillment', 'PartiallyFulfilled',
                                  'Fulfilled', 'PartiallyRefunded', 'Refunded', 'Cancelled'));
            """, orderId);
    }

    [Fact]
    public async Task An_Order_With_An_Unknown_Status_Is_Refused()
    {
        await using (var seed = CreateContext())
        {
            seed.Users.Add(NewUser("+38000555040"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var userId = context.Users.Select(u => u.Id).First();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO orders (id, user_id, price, status, kind, created_at_utc, updated_at_utc, is_deleted)
            VALUES ({0}, {1}, 100, 'PendingPaymnt', 'Purchase', now(), now(), false)
            """,
            Guid.NewGuid(), userId);

        // A typo'd status is the whole failure mode: the dispatcher switches on it, matches
        // nothing, and the order silently never advances.
        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task An_Order_With_An_Unknown_Kind_Is_Refused()
    {
        await using (var seed = CreateContext())
        {
            seed.Users.Add(NewUser("+38000555041"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var userId = context.Users.Select(u => u.Id).First();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO orders (id, user_id, price, status, kind, created_at_utc, updated_at_utc, is_deleted)
            VALUES ({0}, {1}, 100, 'PendingPayment', 'ReceivedFromSupplier', now(), now(), false)
            """,
            Guid.NewGuid(), userId);

        await act.Should().ThrowAsync<PostgresException>(
            "a kind outside the enum set would silently drop the order out of revenue reporting");
    }

    [Fact]
    public async Task A_Voucher_With_An_Unknown_Status_Is_Refused()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_vouchers (id, provider, fuel_type_id, liters, provider_expiration_date,
                                       customer_expiration_date, voucher_number, qr_payload,
                                       created_at_utc, updated_at_utc, status, is_test_data, is_deleted)
            VALUES ({0}, 'okko', 'okko-dp', 10, '2027-01-01', '2027-01-01', 'bad-status', 'bad-status-qr',
                    now(), now(), 'Availble', false, false)
            """, Guid.NewGuid());

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task An_Outbox_Event_With_An_Unknown_Type_Is_Refused()
    {
        await using var context = CreateContext();

        // The jsonb payload is doubled up because ExecuteSqlRaw treats braces as placeholders.
        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO outbox_events (event_type, payload, processed, created_at_utc)
            VALUES ('OrderFuifilled', '{{}}', false, now())
            """);

        await act.Should().ThrowAsync<PostgresException>(
            "a queue row nobody recognises is the worst shape a dispatch failure can take");
    }

    /// <summary>
    /// varchar(20) was exactly the length of the longest VoucherStatus member, so the next member
    /// added to the enum would have failed at write time instead of at compile time.
    /// </summary>
    [Fact]
    public async Task The_Longest_Voucher_Status_Fits_With_Room_To_Spare()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_vouchers (id, provider, fuel_type_id, liters, provider_expiration_date,
                                       customer_expiration_date, voucher_number, qr_payload,
                                       created_at_utc, updated_at_utc, status, is_test_data, is_deleted)
            VALUES ({0}, 'okko', 'okko-dp', 10, '2027-01-01', '2027-01-01', 'longest-status', 'longest-status-qr',
                    now(), now(), 'VerifiedWithWarnings', false, false)
            """, Guid.NewGuid());

        await act.Should().NotThrowAsync("the longest member is 20 chars and the column is wider");
    }

    // ── F-02: the query filter users never had ───────────────────────────────────────────────

    /// <summary>
    /// A soft delete that does not hide the row is a flag nobody enforces. The global filter is
    /// what makes "deleted" mean something to every reader at once, instead of relying on each
    /// of them to remember.
    /// </summary>
    [Fact]
    public async Task A_Soft_Deleted_User_Is_Invisible_To_Every_Reader()
    {
        var userId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            var user = NewUser("+38000555020");
            user.Id = userId;
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        await using (var act = CreateContext())
        {
            var user = await act.Users.FirstAsync(u => u.Id == userId);
            user.IsDeleted = true;
            await act.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        (await verify.Users.CountAsync(u => u.Id == userId)).Should().Be(0);
        (await verify.Users.IgnoreQueryFilters().CountAsync(u => u.Id == userId))
            .Should().Be(1, "the row is history and is kept — just not readable as a live account");
    }
}

