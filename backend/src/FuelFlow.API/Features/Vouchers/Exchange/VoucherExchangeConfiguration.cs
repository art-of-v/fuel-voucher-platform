using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Exchange;

internal sealed class VoucherExchangeConfiguration : IEntityTypeConfiguration<VoucherExchange>
{
    public void Configure(EntityTypeBuilder<VoucherExchange> builder)
    {
        builder.ToTable("voucher_exchanges");

        builder.HasKey(e => e.Id);

        // #246 detector: xmin token. No DDL - xmin is a system column. Raw SQL must never write
        // this table, or the token starts failing saves that did not actually race.
        builder.UseXminAsConcurrencyToken();

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ExchangeBatchId)
            .HasColumnName("exchange_batch_id")
            .IsRequired();

        // #319 / D-13. This was an indexed uuid pointing at nothing: the batch's lifecycle had nowhere
        // to live, so every reader reconstructed the header by folding over the member rows. Now that
        // the parent exists, the reference is real.
        //
        // RESTRICT, matching both voucher sides below and for the same reason: a batch is the record
        // of what was agreed with a supplier, and cascading would let deleting the last exchange row
        // delete that record with it.
        builder.HasOne(e => e.MovementBatch)
            .WithMany()
            .HasForeignKey(e => e.ExchangeBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.OldVoucherId)
            .HasColumnName("old_voucher_id")
            .IsRequired();

        // Nullable audit link to the replacement voucher - null when the old voucher was not paired.
        // Constrained below, like the retired side (#217).
        builder.Property(e => e.NewVoucherId)
            .HasColumnName("new_voucher_id");

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.SurchargeUah)
            .HasColumnName("surcharge_uah")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        /// <summary>
        /// The per-liter cost stamped on THIS pair's new voucher (old voucher cost + its share of the
        /// surcharge). It is per row, not a copy of the batch total, because the old vouchers of one exchange
        /// routinely cost different amounts per liter and each new voucher has to carry its own.
        /// </summary>
        builder.Property(e => e.CostPerLiterApplied)
            .HasColumnName("cost_per_liter_applied")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.ActingUserId)
            .HasColumnName("acting_user_id")
            .IsRequired();

        builder.Property(e => e.ActingUserName)
            .HasColumnName("acting_user_name")
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(e => e.OldVoucher)
            .WithMany()
            .HasForeignKey(e => e.OldVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // The replacement side carries the same RESTRICT as the retired side (#217). It used to be a
        // bare nullable marker with no constraint, which meant deleting a replacement voucher
        // silently orphaned the audit row - exactly the quiet history loss the policy forbids. Both
        // sides are constrained together so the pair can never be half-protected.
        builder.HasOne(e => e.NewVoucher)
            .WithMany()
            .HasForeignKey(e => e.NewVoucherId)
            .OnDelete(DeleteBehavior.Restrict);
        // acting_user_id stays a bare uuid - an exchange is a settlement record and must always be
        // writable; it cannot be refused because an actor row is missing.
        builder.HasIndex(e => e.ExchangeBatchId);
        builder.HasIndex(e => e.NewVoucherId);
        // Supplier reconciliation reads one supplier's exchanged vouchers.
        builder.HasIndex(e => e.SupplierId);
        // ActingUserId is indexed by the FK convention above.

        // A voucher retired into an exchange can never be retired again, so old_voucher_id is
        // unique - this is the database half of the idempotency the handler already checks, and
        // it replaces the plain index that stood here for it.
        builder.HasIndex(e => e.OldVoucherId)
            .IsUnique();

        builder.ToTable(t =>
        {
            // 0 legitimately means "free compensation", so >= 0 and not > 0.
            t.HasCheckConstraint("ck_voucher_exchange_surcharge_non_negative", "surcharge_uah >= 0");
            t.HasCheckConstraint("ck_voucher_exchange_cost_non_negative",
                "cost_per_liter_applied IS NULL OR cost_per_liter_applied >= 0");
            t.HasCheckConstraint("ck_voucher_exchange_not_self",
                "old_voucher_id IS DISTINCT FROM new_voucher_id");
            // fuel_type_id is a catalog natural key, bounded by CHECK - see BrandConfiguration.
            t.HasCheckConstraint("ck_voucher_exchanges_fuel_type_id_length", "length(fuel_type_id) <= 100");
        });
    }
}
