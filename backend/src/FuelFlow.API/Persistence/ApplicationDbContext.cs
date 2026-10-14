using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Support.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Features.Vouchers.SharedModels;
using Microsoft.EntityFrameworkCore;
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

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ILogger<ApplicationDbContext>? logger = null) : base(options)
    {
        _logger = logger;
    }

    public DbSet<FuelVoucher> FuelVouchers => Set<FuelVoucher>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseBatch> PurchaseBatches => Set<PurchaseBatch>();
    public DbSet<QrParameters> QrParameters => Set<QrParameters>();
    public DbSet<VoucherImport> VoucherImports => Set<VoucherImport>();
    public DbSet<VoucherImportError> VoucherImportErrors => Set<VoucherImportError>();
    public DbSet<VoucherRenewalItem> VoucherRenewalItems => Set<VoucherRenewalItem>();
    public DbSet<VoucherExchange> VoucherExchanges => Set<VoucherExchange>();
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
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserPushToken> PushTokens => Set<UserPushToken>();

    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StationNode> StationNodes => Set<StationNode>();
    public DbSet<FuelTypeEntity> FuelTypes => Set<FuelTypeEntity>();
    public DbSet<FuelPackage> FuelPackages => Set<FuelPackage>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<UserContract> UserContracts => Set<UserContract>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();
    public DbSet<CompanyMember> CompanyMembers => Set<CompanyMember>();
    public DbSet<AuditEventOutbox> AuditEventOutbox => Set<AuditEventOutbox>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    public DbSet<FeedbackMessage> FeedbackMessages => Set<FeedbackMessage>();
    public DbSet<FeedbackMessageReply> FeedbackMessageReplies => Set<FeedbackMessageReply>();
    public DbSet<FeedbackChatBinding> FeedbackChatBindings => Set<FeedbackChatBinding>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder.Entity<Station>().HasData(SeedData.Stations);
        modelBuilder.Entity<FuelTypeEntity>().HasData(SeedData.FuelTypes);
        modelBuilder.Entity<FuelPackage>().HasData(SeedData.FuelPackages);
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


