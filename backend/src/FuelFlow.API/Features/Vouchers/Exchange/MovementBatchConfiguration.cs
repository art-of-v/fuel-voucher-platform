using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// D-13 / #319. The parent <c>voucher_exchanges.exchange_batch_id</c> never had.
/// </summary>
/// <remarks>
/// <para>
/// Two decisions here are load-bearing and were not the obvious choices.
/// </para>
/// <para>
/// <b>The foreign key is RESTRICT, not CASCADE.</b> A batch is the record of what was agreed with a
/// supplier. Cascading would let deleting the last exchange row delete the settlement it belonged to,
/// and with it the only statement that anything was agreed — the same reasoning that makes
/// <c>voucher_exchanges</c> restrict on both voucher sides and that <c>refunds.order_id</c> restricts
/// on orders.
/// </para>
/// <para>
/// <b><c>surcharge_uah</c> is <c>numeric(14,2)</c> here and <c>numeric(10,2)</c> on the exchange rows.</b>
/// Not an oversight: the row share and the batch total are different quantities and D-13's stated
/// precision contract is <c>numeric(14,2)</c> for domain money. <c>numeric(10,2)</c> caps a single
/// share at 99,999,999.99 UAH, which is ample for one voucher's share of one settlement and is the
/// column that has existed longest. Widening it is a separate, deliberate change rather than something
/// to smuggle in here.
/// </para>
/// </remarks>
internal sealed class MovementBatchConfiguration : IEntityTypeConfiguration<MovementBatch>
{
    public void Configure(EntityTypeBuilder<MovementBatch> builder)
    {
        builder.ToTable("movement_batches");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Kind)
            .HasColumnName("kind")
            .HasMaxLength(20)
            .IsRequired().HasComment("What kind of settlement this is - an exchange, a return, a write-off.");

        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id").HasComment("The counterparty. NULL for a batch that is not a supplier settlement.");

        // Null for a batch that is not a supplier settlement. Restrict rather than SetNull: the
        // counterparty of a settlement must stay nameable, and a supplier that is deactivated is not a
        // supplier that has been erased - same rule as voucher_exchanges.supplier_id.
        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(12)
            .IsRequired().HasComment("The lifecycle state. A batch is created already confirmed, because that is the only way" +
                    "the application makes one: the operator confirms the exchange and the batch records it in" +
                    "the same transaction. The rest of the set is the forward shape - a draft that is opened and" +
                    "later settled is the obvious next workflow - and the CHECK is what stops an unrecognised" +
                    "word arriving as a status later.");

        builder.Property(e => e.SurchargeUah)
            .HasColumnName("surcharge_uah")
            .HasColumnType("numeric(14,2)")
            .IsRequired().HasComment("What the brand charged for the settlement AS A WHOLE. This is the amount on the command," +
                    "not the sum of the rows' shares: each exchange row carries only its own share, and those" +
                    "shares sum back to the total only when every retired voucher was paired with a" +
                    "replacement. An exchange that returns vouchers without reissuing them contributes retired" +
                    "litres to the numerators and nothing to the denominator, so the shares over-count. The" +
                    "number the brand charged is the authoritative one, so it is this.");

        // Bare uuid, no FK. A settlement record has to be writable whatever happened to the actor row,
        // which is why voucher_exchanges.acting_user_id and operator_voucher_renewals.acting_user_id
        // are bare too. §G of TARGET_SCHEMA says the same in general terms.
        builder.Property(e => e.OpenedByUserId)
            .HasColumnName("opened_by_user_id")
            .IsRequired().HasComment("The operator who opened the settlement.");

        builder.Property(e => e.OpenedByUserName)
            .HasColumnName("opened_by_user_name")
            .HasMaxLength(200).HasComment("That operator's name as it read at the time. A copy, so the record stays readable after a" +
                    "rename - never a join target.");

        builder.Property(e => e.OpenedAtUtc)
            .HasColumnName("opened_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired().HasComment("When the settlement was opened.");

        builder.Property(e => e.ConfirmedAtUtc)
            .HasColumnName("confirmed_at_utc")
            .HasColumnType("timestamp with time zone").HasComment("When the settlement was confirmed. Non-null exactly when the status is confirmed, which" +
                    "is a database CHECK rather than a convention: a batch that says confirmed and carries no" +
                    "timestamp cannot answer 'when'.");

        builder.Property(e => e.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(500).HasComment("Why a batch was rejected, when it was.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired().HasComment("When the row was written.");

        // The batch list is ordered newest-first and filtered by supplier; neither should scan.
        builder.HasIndex(e => e.OpenedAtUtc)
            .HasDatabaseName("ix_movement_batches_opened_at_utc");

        builder.HasIndex(e => e.SupplierId)
            .HasDatabaseName("ix_movement_batches_supplier_id");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_movement_batches_kind",
                "kind IN ('voucher_exchange','bulk_issuance')");

            t.HasCheckConstraint(
                "ck_movement_batches_status",
                "status IN ('draft','confirmed','rejected','cancelled')");

            // 0 legitimately means "free compensation" - the same reason voucher_exchanges uses >= 0
            // and not > 0.
            t.HasCheckConstraint("ck_movement_batches_surcharge_non_negative", "surcharge_uah >= 0");

            // "confirmed but never confirmed" is the state that makes a settlement record lie, and it
            // is exactly what a half-finished write produces. The pairing is a CHECK because the two
            // columns are only ever written together and nothing else enforces that.
            t.HasCheckConstraint("ck_movement_batches_confirmed_has_timestamp",
                "(status = 'confirmed') = (confirmed_at_utc IS NOT NULL)");

            // A rejection with nothing to say why is not auditable, and a rejection with a reason but
            // no rejected status is a contradiction.
            t.HasCheckConstraint("ck_movement_batches_rejection_reason",
                "(status = 'rejected') = (rejection_reason IS NOT NULL)");

            // A supplier settlement names its supplier; a batch of another kind does not need to.
            t.HasCheckConstraint("ck_movement_batches_supplier_matches_kind",
                "kind <> 'voucher_exchange' OR supplier_id IS NOT NULL");
        });
    }
}