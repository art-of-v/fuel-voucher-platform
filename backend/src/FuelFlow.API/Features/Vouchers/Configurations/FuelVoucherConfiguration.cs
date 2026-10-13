using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class FuelVoucherConfiguration : IEntityTypeConfiguration<FuelVoucher>
{
    public void Configure(EntityTypeBuilder<FuelVoucher> builder)
    {
        builder.ToTable("fuel_vouchers");

        builder.HasKey(e => e.Id);

        // #246: xmin token. No DDL — xmin is a system column. Raw SQL must never write this table
        // in the same request as a tracked save of the same row, or the token starts failing saves
        // that did not actually race. Every raw write here is a compare-and-swap, and FulfillmentService
        // now clears the tracker before reading rows its own statements just claimed, which is what
        // makes that rule hold rather than merely be intended.
        builder.UseXminAsConcurrencyToken();

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.SupplierExpirationDate)
            .HasColumnName("supplier_expiration_date")
            .IsRequired();

        builder.Property(e => e.CustomerExpirationDate)
            .HasColumnName("customer_expiration_date")
            .IsRequired();

        builder.Property(e => e.VoucherNumber)
            .HasColumnName("voucher_number")
            .HasMaxLength(100)
            .IsRequired();

        // Bounded on purpose. The QR payload is a bearer instrument - whoever holds it can have
        // the voucher redeemed at the pump - and it is the widest value on the table. A payload
        // that does not fit in 2000 characters will not scan anyway, and the cap keeps the
        // unique index below TOAST so the entry is a predictable size.
        builder.Property(e => e.QrPayload)
            .HasColumnName("qr_payload")
            .HasColumnType("varchar(2000)")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.FuelSubtype)
            .HasColumnName("fuel_subtype")
            .HasMaxLength(50);

        // Read-only and never written, but projected by live endpoints, so it stays - bounded.
        builder.Property(e => e.RedemptionRules)
            .HasColumnName("redemption_rules")
            .HasMaxLength(1000);

        builder.Property(e => e.ImageUrl)
            .HasColumnName("image_url")
            .HasMaxLength(500);

        builder.Property(e => e.AssignedToUserId)
            .HasColumnName("assigned_to_user_id");

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id");

        builder.Property(e => e.WorkerUserId)
            .HasColumnName("worker_user_id");

        builder.Property(e => e.ImportJobId)
            .HasColumnName("import_job_id");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id");

        builder.Property(e => e.QrParametersId)
            .HasColumnName("qr_parameters_id");

        builder.HasOne(e => e.QrParameters)
            .WithMany()
            .HasForeignKey(e => e.QrParametersId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.AssignedToUser)
            .WithMany()
            .HasForeignKey(e => e.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.LegalEntity)
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.WorkerUser)
            .WithMany()
            .HasForeignKey(e => e.WorkerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ImportJob)
            .WithMany()
            .HasForeignKey(e => e.ImportJobId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict, not Cascade. A voucher is paper the customer already paid for, and this
        // constraint was the sharpest edge in the schema: deleting a fuel type walked straight
        // into fuel_vouchers and destroyed every voucher sold against it. #217 fixed the code
        // path (the handlers archive now), which left the constraint armed with nothing
        // pointing at it. The catalog is archived via IsDeleted; this is the seatbelt for a
        // DELETE arriving from anywhere else, including a hand-written psql session.
        builder.HasOne(e => e.FuelType)
            .WithMany()
            .HasForeignKey(e => e.FuelTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not SetNull: deleting an order must never silently strip the only trace of
        // where a voucher in someone's hands came from. That matters as soon as the "a held
        // voucher always has an order" CHECK lands, and it is the safe choice before it does.
        builder.HasOne<FuelFlow.Features.Orders.SharedModels.Order>()
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id");

        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.CostPerLiter)
            .HasColumnName("cost_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.IsTestData)
            .HasColumnName("is_test_data")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasQueryFilter(e => !e.IsDeleted);

        // These live in the model, not in a hand-written migration, so EF owns them: it will
        // never drop one by accident, and a regenerated `migrations script` can reproduce the
        // schema from the model. The held-voucher rule used to exist only as raw SQL, invisible
        // to EF and coupled to the VoucherStatus member names by literal string.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_voucher_held_has_order",
                "status NOT IN ('Assigned', 'Used', 'Blocked') OR order_id IS NOT NULL");
            t.HasCheckConstraint("ck_voucher_liters_positive", "liters > 0");
            t.HasCheckConstraint("ck_voucher_cost_non_negative", "cost_per_liter IS NULL OR cost_per_liter >= 0");

            // The value set VoucherStatus defines. varchar(20) was exactly the length of
            // VerifiedWithWarnings, so the column had zero headroom and the next member added to
            // the enum would have failed at write time rather than at compile time; 32 gives room.
            t.HasCheckConstraint("ck_voucher_status", """
                status IN ('Imported', 'VerifiedWithWarnings', 'VerificationFailed', 'Available',
                           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')
                """);

            // Deliberately NOT constrained: customer_expiration_date <= supplier_expiration_date.
            // It looks obviously true and is not - ConfirmOperatorRenewalCommandHandler stamps the
            // customer's promise from the *renewal term*, not from the paper's own date ("the extra
            // real life is a reserve"), so a deliberate business rule produces customer > brand.
            // A constraint here would refuse a renewal the business has already decided to allow.
            // VOUCHER_RENEWAL.md:371-374 records that as an open commercial decision, so it stays
            // a decision rather than becoming an exception nobody can add.
        });

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // These two arrived in PascalCase because the mapping was forgotten - the only
        // snake_case violations left on this table. Named explicitly, like every other column.
        builder.Property(e => e.IssuedAtUtc)
            .HasColumnName("issued_at_utc");

        builder.Property(e => e.UsedAtUtc)
            .HasColumnName("used_at_utc");

        builder.Property(e => e.VerificationMismatchPercent)
            .HasColumnName("verification_mismatch_percent")
            .HasColumnType("double precision");

        builder.Property(e => e.VerificationMismatchedModules)
            .HasColumnName("verification_mismatched_modules");

        builder.Property(e => e.VerificationTotalModules)
            .HasColumnName("verification_total_modules");

        // Both keys are filtered to live rows, so an archived voucher releases its number and its
        // QR payload for reuse. Without the filter the two paths disagree: the import's duplicate
        // check reads through the !IsDeleted query filter, so it calls a number free, and then the
        // insert hits a DB-global unique index and the whole import dies on an error the operator
        // cannot interpret. #1067 made soft delete the only delete path, so archived rows - and the
        // keys they squat on - are permanent.
        //
        // The dedup guarantee this buys is the one that matters: the same paper cannot be live
        // twice under two numbers. Re-importing paper that was archived by mistake is a recovery
        // path, not an attack surface, and it now works instead of exploding.
        builder.HasIndex(e => e.VoucherNumber)
            .IsUnique()
            .HasFilter("is_deleted = false");

        // Security control, not just an index: dedup on the QR payload means the same paper cannot
        // be imported twice under two numbers. Worth the storage.
        builder.HasIndex(e => e.QrPayload)
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(e => e.SupplierExpirationDate);
        builder.HasIndex(e => e.CustomerExpirationDate);
        builder.HasIndex(e => e.WorkerUserId);
        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.ImportJobId);
        builder.HasIndex(e => e.SupplierId);

        // Cleanup and "is this stock real?" queries filter on it, so it is indexed alongside status.
        builder.HasIndex(e => new { e.Status, e.IsTestData, e.IsDeleted });
        builder.HasIndex(e => new { e.Brand, e.FuelTypeId, e.Liters, e.Status });
        // The blended-cost recompute reads this pool filtered by fuel and status.
        builder.HasIndex(e => new { e.FuelTypeId, e.Status, e.IsDeleted });
        builder.HasIndex(e => new { e.AssignedToUserId, e.Status });
        builder.HasIndex(e => new { e.LegalEntityId, e.WorkerUserId, e.Status, e.IsDeleted });

        // Dropped as strict left-prefix duplicates of the composites above: a bare (status),
        // (fuel_type_id), (assigned_to_user_id) and (legal_entity_id) can serve nothing the
        // wider index cannot serve better. Also dropped: a unique index on external_id, a column
        // nothing in the codebase ever writes, so it guarded 100% NULLs forever.

        // The FK side of the same natural-key trade: fuel_type_id points at fuel_types.id ("okko-95"),
        // which is deliberately a readable string rather than a UUID. Bounded by CHECK so the
        // reference cannot drift into something arbitrary - see StationConfiguration.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_fuel_vouchers_fuel_type_id_length", "length(fuel_type_id) <= 100");
        });
    }
}
