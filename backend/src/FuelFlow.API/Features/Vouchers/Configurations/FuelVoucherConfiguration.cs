using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
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

        // The fuel id is a uuid since planning D-21: a uuid column, so this can carry a real
        // foreign key one day - which is what D-02 is waiting for.
           builder.Property(e => e.FuelTypeId)
               .HasColumnName("fuel_type_id")
               .HasColumnType("uuid")
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
            // bytea, not varchar: this holds AES-GCM output - nonce, tag, ciphertext. The conversion
            // itself is applied in ApplicationDbContext.OnModelCreating, because it needs the cipher
            // instance and this configuration is discovered by assembly scan with no access to DI.
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(e => e.QrPayloadSha256)
            .HasColumnName("qr_payload_sha256")
            .HasColumnType("bytea")
            // Required, and that is load-bearing rather than tidiness. PostgreSQL treats NULLs as
            // distinct inside a unique index, so a nullable hash column would let any number of rows
            // through with a NULL hash and deduplication would stop working with no error anywhere -
            // the same paper imported twice under two numbers, which is exactly what this index
            // exists to prevent.
            .IsRequired()
            // Filled automatically on insert, so no writer has to remember. See the generator for why
            // a forgotten hash is worse than a failed write.
            .HasValueGenerator<FuelFlow.Features.Vouchers.Security.QrPayloadHashValueGenerator>();

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

        builder.Property(e => e.DataKind)
            .HasColumnName("data_kind")
            .HasMaxLength(10)
            .IsRequired();

        // HasDefaultValue as well as the CLR default: the value has to reach the database for the
        // write paths that never mention this axis (every existing import path among them), and EF
        // omits an unset column from the INSERT. Without the database-side default the column would
        // arrive as NULL and the NOT NULL would reject it.
        builder.Property(e => e.AcquisitionSource)
            .HasColumnName("acquisition_source")
            .HasMaxLength(10)
            .HasDefaultValue(AcquisitionSources.Default)
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
            // The value set DataKinds.All documents - see DataKinds.
            t.HasCheckConstraint("ck_fuel_vouchers_data_kind", "data_kind IN ('real', 'qa')");
            // The value set AcquisitionSources.All documents - see AcquisitionSources.
            t.HasCheckConstraint("ck_fuel_vouchers_acquisition_source", "acquisition_source IN ('direct', 'partner')");

            // The value set VoucherStatus defines. varchar(20) was exactly the length of
            // VerifiedWithWarnings, so the column had zero headroom and the next member added to
            // the enum would have failed at write time rather than at compile time; 32 gives room.
            t.HasCheckConstraint("ck_voucher_status", """
                status IN ('Imported', 'VerifiedWithWarnings', 'VerificationFailed', 'Available',
                           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')
                """);

            // We promise a customer fuel only while the supplier's paper is still alive. Without this
            // the rule lived in code, on some paths, and a path that forgot it sold fuel the pump will
            // refuse.
            t.HasCheckConstraint("ck_voucher_customer_term_within_supplier_term",
                "customer_expiration_date <= supplier_expiration_date");

            // The ownership matrix, in the database (D-02, "Consequence - ownership becomes enforceable").
            //
            // Declared here rather than as raw SQL in the migration so EF owns it and the model and the
            // installed constraint can never drift - the failure CheckConstraintColumnDriftTests was
            // written about. Because holder_kind is generated from the very columns the predicate
            // mentions, each disjunct is a check that a combination is coherent rather than a restatement
            // of the CASE above:
            //
            //   stock    - nobody holds it, so assigned_to_user_id must be null too
            //   worker   - belongs to a worker AND to the company that issued it, which is why
            //              legal_entity_id is required: the fuel is the company's and is in the worker's
            //              hands. GiftVouchersCommand is the only writer of worker_user_id and already
            //              refuses a voucher whose legal_entity_id is not the issuing company.
            //   personal - a customer's own fuel, so there is no company and no worker on the row
            //   company  - company fuel in the customer's own wallet, so there is no worker on the row
            //
            // 'stock' is the ELSE of the generated CASE, so it is also the only value that can reach
            // this predicate with a null assigned_to_user_id; and the ORs are exhaustive, so any value
            // outside the four is refused as well. That is why there is no separate CHECK on the
            // value set.
            // The value set is the enum member names, PascalCase, matching ck_voucher_status and
            // every other enum-derived value set in this schema. The generated column writes those
            // exact strings because the property has a string conversion - and a CHECK that compared
            // against lowercase would refuse every row the column had just written.
            t.HasCheckConstraint("ck_voucher_holder", """
                (holder_kind = 'Stock'    AND assigned_to_user_id IS NULL)
                OR (holder_kind = 'Worker'   AND legal_entity_id IS NOT NULL AND worker_user_id IS NOT NULL)
                OR (holder_kind = 'Personal' AND worker_user_id IS NULL AND assigned_to_user_id IS NOT NULL)
                OR (holder_kind = 'Company'  AND worker_user_id IS NULL AND assigned_to_user_id IS NOT NULL)
                """);
        });

        // GENERATED ALWAYS AS ... STORED, not a computed value EF holds in memory: the answer has to
        // be the same in SQL, in a report, and in psql, and ck_voucher_holder below reads it. Postgres
        // does allow a CHECK to reference a generated column - verified against 16 rather than assumed.
        //
        // Precedence is the whole point. worker_user_id first, because issuing fuel to a worker leaves
        // assigned_to_user_id on the owner on purpose; testing assigned_to_user_id first would report
        // the owner as the holder of fuel they had already given away.
        builder.Property(e => e.HolderKind)
            .HasColumnName("holder_kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasComputedColumnSql(
                """
                CASE
                    WHEN worker_user_id IS NOT NULL THEN 'Worker'
                    WHEN legal_entity_id IS NOT NULL AND assigned_to_user_id IS NOT NULL THEN 'Company'
                    WHEN assigned_to_user_id IS NOT NULL THEN 'Personal'
                    ELSE 'Stock'
                END
                """,
                stored: true)
            .ValueGeneratedOnAddOrUpdate();

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
        //
        // It sits on the SHA-256, not the payload. It used to sit on the payload itself, which was
        // possible only because that column was plaintext - and it is the reason the payload was
        // stored in the clear at all. AES-GCM produces a fresh nonce per encryption, so the same QR
        // encrypts differently every time and cannot be compared; the hash can. Deduplication is
        // unchanged in strength: both are one-way, and a collision would need a SHA-256 preimage.
        builder.HasIndex(e => e.QrPayloadSha256)
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(e => e.SupplierExpirationDate);
        builder.HasIndex(e => e.CustomerExpirationDate);
        builder.HasIndex(e => e.WorkerUserId);
        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.ImportJobId);
        builder.HasIndex(e => e.SupplierId);

        // Cleanup and "is this stock real?" queries filter on it, so it is indexed alongside status.
        builder.HasIndex(e => new { e.Status, e.DataKind, e.IsDeleted });
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
        // reference cannot drift into something arbitrary - see BrandConfiguration.
        builder.ToTable(t =>
        {
            });
    }
}
