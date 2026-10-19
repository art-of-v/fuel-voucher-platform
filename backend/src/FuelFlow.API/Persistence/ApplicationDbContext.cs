using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Settlement.SharedModels;
using FuelFlow.Features.Support.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Features.Vouchers.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FuelFlow.Persistence;

public sealed class ApplicationDbContext : DbContext, IImportVouchersDbContext
{
    /// <summary>
    /// Optional so the ~100 `new ApplicationDbContext(options)` call sites in the test suites keep
    /// compiling; DI resolves it in the app, and a test that does not care about concurrency logging
    /// gets a null logger rather than having to pass one.
    /// </summary>
    private readonly ILogger<ApplicationDbContext>? _logger;

/// <summary>
    /// Encrypts the voucher QR payload at rest. Required, and it is required because of what happens
    /// when it is absent rather than what happens when it is present.
    /// </summary>
    /// <remarks>
    /// EF caches one model per context type. A context built without a cipher produces a model whose
    /// QR column maps to raw <c>bytea</c> with no converter, and that model is then reused by every
    /// later context - so a single missing injection anywhere turns every voucher read in the process
    /// into <c>42804: qr_payload cannot be cast to bytea</c>, which is at least loud but is a very
    /// confusing place to learn it. Making the parameter mandatory means the compiler settles the
    /// argument at every construction site instead of leaving it to whoever is refactoring.
    /// </remarks>
private readonly FuelFlow.Features.Vouchers.Security.IQrCipher _qrCipher;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        FuelFlow.Features.Vouchers.Security.IQrCipher qrCipher,
        ILogger<ApplicationDbContext>? logger = null) : base(options)
    {
        _logger = logger;
        _qrCipher = qrCipher;
    }

    public DbSet<FuelVoucher> FuelVouchers => Set<FuelVoucher>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseBatch> PurchaseBatches => Set<PurchaseBatch>();
    public DbSet<QrParameters> QrParameters => Set<QrParameters>();
    public DbSet<VoucherImport> VoucherImports => Set<VoucherImport>();
    public DbSet<VoucherImportError> VoucherImportErrors => Set<VoucherImportError>();
    public DbSet<VoucherExchange> VoucherExchanges => Set<VoucherExchange>();
    // D-13 / #319: the parent voucher_exchanges.exchange_batch_id never had. Still additive - the
    // three movement tables are folded into one ledger in later stages of D-13.
    public DbSet<MovementBatch> MovementBatches => Set<MovementBatch>();
    // D-13: the state history voucher_movements deliberately does not carry - see VoucherEvent.
    public DbSet<VoucherEvent> VoucherEvents => Set<VoucherEvent>();
    // D-13 / #319: the single ledger the three movement tables fold into. Also additive for now -
    // nothing writes it until every writer is gated on its compare-and-swap actually changing a row.
    public DbSet<VoucherMovement> VoucherMovements => Set<VoucherMovement>();
    public DbSet<MonobankStatementEntry> MonobankStatementEntries => Set<MonobankStatementEntry>();
    public DbSet<OperatorVoucherRenewal> OperatorVoucherRenewals => Set<OperatorVoucherRenewal>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<Fulfillment> Fulfillments => Set<Fulfillment>();
    public DbSet<OrderLineItem> OrderLineItems => Set<OrderLineItem>();
    // No Refunds set: a refund is a Payment with kind = 'Refund'. See Features/Payments.
    // A payment is an entity, not four columns on orders; a webhook delivery is stored before it is
    // acted on. See Features/Payments for why both arrived at once.
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    // Every message the settlement channel receives, including the ones that produced no payment.
    // Its unique (chat_id, message_id) is what stops a Telegram redelivery becoming a second payment.
    public DbSet<SettlementMessage> SettlementMessages => Set<SettlementMessage>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // Pricing configuration as effective-dated, approval-gated rows rather than app_settings keys.
    // See D-09 in the planning repo; nothing reads this yet.
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserPushToken> PushTokens => Set<UserPushToken>();

    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<BrandStation> BrandStations => Set<BrandStation>();
    public DbSet<FuelTypeEntity> FuelTypes => Set<FuelTypeEntity>();

    // No FuelPackages DbSet: the table is gone (SplitFuelPackagesIntoOffers then DropFuelPackages).
    // The `FuelPackage` class stays, but as an API shape only - `PackageProjection` builds one from a
    // volume and its offer, and the admin endpoints still speak it. Registering it here is what would
    // make EF try to map it, and there is no longer a table to map it to.

    // The split target of #318. Nothing reads these yet in this PR: they are created, backfilled and
    // constrained so the migration can be proved against a real database before any read path moves.
    public DbSet<FuelNominal> FuelNominals => Set<FuelNominal>();

    // The append-only record of what each offer was priced at. Read by nothing in the application yet
    // on purpose: the offer's own columns remain the current price, so every existing read path is
    // untouched, and the history exists for the money questions that today's price cannot answer -
    // see FuelOfferPrice for those.
    public DbSet<FuelOfferPrice> FuelOfferPrices => Set<FuelOfferPrice>();

    // The voucher side's crosswalk: which QR product code means which grade at which network. Read by
    // the OKKO voucher parser to resolve a fuel by the one signal in a parsed voucher that is not a
    // name. #349 step 3b.
    public DbSet<FuelTypeProductCode> FuelTypeProductCodes => Set<FuelTypeProductCode>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<UserContract> UserContracts => Set<UserContract>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();
    public DbSet<CompanyMember> CompanyMembers => Set<CompanyMember>();
    public DbSet<AuditEventOutbox> AuditEventOutbox => Set<AuditEventOutbox>();
    public DbSet<OperatorLegalEntity> OperatorLegalEntities => Set<OperatorLegalEntity>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<NaturalPerson> NaturalPersons => Set<NaturalPerson>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<LedgerMappingRule> LedgerMappingRules => Set<LedgerMappingRule>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    public DbSet<FeedbackMessage> FeedbackMessages => Set<FeedbackMessage>();
    public DbSet<FeedbackMessageReply> FeedbackMessageReplies => Set<FeedbackMessageReply>();
    public DbSet<FeedbackChatBinding> FeedbackChatBindings => Set<FeedbackChatBinding>();
    public DbSet<VoucherTermPricing> VoucherTermPricings => Set<VoucherTermPricing>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // The QR payload is encrypted on its way into the column and decrypted on the way out, so
        // the ~19 call sites that read or write it are unchanged. Applied here rather than in
        // FuelVoucherConfiguration because the converter needs the cipher instance and that
        // configuration is discovered by assembly scan, which has no DI.
        //
        // The lambdas capture the instance, not the key material. EF caches the model per context
        // type, so baking the key bytes into the model would freeze them at first use - and a
        // rotated key would then silently fail to decrypt anything written since. Reading through
        // the instance means the cached model keeps working when the key is rotated underneath it.
var cipher = _qrCipher;
        var voucherQr = modelBuilder.Entity<FuelVoucher>().Metadata.FindProperty(
            nameof(FuelVoucher.QrPayload));

        voucherQr!.SetValueConverter(new ValueConverter<string, byte[]>(
            plain => cipher.Encrypt(plain),
            stored => cipher.Decrypt(stored)));

        modelBuilder.Entity<Brand>().HasData(SeedData.Brands);
        modelBuilder.Entity<FuelTypeEntity>().HasData(SeedData.FuelTypes);
        // fuel_nominals is NOT seeded here. The 2026-10-18 backfill already wrote a row for every
        // live volume, which is exactly this set; seeding it again inserts the same primary keys a
        // second time and the migration fails. One source for the sizes: the backfill.
        //
        // The catalogue used to be seeded as offers here too. It is not: `SeedData.Catalogue`
        // returns volume rows, which only the historical 20261015151014 migration still needs.
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesGuardedAsync(cancellationToken);
    }
    // The synchronous SaveChanges() is deliberately NOT wrapped. Every write in this app is async
    // (there is not one bare SaveChanges() call in src), and guarding it would mean blocking on
    // SaveChangesAsync with GetAwaiter().GetResult() - a deadlock waiting for a context that has no
    // SynchronizationContext today and no guarantee of one tomorrow, in exchange for logging that
    // nothing would emit.

    private async Task<int> SaveChangesGuardedAsync(CancellationToken cancellationToken)
    {
        // Captured before the save: a Modified order's original value is still readable here and
        // stops being comparable afterwards.
        var transitions = CaptureOrderTransitions();

        try
        {
            var affected = await base.SaveChangesAsync(cancellationToken);

            if (transitions.Count > 0)
            {
                await RecordOrderEventsAsync(transitions, cancellationToken);
            }

            return affected;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // #246 detector. Logged here rather than in the HTTP exception handler because every
            // write in the app goes through this method - including background jobs, which never
            // touch the HTTP pipeline and would otherwise fail with no record of what collided.
            //
            // Warning, not Error: a concurrency token is only on a handful of tables and this fires
            // whenever a row changed underneath a writer, which is the whole point of the token -
            // most such collisions are benign and the caller simply retries. The entity type and key
            // are what a follow-up needs to know which handlers genuinely race in production; two
            // attempts to find that by reading the code both came back wrong.
            //
            // Rethrown, never swallowed: the caller has to learn its write did not land, or the lost
            // update this is here to expose is simply recreated.
            if (_logger is not null)
            {
                foreach (var entry in ex.Entries)
                {
                    var key = entry.Metadata.FindPrimaryKey()?.Properties
                        .Select(p => p.Name)
                        .FirstOrDefault();

                    _logger.LogWarning(
                        ex,
                        "Concurrency conflict on {Entity} ({KeyProperty}={Key}) - the row changed since it was read, so this write was not applied",
                        entry.Metadata.ClrType.Name,
                        key ?? "id",
                        key is null ? null : entry.Property(key)?.CurrentValue);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// The order status changes staged in this unit of work, paired with what they came from.
    /// </summary>
    private List<OrderTransition> CaptureOrderTransitions()
    {
        var transitions = new List<OrderTransition>();

        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Added))
                continue;

            // Added counts as a creation: an order's first row is its own history, and a table that
            // is silently empty until the second status change is not much of a record.
            if (entry.State == EntityState.Added)
            {
                transitions.Add(new OrderTransition(entry.Entity.Id, (OrderStatus?)null, entry.Entity.Status, true));
                continue;
            }

            // Order.Status is an enum with a string conversion applied at the provider level, so
            // OriginalValue is already OrderStatus here - not the stored string.
            var before = entry.Property(o => o.Status).OriginalValue as OrderStatus?;

            if (before != entry.Entity.Status)
            {
                transitions.Add(new OrderTransition(entry.Entity.Id, before, entry.Entity.Status, false));
            }
        }

        return transitions;
    }

    private async Task RecordOrderEventsAsync(
        IReadOnlyList<OrderTransition> transitions, CancellationToken cancellationToken)
    {
        foreach (var transition in transitions)
        {
            OrderEvents.Add(new OrderEvent
            {
                Id = Guid.CreateVersion7(),
                OrderId = transition.OrderId,
                EventType = transition.IsCreation ? "Created" : "StatusChanged",
                FromStatus = transition.FromStatus?.ToString(),
                ToStatus = transition.ToStatus.ToString(),
                // The invoice id the order carries is the external reference a webhook delivering
                // twice would repeat, so it is what makes a duplicate findable later.
                ExternalRef = await CurrentInvoiceIdAsync(transition.OrderId, cancellationToken),
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        await base.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> CurrentInvoiceIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // Read from the database rather than the change tracker: the order may have been saved in an
        // earlier SaveChanges within the same request, and an entry the tracker already released has
        // no value left to give.
        return await Orders
            .Where(o => o.Id == orderId)
            .Select(o => o.MonobankInvoiceId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private readonly record struct OrderTransition(
        Guid OrderId, OrderStatus? FromStatus, OrderStatus ToStatus, bool IsCreation);
}


