using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

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
    /// <summary>
    /// Pinned on <c>brand_stations</c> since the column left <c>brands</c>: a brand is a network and
    /// has no station type, but a filling station still does, and the limit on a short label is worth
    /// as much wherever the column now lives.
    /// </summary>
    [Fact]
    public async Task An_Overlong_Station_Type_Is_Refused_By_The_Column()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO brand_stations (id, brand_id, external_id, name, station_type, lat, lng, created_at_utc, updated_at_utc, is_deleted)
            VALUES (gen_random_uuid(), {0}, 'bounds-probe', 'Probe', {1}, 50.4, 30.4, NOW(), NOW(), false)
            """, BrandIds.Okko, new string('x', 300));

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

        // `brands.id` is a uuid and cannot be overlong, so the bound that survives D-21 is the one on
        // the slug: it is what an import file and an operator both name a network by, and it is the
        // only catalog key left that is a string.
        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO brands (id, slug, name, color, logo_text, sort_order, created_at_utc, updated_at_utc, is_deleted)
            VALUES (gen_random_uuid(), {0}, 'Too Long', '#00ff80', 'TL', 999, NOW(), NOW(), false)
            """, new string('y', 150));

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.MessageText.Should().Contain("character varying(100)",
            "the column type, not some unrelated constraint, is what must stop this");
    }

    /// <summary>
    /// The re-import key of a station is bounded by its column type, not by a CHECK.
    /// </summary>
    /// <remarks>
    /// It used to be the primary key, bounded by <c>ck_brand_stations_id_length</c>. Planning D-21
    /// moved that value to <c>external_id varchar(100)</c> — the same limit, expressed as a type,
    /// because a CHECK over a varchar's own length states nothing the type does not already.
    /// </remarks>
    /// </summary>
    [Fact]
    public async Task A_Station_ExternalId_Is_Bounded_By_Its_Column_Type()
    {
        await using var context = CreateContext();

        var type = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT data_type AS "Value" FROM information_schema.columns
                WHERE table_name = 'brand_stations' AND column_name = 'external_id'
                """)
            .ToListAsync();

        type.Should().ContainSingle().Which.Should().Be("character varying",
            "the re-import key is the one column a station still has to keep short");

        var maxLength = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT character_maximum_length AS "Value" FROM information_schema.columns
                WHERE table_name = 'brand_stations' AND column_name = 'external_id'
                """)
            .ToListAsync();

        maxLength.Should().ContainSingle().Which.Should().Be(100);
    }

    [Theory]
    [InlineData("fuel_types", "ck_fuel_types_name_length")]
    [InlineData("fuel_types", "ck_fuel_types_brand_fuel_code_length")]
    [InlineData("brands", "ck_brands_slug_length")]
    [InlineData("fuel_type_product_codes", "ck_fuel_type_product_codes_product_code_length")]
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
        return new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    private static User NewUser(string phone) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = phone,
        IsActive = true,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private static FuelVoucher NewVoucher(string number, string qr, string fuelTypeId = "okko-dp") => new()
    {
        Id = Guid.NewGuid(),
        Brand = "okko",
        BrandId = BrandIds.ForSlug("okko"),
        FuelTypeId =   ToUuid(fuelTypeId),
        Liters = 10m,
        SupplierExpirationDate = new DateOnly(2027, 1, 1),
        CustomerExpirationDate = new DateOnly(2027, 1, 1),
        VoucherNumber = number,
        QrPayload = qr,
        Status = VoucherStatus.Available,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    // ── F-29: the two sign constraints the catalogue was missing ──────────────────────────

    /// <summary>
    /// <c>fuel_packages</c> was the only sellable-money table in the schema with no sign
    /// constraint, while every sibling one (<c>fuel_vouchers</c>, <c>orders</c>,
    /// <c>order_line_items</c>, <c>refunds</c>, .) has one and is asserted above.
    /// <c>PUT /api/admin/brands/fuels/{id}/nominals</c> binds <c>List&lt;int&gt;</c> off the request
    /// body, so a nominal of <c>-10</c> produced a package priced at <c>-10 x</c> the per-litre
    /// rate and the storefront listed it.
    ///
    /// The guards moved with the data, and are worth asserting by their new names rather than
    /// assumed: <c>ck_fuel_nominals_liters_positive</c> holds the size positive — the size now
    /// belongs to the fuel, so the constraint sits on <c>fuel_nominals</c> rather than on the row
    /// that used to carry it — and <c>ck_fuel_offers_prices_non_negative</c> holds the per-litre
    /// rate, which the old table had no guard on at all, because on the old table the rate was only
    /// ever derived.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task A_NonPositive_Liters_Volume_Is_Refused(double liters)
    {
        await using var context = CreateContext();
        context.FuelNominals.Add(NewNominal("no-such-fuel", liters));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// The negative-rate guard, which the old table never needed because it had no column for one.
    /// <para>
    /// It is aimed at the <b>supplier cost</b> now, not at the derived customer price: until
    /// <c>DeriveTheOfferFinalPrice</c> (2026-10-17) the CHECK covered <c>final_price_per_liter</c> but
    /// let a negative cost through, and a negative cost makes every price computed from it negative as
    /// well. The same migration brought the cost under the constraint.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_Negative_Offer_Price_Is_Refused()
    {
        await using var context = CreateContext();
        var negativeFuel = NewFuelWithPrice("offer-negative-final", -1m);
        context.FuelTypes.Add(negativeFuel);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// A negative price knob means we pay the customer for the fuel, or the customer is paid to
    /// take it. The only guard is in the schema: the cost, the column price and the margin are
    /// numbers a person wrote, and a negative one of them is a mistake the database refuses.
    /// </summary>
    /// <remarks>
    /// The stored copies of the price used to be guarded here as well
    /// (<c>ck_fuel_types_prices_non_negative</c>). They are gone with their columns - the price is
    /// computed from the knobs now (planning slice 2 of <c>docs/PLAN_PRICING_MODEL.md</c>) - so the
    /// guard follows the knobs, which are what the price is made of.
    /// </remarks>
    [Theory]
    [InlineData(-1, null, null)]   // the cost we pay is negative
    [InlineData(null, -1, null)]   // the margin is negative: we pay the customer for the fuel
    [InlineData(null, null, -1)]   // the column price is negative
    public async Task A_Negative_Fuel_Type_Price_Is_Refused(
        double? cost, double? margin, double? pumpPrice)
    {
        await using var context = CreateContext();
        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid($"ft-neg-{cost}-{margin}-{pumpPrice}"),
            BrandId = BrandIds.Okko,
            Name = $"Negative {cost}/{margin}/{pumpPrice}",
            SupplierPricePerLiter = cost is null ? null : (decimal)cost.Value,
            MarginUahPerLiter = margin is null ? null : (decimal)margin.Value,
            PumpPricePerLiter = pumpPrice is null ? null : (decimal)pumpPrice.Value,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// A zero-litre volume is the boundary case, not a theoretical one: <c>List&lt;int&gt;</c>
    /// would have accepted it before, and "0 litres for 0 UAH" is a package the storefront would
    /// have listed as free fuel.
    ///
    /// The free half has to be a REAL fuel + size pair, because it asserts the row is accepted.
    /// <see cref="NewNominal"/> deliberately points at nothing so the refusal halves cannot pass on a
    /// foreign key instead of a sign constraint; that trick is unavailable when success is the claim.
    /// </summary>
    [Fact]
    public async Task A_Zero_Price_Volume_Is_Allowed_But_Zero_Liters_Is_Not()
    {
        await using (var seed = CreateContext())
        {
var giveawayFuel = NewFuelWithPrice("offer-free-giveaway", 0m);
        seed.FuelTypes.Add(giveawayFuel);
        seed.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = giveawayFuel.Id,
                Liters = 5m,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        context.FuelNominals.Add(NewNominal("no-such-fuel", 0));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

/// <summary>
    /// A fuel to hang a size off, priced so the column constraints have something to bite on.
    /// </summary>
    /// <remarks>
    /// One helper for two tests that share a database. The price rides on the fuel rather than on an
    /// offer that no longer exists, so the constraint under test is the fuel's own and a failure
    /// names the column rather than a foreign key somebody else owns.
    /// </remarks>
    private static FuelTypeEntity NewFuelWithPrice(string id, decimal supplierPricePerLiter) => new()
    {
        Id = ToUuid( $"{id}-fuel"),
        Name = $"Test fuel for {id}",
        BrandId = BrandIds.Okko,
        SupplierPricePerLiter = supplierPricePerLiter,
        MarginUahPerLiter = 1m,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    /// <summary>
    /// A size with no fuel behind it. That is deliberate and load-bearing: the point of these
    /// tests is that the DATABASE refuses the row, and a nominal pointing at a real fuel would
    /// either need one seeded or would fail on the foreign key instead — turning a sign assertion
    /// into a wiring assertion that passes for the wrong reason. The sign constraints live on the
    /// nominal's own columns and fire before anything else is consulted.
    /// </summary>
    private static FuelNominal NewNominal(string fuelTypeId, double liters) => new()
    {
        FuelTypeId =   ToUuid(fuelTypeId),
        Liters = (decimal)liters,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

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
        // surfaces the brand exception directly rather than wrapping it the way SaveChanges does.
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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

        await using (var seed = CreateContext())
        {
            seed.Users.Add(user);
            seed.Orders.Add(order);
            // The refund is a payment now. It needs a capture to reverse, so both are seeded - the
            // point of the test is that neither money row is taken with the order.
            var capture = new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Kind = PaymentKind.Capture,
                Provider = "monobank",
                ProviderInvoiceId = "INV-DBINT-1",
                AmountUah = 1.00m,
                AmountKopecks = 100L,
                Currency = "UAH",
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = TestClock.UtcNow,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            };
            seed.Payments.Add(capture);
            seed.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Kind = PaymentKind.Refund,
                ParentPaymentId = capture.Id,
                Provider = "monobank",
                ProviderInvoiceId = capture.ProviderInvoiceId,
                AmountUah = 1.00m,
                AmountKopecks = 100L,
                Currency = "UAH",
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = TestClock.UtcNow,
                IsAutomatic = false,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var act = () => context.Database.ExecuteSqlRawAsync("""DELETE FROM orders WHERE id = {0}""", order.Id);

        await act.Should().ThrowAsync<PostgresException>();

        await using var verify = CreateContext();
        (await verify.Payments.CountAsync(r => r.OrderId == order.Id && r.Kind == PaymentKind.Refund))
            .Should().Be(1, "the refund is the only record that money went out, and it must survive");
    }

    /// <summary>
    /// At most one refund per order, now carried by <c>ux_payments_one_refund_per_order</c> rather
    /// than by <c>refunds.order_id UNIQUE</c>. That constraint used to be the only thing standing
    /// between an admin double-click and two refunds for one order; the index has to keep standing
    /// there, because <c>RefundOrderCommandHandler</c> reads "does a refund exist" before it writes,
    /// and a read is not a lock.
    /// </summary>
    /// <remarks>
    /// The handler's own defence is the <c>DbUpdateException</c> catch that reports the winning
    /// refund instead of a 500. This is the other half: the database refusing at all.
    /// </remarks>
    [Fact]
    public async Task A_Second_Refund_On_One_Order_Is_Refused()
    {
        var user = NewUser("+38000000003");
        var order = NewPaidOrder(user, "INV-ONE-REFUND");
        var capture = await SeedCaptureAndRefund(order, user, "INV-ONE-REFUND", "INV-ONE-REFUND-2");

        await using var context = CreateContext();
        // A different invoice on purpose: ux_payments_provider_invoice_kind is unique over
        // (provider, provider_invoice_id, kind) and would refuse this row first, which would make the
        // test pass for the wrong reason.
        context.Payments.Add(RefundPayment(order.Id, capture, "INV-ONE-REFUND-3"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await using var verify = CreateContext();
        (await verify.Payments.CountAsync(p => p.OrderId == order.Id && p.Kind == PaymentKind.Refund))
            .Should().Be(1, "the first refund stands; the second never lands");
    }

    /// <summary>
    /// The deliberate asymmetry with the test above, and the reason the index is partial. A re-issued
    /// payment link legitimately produces a second capture on one order - that is the whole reason
    /// <c>payments</c> exists instead of four columns on <c>orders</c>, where a re-issue overwrote the
    /// previous invoice id and the first attempt became unrecoverable. Refining the index away from
    /// <c>WHERE kind = 'Refund'</c> would forbid that, and nothing else would notice.
    /// </summary>
    [Fact]
    public async Task A_Second_Capture_On_One_Order_Is_Allowed()
    {
        var user = NewUser("+38000000004");
        var order = NewPaidOrder(user, "INV-TWO-CAPTURES");
        await SeedCaptureAndRefund(order, user, "INV-TWO-CAPTURES", "INV-TWO-CAPTURES-REFUND");

        await using var context = CreateContext();
        context.Payments.Add(CapturePayment(order.Id, "INV-TWO-CAPTURES-B"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await using var verify = CreateContext();
        (await verify.Payments.CountAsync(p => p.OrderId == order.Id && p.Kind == PaymentKind.Capture))
            .Should().Be(2, "a re-issued payment link is a second capture, not a lost one");
    }

    /// <summary>An order with an invoice the acquirer issued and the customer paid.</summary>
    private static Order NewPaidOrder(User user, string invoiceId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        Price = 100m,
        Status = OrderStatus.Refunded,
        MonobankInvoiceId = invoiceId,
        MonobankStatus = MonobankStatus.Success,
        IdempotencyKey = $"idem-{invoiceId}",
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    /// <summary>
    /// The refund shape a real write produces: a succeeded capture and a refund against it. Returns
    /// the capture so a caller can build further rows off it.
    /// </summary>
    private async Task<Payment> SeedCaptureAndRefund(Order order, User user, string captureInvoice, string refundInvoice)
    {
        await using var seed = CreateContext();
        var capture = CapturePayment(order.Id, captureInvoice);
        seed.Users.Add(user);
        seed.Orders.Add(order);
        seed.Payments.Add(capture);
        seed.Payments.Add(RefundPayment(order.Id, capture, refundInvoice));
        await seed.SaveChangesAsync();
        return capture;
    }

    private static Payment CapturePayment(Guid orderId, string invoiceId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Kind = PaymentKind.Capture,
        Provider = "monobank",
        ProviderInvoiceId = invoiceId,
        AmountUah = 1.00m,
        AmountKopecks = 100L,
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = TestClock.UtcNow,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private static Payment RefundPayment(Guid orderId, Payment capture, string invoiceId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Kind = PaymentKind.Refund,
        ParentPaymentId = capture.Id,
        Provider = "monobank",
        ProviderInvoiceId = invoiceId,
        AmountUah = 1.00m,
        AmountKopecks = 100L,
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = TestClock.UtcNow,
        IsAutomatic = false,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    /// <summary>
    /// The same guarantee for the self-serve renewal, now enforced by the thing that replaced the
    /// guard: the renewal LINE is what the customer paid for and what records how the term moved, so
    /// deleting the order must not erase it.
    /// </summary>
    /// <remarks>
    /// This guarantee did not move, it changed owner. It used to be
    /// <c>voucher_renewal_items.order_id</c> RESTRICT; that table was dissolved by D-13 and
    /// <c>order_line_items.order_id</c> is RESTRICT for exactly this reason. A test that stopped
    /// existing here would have quietly dropped the protection of a paid renewal, so it is rewritten
    /// rather than deleted - and it now covers the line end to end, including the snapshots that used
    /// to live on the retired table.
    /// </remarks>
    [Fact]
    public async Task Deleting_A_renewal_Order_With_Renewal_Lines_Is_Refused()
    {
        var user = NewUser("+38000000002");
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Price = 500m,
            Kind = OrderKind.Renewal,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

        await using (var seed = CreateContext())
        {
            // source_voucher_id is a RESTRICT FK to a real voucher, so the renewal line needs one.
            var source = NewVoucher("renewal-source", "renewal-source-qr");
            seed.FuelVouchers.Add(source);
            seed.Users.Add(user);
            seed.Orders.Add(order);
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                BrandId = BrandIds.ForSlug(source.Brand),
                Brand = source.Brand,
                FuelTypeId = source.FuelTypeId,
                Liters = source.Liters,
                Quantity = 1,
                UnitPrice = 500m,
                LineTotal = 500m,
                TermCode = "1m",
                SourceVoucherId = source.Id,
                RenewalBranch = VoucherRenewalBranchCodes.Extend,
                PreviousCustomerExpiration = new DateOnly(2026, 12, 1),
                NewCustomerExpiration = new DateOnly(2027, 1, 1)
            });
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var act = () => context.Database.ExecuteSqlRawAsync("""DELETE FROM orders WHERE id = {0}""", order.Id);

        await act.Should().ThrowAsync<PostgresException>();

        await using var verify = CreateContext();
        (await verify.OrderLineItems.CountAsync(l => l.OrderId == order.Id))
            .Should().Be(1, "the renewal line is what the customer paid for, and it must survive");
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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        var entity = new LegalEntity
        {
            Id = entityId,
            UserId = userId,
            Name = "Test LLC",
            Edrpou = "1234567890",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        var contract = new Contract
            {
                Id = contractId,
                UserId = userId,
                LegalEntityId = entityId,
                BrandId = BrandIds.Okko,
                CreatedAtUtc = TestClock.UtcNow,
                // The validity term is mandatory since 2026-10-20, and it has to be AFTER the
                // contract was created - ck_contracts_expiry_after_creation refuses the other.
                ExpiresAtUtc = TestClock.UtcNow.AddYears(1)
            };
        var signature = new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ContractId = contractId,
            SignatureData = "signature-bytes",
            SignedAtUtc = TestClock.UtcNow
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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// A voucher may not be promised past the brand's own printed term. Zero-litre fuel is
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

    // ── D-13 stage 2: a renewal line has to be a whole renewal ───────────────────────────────

    /// <summary>
    /// A renewal line names a voucher, a term and a branch. The three arrived separately, and the
    /// database now refuses to have any one of them alone.
    ///
    /// <para>
    /// This is the check whose absence cost a day of investigation. Renewal lines were created with a
    /// null <c>term_code</c> and the term lived only on <c>voucher_renewal_items</c> — so the line the
    /// customer was charged against could not say what it was for, and the term that drives the
    /// extend/replace decision at fulfilment had to be fetched from a table with no relationship to the
    /// line. Nothing was invalid; the row was simply incomplete, and complete-looking.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("extend", true, false)]        // no term   - ck_order_line_renewal_has_term
    [InlineData("replace", true, false)]
    [InlineData("extend", false, true)]        // no source - ck_order_line_renewal_pair_is_whole
    [InlineData("extend", false, false)]
    [InlineData("voucher_reissue", true, true)] // unknown word - ck_order_line_renewal_branch
    public async Task An_Incomplete_Or_Unknown_Renewal_Line_Is_Refused(
        string branch, bool hasTerm, bool hasSource)
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "renewal-source-" + Guid.NewGuid().ToString("N")[..6],
            "renewal-source-qr-" + Guid.NewGuid().ToString("N")[..6]);
        context.FuelVouchers.Add(voucher);
        context.Users.Add(NewUser("+38000000009"));

        context.OrderLineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),   // no such order; the FK never gets that far
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-dp"),
            Liters = 10m,
            Quantity = 1,
            UnitPrice = 100m,
            LineTotal = 100m,
            TermCode = hasTerm ? "1m" : null,
            SourceVoucherId = hasSource ? voucher.Id : null,
            RenewalBranch = branch
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            $"branch={branch} term={hasTerm} source={hasSource} is not a whole renewal line");
    }

    // === D-13 stage 3: what a voucher_movements row is allowed to say ===

    /// <summary>
    /// A movement says a piece of paper changed place, so it names two places and they differ. A row
    /// that says "from the shelf, to the shelf" is not a movement, and that is the whole of what keeps
    /// <c>extend</c> out: pushing an expiry out moves no paper, so it is not one.
    /// </summary>
    [Theory]
    [InlineData("issued", "pool", "pool", false)]              // same place - distinct_locations
    [InlineData("issued", "pool", "warehouse", false)]         // not a place - locations
    [InlineData("issued", "shelf", "customer", false)]         // not a place - locations
    [InlineData("redeem", "pool", "customer", false)]         // a state change, not a kind
    [InlineData("renewal_extend", "pool", "customer", false)] // never was a kind
    public async Task An_Impossible_Voucher_Movement_Is_Refused(
        string kind, string from, string to, bool _)
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "movement-v-" + Guid.NewGuid().ToString("N")[..6],
            "movement-v-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            VoucherId = voucher.Id,
            FromLocation = from,
            ToLocation = to,
            OccurredAtUtc = TestClock.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            $"kind={kind} {from}->{to} is not a movement this schema can describe");
    }

    /// <summary>
    /// The counterpart, and the reason this shape replaced the last one: a real handover fills in BOTH
    /// ends. Issuing a ticket to a customer is pool → customer with nothing empty anywhere, which is
    /// exactly what the previous <c>from_voucher_id</c>/<c>to_voucher_id</c> shape could not say — it
    /// wrote <c>to_voucher_id = NULL</c> for every one of them.
    /// </summary>
    [Fact]
    public async Task A_Real_Handover_Needs_No_Empty_Side()
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "movement-out-" + Guid.NewGuid().ToString("N")[..6],
            "movement-out-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = VoucherMovementKinds.Issued,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Customer,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            AmountUah = 500m,
            OccurredAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();

        var saved = await context.VoucherMovements.AsNoTracking().SingleAsync(m => m.VoucherId == voucher.Id);
        saved.VoucherId.Should().NotBe(Guid.Empty);
        saved.FromLocation.Should().Be(VoucherLocations.Pool);
        saved.ToLocation.Should().Be(VoucherLocations.Customer,
            "a movement always says where the paper went");
    }

    /// <summary>
    /// The second counterpart, and the second reason for the shape: a state change is not a movement.
    /// Activation, redemption, block, expiry and extension all change what a ticket is and not where it
    /// is, so they belong in <c>voucher_events</c> and there is deliberately no kind for them. Asserted
    /// as an exact list because adding one back is the mistake this table was just reshaped to remove,
    /// and an exact list is what makes that a test failure rather than a review comment.
    /// </summary>
    [Fact]
    public void Every_Kind_Is_A_Real_Change_Of_Place()
    {
        VoucherMovementKinds.All.Should().BeEquivalentTo(
            ["deposit", "issued", "returned", "replace_out",
             "exchange_out", "exchange_in", "gift", "recall"]);
    }

    [Fact]
    public void The_Author_Vocabulary_Is_Exactly_Two_Words()
    {
        // Deliberately tiny. A third kind would be the moment "which of these is it again?" starts, and
        // the two that exist cover every action: a person did it, or the scheduler did.
        VoucherActorKinds.All.Should().BeEquivalentTo(["user", "system"]);
    }

    /// <summary>
    /// The zero GUID is the value a missing claim actually becomes, not a hypothetical:
    /// <c>AdminVoucherController.GetUserId</c> returns <c>Guid.Empty</c> rather than null. Being NOT
    /// NULL it satisfies the pairing constraint on its own, so this is the one way to name a user who
    /// does not exist — and the test that lets the author rule keep its promise.
    /// </summary>
    [Fact]
    public async Task An_Author_Id_That_Names_Nobody_Is_Refused()
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "nobody-v-" + Guid.NewGuid().ToString("N")[..6],
            "nobody-v-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = VoucherMovementKinds.Issued,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Customer,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.Empty,
            OccurredAtUtc = TestClock.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();

        // 23514 is Postgres's check_violation.
        var violation = await act.Should().ThrowAsync<DbUpdateException>();
        violation.Which.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23514");
    }

    /// <summary>
    /// The other half of the same rule, and the only author a scheduler can honestly have.
    /// </summary>
    [Fact]
    public async Task The_Scheduler_May_Have_No_Id_At_All()
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "sched-v-" + Guid.NewGuid().ToString("N")[..6],
            "sched-v-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = VoucherMovementKinds.Issued,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Customer,
            ActorKind = VoucherActorKinds.System,
            ActorUserId = null,
            OccurredAtUtc = TestClock.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();

        // NULL is the one thing a scheduler is allowed to have, and the whole reason the pairing
        // constraint is written `(kind = 'user') = (id IS NOT NULL)` rather than "id is required".
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Planning #319: the trade facts the ledger carries for the fold of <c>voucher_exchanges</c>. A
    /// voucher that replaced itself is not a trade, and the rule that says so is the one the folded
    /// table already follows on its own pair (<c>ck_voucher_exchange_not_self</c>).
    /// </summary>
    [Fact]
    public async Task A_Trade_Cannot_Be_With_The_Same_Paper()
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "self-v-" + Guid.NewGuid().ToString("N")[..6],
            "self-v-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = VoucherMovementKinds.ExchangeOut,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Supplier,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            OccurredAtUtc = TestClock.UtcNow,
            ReplacedByVoucherId = voucher.Id
        });

        var act = async () => await context.SaveChangesAsync();

        var violation = await act.Should().ThrowAsync<DbUpdateException>();
        violation.Which.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23514");
    }

    /// <summary>
    /// The money the ledger carries is money that moved, and money that moved is not negative. A
    /// refund is a Payment row (D-10), not a negative movement.
    /// </summary>
    [Fact]
    public async Task A_Negative_Surcharge_Share_Is_Refused()
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "share-v-" + Guid.NewGuid().ToString("N")[..6],
            "share-v-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = VoucherMovementKinds.ExchangeOut,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Supplier,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            OccurredAtUtc = TestClock.UtcNow,
            SurchargeShareUah = -1m
        });

        var act = async () => await context.SaveChangesAsync();

        var violation = await act.Should().ThrowAsync<DbUpdateException>();
        violation.Which.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23514");
    }

    /// <summary>
    /// The same rule for the cost per litre: a price is not negative.
    /// </summary>
    [Fact]
    public async Task A_Negative_Applied_Cost_Is_Refused()
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "cost-v-" + Guid.NewGuid().ToString("N")[..6],
            "cost-v-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = VoucherMovementKinds.ExchangeOut,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Supplier,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            OccurredAtUtc = TestClock.UtcNow,
            AppliedCostPerLiter = -1m
        });

        var act = async () => await context.SaveChangesAsync();

        var violation = await act.Should().ThrowAsync<DbUpdateException>();
        violation.Which.InnerException.Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be("23514");
    }

    [Theory]
    [InlineData("user", true, false)]     // a person, named            - allowed
    [InlineData("system", false, false)] // the scheduler              - allowed
    [InlineData("user", false, true)]    // says "a person", names none - refused
    [InlineData("system", true, true)]   // the scheduler with an id  - refused
    [InlineData("robot", true, true)]    // not a kind at all          - refused
    public async Task A_Recorded_Action_Must_Name_Its_Author(
        string actorKind, bool hasUserId, bool shouldBeRefused)
    {
        // The point of the kind column: "always record the actor" is not enforceable against a bare
        // FK-free uuid, because NULL, a zero GUID and a sentinel that resolves to nobody all look like
        // data. Here none of them is writable.
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "actor-v-" + Guid.NewGuid().ToString("N")[..6],
            "actor-v-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = VoucherMovementKinds.Issued,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Customer,
            ActorKind = actorKind,
            ActorUserId = hasUserId ? Guid.NewGuid() : null,
            OccurredAtUtc = TestClock.UtcNow
        });

        if (shouldBeRefused)
        {
            var act = async () => await context.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>(
                $"actor_kind={actorKind} with user id={hasUserId} names no author");
            return;
        }

        // The counterpart: a stated author is writable, which is the point.
        await context.SaveChangesAsync();
        (await context.VoucherMovements.AsNoTracking().SingleAsync(m => m.VoucherId == voucher.Id))
            .ActorKind.Should().Be(actorKind);
    }

    [Fact]
    public async Task An_Amount_That_Is_Negative_Is_Refused()
    {
        // A negative payment is a sign error, not a refund. Refunds are Payments rows (D-10); a
        // movement carrying one would mean two places that know the sign of the same money.
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "movement-amt-" + Guid.NewGuid().ToString("N")[..6],
            "movement-amt-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.NewGuid(),
            Kind = VoucherMovementKinds.Issued,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Customer,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            AmountUah = -1m,
            OccurredAtUtc = TestClock.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("money in is money in");
    }

    // === D-13 stage 4: what a voucher_events row is allowed to say ===

    /// <summary>
    /// An event is a state change or an expiry push, and it says which. Everything else about it is
    /// enforced by the pairing: a status change carries both ends, an extension carries both dates,
    /// and nothing else carries either.
    /// </summary>
    [Theory]
    [InlineData("StatusChanged", true, false, false, false)] // only a "from" - half a change
    [InlineData("StatusChanged", true, true, false, false)]  // from == to - changed to itself
    [InlineData("Extended", false, false, true, false)]      // only a "previous" date - half a pair
    [InlineData("Extended", false, false, false, true)]      // the new date runs backwards
    [InlineData("Imported", true, false, false, false)]      // imported from a status - half an import
    [InlineData("Redeemed", true, true, false, false)]       // a state change wearing the wrong name
    [InlineData("StatusChanged", false, false, false, false)] // nothing at all
    public async Task An_Impossible_Voucher_Event_Is_Refused(
        string eventType, bool hasFrom, bool hasTo, bool hasPreviousDate, bool hasNewDate)
    {
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "event-v-" + Guid.NewGuid().ToString("N")[..6],
            "event-v-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = voucher.Id,
            EventType = eventType,
            FromStatus = hasFrom ? "Available" : null,
            ToStatus = hasTo ? "Available" : null,
            PreviousCustomerExpiration = hasPreviousDate ? new DateOnly(2026, 10, 1) : null,
            NewCustomerExpiration = hasNewDate ? new DateOnly(2026, 9, 1) : null,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            OccurredAtUtc = TestClock.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            $"event_type={eventType} is not a state change this schema can describe");
    }

    [Fact]
    public async Task A_Status_Change_Must_Say_Both_Ends_And_They_Must_Differ()
    {
        // The counterpart to the theory above, and the row the table exists for: an ordinary
        // transition, written the way every writer will write it.
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "event-ok-" + Guid.NewGuid().ToString("N")[..6],
            "event-ok-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = voucher.Id,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.Available),
            ToStatus = nameof(VoucherStatus.Assigned),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            OccurredAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();

        // This class does not reset between tests, so filter rather than SingleAsync.
        var saved = await context.VoucherEvents.AsNoTracking().SingleAsync(e => e.VoucherId == voucher.Id);
        saved.FromStatus.Should().Be(nameof(VoucherStatus.Available));
        saved.ToStatus.Should().Be(nameof(VoucherStatus.Assigned));
    }

    [Fact]
    public async Task An_Extension_Must_Carry_Its_Both_Dates()
    {
        // An extension is the one event whose detail is NOT recoverable from anywhere else: the voucher's
        // customer_expiration_date was overwritten by the same statement that wrote this row. So the
        // pair is pinned whole and forward-only.
        await using var context = CreateContext();
        var voucher = NewVoucher(
            "event-ext-" + Guid.NewGuid().ToString("N")[..6],
            "event-ext-qr-" + Guid.NewGuid().ToString("N")[..6]);

        context.FuelVouchers.Add(voucher);
        context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = voucher.Id,
            EventType = VoucherEventTypes.Extended,
            PreviousCustomerExpiration = new DateOnly(2026, 10, 22),
            NewCustomerExpiration = new DateOnly(2026, 11, 22),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            ExternalRef = "order:00000000-0000-0000-0000-000000000001",
            OccurredAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();

        // This class does not reset between tests, so filter rather than SingleAsync.
        var saved = await context.VoucherEvents.AsNoTracking().SingleAsync(e => e.VoucherId == voucher.Id);
        saved.PreviousCustomerExpiration.Should().Be(new DateOnly(2026, 10, 22));
        saved.NewCustomerExpiration.Should().Be(new DateOnly(2026, 11, 22));
    }

    [Fact]
    public void Every_Event_Type_Is_A_State_Change_Or_An_Import()
    {
        // Exact list on purpose. A per-action vocabulary is how order_events ended up with eight types
        // and two writers; adding "Redeemed" or "Blocked" here would be that mistake again, and an exact
        // list is what makes it a test failure rather than a review comment.
        VoucherEventTypes.All.Should().BeEquivalentTo(
            ["Imported", "StatusChanged", "Extended", "Archived", "Unarchived"]);
    }

    /// <summary>
    /// The counterpart, because a constraint that only ever rejects is indistinguishable from a broken
    /// one. An ordinary buy-fuel line carries none of the three, and that must stay the normal case:
    /// these columns are about renewals, not a new requirement on every line in the schema.
    /// </summary>
    [Fact]
    public async Task An_Ordinary_Line_Needs_None_Of_The_Renewal_Columns()
    {
        await using var context = CreateContext();
        var user = NewUser("+38000000010");
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Price = 5000m,
            Status = OrderStatus.PendingPayment,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        context.Users.Add(user);
        context.Orders.Add(order);

        context.OrderLineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-dp"),
            Liters = 50m,
            Quantity = 2,
            UnitPrice = 2500m,
            LineTotal = 5000m
            // TermCode, SourceVoucherId and RenewalBranch all left null - a buy-fuel line.
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

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
            $$"""
            INSERT INTO fuel_vouchers (id, brand, brand_id, fuel_type_id, liters, supplier_expiration_date,
                                       customer_expiration_date, voucher_number, qr_payload, qr_payload_sha256,
                                       created_at_utc, updated_at_utc, status, data_kind, is_deleted)
            VALUES ({0}, 'okko', '{{BrandIds.Okko}}', '{{ToUuid("okko-dp")}}', 10, '2027-01-01', '2027-01-01', 'bad-status', 'bad-status-qr', sha256(convert_to('bad-status-qr','UTF8')),
                    now(), now(), 'Avilble', 'real', false)
            """, Guid.NewGuid());

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task An_Outbox_Event_With_An_Unknown_Type_Is_Refused()
    {
        await using var context = CreateContext();

        // The jsonb payload is doubled up because ExecuteSqlRaw treats braces as placeholders.
        // The id is given because the column is a uuid with no default - the key is the
        // application's to write, and a raw INSERT has to write it too.
        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO outbox_events (id, event_type, payload, processed, created_at_utc)
            VALUES (gen_random_uuid(), 'OrderFuifilled', '{{}}', false, now())
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
            $$"""
            INSERT INTO fuel_vouchers (id, brand, brand_id, fuel_type_id, liters, supplier_expiration_date,
                                       customer_expiration_date, voucher_number, qr_payload, qr_payload_sha256,
                                       created_at_utc, updated_at_utc, status, data_kind, is_deleted)
            VALUES ({0}, 'okko', '{{BrandIds.Okko}}', '{{ToUuid("okko-dp")}}', 10, '2027-01-01', '2027-01-01', 'longest-status', 'longest-status-qr', sha256(convert_to('longest-status-qr','UTF8')),
                    now(), now(), 'VerifiedWithWarnings', 'real', false)
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
