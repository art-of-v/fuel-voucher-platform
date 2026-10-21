using FuelFlow.Features.Orders.SharedModels;
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
    /// The comments of a property, read as the annotation they are stored as. The relational
    /// accessor for it lives on a read-only interface, and the design-time model hands out
    /// the mutable one - so the annotation is what both agree on.
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
