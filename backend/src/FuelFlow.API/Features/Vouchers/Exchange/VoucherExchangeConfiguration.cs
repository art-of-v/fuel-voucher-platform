using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
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
            .IsRequired().HasComment("Groups every old voucher retired in one confirm action. The batch's lifecycle lives in" +
                    "movement_batches, which this points at.");

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
            .IsRequired().HasComment("The retired stock voucher.");

        // Nullable audit link to the replacement voucher - null when the old voucher was not paired.
        // Constrained below, like the retired side (#217).
        builder.Property(e => e.NewVoucherId)
            .HasColumnName("new_voucher_id").HasComment("The new stock voucher this old one was replaced by, or NULL when the old voucher was not" +
                    "paired - more old than new in its bucket.");

        // The fuel id is a uuid since planning D-21: a uuid column, so this can carry a real
        // foreign key one day - which is what D-02 is waiting for.
           builder.Property(e => e.FuelTypeId)
               .HasColumnName("fuel_type_id")
               .HasColumnType("uuid")
               .IsRequired().HasComment("The fuel of the retired voucher, and of its paired replacement - the same bucket.");

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired().HasComment("The brand of the retired voucher (okko/wog/...), denormalized for audit.");

        // The key the brand text above is a copy of. Required, because the composite foreign key
        // below is what refuses a row whose brand is not its fuel's brand - and a foreign key with a
        // NULL in it is not compared at all, so a nullable key would be a key the database does not
        // check.
        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid")
            .IsRequired().HasComment("The brand's id - the key the text above is a copy of. The composite foreign key" +
                    "(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand is" +
                    "not its fuel's brand.");

        builder.Property(e => e.SurchargeUah)
            .HasColumnName("surcharge_uah")
            .HasColumnType("numeric(10,2)")
            .IsRequired().HasComment("This row's OWN share of the batch surcharge - the brand charge spread pro-rata over this" +
                    "pair's litres, NOT the batch total repeated. Do not read one row and call it the batch" +
                    "charge: a four-voucher exchange would report a quarter of what was actually paid. Sum the" +
                    "rows of an ExchangeBatchId.");

        /// <summary>
        /// The per-liter cost stamped on THIS pair's new voucher (old voucher cost + its share of the
        /// surcharge). It is per row, not a copy of the batch total, because the old vouchers of one exchange
        /// routinely cost different amounts per liter and each new voucher has to carry its own.
        /// </summary>
        builder.Property(e => e.CostPerLiterApplied)
            .HasColumnName("cost_per_liter_applied")
            .HasColumnType("numeric(10,4)").HasComment("The cost per litre set on the new fuel batch for this exchange (the old blended cost plus the" +
                    "surcharge per litre), when there is one.");

        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired().HasComment("The supplier the old vouchers were bought from and the new ones were obtained from. One" +
                    "exchange involves exactly one supplier, so every row of a batch carries the same value - but" +
                    "it is recorded per row because this table is also read one voucher at a time during supplier" +
                    "reconciliation, where the row alone has to answer 'who do we settle with for this voucher'.");

        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.ActingUserId)
            .HasColumnName("acting_user_id")
            .IsRequired().HasComment("The admin who recorded the exchange.");

        builder.Property(e => e.ActingUserName)
            .HasColumnName("acting_user_name")
            .HasMaxLength(200).HasComment("The admin's display name at the time, for the audit. The name is frozen, so it still reads" +
                    "right after the person is renamed or leaves.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired().HasComment("When the exchange was recorded.");

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
        // The key is the PAIR (fuel type, brand), not the fuel alone - which is what D-21's second
        // slice needs. A foreign key over one column says "this fuel exists"; a foreign key over two
        // says "this fuel exists AND this exchange's brand is that fuel's brand", and the second half
        // is the fact the brand text on this row is a copy of. The pair points at the fuel's own
        // alternate key (id, brand_id), so the key spans one hop instead of two.
        //
        // This table carried no foreign key to fuel_types at all before - the reference was real and
        // single-parent, but nothing in the database said so. The composite key says more than a
        // plain one would: it also says the exchange's brand is the fuel's brand.
        //
        // Both halves of the pair are NOT NULL, which is what makes the key a key: a foreign key
        // with a NULL in it is not compared at all, so a nullable brand_id would be a brand the
        // database never checks.
        builder.HasOne<FuelTypeEntity>()
            .WithMany()
            .HasForeignKey(e => new { e.FuelTypeId, e.BrandId })
            .HasPrincipalKey(e => new { e.Id, e.BrandId })
            .OnDelete(DeleteBehavior.Restrict);

        // Postgres does not index a foreign key on its own, and the parent's delete check and
        // every join down the pair scan without one. The pair's index serves the joins down
        // fuel_type_id too - it leads with that column - so there is no second index on it.
        builder.HasIndex(e => new { e.FuelTypeId, e.BrandId })
            .HasDatabaseName("ix_voucher_exchanges_fuel_type_id_brand_id");

        // acting_user_id stays a bare uuid - an exchange is a settlement record and must always be
        // writable; it cannot be refused because an actor row is missing.
        builder.HasIndex(e => e.ExchangeBatchId);
        builder.HasIndex(e => e.NewVoucherId);
        // Supplier reconciliation reads one supplier's exchanged vouchers.
        builder.HasIndex(e => e.SupplierId);
        // acting_user_id is written and never read: an exchange is not filtered by who recorded it.
        // An index there would be the exact defect F-11 removed.

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
            // fuel_type_id is a uuid since planning D-21's second slice, and a uuid has no length to
            // bound - the check that bounded it as text went with the text.
        });
    }
}
