using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Support.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Settlement.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The schema carries its own documentation.
/// </summary>
/// <remarks>
/// <para>
/// The code documents the model well, and the database documented nothing at all: 0 of 57
/// tables and 0 of 600 columns carried a comment. The code is not what an operator, a BI tool
/// or the next engineer opens when the code is not in front of them - and the columns whose
/// names do not carry their meaning are exactly the ones they need: which of the price
/// columns is the truth, and what a NULL in it means.
/// </para>
/// <para>
/// These assertions are about the MODEL, which is where the comment is written and where the
/// migration reads it from - so a comment that is deleted from the model fails here, and a
/// migration that does not carry it to the database fails the next from-zero build. The
/// comment is read as the annotation it is stored as, because the relational accessor for it
/// refuses the read-optimised runtime model.
/// </para>
/// </remarks>
public sealed class SchemaCommentTests
{
    private static readonly string[] ThePriceColumns =
    [
        nameof(FuelTypeEntity.SupplierPricePerLiter),
        nameof(FuelTypeEntity.MarginUahPerLiter),
        nameof(FuelTypeEntity.MarginPercent),
        nameof(FuelTypeEntity.PumpPricePerLiter),
        nameof(FuelTypeEntity.MinDiscountPerLiter),
        nameof(FuelTypeEntity.BasePrice),
        nameof(FuelTypeEntity.DiscountPrice),
        nameof(FuelTypeEntity.AllowBelowCost)
    ];

    /// <summary>
    /// The audit tables, and the columns that answer "what happened, to what, and who".
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheAuditTables =
    [
        (typeof(AuditEventOutbox),
        [
            nameof(AuditEventOutbox.AggregateType),
            nameof(AuditEventOutbox.AggregateId),
            nameof(AuditEventOutbox.ScopeId),
            nameof(AuditEventOutbox.EventType),
            nameof(AuditEventOutbox.OldValue),
            nameof(AuditEventOutbox.NewValue),
            nameof(AuditEventOutbox.ChangedByUserId),
            nameof(AuditEventOutbox.ChangedByUserName),
            nameof(AuditEventOutbox.Summary),
            nameof(AuditEventOutbox.ChangedAtUtc)
        ]),
        (typeof(VoucherEvent),
        [
            nameof(VoucherEvent.VoucherId),
            nameof(VoucherEvent.EventType),
            nameof(VoucherEvent.FromStatus),
            nameof(VoucherEvent.ToStatus),
            nameof(VoucherEvent.PreviousCustomerExpiration),
            nameof(VoucherEvent.NewCustomerExpiration),
            nameof(VoucherEvent.ExternalRef),
            nameof(VoucherEvent.ActorKind),
            nameof(VoucherEvent.ActorUserId),
            nameof(VoucherEvent.ActorPartyId),
            nameof(VoucherEvent.Reason),
            nameof(VoucherEvent.OccurredAtUtc),
            nameof(VoucherEvent.Payload)
        ]),
        (typeof(OrderEvent),
        [
            nameof(OrderEvent.OrderId),
            nameof(OrderEvent.EventType),
            nameof(OrderEvent.FromStatus),
            nameof(OrderEvent.ToStatus),
            nameof(OrderEvent.ExternalRef),
            nameof(OrderEvent.Payload),
            nameof(OrderEvent.ActorUserId),
            nameof(OrderEvent.OccurredAtUtc)
        ])
    ];

    [Fact]
    public void The_Price_Columns_Should_Say_What_They_Are()
    {
        var context = BuildContext();
        var fuelType = context.Model.FindEntityType(typeof(FuelTypeEntity));

        Assert.NotNull(fuelType);

        foreach (var column in ThePriceColumns)
        {
            var property = fuelType.FindProperty(column);
            Assert.NotNull(property);

            var comment = CommentOf(property);
            Assert.False(string.IsNullOrWhiteSpace(comment),
                $"{column} carries no comment, and its name does not say what it is: the price "
                + "model has a cost, a margin, a column price, a minimum customer discount and "
                + "a list price, and a reader of the database cannot tell them apart.");
        }
    }

    [Fact]
    public void The_Comments_Should_Say_What_A_Null_Means()
    {
        var context = BuildContext();
        var fuelType = context.Model.FindEntityType(typeof(FuelTypeEntity));

        Assert.NotNull(fuelType);

        // "Not stated" and "zero" are different facts, and the comment is where the difference
        // is written down: a cost of zero would price the fuel at its margin and blind the
        // below-cost guard, so a reader has to be able to tell the two apart.
        var cost = fuelType.FindProperty(nameof(FuelTypeEntity.SupplierPricePerLiter));
        var margin = fuelType.FindProperty(nameof(FuelTypeEntity.MarginUahPerLiter));

        Assert.NotNull(cost);
        Assert.NotNull(margin);
        Assert.Contains("NULL", CommentOf(cost), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NULL", CommentOf(margin), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Audit_Tables_Should_Say_What_They_Are()
    {
        var context = BuildContext();
        foreach (var (entityType, columns) in TheAuditTables)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. An audit row answers 'what "
                    + "happened, to what, and who' - and the column names alone do not say which "
                    + "of those a column is.");
            }
        }
    }

    /// <summary>
    /// The money tables, and the columns that answer "how much, in what, and whose".
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheMoneyTables =
    [
        (typeof(Payment),
        [
            nameof(Payment.OrderId),
            nameof(Payment.UserId),
            nameof(Payment.Kind),
            nameof(Payment.ParentPaymentId),
            nameof(Payment.Provider),
            nameof(Payment.ProviderInvoiceId),
            nameof(Payment.ProviderPaymentId),
            nameof(Payment.AmountUah),
            nameof(Payment.AmountKopecks),
            nameof(Payment.ProviderAmountKopecks),
            nameof(Payment.Currency),
            nameof(Payment.Status),
            nameof(Payment.PaidAtUtc),
            nameof(Payment.WebhookEventId),
            nameof(Payment.ProviderPayload),
            nameof(Payment.CreatedByUserId),
            nameof(Payment.CreatedByUserName),
            nameof(Payment.IsAutomatic),
            nameof(Payment.ErrorMessage)
        ]),
        (typeof(JournalEntry),
        [
            nameof(JournalEntry.EntryNo),
            nameof(JournalEntry.EntryDate),
            nameof(JournalEntry.PeriodId),
            nameof(JournalEntry.SourceType),
            nameof(JournalEntry.SourceEvent),
            nameof(JournalEntry.SourceId),
            nameof(JournalEntry.Description),
            nameof(JournalEntry.Status),
            nameof(JournalEntry.PostedAtUtc),
            nameof(JournalEntry.PostedByUserId),
            nameof(JournalEntry.ReversedByEntryId),
            nameof(JournalEntry.TamperHash)
        ]),
        (typeof(SettlementMessage),
        [
            nameof(SettlementMessage.ChatId),
            nameof(SettlementMessage.MessageId),
            nameof(SettlementMessage.TelegramUserId),
            nameof(SettlementMessage.TelegramDisplayName),
            nameof(SettlementMessage.Text),
            nameof(SettlementMessage.SentAtUtc),
            nameof(SettlementMessage.Outcome),
            nameof(SettlementMessage.RejectionReason),
            nameof(SettlementMessage.AmountUah),
            nameof(SettlementMessage.Code),
            nameof(SettlementMessage.ResolvedUserId),
            nameof(SettlementMessage.PaymentId)
        ]),
        (typeof(VoucherTermPricing),
        [
            nameof(VoucherTermPricing.Scheme),
            nameof(VoucherTermPricing.TermCode),
            nameof(VoucherTermPricing.Enabled),
            nameof(VoucherTermPricing.RatePerLiter),
            nameof(VoucherTermPricing.ValidFrom),
            nameof(VoucherTermPricing.ValidTo),
            nameof(VoucherTermPricing.UpdatedByUserId),
            nameof(VoucherTermPricing.UpdatedByUserName)
        ])
    ];

    [Fact]
    public void The_Money_Tables_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheMoneyTables)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. A money column answers 'how "
                    + "much, in what, and whose' - and the column name alone does not say which "
                    + "of those it is, nor whether a NULL means 'not stated' or 'nothing'.");
            }
        }
    }

    /// <summary>
    /// The two units of a payment, and the third column that is not a second copy of either.
    /// </summary>
    [Fact]
    public void The_Payment_Amounts_Should_Say_Which_Is_Whose()
    {
        var context = BuildContext();
        var payment = context.Model.FindEntityType(typeof(Payment));
        Assert.NotNull(payment);

        var ours = CommentOf(payment.FindProperty(nameof(Payment.AmountUah))!);
        var wire = CommentOf(payment.FindProperty(nameof(Payment.AmountKopecks))!);
        var acquirer = CommentOf(payment.FindProperty(nameof(Payment.ProviderAmountKopecks))!);

        Assert.Contains("WE", ours, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wire", wire, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ACQUIRER", acquirer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NULL", acquirer, StringComparison.OrdinalIgnoreCase);
    }



    /// <summary>
    /// The ledger and the custody: the tables the accountant reads, and the one that answers
    /// "where is this paper".
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheLedgerTables =
    [
        (typeof(VoucherMovement),
        [
            nameof(VoucherMovement.BatchId),
            nameof(VoucherMovement.Kind),
            nameof(VoucherMovement.VoucherId),
            nameof(VoucherMovement.FromLocation),
            nameof(VoucherMovement.ToLocation),
            nameof(VoucherMovement.OrderId),
            nameof(VoucherMovement.ActorKind),
            nameof(VoucherMovement.ActorUserId),
            nameof(VoucherMovement.ActorPartyId),
            nameof(VoucherMovement.AmountUah),
            nameof(VoucherMovement.CostDeltaUah),
            nameof(VoucherMovement.OccurredAtUtc),
            nameof(VoucherMovement.Reason),
            nameof(VoucherMovement.LineItemId)
        ]),
        (typeof(ChartOfAccount),
        [
            nameof(ChartOfAccount.Code),
            nameof(ChartOfAccount.NameUkr),
            nameof(ChartOfAccount.AccountType),
            nameof(ChartOfAccount.NormalBalance),
            nameof(ChartOfAccount.ParentId),
            nameof(ChartOfAccount.IsPostable),
            nameof(ChartOfAccount.IsActive)
        ]),
        (typeof(AccountingPeriod),
        [
            nameof(AccountingPeriod.PeriodStart),
            nameof(AccountingPeriod.PeriodEnd),
            nameof(AccountingPeriod.Status),
            nameof(AccountingPeriod.ClosedAtUtc),
            nameof(AccountingPeriod.ClosedByUserId)
        ])
    ];

    [Fact]
    public void The_Ledger_And_The_Custody_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheLedgerTables)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. The ledger is what the "
                    + "accountant reads and the custody is what answers 'where is this paper' - "
                    + "and the column names alone do not say which is which.");
            }
        }
    }

    /// <summary>
    /// The cost delta is recorded at the time, and that is the whole point of it.
    /// </summary>
    [Fact]
    public void The_Cost_Delta_Should_Say_Why_It_Is_Recorded()
    {
        var context = BuildContext();
        var movement = context.Model.FindEntityType(typeof(VoucherMovement));
        Assert.NotNull(movement);

        var comment = CommentOf(movement.FindProperty(nameof(VoucherMovement.CostDeltaUah))!);

        Assert.Contains("NULL", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restamp", comment, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The tables where money meets the outside world: the acquirer's statement, the
    /// acquirer's webhooks, and the errors the system kept.
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheIntegrationTables =
    [
        (typeof(MonobankStatementEntry),
        [
            nameof(MonobankStatementEntry.Merchant),
            nameof(MonobankStatementEntry.StatementId),
            nameof(MonobankStatementEntry.InvoiceId),
            nameof(MonobankStatementEntry.Reference),
            nameof(MonobankStatementEntry.Destination),
            nameof(MonobankStatementEntry.AmountKopecks),
            nameof(MonobankStatementEntry.Ccy),
            nameof(MonobankStatementEntry.Status),
            nameof(MonobankStatementEntry.AcquirerCreatedAtUtc),
            nameof(MonobankStatementEntry.AcquirerModifiedAtUtc),
            nameof(MonobankStatementEntry.ExtRef),
            nameof(MonobankStatementEntry.MaskedCard),
            nameof(MonobankStatementEntry.Rrn),
            nameof(MonobankStatementEntry.ApprovalCode),
            nameof(MonobankStatementEntry.RawPayload),
            nameof(MonobankStatementEntry.FirstSeenAtUtc)
        ]),
        (typeof(WebhookEvent),
        [
            nameof(WebhookEvent.Provider),
            nameof(WebhookEvent.ProviderEventId),
            nameof(WebhookEvent.ProviderStatus),
            nameof(WebhookEvent.InvoiceId),
            nameof(WebhookEvent.AmountKopecks),
            nameof(WebhookEvent.ProviderCreatedAtUtc),
            nameof(WebhookEvent.ProviderModifiedAtUtc),
            nameof(WebhookEvent.Payload),
            nameof(WebhookEvent.Signature),
            nameof(WebhookEvent.KeyId),
            nameof(WebhookEvent.Status),
            nameof(WebhookEvent.ReceivedAtUtc),
            nameof(WebhookEvent.ProcessedAtUtc),
            nameof(WebhookEvent.Attempts),
            nameof(WebhookEvent.Error)
        ]),
        (typeof(ErrorLog),
        [
            nameof(ErrorLog.LoggedAtUtc),
            nameof(ErrorLog.Level),
            nameof(ErrorLog.Message),
            nameof(ErrorLog.ExceptionType),
            nameof(ErrorLog.ExceptionMessage),
            nameof(ErrorLog.StackTrace),
            nameof(ErrorLog.Source),
            nameof(ErrorLog.RequestPath),
            nameof(ErrorLog.RequestMethod),
            nameof(ErrorLog.UserName),
            nameof(ErrorLog.TraceId),
            nameof(ErrorLog.ResolvedAtUtc),
            nameof(ErrorLog.ResolvedByUserId),
            nameof(ErrorLog.ResolvedByUserName)
        ])
    ];

    [Fact]
    public void The_Integration_Tables_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheIntegrationTables)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. These are the tables where "
                    + "money meets the outside world - and a column that is EVIDENCE (what the "
                    + "acquirer said, what the customer typed) must not be mistaken for a key.");
            }
        }
    }

    /// <summary>
    /// The tables of a customer's life: their notifications, their feedback, the support
    /// mailbox, and the company they work for.
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheLifecycleTables =
    [
        (typeof(Notification),
        [
            nameof(Notification.UserId),
            nameof(Notification.Title),
            nameof(Notification.Message),
            nameof(Notification.IsRead),
            nameof(Notification.CreatedAtUtc),
            nameof(Notification.UpdatedAtUtc)
        ]),
        (typeof(FeedbackMessage),
        [
            nameof(FeedbackMessage.UserId),
            nameof(FeedbackMessage.TelegramChatId),
            nameof(FeedbackMessage.TelegramMessageId),
            nameof(FeedbackMessage.Text),
            nameof(FeedbackMessage.Verdict),
            nameof(FeedbackMessage.Category),
            nameof(FeedbackMessage.Severity),
            nameof(FeedbackMessage.Summary),
            nameof(FeedbackMessage.Language),
            nameof(FeedbackMessage.Confidence),
            nameof(FeedbackMessage.ClassifierError),
            nameof(FeedbackMessage.ClassifiedAtUtc),
            nameof(FeedbackMessage.StaffChatId),
            nameof(FeedbackMessage.StaffMessageId),
            nameof(FeedbackMessage.StaffRepliedAtUtc),
            nameof(FeedbackMessage.Status),
            nameof(FeedbackMessage.AppVersion),
            nameof(FeedbackMessage.Platform),
            nameof(FeedbackMessage.CreatedAtUtc)
        ]),
        (typeof(SupportMessage),
        [
            nameof(SupportMessage.Email),
            nameof(SupportMessage.Message),
            nameof(SupportMessage.UserAgent),
            nameof(SupportMessage.IpAddress),
            nameof(SupportMessage.EmailSentAtUtc),
            nameof(SupportMessage.SendError),
            nameof(SupportMessage.CreatedAtUtc)
        ]),
        (typeof(CompanyMember),
        [
            nameof(CompanyMember.LegalEntityId),
            nameof(CompanyMember.WorkerUserId),
            nameof(CompanyMember.JoinedAtUtc),
            nameof(CompanyMember.IsDeleted),
            nameof(CompanyMember.DeletedAtUtc)
        ]),
        (typeof(CompanyInvitation),
        [
            nameof(CompanyInvitation.LegalEntityId),
            nameof(CompanyInvitation.OwnerUserId),
            nameof(CompanyInvitation.WorkerPhoneNumber),
            nameof(CompanyInvitation.WorkerUserId),
            nameof(CompanyInvitation.Status),
            nameof(CompanyInvitation.CreatedAtUtc),
            nameof(CompanyInvitation.UpdatedAtUtc)
        ])
    ];

    [Fact]
    public void The_Lifecycle_Tables_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheLifecycleTables)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. These are the tables of a "
                    + "customer's life, and the ones with a NULL that means something - not "
                    + "classified, not read, not sent - are the ones a reader cannot tell "
                    + "apart from the names.");
            }
        }
    }

    /// <summary>
    /// A NULL that means "not yet" and a NULL that means "never" are different facts, and
    /// the comment is where the difference is written down.
    /// </summary>
    [Fact]
    public void The_Verdict_Should_Say_That_Null_Is_Not_Junk()
    {
        var context = BuildContext();
        var feedback = context.Model.FindEntityType(typeof(FeedbackMessage));
        Assert.NotNull(feedback);

        var comment = CommentOf(feedback.FindProperty(nameof(FeedbackMessage.Verdict))!);

        Assert.Contains("not classified", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("junk", comment, StringComparison.OrdinalIgnoreCase);
    }



    /// <summary>
    /// The account tables: who the customer is, what they may do, and the tokens that stand
    /// in for them.
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheAccountTables =
    [
        (typeof(User),
        [
            nameof(User.PhoneNumber),
            nameof(User.RoleId),
            nameof(User.CreatedAtUtc),
            nameof(User.LastLoginAtUtc),
            nameof(User.UpdatedAtUtc),
            nameof(User.Email),
            nameof(User.FirstName),
            nameof(User.LastName),
            nameof(User.Birthdate),
            nameof(User.ProfileImageUrl),
            nameof(User.PartyId),
            nameof(User.ReferralCode),
            nameof(User.ReferredBy),
            nameof(User.SettlementCode)
        ]),
        (typeof(Role),
        [
            nameof(Role.Name),
            nameof(Role.Level),
            nameof(Role.CreatedAtUtc)
        ]),
        (typeof(Device),
        [
            nameof(Device.UserId),
            nameof(Device.DeviceId),
            nameof(Device.PublicKey),
            nameof(Device.DeviceModel),
            nameof(Device.OsVersion),
            nameof(Device.AppVersion),
            nameof(Device.Status),
            nameof(Device.CreatedAt),
            nameof(Device.LastSeenAt)
        ]),
        (typeof(RefreshToken),
        [
            nameof(RefreshToken.UserId),
            nameof(RefreshToken.FamilyId),
            nameof(RefreshToken.DeviceId),
            nameof(RefreshToken.RoleNameAtIssue),
            nameof(RefreshToken.Token),
            nameof(RefreshToken.ExpiresAtUtc),
            nameof(RefreshToken.CreatedAtUtc),
            nameof(RefreshToken.IsRevoked),
            nameof(RefreshToken.RevokedAtUtc)
        ]),
        (typeof(VerificationCode),
        [
            nameof(VerificationCode.PhoneNumber),
            nameof(VerificationCode.Code),
            nameof(VerificationCode.ExpiresAtUtc),
            nameof(VerificationCode.CreatedAtUtc),
            nameof(VerificationCode.IsUsed),
            nameof(VerificationCode.UsedAtUtc),
            nameof(VerificationCode.FailedAttempts)
        ]),
        (typeof(Party),
        [
            nameof(Party.PartyType),
            nameof(Party.DisplayName),
            nameof(Party.CreatedAtUtc)
        ])
    ];


    [Fact]
    public void The_Account_Tables_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheAccountTables)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. These are the tables that "
                    + "say who the customer is and what they may do - and a role level, a token "
                    + "family and a settlement code all mean something that the name alone "
                    + "does not say.");
            }
        }
    }

    /// <summary>
    /// The settlement code is transcribed by hand, so the alphabet that avoids the pairs
    /// people misread is part of what the column is.
    /// </summary>
    [Fact]
    public void The_Settlement_Code_Should_Say_Why_Its_Alphabet_Is_What_It_Is()
    {
        var context = BuildContext();
        var user = context.Model.FindEntityType(typeof(User));
        Assert.NotNull(user);

        var comment = CommentOf(user.FindProperty(nameof(User.SettlementCode))!);

        Assert.Contains("transcribed", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("immutable", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mistyped", comment, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The order line and the voucher - the two rows the business is actually made of.
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheOrderTables =
    [
        (typeof(OrderLineItem),
        [
            nameof(OrderLineItem.Brand),
            nameof(OrderLineItem.BrandId),
            nameof(OrderLineItem.Liters),
            nameof(OrderLineItem.Quantity),
            nameof(OrderLineItem.UnitPrice),
            nameof(OrderLineItem.LineTotal),
            nameof(OrderLineItem.OriginalLineTotal),
            nameof(OrderLineItem.TermCode),
            nameof(OrderLineItem.SourceVoucherId),
            nameof(OrderLineItem.RenewalBranch),
            nameof(OrderLineItem.PreviousCustomerExpiration),
            nameof(OrderLineItem.NewCustomerExpiration),
            nameof(OrderLineItem.PricingBasis)
        ]),
        (typeof(FuelVoucher),
        [
            nameof(FuelVoucher.Brand),
            nameof(FuelVoucher.BrandId),
            nameof(FuelVoucher.Liters),
            nameof(FuelVoucher.SupplierExpirationDate),
            nameof(FuelVoucher.CustomerExpirationDate),
            nameof(FuelVoucher.VoucherNumber),
            nameof(FuelVoucher.QrPayload),
            nameof(FuelVoucher.QrPayloadSha256),
            nameof(FuelVoucher.IssuedAtUtc),
            nameof(FuelVoucher.UsedAtUtc),
            nameof(FuelVoucher.Status),
            nameof(FuelVoucher.AssignedToUserId),
            nameof(FuelVoucher.WorkerUserId),
            nameof(FuelVoucher.ImportJobId),
            nameof(FuelVoucher.OrderId),
            nameof(FuelVoucher.SupplierId),
            nameof(FuelVoucher.CostPerLiter),
            nameof(FuelVoucher.DataKind),
            nameof(FuelVoucher.HolderKind),
            nameof(FuelVoucher.IsDeleted),
            nameof(FuelVoucher.VerificationMismatchPercent),
            nameof(FuelVoucher.VerificationMismatchedModules),
            nameof(FuelVoucher.VerificationTotalModules)
        ])
    ];


    [Fact]
    public void The_Order_Line_And_The_Voucher_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheOrderTables)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. These are the two rows the "
                    + "business is made of, and on them two similar numbers mean different "
                    + "things: the list price and the price paid, the supplier's term and the "
                    + "customer's, the cost and the selling price.");
            }
        }
    }

    /// <summary>
    /// The pricing basis is WHY the line cost what it cost, and it is frozen - which is the
    /// whole point of it.
    /// </summary>
    [Fact]
    public void The_Pricing_Basis_Should_Say_That_It_Is_Frozen()
    {
        var context = BuildContext();
        var line = context.Model.FindEntityType(typeof(OrderLineItem));
        Assert.NotNull(line);

        var comment = CommentOf(line.FindProperty(nameof(OrderLineItem.PricingBasis))!);

        Assert.Contains("frozen at checkout", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not backfilled", comment, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The last tables: what a fact becomes in the books, what we are known by, what arrived from
    /// the supplier's file, and the company that signs.
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheLastTables =
    [
        (typeof(LedgerMappingRule),
        [
            nameof(LedgerMappingRule.SourceType),
            nameof(LedgerMappingRule.SourceEvent),
            nameof(LedgerMappingRule.AccountId),
            nameof(LedgerMappingRule.Side)
        ]),
        (typeof(BankAccount),
        [
            nameof(BankAccount.OperatorLegalEntityId),
            nameof(BankAccount.BankCode),
            nameof(BankAccount.AccountRef),
            nameof(BankAccount.Currency),
            nameof(BankAccount.IsDefault),
            nameof(BankAccount.IsActive)
        ]),
        (typeof(OperatorLegalEntity),
        [
            nameof(OperatorLegalEntity.Edrpou),
            nameof(OperatorLegalEntity.Name),
            nameof(OperatorLegalEntity.LegalForm),
            nameof(OperatorLegalEntity.VatNumber),
            nameof(OperatorLegalEntity.Rnkrr),
            nameof(OperatorLegalEntity.TaxRegime),
            nameof(OperatorLegalEntity.RegisteredAddress),
            nameof(OperatorLegalEntity.IsActive),
            nameof(OperatorLegalEntity.CreatedAtUtc),
            nameof(OperatorLegalEntity.UpdatedAtUtc)
        ]),
        (typeof(DocumentSequence),
        [
            nameof(DocumentSequence.Name),
            nameof(DocumentSequence.LastValue)
        ]),
        (typeof(NaturalPerson),
        [
            nameof(NaturalPerson.PartyId),
            nameof(NaturalPerson.Birthdate),
            nameof(NaturalPerson.TaxId)
        ]),
        (typeof(VoucherImport),
        [
            nameof(VoucherImport.FileName),
            nameof(VoucherImport.PageCount),
            nameof(VoucherImport.StartedAtUtc),
            nameof(VoucherImport.CompletedAtUtc),
            nameof(VoucherImport.Status),
            nameof(VoucherImport.ImportedCount),
            nameof(VoucherImport.DuplicateCount),
            nameof(VoucherImport.FailedCount),
            nameof(VoucherImport.VerificationFailedCount),
            nameof(VoucherImport.VerifiedWithWarningsCount)
        ]),
        (typeof(VoucherExchange),
        [
            nameof(VoucherExchange.ExchangeBatchId),
            nameof(VoucherExchange.OldVoucherId),
            nameof(VoucherExchange.NewVoucherId),
            nameof(VoucherExchange.FuelTypeId),
            nameof(VoucherExchange.Brand),
            nameof(VoucherExchange.BrandId),
            nameof(VoucherExchange.SurchargeUah),
            nameof(VoucherExchange.CostPerLiterApplied),
            nameof(VoucherExchange.SupplierId),
            nameof(VoucherExchange.ActingUserId),
            nameof(VoucherExchange.ActingUserName),
            nameof(VoucherExchange.CreatedAtUtc)
        ]),
        (typeof(LegalEntity),
        [
            nameof(LegalEntity.UserId),
            nameof(LegalEntity.Name),
            nameof(LegalEntity.Edrpou),
            nameof(LegalEntity.VatNumber),
            nameof(LegalEntity.Address),
            nameof(LegalEntity.DirectorName),
            nameof(LegalEntity.Phone),
            nameof(LegalEntity.Email),
            nameof(LegalEntity.CreatedAtUtc),
            nameof(LegalEntity.UpdatedAtUtc)
        ])
    ];



    [Fact]
    public void The_Last_Tables_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheLastTables)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. A rule that maps a fact to an "
                    + "account, an account that money arrives into, a counter that hands out entry "
                    + "numbers, a file that arrived, a company that signs - these are the columns "
                    + "someone reads with the code closed.");
            }
        }
    }

    /// <summary>
    /// The surcharge on an exchange row is this row's OWN share, not the batch's. Reading one row
    /// as the batch charge shipped once already and cost real money, so the comment has to say so
    /// where the number is read.
    /// </summary>
    [Fact]
    public void The_Exchange_Surcharge_Should_Say_That_It_Is_One_Row_Share()
    {
        var context = BuildContext();
        var exchange = context.Model.FindEntityType(typeof(VoucherExchange));
        Assert.NotNull(exchange);

        var comment = CommentOf(exchange.FindProperty(nameof(VoucherExchange.SurchargeUah))!);

        Assert.Contains("NOT the batch total", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExchangeBatchId", comment, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The supplier side and the contracts: who we buy from, what came in, and what was signed.
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheSupplierAndTheContract =
    [
        (typeof(Supplier),
        [
            nameof(Supplier.Name),
            nameof(Supplier.LegalForm),
            nameof(Supplier.Phone),
            nameof(Supplier.Email),
            nameof(Supplier.EdrIpn),
            nameof(Supplier.Rnkrr),
            nameof(Supplier.Address),
            nameof(Supplier.Notes),
            nameof(Supplier.IsActive),
            nameof(Supplier.CreatedAtUtc),
            nameof(Supplier.UpdatedAtUtc),
            nameof(Supplier.AcquisitionSource)
        ]),
        (typeof(PurchaseBatch),
        [
            nameof(PurchaseBatch.ImportJobId),
            nameof(PurchaseBatch.FuelTypeId),
            nameof(PurchaseBatch.Brand),
            nameof(PurchaseBatch.BrandId),
            nameof(PurchaseBatch.SupplierId),
            nameof(PurchaseBatch.EnteredByUserId),
            nameof(PurchaseBatch.CreatedAtUtc),
            nameof(PurchaseBatch.UpdatedAtUtc)
        ]),
        (typeof(QrParameters),
        [
            nameof(QrParameters.Version),
            nameof(QrParameters.EccLevel),
            nameof(QrParameters.MaskPattern),
            nameof(QrParameters.EncodingMode),
            nameof(QrParameters.CreatedAtUtc)
        ]),
        (typeof(VoucherImportError),
        [
            nameof(VoucherImportError.ImportId),
            nameof(VoucherImportError.PageNumber),
            nameof(VoucherImportError.VoucherNumber),
            nameof(VoucherImportError.ErrorMessage),
            nameof(VoucherImportError.RawText),
            nameof(VoucherImportError.CreatedAtUtc)
        ]),
        (typeof(Contract),
        [
            nameof(Contract.UserId),
            nameof(Contract.LegalEntityId),
            nameof(Contract.BrandId),
            nameof(Contract.CreatedAtUtc),
            nameof(Contract.ExpiresAtUtc)
        ]),
        (typeof(UserContract),
        [
            nameof(UserContract.UserId),
            nameof(UserContract.ContractId),
            nameof(UserContract.SignatureData),
            nameof(UserContract.SignedAtUtc)
        ])
    ];



    [Fact]
    public void The_Supplier_And_The_Contract_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheSupplierAndTheContract)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. A supplier, a batch of stock, a "
                    + "file that did not read, a contract and a signature - these are the columns a "
                    + "person opens the database to find, with the code closed.");
            }
        }
    }

    /// <summary>
    /// A contract without a term is not a contract. The comment has to say that, because the
    /// column is what stops a signature.
    /// </summary>
    [Fact]
    public void The_Contract_Should_Say_Why_Its_Term_Is_Mandatory()
    {
        var context = BuildContext();
        var contract = context.Model.FindEntityType(typeof(Contract));
        Assert.NotNull(contract);

        var comment = CommentOf(contract.FindProperty(nameof(Contract.ExpiresAtUtc))!);

        Assert.Contains("stops a signature", comment, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The order and the rest: the order, what it delivered, what it published, the settlement
    /// and the renewal, the push token, the settings, and the two tables of the feedback chat.
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheOrderAndTheRest =
    [
        (typeof(Order),
        [
            nameof(Order.UserId),
            nameof(Order.LegalEntityId),
            nameof(Order.Price),
            nameof(Order.Status),
            nameof(Order.Kind),
            nameof(Order.SourceOrderId),
            nameof(Order.MonobankInvoiceId),
            nameof(Order.MonobankPaymentUrl),
            nameof(Order.MonobankStatus),
            nameof(Order.MonobankMerchant),
            nameof(Order.IdempotencyKey),
            nameof(Order.CreatedAtUtc),
            nameof(Order.UpdatedAtUtc),
            nameof(Order.FulfilledAtUtc),
            nameof(Order.PartiallyFulfilledSinceUtc),
            nameof(Order.LastWebhookProcessedAtUtc),
            nameof(Order.LastWebhookModifiedDateUtc),
            nameof(Order.IsDeleted)
        ]),
        (typeof(Fulfillment),
        [
            nameof(Fulfillment.OrderId),
            nameof(Fulfillment.VoucherId),
            nameof(Fulfillment.FulfilledAtUtc),
            nameof(Fulfillment.LineItemId),
            nameof(Fulfillment.ReleasedAtUtc),
            nameof(Fulfillment.ReleaseReason)
        ]),
        (typeof(OutboxEvent),
        [
            nameof(OutboxEvent.EventType),
            nameof(OutboxEvent.Payload),
            nameof(OutboxEvent.Processed),
            nameof(OutboxEvent.ProcessedAtUtc),
            nameof(OutboxEvent.CreatedAtUtc)
        ]),
        (typeof(MovementBatch),
        [
            nameof(MovementBatch.Kind),
            nameof(MovementBatch.SupplierId),
            nameof(MovementBatch.Status),
            nameof(MovementBatch.SurchargeUah),
            nameof(MovementBatch.OpenedByUserId),
            nameof(MovementBatch.OpenedByUserName),
            nameof(MovementBatch.OpenedAtUtc),
            nameof(MovementBatch.ConfirmedAtUtc),
            nameof(MovementBatch.RejectionReason),
            nameof(MovementBatch.CreatedAtUtc)
        ]),
        (typeof(OperatorVoucherRenewal),
        [
            nameof(OperatorVoucherRenewal.VoucherId),
            nameof(OperatorVoucherRenewal.ReplacementVoucherId),
            nameof(OperatorVoucherRenewal.CustomerUserId),
            nameof(OperatorVoucherRenewal.Branch),
            nameof(OperatorVoucherRenewal.TermCode),
            nameof(OperatorVoucherRenewal.OldExpiration),
            nameof(OperatorVoucherRenewal.NewExpiration),
            nameof(OperatorVoucherRenewal.SurchargeUah),
            nameof(OperatorVoucherRenewal.InvoiceNumber),
            nameof(OperatorVoucherRenewal.InvoiceDate),
            nameof(OperatorVoucherRenewal.ActingUserId),
            nameof(OperatorVoucherRenewal.ActingUserName),
            nameof(OperatorVoucherRenewal.CreatedAtUtc)
        ]),
        (typeof(UserPushToken),
        [
            nameof(UserPushToken.UserId),
            nameof(UserPushToken.Token),
            nameof(UserPushToken.Platform),
            nameof(UserPushToken.DeviceId),
            nameof(UserPushToken.IsActive),
            nameof(UserPushToken.CreatedAtUtc),
            nameof(UserPushToken.UpdatedAtUtc),
            nameof(UserPushToken.LastSeenAtUtc)
        ]),
        (typeof(AppSetting),
        [
            nameof(AppSetting.Key),
            nameof(AppSetting.Value),
            nameof(AppSetting.UpdatedAtUtc),
            nameof(AppSetting.UpdatedByUserId),
            nameof(AppSetting.UpdatedByUserName)
        ]),
        (typeof(FeedbackMessageReply),
        [
            nameof(FeedbackMessageReply.FeedbackMessageId),
            nameof(FeedbackMessageReply.StaffUserId),
            nameof(FeedbackMessageReply.StaffUserName),
            nameof(FeedbackMessageReply.Text),
            nameof(FeedbackMessageReply.DeliveredMessageId),
            nameof(FeedbackMessageReply.DeliveryError),
            nameof(FeedbackMessageReply.SentAtUtc)
        ]),
        (typeof(FeedbackChatBinding),
        [
            nameof(FeedbackChatBinding.TelegramChatId),
            nameof(FeedbackChatBinding.UserId),
            nameof(FeedbackChatBinding.BoundAtUtc)
        ])
    ];



    [Fact]
    public void The_Order_And_The_Rest_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheOrderAndTheRest)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. The order, the delivery, the"
                    + " settlement, the renewal, the push token, the settings - the tables a person"
                    + " opens the database to find, with the code closed.");
            }
        }
    }

    /// <summary>
    /// The settlement's surcharge is the amount the brand charged - the authoritative number -
    /// and not the sum of the rows' shares, which over-count when a retired voucher was not
    /// replaced. That distinction has already cost real money once.
    /// </summary>
    [Fact]
    public void The_Settlement_Should_Say_That_Its_Surcharge_Is_The_Authoritative_Number()
    {
        var context = BuildContext();
        var batch = context.Model.FindEntityType(typeof(MovementBatch));
        Assert.NotNull(batch);

        var comment = CommentOf(batch.FindProperty(nameof(MovementBatch.SurchargeUah))!);

        Assert.Contains("AS A WHOLE", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("over-count", comment, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The last columns of the tables that are already documented - the ones a reader meets
    /// last, and the ones that are easiest to get wrong.
    /// </summary>
    private static readonly (Type Entity, string[] Columns)[] TheLastColumns =
    [
        (typeof(User),
        [
            nameof(User.BonusBalance),
            nameof(User.IsActive),
            nameof(User.TokenVersion),
            nameof(User.IsDeleted),
            nameof(User.IsBanned),
            nameof(User.IsQaAccount),
            nameof(User.PendingEmail),
            nameof(User.PendingEmailExpiresAtUtc),
            nameof(User.PendingEmailTokenHash)
        ]),
        (typeof(FuelVoucher),
        [
            nameof(FuelVoucher.FuelTypeId),
            nameof(FuelVoucher.QrParametersId),
            nameof(FuelVoucher.LegalEntityId),
            nameof(FuelVoucher.AcquisitionSource),
            nameof(FuelVoucher.CreatedAtUtc),
            nameof(FuelVoucher.UpdatedAtUtc)
        ]),
        (typeof(Payment),
        [
            nameof(Payment.Merchant),
            nameof(Payment.CreatedAtUtc),
            nameof(Payment.UpdatedAtUtc)
        ]),
        (typeof(JournalEntry),
        [
            nameof(JournalEntry.CreatedAtUtc),
            nameof(JournalEntry.CreatedByUserId)
        ]),
        (typeof(OrderLineItem),
        [
            nameof(OrderLineItem.OrderId),
            nameof(OrderLineItem.FuelTypeId)
        ]),
        (typeof(VoucherTermPricing),
        [
            nameof(VoucherTermPricing.CreatedAtUtc),
            nameof(VoucherTermPricing.UpdatedAtUtc)
        ]),
        (typeof(MonobankStatementEntry),
        [
            nameof(MonobankStatementEntry.LastSeenAtUtc)
        ]),
        (typeof(Role),
        [
            nameof(Role.IsDefault)
        ]),
        (typeof(SettlementMessage),
        [
            nameof(SettlementMessage.CreatedAtUtc)
        ])
    ];



    [Fact]
    public void The_Last_Columns_Should_Say_What_They_Are()
    {
        var context = BuildContext();

        foreach (var (entityType, columns) in TheLastColumns)
        {
            var entity = context.Model.FindEntityType(entityType);
            Assert.NotNull(entity);

            foreach (var column in columns)
            {
                var property = entity.FindProperty(column);
                Assert.NotNull(property);

                var comment = CommentOf(property);
                Assert.False(string.IsNullOrWhiteSpace(comment),
                    $"{entityType.Name}.{column} carries no comment. These are the columns a reader "
                    + "meets last, and the ones that are easiest to get wrong: a balance that is a "
                    + "balance and not a price, a token hash that is a hash and not a token, a "
                    + "banned account that is banned and not deleted.");
            }
        }
    }

    /// <summary>
    /// The confirmation token is stored as a hash. A database that is read by somebody must not be
    /// able to confirm an address, and a comment that does not say so is a comment that a future
    /// author reads as "the token is here".
    /// </summary>
    [Fact]
    public void The_Pending_Email_Token_Should_Say_That_It_Is_A_Hash()
    {
        var context = BuildContext();
        var user = context.Model.FindEntityType(typeof(User));
        Assert.NotNull(user);

        var comment = CommentOf(user.FindProperty(nameof(User.PendingEmailTokenHash))!);

        Assert.Contains("never the token itself", comment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The comments of a property, read as the annotation they are stored as.
    /// </summary>
    private static string? CommentOf(IReadOnlyProperty property)
        => property.FindAnnotation(RelationalAnnotationNames.Comment)?.Value as string;

    private static ApplicationDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options, TestQrCipher.Instance);
    }
}
